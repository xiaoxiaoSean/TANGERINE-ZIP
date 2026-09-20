using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace TANGERINE_ZIP.Services;

// Stage head: CTXMN
internal static class ContextMenuRegistrationService
{
    private const string FileShellPath = @"Software\Classes\*\shell";
    private const string ExtractVerbName = "TZIP.Extract";
    private const string CompressVerbName = "TZIP.Compress";
    private const string OpenVerbName = "TZIP.Open";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public static void Create(string executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new FileNotFoundException(executablePath); //CTXMN0001

            using RegistryKey shellKey = Registry.CurrentUser.CreateSubKey(FileShellPath, writable: true)
                ?? throw new InvalidOperationException();
            CreateVerb(shellKey, ExtractVerbName, LanguageManager.Get("ContextExtractMenu"), executablePath, "--context-extract");
            CreateVerb(shellKey, CompressVerbName, LanguageManager.Get("ContextCompressMenu"), executablePath, "--context-compress");
            CreateVerb(shellKey, OpenVerbName, LanguageManager.Get("ContextOpenMenu"), executablePath, "--context-open");
            NotifyShell();
        }
        catch (StageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StageException("CTXMN0001", exception.Message, exception); //CTXMN0001
        }
    }

    public static void Delete()
    {
        try
        {
            using RegistryKey? shellKey = Registry.CurrentUser.OpenSubKey(FileShellPath, writable: true);
            if (shellKey is not null)
            {
                shellKey.DeleteSubKeyTree(ExtractVerbName, throwOnMissingSubKey: false);
                shellKey.DeleteSubKeyTree(CompressVerbName, throwOnMissingSubKey: false);
                shellKey.DeleteSubKeyTree(OpenVerbName, throwOnMissingSubKey: false);
            }
            NotifyShell();
        }
        catch (Exception exception)
        {
            throw new StageException("CTXMN0002", exception.Message, exception); //CTXMN0002
        }
    }

    private static void CreateVerb(RegistryKey shellKey, string verbName, string caption, string executablePath, string action)
    {
        using RegistryKey verbKey = shellKey.CreateSubKey(verbName, writable: true)
            ?? throw new InvalidOperationException();
        verbKey.SetValue(string.Empty, caption, RegistryValueKind.String);
        verbKey.SetValue("Icon", $"\"{executablePath}\",0", RegistryValueKind.String);
        // Player tells Explorer that this verb accepts a multi-file selection.
        verbKey.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
        using RegistryKey commandKey = verbKey.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException();
        commandKey.SetValue(string.Empty, $"\"{executablePath}\" {action} \"%1\"", RegistryValueKind.String);
    }

    private static void NotifyShell() => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
