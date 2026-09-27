using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;

namespace TANGERINE_ZIP.Services;

// TEMP_D is deliberately a plain, extensionless UTF-8 file beside the app.
// Worker processes read the same path, while Settings can replace it atomically.
internal static class TempDirectorySettings
{
    private const string FileName = "TEMP_D";
    private const string WorkspaceName = "TangerineZipWorkspace";
    private const long StartupHeadroom = 64L * 1024 * 1024;
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private static string? _currentPath;
    private static Mutex? _instanceMutex;

    public static string CurrentPath => _currentPath ??
        throw new StageException("TMPDR0001", LanguageManager.Get("TempDirectoryNotConfigured")); //TMPDR0001

    public static void Initialize()
    {
        string configuration = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(configuration))
            throw new StageException("TMPDR0001", LanguageManager.Get("TempDirectoryNotConfigured")); //TMPDR0001
        string path;
        try { path = File.ReadAllText(configuration, new UTF8Encoding(false, true)).Trim(); }
        catch (Exception exception)
        {
            throw new StageException("TMPDR0002", LanguageManager.Get("TempDirectoryUnavailable"), exception); //TMPDR0002
        }
        Validate(path);
        string fullPath = Path.GetFullPath(path);
        EnsureWorkspace(fullPath);
        _currentPath = fullPath;
    }

    public static async Task SetAsync(string path)
    {
        await SaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Validate(path);
            string fullPath = Path.GetFullPath(path);
            EnsureWorkspace(fullPath);
            string configuration = Path.Combine(AppContext.BaseDirectory, FileName);
            string temporary = configuration + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                ResourcePreflight.Check(configuration, 4096, 16L * 1024 * 1024);
                await File.WriteAllTextAsync(temporary, fullPath, new UTF8Encoding(false)).ConfigureAwait(false);
                File.Move(temporary, configuration, true);
                if (File.ReadAllText(configuration, Encoding.UTF8) != fullPath)
                    throw new IOException(LanguageManager.Get("TempDirectorySaveFailed"));
                _currentPath = fullPath;
            }
            catch (Exception exception)
            {
                throw new StageException("TMPDR0004", LanguageManager.Get("TempDirectorySaveFailed"), exception); //TMPDR0004
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception cleanupError)
                {
                    throw new StageException("TMPDR0004", LanguageManager.Get("TempDirectorySaveFailed"), cleanupError); //TMPDR0004
                }
            }
        }
        finally { SaveGate.Release(); }
    }

    public static string GetDirectory(long requiredBytes = StartupHeadroom)
    {
        string path = CurrentPath;
        Validate(path, requiredBytes);
        return EnsureWorkspace(path);
    }

    private static string EnsureWorkspace(string path)
    {
        // All disposable files live below an app-owned directory. The chosen
        // parent may contain unrelated user files and must never be emptied.
        string workspace = Path.Combine(path, WorkspaceName);
        try
        {
            Directory.CreateDirectory(workspace);
            if (File.GetAttributes(workspace).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException(LanguageManager.Get("TempDirectoryUnavailable"));
            Validate(workspace, 0);
            return workspace;
        }
        catch (Exception exception)
        {
            throw new StageException("TMPDR0005", LanguageManager.Get("TempDirectoryUnavailable"), exception); //TMPDR0005
        }
    }

    public static void ClearOnFirstInstanceStartup()
    {
        string workspace = GetDirectory();
        try
        {
            // A process-lifetime mutex prevents another GUI instance from
            // deleting files still needed by the first GUI instance. The
            // process check also covers context commands and worker processes.
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                Path.GetFullPath(AppContext.BaseDirectory))));
            _instanceMutex = new Mutex(true, "Local\\TangerineZipTemp_" + identity, out bool firstInstance);
            if (!firstInstance) return;
            using Process current = Process.GetCurrentProcess();
            Process[] candidates = Process.GetProcessesByName(current.ProcessName);
            try
            {
                if (candidates.Any(process => process.Id != current.Id)) return;
            }
            finally { foreach (Process candidate in candidates) candidate.Dispose(); }

            // Enumerate only direct children of our private directory. A
            // junction is deleted as a link; it is never traversed into its
            // target. Cleanup errors are reported instead of silently leaving
            // a partially cleared workspace behind.
            foreach (string entry in Directory.EnumerateFileSystemEntries(workspace))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory))
                    Directory.Delete(entry, !attributes.HasFlag(FileAttributes.ReparsePoint));
                else File.Delete(entry);
            }
        }
        catch (Exception exception)
        {
            throw new StageException("TMPDR0006", LanguageManager.Get("TempDirectoryCleanupFailed"), exception); //TMPDR0006
        }
    }

    private static void Validate(string path, long requiredBytes = StartupHeadroom)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            throw new StageException("TMPDR0002", LanguageManager.Get("TempDirectoryUnavailable")); //TMPDR0002
        string fullPath = Path.GetFullPath(path);
        try
        {
            ResourcePreflight.Check(fullPath, requiredBytes, 16L * 1024 * 1024);
            string probe = Path.Combine(fullPath, ".tzip-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                using FileStream stream = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.WriteByte(0);
            }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }
        catch (StageException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("TMPDR0002", LanguageManager.Get("TempDirectoryUnavailable"), exception); //TMPDR0002
        }
    }
}
