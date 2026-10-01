// Hash codec: cssxn/UserChoiceLatestHash (MIT), adapted through the native
// read-only export in third_party/UserChoiceLatestHash/TzipHashExport.cpp.
// Windows does not document this association format. Every write is gated by
// an existing-hash check (when present) and an effective Shell query afterward.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TANGERINE_ZIP.Services;

// Stage head: LUCAS (LatestUserChoiceAssociationService).
internal static class LatestUserChoice
{
    private const string FileExtsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\";
    private const string AppDefaultsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SystemProtectedUserData\";
    private const string LatestName = "UserChoiceLatest";
    private const string Salt0 = "Copyright (C) Microsoft. All rights reserved {3822B7CA-C2F4-4889-B8CC-4CE39A8FB81C}";
    private const string Salt1 = "Copyright (C) Microsoft. All rights reserved {D185E0A1-E265-4724-AA21-3A17B038D72E}";
    private const string Salt2 = "Copyright (C) Microsoft. All rights reserved {97B6BCF4-C367-4577-95BE-73BD3053A5E0}";
    private const string NativeResource = "TANGERINE_ZIP.TzipLatestHash.dll";
    private static readonly Lazy<ComputeHashDelegate> NativeHash = new(LoadNativeHash);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ComputeHashDelegate([MarshalAs(UnmanagedType.LPWStr)] string input,
        StringBuilder output, uint outputChars);

    internal static bool IsLatestFormatActive(string extension)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string? sid = identity.User?.Value;
            if (sid == null) return false;
            using RegistryKey? baseKey = Registry.CurrentUser.OpenSubKey(FileExtsPath + extension);
            if (baseKey?.OpenSubKey(LatestName) is RegistryKey latest)
            {
                latest.Dispose();
                return true;
            }
            // Once this per-user machine setting is 1, Windows ignores the older
            // UserChoice hash even for extensions that have no Latest key yet.
            using RegistryKey? settings = Registry.LocalMachine.OpenSubKey(
                AppDefaultsPath + sid + @"\AnyoneRead\AppDefaults");
            if (settings?.GetValue("HashVersion") is not int version) return false;
            if (version == 1) return true;
            if (version == 0) return false;
            throw new StageException("LUCAS0002", LanguageManager.Get("DefaultAppsLatestUnsupported")); //LUCAS0002
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new StageException("LUCAS0002", LanguageManager.Get("DefaultAppsLatestUnsupported"), error); //LUCAS0002
        }
    }

    internal static void SetDefault(string extension, string progId, Func<string?> queryDefault)
    {
        if (!string.Equals(DefaultAppAssociationService.GetProgId(extension), progId,
                StringComparison.OrdinalIgnoreCase))
            throw new StageException("LUCAS0002", LanguageManager.Get("DefaultAppsLatestUnsupported")); //LUCAS0002
        if (!IsLatestFormatActive(extension))
            throw new StageException("LUCAS0002", LanguageManager.Get("DefaultAppsLatestUnsupported")); //LUCAS0002
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value
            ?? throw new StageException("LUCAS0001", LanguageManager.Get("DefaultAppsLatestHashFailed")); //LUCAS0001
        string machineId = ReadMachineId();
        using Mutex gate = new(false, @"Local\TangerineZip.DefaultApps." + sid);
        bool acquired = false;
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
                throw new StageException("LUCAS0006", LanguageManager.Get("DefaultAppsRegistrationBusy"), error); //LUCAS0006
            }

            using RegistryKey association = Registry.CurrentUser.CreateSubKey(FileExtsPath + extension, true)
                ?? throw new IOException("FileExts association key unavailable.");
            // A process terminated between the two renames may leave the old
            // valid key under our backup name and no active Latest key. Repair
            // that precise state before starting another transaction.
            RecoverInterruptedRename(association);
            using (RegistryKey? existing = association.OpenSubKey(LatestName))
            {
                if (existing != null && !ValidateExisting(existing, extension, sid, machineId))
                    throw new StageException("LUCAS0002", LanguageManager.Get("DefaultAppsLatestUnsupported")); //LUCAS0002
            }

            // Build a complete, correctly hashed sibling first. A rename changes
            // the parent but preserves the nested ProgId key's write timestamp.
            // This avoids exposing a half-written UserChoiceLatest to Explorer.
            string transaction = Guid.NewGuid().ToString("N");
            string pending = "TzipPending" + transaction;
            string previous = "TzipPrevious" + transaction;
            string rejected = "TzipRejected" + transaction;
            bool movedPrevious = false;
            bool movedPending = false;
            bool hadPrevious;
            using (RegistryKey? prior = association.OpenSubKey(LatestName))
                hadPrevious = prior is not null;
            try
            {
                using (RegistryKey target = association.CreateSubKey(pending, true)
                    ?? throw new IOException("Pending association key unavailable."))
                using (RegistryKey nested = target.CreateSubKey("ProgId", true)
                    ?? throw new IOException("Pending ProgId key unavailable."))
                {
                    nested.SetValue("ProgId", progId, RegistryValueKind.String);
                    string hash = ComputeHash(extension, sid, machineId, progId, LastWriteFileTime(nested));
                    target.SetValue("Hash", hash, RegistryValueKind.String);
                    if (!ValidateExisting(target, extension, sid, machineId))
                        throw new IOException("Pending hash did not match its key timestamp.");
                }

                if (hadPrevious)
                {
                    Rename(association, LatestName, previous);
                    movedPrevious = true;
                }
                Rename(association, pending, LatestName);
                movedPending = true;
                NotifyShell();
                VerifyDefault(progId, queryDefault);
                using (RegistryKey? installed = association.OpenSubKey(LatestName))
                    if (installed == null || !ValidateExisting(installed, extension, sid, machineId))
                        throw new StageException("LUCAS0004", LanguageManager.Get("DefaultAppsLatestRejected")); //LUCAS0004

                // The old choice is no longer active. A cleanup failure leaves a
                // private backup but must not misreport a verified association.
                TryDelete(association, previous);
            }
            catch (Exception operationError)
            {
                try
                {
                    if (movedPending)
                    {
                        Rename(association, LatestName, rejected);
                        movedPending = false;
                    }
                    if (movedPrevious)
                    {
                        Rename(association, previous, LatestName);
                        movedPrevious = false;
                    }
                    TryDelete(association, rejected);
                    NotifyShell();
                }
                catch (Exception rollbackError)
                {
                    throw new StageException("LUCAS0005", LanguageManager.Get("DefaultAppsLatestRollbackFailed"),
                        new AggregateException(operationError, rollbackError)); //LUCAS0005
                }
                if (operationError is StageException) throw;
                throw new StageException("LUCAS0003", LanguageManager.Get("DefaultAppsLatestWriteFailed"), operationError); //LUCAS0003
            }
            finally { TryDelete(association, pending); }
        }
        catch (Exception error) when (error is not StageException)
        {
            throw new StageException("LUCAS0003", LanguageManager.Get("DefaultAppsLatestWriteFailed"), error); //LUCAS0003
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }

    private static bool ValidateExisting(RegistryKey choice, string extension, string sid, string machineId)
    {
        using RegistryKey? nested = choice.OpenSubKey("ProgId");
        string? progId = nested?.GetValue("ProgId") as string;
        string? savedHash = choice.GetValue("Hash") as string;
        return nested != null && !string.IsNullOrEmpty(progId) && !string.IsNullOrEmpty(savedHash) &&
            string.Equals(savedHash, ComputeHash(extension, sid, machineId, progId, LastWriteFileTime(nested)),
                StringComparison.Ordinal);
    }

    private static void RecoverInterruptedRename(RegistryKey association)
    {
        using RegistryKey? current = association.OpenSubKey(LatestName);
        if (current != null) return;
        string[] backups = association.GetSubKeyNames()
            .Where(name => name.StartsWith("TzipPrevious", StringComparison.Ordinal) && name.Length == 44)
            .ToArray();
        if (backups.Length == 0) return;
        if (backups.Length != 1)
            throw new StageException("LUCAS0005", LanguageManager.Get("DefaultAppsLatestRollbackFailed")); //LUCAS0005
        try
        {
            Rename(association, backups[0], LatestName);
            NotifyShell();
        }
        catch (Exception error)
        {
            throw new StageException("LUCAS0005", LanguageManager.Get("DefaultAppsLatestRollbackFailed"), error); //LUCAS0005
        }
    }

    private static string ComputeHash(string extension, string sid, string machineId,
        string progId, long nestedFileTime)
    {
        // Windows' SYSTEMTIME round-trip keeps milliseconds and discards the
        // remaining 100 ns ticks before rendering two lowercase hex DWORDs.
        long normalized = nestedFileTime - nestedFileTime % TimeSpan.TicksPerMillisecond;
        string time = normalized.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        string canonical = (machineId[^1] % 3) switch
        {
            0 => Salt0 + extension + time + machineId + progId + sid,
            1 => Salt1 + time + extension + sid + machineId + progId,
            _ => sid + time + Salt2 + extension + machineId + progId
        };
        try
        {
            StringBuilder output = new(32);
            if (NativeHash.Value(canonical, output, 32) != 1 || output.Length != 12)
                throw new InvalidDataException("Native UserChoiceLatest codec rejected its input.");
            return output.ToString();
        }
        catch (Exception error)
        {
            throw new StageException("LUCAS0001", LanguageManager.Get("DefaultAppsLatestHashFailed"), error); //LUCAS0001
        }
    }

    private static string ReadMachineId()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SQMClient");
            string? value = key?.GetValue("MachineID") as string;
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("SQMClient MachineID is missing.");
            string machineId = value.Trim('{', '}');
            if (machineId.Length == 0)
                throw new InvalidDataException("SQMClient MachineID is empty.");
            return machineId;
        }
        catch (Exception error)
        {
            throw new StageException("LUCAS0001", LanguageManager.Get("DefaultAppsLatestHashFailed"), error); //LUCAS0001
        }
    }

    private static ComputeHashDelegate LoadNativeHash()
    {
        using Stream source = typeof(LatestUserChoice).Assembly.GetManifestResourceStream(NativeResource)
            ?? throw new FileNotFoundException("Embedded UserChoiceLatest codec missing.");
        using MemoryStream memory = new();
        source.CopyTo(memory);
        byte[] data = memory.ToArray();
        string fingerprint = Convert.ToHexString(SHA256.HashData(data));
        string directory = Path.Combine(Path.GetTempPath(), "TangerineZip", "NativeHash");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fingerprint + ".dll");
        if (!File.Exists(path))
        {
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, data);
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (!SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(SHA256.HashData(data)))
            throw new InvalidDataException("Extracted hash codec failed its embedded SHA-256 check.");
        IntPtr module = NativeLibrary.Load(path);
        IntPtr export = NativeLibrary.GetExport(module, "TzipComputeLatestHash");
        return Marshal.GetDelegateForFunctionPointer<ComputeHashDelegate>(export);
    }

    private static long LastWriteFileTime(RegistryKey key)
    {
        int error = RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, out long fileTime);
        if (error != 0) throw new Win32Exception(error);
        return fileTime;
    }

    private static void Rename(RegistryKey parent, string oldName, string newName)
    {
        int error = RegRenameKey(parent.Handle, oldName, newName);
        if (error != 0) throw new Win32Exception(error, $"Registry rename {oldName} failed.");
    }

    private static void TryDelete(RegistryKey parent, string name)
    {
        try { parent.DeleteSubKeyTree(name, false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
    }

    private static void VerifyDefault(string progId, Func<string?> queryDefault)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (string.Equals(queryDefault(), progId, StringComparison.OrdinalIgnoreCase)) return;
            Thread.Sleep(120);
        }
        throw new StageException("LUCAS0004", LanguageManager.Get("DefaultAppsLatestRejected")); //LUCAS0004
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW", ExactSpelling = true)]
    private static extern int RegQueryInfoKey(SafeRegistryHandle key,
        IntPtr className, IntPtr classLength, IntPtr reserved, IntPtr subkeys, IntPtr maxSubkeyLength,
        IntPtr maxClassLength, IntPtr values, IntPtr maxValueNameLength, IntPtr maxValueLength,
        IntPtr securityDescriptorLength, out long lastWriteTime);

    [DllImport("advapi32.dll", EntryPoint = "RegRenameKey", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegRenameKey(SafeRegistryHandle key, string oldName, string newName);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
