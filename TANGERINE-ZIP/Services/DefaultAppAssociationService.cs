using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TANGERINE_ZIP.Services;

internal sealed record AssociationFormat(string Label, IReadOnlyList<string> Extensions);
internal sealed record AssociationRegistrationValue(string KeyPath, string Name, string Value);
internal sealed record DefaultSettingsLaunch(string Uri, IReadOnlyList<StageException> Warnings);

// Stage head: DEFAS (DefaultAppAssociationService).
// Candidate registration and the explicit Settings handoff live here. The
// automatic action picks classic SFTA or UserChoiceLatest according to the
// current user's Windows hash version and verifies the effective association.
internal static class DefaultAppAssociationService
{
    internal const string RegisteredApplicationName = "TANGERINE ZIP";
    private const string CapabilitiesPath = @"Software\TangerineZip\DefaultApps\Capabilities";

    // One checkbox per supported archive format. Zstandard has two public suffixes;
    // each suffix needs its own default association. Disk images ISO/WIM are
    // deliberately excluded, as are unadvertised aliases and generic .001 volumes.
    internal static IReadOnlyList<AssociationFormat> Formats { get; } = Array.AsReadOnly(new[]
    {
        new AssociationFormat("ZIP", Array.AsReadOnly(new[] { ".zip" })),
        new AssociationFormat("RAR", Array.AsReadOnly(new[] { ".rar" })),
        new AssociationFormat("7z", Array.AsReadOnly(new[] { ".7z" })),
        new AssociationFormat("TAR", Array.AsReadOnly(new[] { ".tar" })),
        new AssociationFormat("GZip", Array.AsReadOnly(new[] { ".gz" })),
        new AssociationFormat("BZip2", Array.AsReadOnly(new[] { ".bz2" })),
        new AssociationFormat("XZ", Array.AsReadOnly(new[] { ".xz" })),
        new AssociationFormat("LZ4", Array.AsReadOnly(new[] { ".lz4" })),
        new AssociationFormat("Zstandard", Array.AsReadOnly(new[] { ".zst", ".zstd" }))
    });

    private static void ValidateExtension(string extension)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10) ||
            !Formats.Any(format => format.Extensions.Contains(extension, StringComparer.Ordinal)))
            throw new StageException("DEFAS0001", LanguageManager.Get("DefaultAppsUnsupported")); //DEFAS0001
    }

    internal static string GetProgId(string extension)
    {
        ValidateExtension(extension);
        return "TangerineZip.Archive." + extension[1..];
    }

    internal static void SetThisAppDefault(string extension, string executablePath)
    {
        ValidateExtension(extension);
        RegisterHandler(extension, executablePath);
        string progId = GetProgId(extension);
        if (string.Equals(QueryCurrentProgId(extension), progId, StringComparison.OrdinalIgnoreCase)) return;
        if (LatestUserChoice.IsLatestFormatActive(extension))
            LatestUserChoice.SetDefault(extension, progId, () => QueryCurrentProgId(extension));
        else
            SftaUserChoice.SetDefault(extension, progId, () => QueryCurrentProgId(extension));
    }

    internal static string GetExecutablePath()
    {
        // Environment.ProcessPath also handles renamed release apphosts and
        // single-file publishing, where Assembly.Location is empty. A dotnet.exe
        // host is not a usable handler: Windows must launch the application itself.
        string? path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new StageException("DEFAS0002", LanguageManager.Get("DefaultAppsExecutableMissing")); //DEFAS0002
        return Path.GetFullPath(path);
    }

    // The explicit write plan makes the registration scope reviewable and testable
    // without changing actual associations. Extension default values are untouched;
    // OpenWithProgids advertises a candidate rather than choosing a default.
    internal static IReadOnlyList<AssociationRegistrationValue> BuildRegistrationPlan(string extension, string executablePath, string fileIconPath)
    {
        string progId = GetProgId(extension);
        string handler = @"Software\Classes\" + progId;
        // Register a format-specific, persistent ICO on our ProgID. Writing an
        // icon on the extension itself would affect other apps' defaults too.
        // The application identity keeps the EXE's plain logo, without a format.
        string fileIcon = $"\"{fileIconPath}\",0";
        string applicationIcon = $"\"{executablePath}\",0";
        return new AssociationRegistrationValue[]
        {
            new(handler, "", "TANGERINE ZIP " + extension),
            new(handler + @"\DefaultIcon", "", fileIcon),
            new(handler + @"\shell", "", "open"),
            new(handler + @"\shell\open\command", "", $"\"{executablePath}\" \"%1\""),
            new(@"Software\Classes\" + extension + @"\OpenWithProgids", progId, ""),
            new(CapabilitiesPath, "ApplicationName", RegisteredApplicationName),
            new(CapabilitiesPath, "ApplicationDescription", LanguageManager.Get("DefaultAppsApplicationDescription")),
            new(CapabilitiesPath, "ApplicationIcon", applicationIcon),
            new(CapabilitiesPath + @"\FileAssociations", extension, progId),
            new(@"Software\RegisteredApplications", RegisteredApplicationName, CapabilitiesPath)
        };
    }

    private sealed record SavedValue(string KeyPath, string Name, bool Existed, object? Value, RegistryValueKind Kind);

    internal static void RegisterHandler(string extension, string executablePath)
    {
        ValidateExtension(extension);
        if (!File.Exists(executablePath))
            throw new StageException("DEFAS0002", LanguageManager.Get("DefaultAppsExecutableMissing")); //DEFAS0002
        Mutex? gate = null;
        bool acquired = false;
        try
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                string sid = identity.User?.Value ?? throw new InvalidOperationException(LanguageManager.Get("DefaultAppsRegistrationBusy"));
                gate = new Mutex(false, @"Local\TangerineZip.DefaultApps." + sid);
                try { acquired = gate.WaitOne(TimeSpan.FromSeconds(5)); }
                catch (AbandonedMutexException)
                {
                    // We own an abandoned mutex. A terminated process may have left
                    // a partial candidate registration; applying the entire plan again
                    // repairs it. No protected default choice is modified by either run.
                    acquired = true;
                }
                if (!acquired) throw new TimeoutException(LanguageManager.Get("DefaultAppsRegistrationBusy"));
            }
            catch (Exception exception)
            {
                throw new StageException("DEFAS0009", LanguageManager.Get("DefaultAppsRegistrationBusy"), exception); //DEFAS0009
            }
            // Finish extracting and verifying the icon before touching registry
            // values. This also repairs a missing/damaged cache file when the user
            // reruns the flow for an extension that already defaults to this app.
            string fileIconPath = ArchiveFileIconService.EnsureIcon(extension);
            ApplyRegistrationPlan(Registry.CurrentUser, BuildRegistrationPlan(extension, executablePath, fileIconPath));
            try
            {
                // Invalidate Shell's association cache after all readbacks succeed.
                // This informs Explorer of a new candidate; it does not choose that app.
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED / SHCNF_IDLIST
            }
            catch (Exception exception)
            {
                throw new StageException("DEFAS0008", LanguageManager.Get("DefaultAppsNotifyFailed"), exception); //DEFAS0008
            }
        }
        finally
        {
            // Mutex ownership is thread-affine. This method is synchronous and its
            // caller runs it wholly on one worker thread, including rollback/release.
            if (acquired) gate!.ReleaseMutex();
            gate?.Dispose();
        }
    }

    // Accept a registry root so the journal can also be exercised under an isolated
    // disposable test key. Production always passes Registry.CurrentUser; no HKLM
    // write path is exposed by the window, and this helper does not notify the Shell.
    internal static void ApplyRegistrationPlan(RegistryKey root, IEnumerable<AssociationRegistrationValue> plan)
    {
        List<SavedValue> journal = [];
        try
        {
            foreach (AssociationRegistrationValue item in plan)
            {
                using RegistryKey key = root.CreateSubKey(item.KeyPath, writable: true)
                    ?? throw new IOException(LanguageManager.Get("DefaultAppsRegistrationFailed"));
                bool existed = key.GetValueNames().Contains(item.Name, StringComparer.OrdinalIgnoreCase);
                // Record before writing: even SetValue can fail after some previous
                // writes succeeded. Preserve both the original value and its kind.
                journal.Add(new(item.KeyPath, item.Name, existed,
                    existed ? key.GetValue(item.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null,
                    existed ? key.GetValueKind(item.Name) : RegistryValueKind.String));
                key.SetValue(item.Name, item.Value, RegistryValueKind.String);
                if (key.GetValueKind(item.Name) != RegistryValueKind.String ||
                    !Equals(key.GetValue(item.Name), item.Value))
                    throw new IOException(LanguageManager.Get("DefaultAppsRegistrationFailed"));
            }
        }
        catch (Exception registrationError)
        {
            List<Exception> rollbackErrors = [];
            foreach (SavedValue saved in journal.AsEnumerable().Reverse())
            {
                try
                {
                    using RegistryKey key = root.CreateSubKey(saved.KeyPath, writable: true)
                        ?? throw new IOException(LanguageManager.Get("DefaultAppsRollbackFailed"));
                    if (saved.Existed) key.SetValue(saved.Name, saved.Value!, saved.Kind);
                    else key.DeleteValue(saved.Name, throwOnMissingValue: false);
                    // Do not delete ancestor keys: other applications or an instance
                    // of this app may have added values since the snapshot was taken.
                }
                catch (Exception rollbackError) { rollbackErrors.Add(rollbackError); }
            }
            if (rollbackErrors.Count > 0)
                throw new StageException("DEFAS0007", LanguageManager.Get("DefaultAppsRollbackFailed"),
                    new AggregateException(new[] { registrationError }.Concat(rollbackErrors))); //DEFAS0007
            throw new StageException("DEFAS0003", LanguageManager.Get("DefaultAppsRegistrationFailed"), registrationError); //DEFAS0003
        }
    }

    internal static string BuildSettingsUri(bool useThisApp, int build, int revision)
    {
        // App-specific Settings URIs were added by the April 2023 cumulative
        // updates, not by the initial Windows 11 release. Never assume all builds
        // >= 22000 understand registeredAppUser. Windows 10 and unpatched 21H2 /
        // 22H2 use the general page and the window supplies the current extension.
        bool appPageSupported = build >= 22631 || (build == 22621 && revision >= 1555) ||
            (build == 22000 && revision >= 1817);
        return useThisApp && appPageSupported
            ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(RegisteredApplicationName)
            : "ms-settings:defaultapps";
    }

    internal static DefaultSettingsLaunch OpenWindowsSettings(bool useThisApp)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            throw new StageException("DEFAS0001", LanguageManager.Get("DefaultAppsUnsupported")); //DEFAS0001
        List<StageException> warnings = [];
        int build = Environment.OSVersion.Version.Build;
        int revision = 0;
        if (useThisApp && build is 22000 or 22621)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                revision = key?.GetValue("UBR") is int value ? value : 0;
            }
            catch (Exception exception)
            {
                // Failing to read the OS revision does not prevent using the
                // general Settings page. Surface the diagnostic instead of silently
                // assuming a newer UI protocol or asking for unnecessary elevation.
                warnings.Add(new StageException("DEFAS0004", LanguageManager.Get("DefaultAppsVersionFallback"), exception)); //DEFAS0004
            }
        }
        string uri = BuildSettingsUri(useThisApp, build, revision);
        try { Launch(uri); }
        catch (Exception exception) when (uri != "ms-settings:defaultapps")
        {
            warnings.Add(new StageException("DEFAS0005", LanguageManager.Get("DefaultAppsLaunchFallback"), exception)); //DEFAS0005
            uri = "ms-settings:defaultapps";
            Launch(uri);
        }
        // A successful launch is only a handoff. Settings can reuse an existing
        // process, so Process.Start returning null is not a failed default change.
        return new(uri, warnings);
    }

    private static void Launch(string uri)
    {
        try { using Process? process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception exception)
        {
            throw new StageException("DEFAS0005", LanguageManager.Get("DefaultAppsLaunchFailed"), exception); //DEFAS0005
        }
    }

    internal static string? QueryCurrentProgId(string extension)
    {
        ValidateExtension(extension);
        try
        {
            // Query the effective Shell association including the user's choice,
            // rather than mistaking our capability registration for the default.
            uint length = 0;
            int result = AssocQueryString(0, 20, extension, null, null, ref length); // ASSOCSTR_PROGID
            if (result is unchecked((int)0x80070483) or unchecked((int)0x80070002)) return null;
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (length is 0 or > 32768) throw new InvalidDataException(LanguageManager.Get("DefaultAppsQueryFailed"));
                StringBuilder buffer = new((int)length);
                result = AssocQueryString(0, 20, extension, null, buffer, ref length);
                if (result == 0) return buffer.ToString();
                if (result is unchecked((int)0x80070483) or unchecked((int)0x80070002)) return null;
                // Retry only a buffer-size race caused by a simultaneous Settings
                // change; other HRESULTs are actionable errors, not "not this app".
                if (result != 1 && result != unchecked((int)0x80004003)) Marshal.ThrowExceptionForHR(result);
            }
            throw new InvalidDataException(LanguageManager.Get("DefaultAppsQueryFailed"));
        }
        catch (Exception exception)
        {
            throw new StageException("DEFAS0006", LanguageManager.Get("DefaultAppsQueryFailed"), exception); //DEFAS0006
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryString(uint flags, uint str, string association, string? extra,
        StringBuilder? output, ref uint length);
}
