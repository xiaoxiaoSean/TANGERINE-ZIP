// Adapted from SFTA 1.3.1 by Danyfirex & Dany3j (Danysys).
// Copyright (c) 2020 Danysys.com. MIT license: third_party/SFTA/LICENSE.
// The hash routines originate from LMongrain, as credited by upstream SFTA.
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace TANGERINE_ZIP.Services;

// Stage head: SFTAS. This is an explicitly requested, unofficial current-user
// association implementation, not a supported Windows default-app API. It never
// disables a protection driver, elevates, changes policies, or starts a process.
internal static class SftaUserChoice
{
    private const string FileExtsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\";
    private sealed record SavedValue(string Name, object Value, RegistryValueKind Kind);

    // Keep the arithmetic structurally aligned with SFTA.pb Hash1/Hash2. PureBasic
    // longs wrap at 32 bits and Shr32 is unsigned; C# must use uint and unchecked.
    internal static string GenerateHash(string extension, string sid, string progId,
        DateTime timestampUtc, string experience)
    {
        DateTime minute = new(timestampUtc.Year, timestampUtc.Month, timestampUtc.Day,
            timestampUtc.Hour, timestampUtc.Minute, 0, DateTimeKind.Utc);
        string input = (extension + sid + progId + minute.ToFileTimeUtc().ToString("x16") + experience).ToLowerInvariant();
        // Both MD5 and the scrambler include the terminating UTF-16 NUL. Ignore
        // the incomplete final eight-byte block, exactly as upstream GenerateHash.
        byte[] bytes = Encoding.Unicode.GetBytes(input + '\0');
        byte[] md5 = MD5.HashData(bytes);
        uint m1 = BinaryPrimitives.ReadUInt32LittleEndian(md5) | 1;
        uint m2 = BinaryPrimitives.ReadUInt32LittleEndian(md5.AsSpan(4)) | 1;
        uint h1 = 0, sum1 = 0, h2 = 0, sum2 = 0;
        unchecked
        {
            for (int offset = 0; offset + 8 <= bytes.Length; offset += 8)
            {
                uint first = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                uint second = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
                uint a = first + h1;
                uint b = a * (m1 + 0x69FB0000) - 0x10FA9605 * (a >> 16);
                uint c = 0x79F8A395 * b + 0x689B6B9F * (b >> 16);
                uint d = 0xEA970001 * c - 0x3C101569 * (c >> 16);
                uint e = d + second;
                uint f = e * (m2 + 0x13DB0000) - 0x3CE8EC25 * (e >> 16);
                uint g = 0x59C3AF2D * f - 0x2232E0F1 * (f >> 16);
                h1 = 0x1EC90001 * g + 0x35BD1EC9 * (g >> 16);
                sum1 += d + h1;

                a = (first + h2) * m1;
                b = 0xB1110000 * a - 0x30674EEF * (a >> 16);
                c = 0x5B9F0000 * b - 0x78F7A461 * (b >> 16);
                d = 0x12CEB96D * (c >> 16) - 0x46930000 * c;
                e = 0x1D830000 * d + 0x257E1D83 * (d >> 16);
                f = m2 * (e + second);
                g = 0x16F50000 * f - 0x5D8BE90B * (f >> 16);
                uint j = 0x96FF0000 * g - 0x2C7C6901 * (g >> 16);
                uint k = 0x2B890000 * j + 0x7C932B89 * (j >> 16);
                h2 = 0x9F690000 * k - 0x405B6097 * (k >> 16);
                sum2 += h2 + e;
            }
        }
        byte[] result = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(result, h1 ^ h2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), sum1 ^ sum2);
        return Convert.ToBase64String(result);
    }

    internal static string ReadExperience()
    {
        try
        {
            // Upstream discovers this text in Shell32 rather than assuming it is
            // stable forever. Scan a bounded file; require a terminated GUID string.
            string path = Path.Combine(Environment.SystemDirectory, "shell32.dll");
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException(path);
            byte[] bytes = File.ReadAllBytes(path);
            byte[] prefix = Encoding.Unicode.GetBytes("User Choice set via Windows User Experience {");
            int start = bytes.AsSpan().IndexOf(prefix);
            if (start < 0) throw new InvalidDataException("Shell32 User Experience string not found.");
            for (int end = start + prefix.Length; end + 3 < bytes.Length && end - start < 1024; end += 2)
                if (bytes[end] == '}' && bytes[end + 1] == 0 && bytes[end + 2] == 0 && bytes[end + 3] == 0)
                    return Encoding.Unicode.GetString(bytes, start, end + 2 - start);
            throw new InvalidDataException("Shell32 User Experience string is not terminated.");
        }
        catch (Exception exception)
        {
            throw new StageException("SFTAS0001", LanguageManager.Get("DefaultAppsHashFailed"), exception); //SFTAS0001
        }
    }

    private static DateTime LastWriteUtc(RegistryKey key)
    {
        int error = RegQueryInfoKey(key.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out long fileTime);
        if (error != 0) throw new Win32Exception(error);
        return DateTime.FromFileTimeUtc(fileTime);
    }

    // Only DefaultAppAssociationService exposes the product-facing entry point,
    // which validates the archive whitelist and fixes the target to our ProgID.
    // Explicit arguments here also permit tests on unique disposable extensions.
    internal static void SetDefault(string extension, string progId, Func<string?> queryDefault)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063) ||
            !extension.StartsWith('.') || extension.Length > 128 ||
            extension.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-'))
            throw new StageException("SFTAS0002", LanguageManager.Get("DefaultAppsAutomaticUnsupported")); //SFTAS0002
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new StageException("SFTAS0001", LanguageManager.Get("DefaultAppsHashFailed")); //SFTAS0001
        using Mutex gate = new(false, @"Local\TangerineZip.DefaultApps." + sid);
        bool acquired = false;
        try
        {
            try
            {
                try { acquired = gate.WaitOne(TimeSpan.FromSeconds(5)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new TimeoutException("SFTA association mutex timed out.");
            }
            catch (Exception exception)
            {
                throw new StageException("SFTAS0006", LanguageManager.Get("DefaultAppsRegistrationBusy"), exception); //SFTAS0006
            }
            string experience = ReadExperience();
            using RegistryKey association = Registry.CurrentUser.CreateSubKey(FileExtsPath + extension, true)
                ?? throw new IOException("Association key unavailable.");
            // Do not replace a newer hash format. A registry write might succeed
            // while Windows ignores it, so detect known incompatible storage first.
            using (RegistryKey? latest = association.OpenSubKey("UserChoiceLatest"))
                if (latest != null) throw new StageException("SFTAS0002", LanguageManager.Get("DefaultAppsAutomaticUnsupported")); //SFTAS0002
            List<SavedValue> saved = [];
            bool existed;
            string? previousProgId;
            using (RegistryKey? choice = association.OpenSubKey("UserChoice"))
            {
                existed = choice != null;
                previousProgId = choice?.GetValue("ProgId") as string;
                if (choice != null)
                {
                    foreach (string name in choice.GetValueNames())
                        saved.Add(new(name, choice.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                            ?? throw new InvalidDataException(name), choice.GetValueKind(name)));
                    string? oldHash = choice.GetValue("Hash") as string;
                    if (previousProgId == null || oldHash == null || oldHash != GenerateHash(extension, sid,
                        previousProgId, LastWriteUtc(choice), experience))
                        throw new StageException("SFTAS0002", LanguageManager.Get("DefaultAppsAutomaticUnsupported")); //SFTAS0002
                }
            }
            bool touched = false;
            try
            {
                // RegDeleteKey can delete a UserChoice with Deny SetValue, while
                // recursive deletion cannot. Delete only this subkey, never parents.
                association.DeleteSubKey("UserChoice", false);
                touched = true;
                WriteChoice(association, extension, progId, sid, experience, []);
                NotifyShell();
                VerifyDefault(progId, queryDefault);
            }
            catch (Exception operationError)
            {
                if (touched)
                {
                    try
                    {
                        // Avoid overwriting a concurrent choice made by another app.
                        using (RegistryKey? current = association.OpenSubKey("UserChoice"))
                            if (current?.GetValue("ProgId") is string now && now != progId)
                                throw new IOException("Another process changed the default during rollback.");
                        association.DeleteSubKey("UserChoice", false);
                        if (existed)
                        {
                            // Restoring the old Hash verbatim would be invalid: key
                            // last-write time has changed. Regenerate for the old app,
                            // preserving other value names, data and registry types.
                            WriteChoice(association, extension, previousProgId!, sid, experience, saved);
                        }
                        NotifyShell();
                        if (existed) VerifyDefault(previousProgId!, queryDefault);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new StageException("SFTAS0005", LanguageManager.Get("DefaultAppsAutomaticRollbackFailed"),
                            new AggregateException(operationError, rollbackError)); //SFTAS0005
                    }
                }
                if (operationError is StageException) throw;
                throw new StageException("SFTAS0003", LanguageManager.Get("DefaultAppsAutomaticWriteFailed"), operationError); //SFTAS0003
            }
        }
        catch (Exception exception) when (exception is not StageException)
        {
            throw new StageException("SFTAS0003", LanguageManager.Get("DefaultAppsAutomaticWriteFailed"), exception); //SFTAS0003
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }

    private static void WriteChoice(RegistryKey association, string extension, string progId,
        string sid, string experience, IReadOnlyList<SavedValue> extra)
    {
        // A minute boundary invalidates UserChoice hashes. Wait only on the worker
        // thread and verify against the actual registry timestamp, not just our clock.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            DateTime now = DateTime.UtcNow;
            if (now.Second == 59) { Thread.Sleep(1100 - now.Millisecond); now = DateTime.UtcNow; }
            string hash = GenerateHash(extension, sid, progId, now, experience);
            using RegistryKey choice = association.CreateSubKey("UserChoice", true)
                ?? throw new IOException("UserChoice key unavailable.");
            foreach (SavedValue value in extra.Where(v =>
                !v.Name.Equals("Hash", StringComparison.OrdinalIgnoreCase) && !v.Name.Equals("ProgId", StringComparison.OrdinalIgnoreCase)))
                choice.SetValue(value.Name, value.Value, value.Kind);
            choice.SetValue("ProgId", progId, RegistryValueKind.String);
            choice.SetValue("Hash", hash, RegistryValueKind.String);
            if (!Equals(choice.GetValue("ProgId"), progId) || !Equals(choice.GetValue("Hash"), hash))
                throw new IOException("UserChoice readback mismatch.");
            if (hash == GenerateHash(extension, sid, progId, LastWriteUtc(choice), experience)) return;
        }
        throw new IOException("UserChoice hash timestamp did not stabilize.");
    }

    private static void VerifyDefault(string progId, Func<string?> queryDefault)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (string.Equals(queryDefault(), progId, StringComparison.OrdinalIgnoreCase)) return;
            Thread.Sleep(100);
        }
        throw new StageException("SFTAS0004", LanguageManager.Get("DefaultAppsAutomaticRejected")); //SFTAS0004
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW", ExactSpelling = true)]
    private static extern int RegQueryInfoKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle key,
        IntPtr className, IntPtr classLength, IntPtr reserved, IntPtr subkeys, IntPtr maxSubkeyLength,
        IntPtr maxClassLength, IntPtr values, IntPtr maxValueNameLength, IntPtr maxValueLength,
        IntPtr securityDescriptorLength, out long lastWriteTime);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
