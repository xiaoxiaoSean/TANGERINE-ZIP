using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TANGERINE_ZIP.Services;

// Stage head: PWVLT. Secrets are stored as per-user generic credentials in
// Windows Credential Manager. No password is written to an app configuration
// file, and all native buffers are released after each operation.
internal static class PasswordVaultService
{
    private const string Prefix = "TANGERINE-ZIP:ArchivePassword:";
    private const int GenericCredential = 1;
    private const int LocalMachinePersistence = 2;
    private const int NotFound = 1168;
    private const int NoLogonSession = 1312;
    private const int MaxBlobBytes = 5 * 512;

    public static IReadOnlyList<string> ListNames()
    {
        IntPtr result = IntPtr.Zero;
        try
        {
            if (!CredEnumerate(Prefix + "*", 0, out int count, out result))
            {
                if (Marshal.GetLastWin32Error() == NotFound) return [];
                throw CredentialError(Marshal.GetLastWin32Error());
            }
            List<string> names = [];
            for (int index = 0; index < count; index++)
            {
                IntPtr pointer = Marshal.ReadIntPtr(result, index * IntPtr.Size);
                Credential credential = Marshal.PtrToStructure<Credential>(pointer);
                if (credential.Type == GenericCredential &&
                    credential.TargetName?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true)
                    names.Add(credential.TargetName[Prefix.Length..]);
            }
            return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("PWVLT0001", LanguageManager.Get("VaultListFailed"), error); //PWVLT0001
        }
        finally { if (result != IntPtr.Zero) CredFree(result); }
    }

    public static string Read(string name)
    {
        ValidateName(name);
        IntPtr result = IntPtr.Zero;
        try
        {
            if (!CredRead(Prefix + name, GenericCredential, 0, out result))
                throw CredentialError(Marshal.GetLastWin32Error());
            Credential credential = Marshal.PtrToStructure<Credential>(result);
            if (credential.CredentialBlobSize is <= 0 or > MaxBlobBytes ||
                credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize % 2 != 0)
                throw new StageException("PWVLT0002", LanguageManager.Get("VaultInvalidSecret")); //PWVLT0002
            byte[] bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try { return Encoding.Unicode.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("PWVLT0003", LanguageManager.Get("VaultReadFailed"), error); //PWVLT0003
        }
        finally { if (result != IntPtr.Zero) CredFree(result); }
    }

    public static void Save(string name, string password)
    {
        ValidateName(name);
        if (string.IsNullOrEmpty(password))
            throw new StageException("PWVLT0004", LanguageManager.Get("ArchivePasswordRequired")); //PWVLT0004
        byte[] bytes = Encoding.Unicode.GetBytes(password);
        if (bytes.Length > MaxBlobBytes)
            throw new StageException("PWVLT0005", LanguageManager.Get("VaultPasswordTooLong")); //PWVLT0005
        IntPtr blob = IntPtr.Zero;
        try
        {
            blob = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Credential credential = new()
            {
                Type = GenericCredential,
                TargetName = Prefix + name,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw CredentialError(Marshal.GetLastWin32Error());
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("PWVLT0006", LanguageManager.Get("VaultSaveFailed"), error); //PWVLT0006
        }
        finally
        {
            Array.Clear(bytes);
            if (blob != IntPtr.Zero)
            {
                // Clear the unmanaged plaintext before releasing its buffer.
                for (int index = 0; index < bytes.Length; index++)
                    Marshal.WriteByte(blob, index, 0);
                Marshal.FreeHGlobal(blob);
            }
        }
    }

    public static void Delete(string name)
    {
        ValidateName(name);
        try
        {
            if (!CredDelete(Prefix + name, GenericCredential, 0) && Marshal.GetLastWin32Error() != NotFound)
                throw CredentialError(Marshal.GetLastWin32Error());
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("PWVLT0007", LanguageManager.Get("VaultDeleteFailed"), error); //PWVLT0007
        }
    }

    // Credential Manager requires a real Windows logon session. Surface this
    // environment failure explicitly instead of reporting a bad password.
    private static Exception CredentialError(int code) => code == NoLogonSession
        ? new StageException("PWVLT0009", LanguageManager.Get("VaultSessionUnavailable")) //PWVLT0009
        : new Win32Exception(code);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl))
            throw new StageException("PWVLT0008", LanguageManager.Get("VaultInvalidName")); //PWVLT0008
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
