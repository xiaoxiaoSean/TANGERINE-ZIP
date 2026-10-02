using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: ARCOM. ZIP archive comments are part of the central directory.
// All changes happen on a sibling copy, then File.Replace creates a backup.
internal static class ArchiveCommentService
{
    public static string Read(string archivePath)
    {
        string archive = EnsureSupported(archivePath);
        try
        {
            using ZipFile zip = new(archive);
            return zip.ZipFileComment ?? string.Empty;
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("ARCOM0002", LanguageManager.Get("CommentReadFailed"), error); //ARCOM0002
        }
    }

    public static string Write(string archivePath, string comment, CancellationToken token)
    {
        string archive = EnsureSupported(archivePath);
        if (Encoding.UTF8.GetByteCount(comment) > ushort.MaxValue)
            throw new StageException("ARCOM0003", LanguageManager.Get("CommentTooLong")); //ARCOM0003
        string temporary = archive + "." + Guid.NewGuid().ToString("N") + ".comment";
        string backup = archive + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "." +
            Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            long archiveBytes = new FileInfo(archive).Length;
            ResourcePreflight.Check(archive, archiveBytes > long.MaxValue / 2
                ? long.MaxValue : archiveBytes * 2, 128L * 1024 * 1024);
            (long Length, DateTime LastWriteUtc) identity = Identity(archive);
            File.Copy(archive, temporary, overwrite: false);
            using (ZipFile zip = new(temporary))
            {
                if (zip.Cast<ZipEntry>().Any(entry => entry.IsCrypted))
                    throw new StageException("ARCOM0001", LanguageManager.Get("CommentUnsupported")); //ARCOM0001
                token.ThrowIfCancellationRequested();
                zip.BeginUpdate();
                zip.SetComment(comment);
                zip.CommitUpdate();
            }
            token.ThrowIfCancellationRequested();
            if (Identity(archive) != identity)
                throw new StageException("ARCOM0004", LanguageManager.Get("ArchiveEditChanged")); //ARCOM0004
            if (FileDetector.DetectFileType(temporary) != FileDetector.FileType.Zip)
                throw new StageException("ARCOM0005", LanguageManager.Get("CommentInvalidOutput")); //ARCOM0005
            File.Replace(temporary, archive, backup, ignoreMetadataErrors: false);
            return backup;
        }
        catch (StageException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            throw new StageException("ARCOM0006", LanguageManager.Get("CommentWriteFailed"), error); //ARCOM0006
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error)
            {
                // Keep the primary write exception intact when cleanup also
                // fails; the trace retains the private file path for repair.
                System.Diagnostics.Trace.TraceError("ARCOM0006: {0}: {1}", temporary, error);
            }
        }
    }

    private static string EnsureSupported(string archivePath)
    {
        try
        {
            string archive = Path.GetFullPath(archivePath);
            if (!File.Exists(archive) || FileDetector.DetectFileType(archive) != FileDetector.FileType.Zip ||
                archive.EndsWith(".001", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(archive + ".001") || File.Exists(Path.ChangeExtension(archive, ".z01")))
                throw new StageException("ARCOM0001", LanguageManager.Get("CommentUnsupported")); //ARCOM0001
            return archive;
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("ARCOM0001", LanguageManager.Get("CommentUnsupported"), error); //ARCOM0001
        }
    }

    private static (long Length, DateTime LastWriteUtc) Identity(string path)
    {
        FileInfo file = new(path);
        file.Refresh();
        return (file.Length, file.LastWriteTimeUtc);
    }
}
