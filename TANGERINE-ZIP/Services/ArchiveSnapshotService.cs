using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: ASNAP. A snapshot is an independent byte-for-byte archive copy.
// It is published only after the source and staged copy hash to the same value.
// Explicit retention prunes only names created by this feature for this source.
internal static class ArchiveSnapshotService
{
    public static async Task<string> CreateAsync(string archivePath, string destinationFolder,
        int keep, CancellationToken token)
    {
        if (keep is < 0 or > 1000)
            throw new StageException("ASNAP0001", LanguageManager.Get("SnapshotInvalidKeep")); //ASNAP0001
        string source;
        string destination;
        try
        {
            source = Path.GetFullPath(archivePath);
            destination = Path.GetFullPath(destinationFolder);
            if (!File.Exists(source) || !Directory.Exists(destination) ||
                !ArchiveCapabilities.CanOpen(FileDetector.DetectFileType(source)))
                throw new StageException("ASNAP0002", LanguageManager.Get("SnapshotInvalidPaths")); //ASNAP0002
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("ASNAP0002", LanguageManager.Get("SnapshotInvalidPaths"), error); //ASNAP0002
        }

        string stem = Path.GetFileNameWithoutExtension(source);
        string extension = Path.GetExtension(source);
        string marker = stem + ".tzip-snapshot-";
        string final = Path.Combine(destination, marker + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
            Guid.NewGuid().ToString("N") + extension);
        string temporary = final + ".tmp";
        Exception? operationError = null;
        try
        {
            FileInfo before = new(source);
            (long Length, DateTime Modified) identity = (before.Length, before.LastWriteTimeUtc);
            ResourcePreflight.Check(final, before.Length > long.MaxValue - 64L * 1024 * 1024
                ? long.MaxValue : before.Length + 64L * 1024 * 1024, 128L * 1024 * 1024);
            await using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, token);
            token.ThrowIfCancellationRequested();
            before.Refresh();
            if ((before.Length, before.LastWriteTimeUtc) != identity)
                throw new StageException("ASNAP0003", LanguageManager.Get("SnapshotSourceChanged")); //ASNAP0003
            // Hash both files after copying, so a source rewrite with the same
            // length and timestamp cannot be mistaken for a valid snapshot.
            string sourceHash = await ArchiveDiagnostics.ComputeHashAsync(source, "SHA256", token);
            string snapshotHash = await ArchiveDiagnostics.ComputeHashAsync(temporary, "SHA256", token);
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(sourceHash), Convert.FromHexString(snapshotHash)))
                throw new StageException("ASNAP0004", LanguageManager.Get("SnapshotHashMismatch")); //ASNAP0004
            File.Move(temporary, final, overwrite: false);
            if (keep > 0)
            {
                // A strict generated-name pattern prevents retention from
                // touching arbitrary archives in the destination folder.
                Regex generated = new("^" + Regex.Escape(marker) +
                    @"\d{8}-\d{6}-[0-9a-f]{32}" + Regex.Escape(extension) + "$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                string[] snapshots = Directory.GetFiles(destination)
                    .Where(path => generated.IsMatch(Path.GetFileName(path)))
                    .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (string old in snapshots.Skip(keep))
                {
                    token.ThrowIfCancellationRequested();
                    File.Delete(old);
                }
            }
            return final;
        }
        catch (StageException error) { operationError = error; throw; }
        catch (OperationCanceledException error) { operationError = error; throw; }
        catch (Exception error)
        {
            operationError = error;
            throw new StageException("ASNAP0005", LanguageManager.Get("SnapshotFailed"), error); //ASNAP0005
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error)
            {
                if (operationError is not null) operationError.Data["CleanupError"] = error;
                else throw new StageException("ASNAP0006", LanguageManager.Get("SnapshotCleanupFailed"), error); //ASNAP0006
            }
        }
    }
}
