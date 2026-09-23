using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TANGERINE_ZIP.Services;

internal sealed record ContextMenuProgress(int Percentage, string ResourceKey, string? Detail = null);
internal enum ContextMenuPresentation { Grouped, Direct }

// Stage head: CTXMN
internal static class ContextMenuRegistrationService
{
    private const string PackageName = "TangerineZip.ContextMenu";
    private const string PackageResourceName = "TANGERINE_ZIP.ContextMenu.TangerineZipContextMenu.msix";
    private const string CertificateResourceName = "TANGERINE_ZIP.ContextMenu.TangerineZipContextMenu.cer";
    private const string SettingsPath = @"Software\TangerineZip\ContextMenu";
    private const string MinimumPackageVersion = "2.1.0.0";
    private const string LegacyCertificateThumbprint = "080D2C60C6B53BD797A97DC3032FD40A17560095";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0;
    private static readonly SemaphoreSlim OperationGate = new(1, 1);
    private static readonly string[] LegacyShellPaths =
    [
        @"Software\Classes\*\shell",
        @"Software\Classes\AllFilesystemObjects\shell",
        @"Software\Classes\SystemFileAssociations\*\shell"
    ];
    private static readonly string[] LegacyVerbNames = ["TZIP.Extract", "TZIP.Compress", "TZIP.Open"];

    internal static X509Certificate2 LoadEmbeddedCertificate()
    {
        using Stream resource = OpenEmbeddedResource(CertificateResourceName, "CTXMN0006");
        using MemoryStream copy = new();
        resource.CopyTo(copy);
        return X509CertificateLoader.LoadCertificate(copy.ToArray());
    }

    internal static ContextMenuPresentation GetSavedMenuMode() =>
        ReadSetting("MenuMode") == "direct" ? ContextMenuPresentation.Direct : ContextMenuPresentation.Grouped;

    internal static async Task CreateAsync(string executablePath, ContextMenuPresentation menuMode,
        IProgress<ContextMenuProgress> progress, CancellationToken token)
    {
        await OperationGate.WaitAsync(token);
        string? temporaryDirectory = null;
        bool packageExisted = false;
        bool certificateOwnershipAdded = false;
        string? installedPackageFamilyName = null;
        string ownerSid = GetCurrentUserSid();
        try
        {
            Report(progress, 3, "ContextProgressValidating");
            token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                throw new StageException("CTXMN0001", LanguageManager.Get("ContextWindows11Required")); //CTXMN0001
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new StageException("CTXMN0002", LanguageManager.Get("ContextExecutableMissing")); //CTXMN0002

            packageExisted = await IsPackageInstalledAsync(token);
            if (packageExisted || HasSavedSettings() || HasLegacyMenus())
            {
                Report(progress, 10, "ContextProgressReplacingExisting");
                await RemoveExistingForCreateAsync(executablePath, packageExisted, progress, token);
                packageExisted = false;
            }
            token.ThrowIfCancellationRequested();

            Report(progress, 18, "ContextProgressExtracting");
            temporaryDirectory = await ExtractEmbeddedPackageAsync(token);
            string packagePath = Path.Combine(temporaryDirectory, "TangerineZipContextMenu.msix");

            Report(progress, 28, "ContextProgressTrustingCertificate");
            certificateOwnershipAdded = await EnsureCertificateAsync(executablePath, ownerSid, token);

            Report(progress, 46, "ContextProgressInstallingPackage");
            await InstallPackageAsync(packagePath, token);
            string packageFamilyName = await GetPackageFamilyNameAsync(token);
            installedPackageFamilyName = packageFamilyName;

            Report(progress, 72, "ContextProgressWritingCommands");
            await WriteCommandFilesAsync(packageFamilyName, executablePath, menuMode, token);

            Report(progress, 90, "ContextProgressVerifying");
            await VerifyInstallationAsync(packageFamilyName, menuMode, token);
            using X509Certificate2 installedCertificate = LoadEmbeddedCertificate();
            using (RegistryKey settings = Registry.CurrentUser.CreateSubKey(SettingsPath, writable: true)
                ?? throw new InvalidOperationException())
            {
                settings.SetValue("PackageFamilyName", packageFamilyName, RegistryValueKind.String);
                settings.SetValue("ExecutablePath", Path.GetFullPath(executablePath), RegistryValueKind.String);
                settings.SetValue("CertificateOwnerSid", HasCurrentCertificateOwnership(ownerSid) ? ownerSid : string.Empty, RegistryValueKind.String);
                settings.SetValue("CertificateThumbprint", installedCertificate.Thumbprint, RegistryValueKind.String);
                settings.SetValue("MenuMode", menuMode == ContextMenuPresentation.Direct ? "direct" : "grouped", RegistryValueKind.String);
            }
            NotifyShell();
            Report(progress, 100, "ContextProgressCompleted");
        }
        catch (OperationCanceledException)
        {
            await RollBackCreateAsync(executablePath, ownerSid, packageExisted, certificateOwnershipAdded, installedPackageFamilyName, progress);
            throw;
        }
        catch (StageException)
        {
            await RollBackCreateAsync(executablePath, ownerSid, packageExisted, certificateOwnershipAdded, installedPackageFamilyName, progress);
            throw;
        }
        catch (Exception exception)
        {
            await RollBackCreateAsync(executablePath, ownerSid, packageExisted, certificateOwnershipAdded, installedPackageFamilyName, progress);
            throw new StageException("CTXMN0003", exception.Message, exception); //CTXMN0003
        }
        finally
        {
            Exception? cleanupFailure = null;
            if (!string.IsNullOrWhiteSpace(temporaryDirectory) && Directory.Exists(temporaryDirectory))
            {
                try { Directory.Delete(temporaryDirectory, recursive: true); }
                catch (Exception exception) { cleanupFailure = exception; }
            }
            OperationGate.Release();
            if (cleanupFailure is not null)
                throw new StageException("CTXMN0015", string.Format(LanguageManager.Get("ContextTemporaryCleanupFailed"), temporaryDirectory), cleanupFailure); //CTXMN0015
        }
    }

    private static bool HasSavedSettings()
    {
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(SettingsPath);
        return settings is not null;
    }

    private static bool HasLegacyMenus()
    {
        foreach (string shellPath in LegacyShellPaths)
        {
            using RegistryKey? shell = Registry.CurrentUser.OpenSubKey(shellPath);
            foreach (string name in LegacyVerbNames)
            {
                using RegistryKey? verb = shell?.OpenSubKey(name);
                if (verb is not null) return true;
            }
        }
        return false;
    }

    // CreateAsync already holds OperationGate. Never call the public DeleteAsync from here.
    private static async Task RemoveExistingForCreateAsync(string executablePath, bool packageExisted,
        IProgress<ContextMenuProgress> progress, CancellationToken token)
    {
        Report(progress, 10, "ContextProgressRemovingLegacy");
        RemoveLegacyMenus();
        token.ThrowIfCancellationRequested();

        Report(progress, 12, "ContextProgressRemovingCommands");
        string? packageFamilyName = ReadSetting("PackageFamilyName");
        if (string.IsNullOrWhiteSpace(packageFamilyName) && packageExisted)
            packageFamilyName = await GetPackageFamilyNameAsync(token);
        if (!string.IsNullOrWhiteSpace(packageFamilyName)) DeleteCommandFiles(packageFamilyName);

        if (packageExisted)
        {
            Report(progress, 14, "ContextProgressRemovingPackage");
            await RemovePackageAsync(token);
        }
        string? ownerSid = ReadSetting("CertificateOwnerSid");
        if (!string.IsNullOrWhiteSpace(ownerSid))
        {
            Report(progress, 16, "ContextProgressRemovingCertificate");
            string thumbprint = ReadSetting("CertificateThumbprint") ?? LegacyCertificateThumbprint;
            await RunElevatedHelperAsync(executablePath, ContextMenuCertificateHelper.RemoveSwitch, ownerSid, token, thumbprint);
        }
        Registry.CurrentUser.DeleteSubKeyTree(SettingsPath, throwOnMissingSubKey: false);
        NotifyShell();
    }

    internal static async Task DeleteAsync(string executablePath, IProgress<ContextMenuProgress> progress, CancellationToken token)
    {
        await OperationGate.WaitAsync(token);
        try
        {
            List<Exception> failures = [];
            Report(progress, 5, "ContextProgressValidating");
            token.ThrowIfCancellationRequested();
            Report(progress, 18, "ContextProgressRemovingLegacy");
            try { RemoveLegacyMenus(); }
            catch (Exception exception) { failures.Add(exception); }

            Report(progress, 38, "ContextProgressRemovingCommands");
            string? packageFamilyName = ReadSetting("PackageFamilyName");
            if (string.IsNullOrWhiteSpace(packageFamilyName) && await IsPackageInstalledAsync(token))
                packageFamilyName = await GetPackageFamilyNameAsync(token);
            try
            {
                if (!string.IsNullOrWhiteSpace(packageFamilyName)) DeleteCommandFiles(packageFamilyName);
            }
            catch (Exception exception) { failures.Add(exception); }
            token.ThrowIfCancellationRequested();

            Report(progress, 58, "ContextProgressRemovingPackage");
            bool packageRemoved = false;
            try { await RemovePackageAsync(token); packageRemoved = true; }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { failures.Add(exception); }

            string? ownerSid = ReadSetting("CertificateOwnerSid");
            bool certificateRemoved = string.IsNullOrWhiteSpace(ownerSid);
            if (!string.IsNullOrWhiteSpace(ownerSid))
            {
                Report(progress, 78, "ContextProgressRemovingCertificate");
                string thumbprint = ReadSetting("CertificateThumbprint") ?? LegacyCertificateThumbprint;
                try { await RunElevatedHelperAsync(executablePath, ContextMenuCertificateHelper.RemoveSwitch, ownerSid, token, thumbprint); certificateRemoved = true; }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { failures.Add(exception); }
            }

            Report(progress, 92, "ContextProgressCleaningSettings");
            if (packageRemoved && certificateRemoved)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(SettingsPath, throwOnMissingSubKey: false); }
                catch (Exception exception) { failures.Add(exception); }
            }
            NotifyShell();
            if (failures.Count > 0)
                throw new StageException("CTXMN0004", string.Join(Environment.NewLine, failures.Select(item => item.Message)), new AggregateException(failures)); //CTXMN0004
            Report(progress, 100, "ContextProgressDeleted");
        }
        catch (OperationCanceledException) { throw; }
        catch (StageException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("CTXMN0004", exception.Message, exception); //CTXMN0004
        }
        finally
        {
            OperationGate.Release();
        }
    }

    private static async Task<bool> EnsureCertificateAsync(string executablePath, string ownerSid, CancellationToken token)
    {
        using X509Certificate2 certificate = LoadEmbeddedCertificate();
        using X509Store store = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        bool trusted = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count > 0;
        bool alreadyOwned = false;
        using (RegistryKey? ownership = Registry.LocalMachine.OpenSubKey($@"{ContextMenuCertificateHelper.OwnershipPath}\{certificate.Thumbprint}"))
        {
            string[] owners = ownership?.GetValue("OwnerSids") as string[] ?? [];
            alreadyOwned = owners.Contains(ownerSid, StringComparer.OrdinalIgnoreCase);
        }
        if (trusted && alreadyOwned) return false;
        int exitCode = await RunElevatedHelperAsync(executablePath, ContextMenuCertificateHelper.InstallSwitch, ownerSid, token);
        return exitCode == 0 && !alreadyOwned;
    }

    private static bool HasCurrentCertificateOwnership(string ownerSid)
    {
        using X509Certificate2 certificate = LoadEmbeddedCertificate();
        using RegistryKey? ownership = Registry.LocalMachine.OpenSubKey(
            $@"{ContextMenuCertificateHelper.OwnershipPath}\{certificate.Thumbprint}");
        return ownership?.GetValue("OwnerSids") is string[] owners
            && owners.Contains(ownerSid, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task RollBackCreateAsync(string executablePath, string ownerSid, bool packageExisted,
        bool certificateOwnershipAdded, string? installedPackageFamilyName, IProgress<ContextMenuProgress> progress)
    {
        List<Exception> failures = [];
        Report(progress, 0, "ContextProgressRollingBack");
        if (!string.IsNullOrWhiteSpace(installedPackageFamilyName))
        {
            try { DeleteCommandFiles(installedPackageFamilyName); }
            catch (Exception exception) { failures.Add(exception); }
            try { Registry.CurrentUser.DeleteSubKeyTree(SettingsPath, throwOnMissingSubKey: false); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (!packageExisted)
        {
            try { await RemovePackageAsync(CancellationToken.None); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (certificateOwnershipAdded)
        {
            try { await RunElevatedHelperAsync(executablePath, ContextMenuCertificateHelper.RemoveSwitch, ownerSid, CancellationToken.None); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0)
            throw new StageException("CTXMN0014", string.Join(Environment.NewLine, failures.Select(item => item.Message)), new AggregateException(failures)); //CTXMN0014
    }

    private static async Task<string> ExtractEmbeddedPackageAsync(CancellationToken token)
    {
        string directory = Path.Combine(Path.GetTempPath(), "TangerineZip", "ContextMenu", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string packagePath = Path.Combine(directory, "TangerineZipContextMenu.msix");
        await using Stream source = OpenEmbeddedResource(PackageResourceName, "CTXMN0005");
        await using FileStream destination = new(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
        await source.CopyToAsync(destination, token);
        return directory;
    }

    private static Stream OpenEmbeddedResource(string resourceName, string stageCode) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new StageException(stageCode, LanguageManager.Get("ContextWizardEmbeddedMissing")); //CTXMN0005

    private static async Task WriteCommandFilesAsync(string packageFamilyName, string executablePath,
        ContextMenuPresentation menuMode, CancellationToken token)
    {
        string menuDirectory = GetCommandDirectory(packageFamilyName);
        Directory.CreateDirectory(menuDirectory);
        foreach (string existingFile in Directory.EnumerateFiles(menuDirectory, "TZIP-*.json", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            File.Delete(existingFile);
        }

        string fullExecutablePath = Path.GetFullPath(executablePath);
        object[] definitions =
        [
            CreateCommand(LanguageManager.Get("ContextExtractMenu"), LanguageManager.Get("ContextExtractSingleMenu"), 10, fullExecutablePath, "--context-extract", allowMultiple: true),
            CreateCommand(LanguageManager.Get("ContextCompressMenu"), LanguageManager.Get("ContextCompressMenu"), 20, fullExecutablePath, "--context-compress", allowMultiple: true),
            CreateCommand(LanguageManager.Get("ContextOpenMenu"), LanguageManager.Get("ContextOpenSingleMenu"), 30, fullExecutablePath, "--context-open", allowMultiple: true)
        ];
        string[] names = ["TZIP-Extract.json", "TZIP-Compress.json", "TZIP-Open.json"];
        for (int index = 0; index < definitions.Length; index++)
        {
            string destinationPath = Path.Combine(menuDirectory, names[index]);
            string temporaryPath = destinationPath + ".tmp";
            string json = JsonSerializer.Serialize(definitions[index], new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), token);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        string modePath = Path.Combine(menuDirectory, "TZIP-mode.txt");
        await File.WriteAllTextAsync(modePath + ".tmp",
            menuMode == ContextMenuPresentation.Direct ? "direct" : "grouped", new UTF8Encoding(false), token);
        File.Move(modePath + ".tmp", modePath, overwrite: true);
    }

    private static object CreateCommand(string title, string titleSingle, int index, string executablePath, string action, bool allowMultiple) => new
    {
        title,
        titleSingle,
        index,
        exe = executablePath,
        param = $"{action} \"{{path}}\"",
        icon = $"{executablePath},0",
        acceptDirectoryFlag = 0,
        acceptFileFlag = 4,
        acceptExts = "*",
        acceptFileRegex = string.Empty,
        acceptMultipleFilesFlag = allowMultiple ? 2 : 0,
        acceptMultipleFilesRuleFlag = 2,
        pathDelimiter = "\" \"",
        paramForMultipleFiles = $"{action} \"{{path}}\"",
        showWindowFlag = 0,
        runAsFlag = 0,
        workingDirectory = "{parent}"
    };

    private static async Task VerifyInstallationAsync(string packageFamilyName,
        ContextMenuPresentation menuMode, CancellationToken token)
    {
        if (!await IsCurrentPackageInstalledAsync(token))
            throw new StageException("CTXMN0007", LanguageManager.Get("ContextRegistrationMismatch")); //CTXMN0007
        string commandDirectory = GetCommandDirectory(packageFamilyName);
        if (Directory.EnumerateFiles(commandDirectory, "TZIP-*.json").Count() != 3)
            throw new StageException("CTXMN0008", LanguageManager.Get("ContextRegistrationMismatch")); //CTXMN0008
        string savedMode = await File.ReadAllTextAsync(Path.Combine(commandDirectory, "TZIP-mode.txt"), token);
        if (savedMode != (menuMode == ContextMenuPresentation.Direct ? "direct" : "grouped"))
            throw new StageException("CTXMN0008", LanguageManager.Get("ContextRegistrationMismatch")); //CTXMN0008
    }

    private static string GetCommandDirectory(string packageFamilyName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", packageFamilyName, "LocalState", "custom_commands");

    private static void DeleteCommandFiles(string packageFamilyName)
    {
        string commandDirectory = GetCommandDirectory(packageFamilyName);
        if (!Directory.Exists(commandDirectory)) return;
        foreach (string file in Directory.EnumerateFiles(commandDirectory, "TZIP-*.json", SearchOption.TopDirectoryOnly)) File.Delete(file);
        foreach (string file in Directory.EnumerateFiles(commandDirectory, "TZIP-*.json.tmp", SearchOption.TopDirectoryOnly)) File.Delete(file);
        string modePath = Path.Combine(commandDirectory, "TZIP-mode.txt");
        if (File.Exists(modePath)) File.Delete(modePath);
        if (File.Exists(modePath + ".tmp")) File.Delete(modePath + ".tmp");
    }

    private static async Task InstallPackageAsync(string packagePath, CancellationToken token)
    {
        _ = await RunPowerShellAsync(
            "Add-AppxPackage -Path $env:TZIP_CONTEXT_ARGUMENT -ForceTargetApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop",
            packagePath, "CTXMN0009", token); //CTXMN0009
    }

    private static async Task RemovePackageAsync(CancellationToken token)
    {
        _ = await RunPowerShellAsync(
            "$items = @(Get-AppxPackage -Name $env:TZIP_CONTEXT_ARGUMENT -ErrorAction Stop); foreach ($item in $items) { Remove-AppxPackage -Package $item.PackageFullName -ErrorAction Stop }",
            PackageName, "CTXMN0010", token); //CTXMN0010
    }

    private static async Task<bool> IsPackageInstalledAsync(CancellationToken token)
    {
        string output = await RunPowerShellAsync(
            "$item = Get-AppxPackage -Name $env:TZIP_CONTEXT_ARGUMENT -ErrorAction SilentlyContinue | Select-Object -First 1; if ($null -ne $item) { [Console]::Out.Write('yes') }",
            PackageName, "CTXMN0011", token);
        return output.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsCurrentPackageInstalledAsync(CancellationToken token)
    {
        string output = await RunPowerShellAsync(
            "$item = Get-AppxPackage -Name $env:TZIP_CONTEXT_ARGUMENT -ErrorAction SilentlyContinue | Sort-Object Version -Descending | Select-Object -First 1; if ($null -ne $item -and [version]$item.Version -ge [version]'" + MinimumPackageVersion + "') { [Console]::Out.Write('yes') }",
            PackageName, "CTXMN0011", token);
        return output.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetPackageFamilyNameAsync(CancellationToken token)
    {
        string output = await RunPowerShellAsync(
            "$item = Get-AppxPackage -Name $env:TZIP_CONTEXT_ARGUMENT -ErrorAction Stop | Sort-Object Version -Descending | Select-Object -First 1; if ($null -eq $item) { throw 'Package not found.' }; [Console]::Out.Write($item.PackageFamilyName)",
            PackageName, "CTXMN0012", token);
        string value = output.Trim();
        if (string.IsNullOrWhiteSpace(value)) throw new StageException("CTXMN0012", LanguageManager.Get("ContextRegistrationMismatch")); //CTXMN0012
        return value;
    }

    private static async Task<string> RunPowerShellAsync(string command, string argument, string stageCode, CancellationToken token)
    {
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string powerShellPath = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        string wrappedCommand = "$ErrorActionPreference='Stop'; $OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); " + command;
        ProcessStartInfo startInfo = new(powerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["TZIP_CONTEXT_ARGUMENT"] = argument;
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedCommand)));
        using Process process = Process.Start(startInfo) ?? throw new StageException(stageCode, LanguageManager.Get("ContextProcessStartFailed")); //CTXMN0013
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(token);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        string output = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0)
            throw new StageException(stageCode, string.IsNullOrWhiteSpace(error) ? LanguageManager.Get("ContextProcessFailed") : error.Trim()); //CTXMN0013
        return output;
    }

    private static async Task<int> RunElevatedHelperAsync(string executablePath, string action, string ownerSid,
        CancellationToken token, string? certificateThumbprint = null)
    {
        try
        {
            ProcessStartInfo startInfo = new(executablePath) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            startInfo.ArgumentList.Add(action);
            startInfo.ArgumentList.Add(ownerSid);
            if (!string.IsNullOrWhiteSpace(certificateThumbprint)) startInfo.ArgumentList.Add(certificateThumbprint);
            using Process process = Process.Start(startInfo) ?? throw new StageException("CTXMN0016", LanguageManager.Get("ContextMenuElevationFailed")); //CTXMN0016
            try { await process.WaitForExitAsync(token); }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            if (process.ExitCode is not (0 or ContextMenuCertificateHelper.CertificatePreexisting or ContextMenuCertificateHelper.CertificateShared))
                throw new StageException("CTXMN0016", LanguageManager.Get("ContextMenuElevationFailed")); //CTXMN0016
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new StageException("CTXMN0017", LanguageManager.Get("ContextMenuElevationCancelled"), exception); //CTXMN0017
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception) { /* WaitForExit surfaces whether cancellation completed. */ }
    }

    private static void RemoveLegacyMenus()
    {
        List<Exception> failures = [];
        foreach (string shellPath in LegacyShellPaths)
        {
            try
            {
                using RegistryKey? shellKey = Registry.CurrentUser.OpenSubKey(shellPath, writable: true);
                foreach (string verbName in LegacyVerbNames) shellKey?.DeleteSubKeyTree(verbName, throwOnMissingSubKey: false);
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0)
            throw new StageException("CTXMN0018", string.Join(Environment.NewLine, failures.Select(item => item.Message)), new AggregateException(failures)); //CTXMN0018
    }

    private static string GetCurrentUserSid() => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new StageException("CTXMN0019", LanguageManager.Get("ContextUserIdentityMissing")); //CTXMN0019

    private static string? ReadSetting(string name)
    {
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(SettingsPath, writable: false);
        return settings?.GetValue(name) as string;
    }

    private static void Report(IProgress<ContextMenuProgress> progress, int percentage, string resourceKey, string? detail = null) =>
        progress.Report(new ContextMenuProgress(percentage, resourceKey, detail));

    private static void NotifyShell() => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
