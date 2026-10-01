using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace TANGERINE_ZIP.Services;

// Stage head: UNRAS (UnregisterAssociationService). This is the inverse of
// DefaultAppAssociationService's current-user registration plan. It never
// removes another application's UserChoice or a machine-wide registration.
internal static class DefaultAppUnregistrationService
{
    private const string FileExtsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\";
    private const string ClassesPath = @"Software\Classes\";
    private const string CapabilitiesPath = @"Software\TangerineZip\DefaultApps\Capabilities";

    internal static int Unregister(Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string[] extensions = DefaultAppAssociationService.Formats.SelectMany(f => f.Extensions).ToArray();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new StageException("UNRAS0001", LanguageManager.Get("DefaultAppsUnregisterBusy")); //UNRAS0001
        using Mutex gate = new(false, @"Local\TangerineZip.DefaultApps." + sid);
        bool acquired = false;
        bool removingRegistration = false;
        try
        {
            try
            {
                try { acquired = gate.WaitOne(TimeSpan.FromSeconds(5)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new TimeoutException("Association mutex timed out.");
            }
            catch (Exception error)
            {
                throw new StageException("UNRAS0001", LanguageManager.Get("DefaultAppsUnregisterBusy"), error); //UNRAS0001
            }

            // Phase one must finish for every format before any ProgID is removed.
            // Otherwise a protected choice could point to a deleted handler.
            List<Exception> choiceErrors = [];
            foreach (string extension in extensions)
            {
                try
                {
                    ClearOurChoices(Registry.CurrentUser, extension, HasOurExplicitChoice);
                    report(string.Format(LanguageManager.Get("DefaultAppsUnregisterChoiceCleared"), extension));
                }
                catch (Exception error)
                {
                    choiceErrors.Add(new InvalidOperationException(extension, error));
                    report(string.Format(LanguageManager.Get("DefaultAppsUnregisterFormatFailed"), extension, error.Message));
                }
            }
            if (choiceErrors.Count != 0)
                throw new StageException("UNRAS0002", LanguageManager.Get("DefaultAppsUnregisterChoiceFailed"),
                    new AggregateException(choiceErrors)); //UNRAS0002

            removingRegistration = true;
            List<Exception> registrationErrors = [];
            foreach (string extension in extensions)
            {
                try
                {
                    RemoveFormatRegistration(Registry.CurrentUser, extension);
                    report(string.Format(LanguageManager.Get("DefaultAppsUnregisterFormatRemoved"), extension));
                }
                catch (Exception error)
                {
                    registrationErrors.Add(new InvalidOperationException(extension, error));
                    report(string.Format(LanguageManager.Get("DefaultAppsUnregisterFormatFailed"), extension, error.Message));
                }
            }
            if (registrationErrors.Count == 0)
            {
                try { RemoveApplicationRegistration(Registry.CurrentUser); }
                catch (Exception error) { registrationErrors.Add(error); }
            }
            NotifyShell();
            // With no explicit app-owned choice left, the Shell can still select
            // our registered handler as a fallback. Only after withdrawing that
            // handler can the effective default be checked meaningfully.
            if (registrationErrors.Count == 0)
                foreach (string extension in extensions)
                    try
                    {
                        if (IsOurEffectiveDefaultWithRetry(extension))
                        {
                            string message = LanguageManager.Get("DefaultAppsUnregisterStillDefault");
                            registrationErrors.Add(new InvalidOperationException(extension + ": " + message));
                            report(string.Format(LanguageManager.Get("DefaultAppsUnregisterFormatFailed"), extension, message));
                        }
                    }
                    catch (Exception error)
                    {
                        registrationErrors.Add(new InvalidOperationException(extension, error));
                        report(string.Format(LanguageManager.Get("DefaultAppsUnregisterFormatFailed"), extension, error.Message));
                    }
            if (registrationErrors.Count == 0)
            {
                try { RemoveCachedIcons(); }
                catch (Exception error) { registrationErrors.Add(error); }
            }
            if (registrationErrors.Count != 0)
                throw new StageException("UNRAS0004", LanguageManager.Get("DefaultAppsUnregisterRegistrationFailed"),
                    new AggregateException(registrationErrors)); //UNRAS0004
            return extensions.Length;
        }
        catch (Exception error) when (error is not StageException)
        {
            throw new StageException(removingRegistration ? "UNRAS0004" : "UNRAS0002",
                LanguageManager.Get(removingRegistration ? "DefaultAppsUnregisterRegistrationFailed" :
                    "DefaultAppsUnregisterChoiceFailed"), error); //UNRAS0002, UNRAS0004
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }

    // The root and explicit-choice probe can be substituted with disposable
    // registry/test probes. Production passes HKCU and reads both choice formats.
    internal static void ClearOurChoices(RegistryKey root, string extension,
        Func<string, bool> hasOurExplicitChoice, bool notifyShell = true)
    {
        string progId = DefaultAppAssociationService.GetProgId(extension);
        using RegistryKey? association = root.OpenSubKey(FileExtsPath + extension, writable: true);
        if (association == null)
        {
            if (hasOurExplicitChoice(extension))
                throw new StageException("UNRAS0003", LanguageManager.Get("DefaultAppsUnregisterChoiceFailed")); //UNRAS0003
            return;
        }
        List<(string Original, string Backup)> moved = [];
        try
        {
            foreach (string name in new[] { "UserChoiceLatest", "UserChoice" })
            {
                string? stored;
                using (RegistryKey? key = association.OpenSubKey(name))
                    stored = name == "UserChoiceLatest"
                        ? key?.OpenSubKey("ProgId") is RegistryKey nested ? ReadAndDispose(nested, "ProgId") : null
                        : key?.GetValue("ProgId") as string;
                if (!string.Equals(stored, progId, StringComparison.OrdinalIgnoreCase)) continue;
                string backup = "TzipUnregister" + Guid.NewGuid().ToString("N");
                Rename(association, name, backup);
                moved.Add((name, backup));
            }
            if (notifyShell) NotifyShell();
            if (hasOurExplicitChoice(extension))
                throw new StageException("UNRAS0003", LanguageManager.Get("DefaultAppsUnregisterChoiceFailed")); //UNRAS0003
        }
        catch (Exception operationError)
        {
            List<Exception> restoreErrors = [];
            foreach ((string original, string backup) in moved.AsEnumerable().Reverse())
                try { Rename(association, backup, original); }
                catch (Exception error) { restoreErrors.Add(error); }
            if (notifyShell) NotifyShell();
            if (restoreErrors.Count != 0)
                throw new StageException("UNRAS0005", LanguageManager.Get("DefaultAppsUnregisterRollbackFailed"),
                    new AggregateException(new[] { operationError }.Concat(restoreErrors))); //UNRAS0005
            throw;
        }
        // Backups are now inactive. A failed deletion remains visible as an error
        // and blocks the later registration phase; retry can remove the leftovers.
        foreach ((string original, string backup) in moved)
            DeleteChoiceBackup(association, original, backup);
        foreach (string orphan in association.GetSubKeyNames().Where(name =>
            name.StartsWith("TzipUnregister", StringComparison.Ordinal) && name.Length == 46))
        {
            string? saved;
            using (RegistryKey? key = association.OpenSubKey(orphan))
            using (RegistryKey? nested = key?.OpenSubKey("ProgId"))
                saved = (nested ?? key)?.GetValue("ProgId") as string;
            if (string.Equals(saved, progId, StringComparison.OrdinalIgnoreCase))
                DeleteChoiceBackup(association, HasNestedProgId(association, orphan) ? "UserChoiceLatest" : "UserChoice", orphan);
        }
    }

    private static bool HasNestedProgId(RegistryKey parent, string name)
    {
        using RegistryKey? key = parent.OpenSubKey(name);
        using RegistryKey? nested = key?.OpenSubKey("ProgId");
        return nested != null;
    }

    private static void DeleteChoiceBackup(RegistryKey parent, string original, string backup)
    {
        // Classic UserChoice is a leaf. RegDeleteKey requires fewer rights than
        // recursive deletion and is the same API path used by the SFTA writer.
        if (original == "UserChoice") parent.DeleteSubKey(backup, false);
        else parent.DeleteSubKeyTree(backup, false);
    }

    private static string? ReadAndDispose(RegistryKey nested, string name)
    {
        using (nested) return nested.GetValue(name) as string;
    }

    private static bool HasOurExplicitChoice(string extension)
    {
        string progId = DefaultAppAssociationService.GetProgId(extension);
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(FileExtsPath + extension);
        using RegistryKey? latest = parent?.OpenSubKey(@"UserChoiceLatest\ProgId");
        using RegistryKey? classic = parent?.OpenSubKey("UserChoice");
        return string.Equals(latest?.GetValue("ProgId") as string, progId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(classic?.GetValue("ProgId") as string, progId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOurEffectiveDefault(string extension) =>
        string.Equals(DefaultAppAssociationService.QueryCurrentProgId(extension),
            DefaultAppAssociationService.GetProgId(extension), StringComparison.OrdinalIgnoreCase);

    private static bool IsOurEffectiveDefaultWithRetry(string extension)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!IsOurEffectiveDefault(extension)) return false;
            Thread.Sleep(120);
        }
        return true;
    }

    // Also callable with an isolated HKCU-like root by tests. The exact ProgID
    // display name is an ownership check before deleting its entire handler tree.
    internal static void RemoveFormatRegistration(RegistryKey root, string extension)
    {
        string progId = DefaultAppAssociationService.GetProgId(extension);
        string handlerPath = ClassesPath + progId;
        using (RegistryKey? handler = root.OpenSubKey(handlerPath))
            if (handler != null && !string.Equals(handler.GetValue(null) as string,
                    "TANGERINE ZIP " + extension, StringComparison.Ordinal))
                throw new InvalidDataException("ProgID is no longer owned by TANGERINE ZIP: " + progId);
        using (RegistryKey? openWith = root.OpenSubKey(ClassesPath + extension + @"\OpenWithProgids", true))
            openWith?.DeleteValue(progId, throwOnMissingValue: false);
        // Older installs or manual registration may have written this direct
        // extension fallback. Remove only an exact self-owned value.
        using (RegistryKey? extensionKey = root.OpenSubKey(ClassesPath + extension, true))
            if (string.Equals(extensionKey?.GetValue("") as string, progId, StringComparison.OrdinalIgnoreCase))
                extensionKey!.DeleteValue("", false);
        root.DeleteSubKeyTree(handlerPath, throwOnMissingSubKey: false);
        using (RegistryKey? associations = root.OpenSubKey(CapabilitiesPath + @"\FileAssociations", true))
            if (string.Equals(associations?.GetValue(extension) as string, progId,
                    StringComparison.OrdinalIgnoreCase))
                associations!.DeleteValue(extension, false);
    }

    internal static void RemoveApplicationRegistration(RegistryKey root)
    {
        using (RegistryKey? remaining = root.OpenSubKey(CapabilitiesPath + @"\FileAssociations"))
            if (remaining != null && remaining.GetValueNames().Length != 0)
                throw new InvalidDataException("Unknown file associations remain under TANGERINE ZIP capabilities.");
        using (RegistryKey? owner = root.OpenSubKey(CapabilitiesPath))
            if (owner?.GetValue("ApplicationName") is string applicationName &&
                !string.Equals(applicationName, DefaultAppAssociationService.RegisteredApplicationName,
                    StringComparison.Ordinal))
                throw new InvalidDataException("Capabilities key is no longer owned by TANGERINE ZIP.");
        using (RegistryKey? registered = root.OpenSubKey(@"Software\RegisteredApplications", true))
            if (string.Equals(registered?.GetValue(DefaultAppAssociationService.RegisteredApplicationName) as string,
                    CapabilitiesPath, StringComparison.OrdinalIgnoreCase))
                registered!.DeleteValue(DefaultAppAssociationService.RegisteredApplicationName, false);

        // Keep unrelated values or child keys that another component may have
        // placed beneath our Capabilities key. Remove only our known fields.
        using RegistryKey? capabilities = root.OpenSubKey(CapabilitiesPath, true);
        if (capabilities == null) return;
        foreach (string name in new[] { "ApplicationName", "ApplicationDescription", "ApplicationIcon" })
            capabilities.DeleteValue(name, false);
        using (RegistryKey? associations = capabilities.OpenSubKey("FileAssociations"))
            if (associations is { SubKeyCount: 0 } && associations.GetValueNames().Length == 0)
                capabilities.DeleteSubKey("FileAssociations", false);
        if (capabilities.SubKeyCount == 0 && capabilities.GetValueNames().Length == 0)
        {
            capabilities.Dispose();
            root.DeleteSubKey(CapabilitiesPath, false);
        }
    }

    private static void RemoveCachedIcons()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) throw new DirectoryNotFoundException("LocalAppData is unavailable.");
        string directory = Path.Combine(local, "TangerineZip", "DefaultApps", "Icons");
        if (!Directory.Exists(directory)) return;
        HashSet<string> names = DefaultAppAssociationService.Formats.SelectMany(f => f.Extensions)
            .Select(ArchiveFileIconService.GetIconName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(directory, "*.ico"))
        {
            string basename = Path.GetFileNameWithoutExtension(file);
            int separator = basename.LastIndexOf('-');
            if (separator < 0 || !names.Contains(basename[..separator])) continue;
            string digest = basename[(separator + 1)..];
            if (digest.Length == 64 && digest.All(Uri.IsHexDigit)) File.Delete(file);
        }
    }

    private static void Rename(RegistryKey parent, string oldName, string newName)
    {
        int error = RegRenameKey(parent.Handle, oldName, newName);
        if (error != 0) throw new Win32Exception(error, $"Could not rename {oldName}.");
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("advapi32.dll", EntryPoint = "RegRenameKey", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegRenameKey(SafeRegistryHandle key, string oldName, string newName);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
