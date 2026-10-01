using System.Security.Cryptography;

namespace TANGERINE_ZIP.Services;

// File-type icons must outlive both the process and a single-file extraction
// directory: Explorer resolves DefaultIcon later, often after the app has closed.
// Materialize immutable embedded ICOs in the current user's persistent LocalAppData
// cache. Nothing here changes a default choice or requires administrator rights.
internal static class ArchiveFileIconService
{
    internal static string GetIconName(string extension)
    {
        // Reuse the association whitelist so ISO/WIM and path-like input can never
        // become cache filenames or arbitrary manifest-resource lookups.
        DefaultAppAssociationService.GetProgId(extension);
        return extension switch
        {
            ".gz" => "gzip",
            ".bz2" => "bzip2",
            ".zst" or ".zstd" => "zstd",
            _ => extension[1..]
        };
    }

    internal static string EnsureIcon(string extension)
    {
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
                throw new DirectoryNotFoundException(LanguageManager.Get("DefaultAppsIconFailed"));
            return EnsureIconInDirectory(extension, Path.Combine(local, "TangerineZip", "DefaultApps", "Icons"));
        }
        catch (StageException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("DEFAS0010", LanguageManager.Get("DefaultAppsIconFailed"), exception); //DEFAS0010
        }
    }

    // The explicit directory enables verification in a disposable workspace
    // without touching the user's real icon cache or live Shell registration.
    // Production only calls EnsureIcon with the application-owned cache directory.
    internal static string EnsureIconInDirectory(string extension, string directory)
    {
        string name = GetIconName(extension);
        string? temporary = null;
        try
        {
            using Stream resource = typeof(ArchiveFileIconService).Assembly.GetManifestResourceStream(
                "TANGERINE_ZIP.FileTypeIcons." + name + ".ico")
                ?? throw new FileNotFoundException(LanguageManager.Get("DefaultAppsIconFailed"));
            using var content = new MemoryStream();
            resource.CopyTo(content);
            byte[] bytes = content.ToArray();
            // Including the full content digest gives changed artwork a new Shell
            // cache identity after upgrades. Retain old files: another installed
            // version or an existing association may still reference those paths.
            string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, name + "-" + digest + ".ico");
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) return target;

            // Publish the entire ICO atomically in the same directory. Never
            // register a partially written file. Concurrent registrations are
            // serialized by the association service's per-user mutex.
            temporary = Path.Combine(directory, ".tzip-icon-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
            temporary = null;
            if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                throw new IOException(LanguageManager.Get("DefaultAppsIconFailed"));
            return target;
        }
        catch (Exception exception)
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception cleanupError)
                {
                    // Preserve the original failure as well as the cleanup error;
                    // the window offers retry/skip/stop with this stable stage code.
                    throw new StageException("DEFAS0011", LanguageManager.Get("DefaultAppsIconCleanupFailed"),
                        new AggregateException(exception, cleanupError)); //DEFAS0011
                }
            }
            throw new StageException("DEFAS0010", LanguageManager.Get("DefaultAppsIconFailed"), exception); //DEFAS0010
        }
    }
}
