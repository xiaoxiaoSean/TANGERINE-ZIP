using System.IO.Compression;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Common.Options;
using SharpCompress.Readers;
using SharpCompress.Writers;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

internal enum ArchiveEditAction { Copy, Move, Delete }

// Writable archives are rewritten into a sibling file and committed with
// File.Replace. This retains the original as a backup at commit time.
internal static class ArchiveEditService
{
    private static (long Length, DateTime LastWriteUtc) Identity(string path)
    {
        FileInfo file = new(path);
        file.Refresh();
        return (file.Length, file.LastWriteTimeUtc);
    }

    public static bool CanEdit(FileDetector.FileType type) => type is
        FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Tar;

    public static async Task<string> EditAsync(string archivePath, IReadOnlyCollection<string> selection,
        string destinationPrefix, ArchiveEditAction action, IProgress<ArchiveProgress>? progress,
        CancellationToken token)
    {
        FileDetector.FileType type = FileDetector.DetectFileType(archivePath);
        if (!CanEdit(type))
            throw new StageException("ARCED0001", LanguageManager.Get("ArchiveEditUnsupported")); //ARCED0001
        return type == FileDetector.FileType.Zip
            ? await Task.Run(() => EditZip(archivePath, selection, destinationPrefix, action, progress, token), token)
            : await EditSharpCompressAsync(archivePath, selection, destinationPrefix, action, progress, token, type);
    }

    private static string EditZip(string archivePath, IReadOnlyCollection<string> selection,
        string destinationPrefix, ArchiveEditAction action, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (archivePath.EndsWith(".001", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(archivePath + ".001") ||
            File.Exists(Path.ChangeExtension(archivePath, ".z01")))
            throw new StageException("ARCED0001", LanguageManager.Get("ArchiveEditUnsupported")); //ARCED0001
        if (selection.Count == 0)
            throw new StageException("ARCED0002", LanguageManager.Get("NoSelectedEntries")); //ARCED0002
        string prefix = Normalize(destinationPrefix);
        if (action != ArchiveEditAction.Delete && !IsSafeKey(prefix, allowEmpty: true))
            throw new StageException("ARCED0003", LanguageManager.Get("ArchiveEditInvalidEntry")); //ARCED0003

        string[] roots = NormalizeRoots(selection);
        (long Length, DateTime LastWriteUtc) sourceIdentity = Identity(archivePath);

        string temporary = archivePath + "." + Guid.NewGuid().ToString("N") + ".editing";
        string backup = archivePath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "." +
            Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            // Rewriting an encrypted archive through ZipArchive would silently
            // drop encryption on the new members. Refuse the operation up front.
            using (IArchive inspection = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions()))
                if (inspection.Entries.Any(entry => !entry.IsDirectory && entry.IsEncrypted))
                    throw new StageException("ARCED0006", LanguageManager.Get("ArchiveEditUnsupported")); //ARCED0006
            using (FileStream sourceFile = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive source = new(sourceFile, ZipArchiveMode.Read, leaveOpen: true,
                entryNameEncoding: string.IsNullOrWhiteSpace(ArchiveService.EntryEncodingName)
                    ? Encoding.UTF8 : Encoding.GetEncoding(ArchiveService.EntryEncodingName)))
            {
                ZipArchiveEntry[] entries = source.Entries.ToArray();
                string[] names = entries.Select(entry => Normalize(entry.FullName).TrimEnd('/')).ToArray();
                ValidateEntries(names, roots);

                Dictionary<string, string> destinations = PlanDestinations(roots, names, prefix, action);
                long originalLength = sourceFile.Length;
                long outputUpperBound = SumLengths(entries, action, roots);
                // A temporary archive and an atomic backup must both fit beside the source.
                ResourcePreflight.Check(archivePath, SaturatingAdd(originalLength,
                    SaturatingAdd(Math.Max(originalLength, outputUpperBound), 64L * 1024 * 1024)),
                    128L * 1024 * 1024);

                using FileStream targetFile = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using ZipArchive target = new(targetFile, ZipArchiveMode.Create, leaveOpen: false,
                    entryNameEncoding: Encoding.UTF8);
                long completed = 0;
                long total = Math.Max(outputUpperBound, 1);
                foreach (ZipArchiveEntry entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    string name = Normalize(entry.FullName);
                    string? root = roots.FirstOrDefault(candidate => IsWithin(name, candidate));
                    if (action == ArchiveEditAction.Delete && root is not null) continue;
                    string outputName = action == ArchiveEditAction.Move && root is not null
                        ? destinations[root] + name[root.Length..] : name;
                    WriteEntry(entry, target, outputName, ref completed, total, progress, token);
                    if (action == ArchiveEditAction.Copy && root is not null)
                        WriteEntry(entry, target, destinations[root] + name[root.Length..],
                            ref completed, total, progress, token);
                }
            }
            token.ThrowIfCancellationRequested();
            if (Identity(archivePath) != sourceIdentity)
                throw new StageException("ARCED0008", LanguageManager.Get("ArchiveEditChanged")); //ARCED0008
            // Source streams are closed before File.Replace on Windows.
            File.Replace(temporary, archivePath, backup, ignoreMetadataErrors: false);
            progress?.Report(new ArchiveProgress(100, string.Empty));
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> EditSharpCompressAsync(string archivePath,
        IReadOnlyCollection<string> selection, string destinationPrefix, ArchiveEditAction action,
        IProgress<ArchiveProgress>? progress, CancellationToken token, FileDetector.FileType type)
    {
        token.ThrowIfCancellationRequested();
        if (archivePath.EndsWith(".001", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(archivePath + ".001"))
            throw new StageException("ARCED0001", LanguageManager.Get("ArchiveEditUnsupported")); //ARCED0001
        if (selection.Count == 0)
            throw new StageException("ARCED0002", LanguageManager.Get("NoSelectedEntries")); //ARCED0002
        string prefix = Normalize(destinationPrefix);
        if (action != ArchiveEditAction.Delete && !IsSafeKey(prefix, allowEmpty: true))
            throw new StageException("ARCED0003", LanguageManager.Get("ArchiveEditInvalidEntry")); //ARCED0003
        string[] roots = NormalizeRoots(selection);
        (long Length, DateTime LastWriteUtc) sourceIdentity = Identity(archivePath);
        string temporary = archivePath + "." + Guid.NewGuid().ToString("N") + ".editing";
        string backup = archivePath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "." +
            Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            using (IArchive source = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions
                { ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8, UTF8 = Encoding.UTF8 } }))
            {
                IArchiveEntry[] entries = source.Entries.ToArray();
                if (entries.Any(entry => !entry.IsDirectory && entry.IsEncrypted))
                    throw new StageException("ARCED0006", LanguageManager.Get("ArchiveEditUnsupported")); //ARCED0006
                // SharpCompress's 7z writer stores zero-length directory streams as
                // ordinary files. Retain directories through member paths, and refuse
                // archives with empty folders that this writer cannot round-trip.
                if (entries.Where(entry => entry.IsDirectory).Any(directory =>
                    !entries.Any(child => !child.IsDirectory && Normalize(child.Key ?? string.Empty)
                        .StartsWith(Normalize(directory.Key ?? string.Empty).TrimEnd('/') + "/",
                            StringComparison.OrdinalIgnoreCase))))
                    throw new StageException("ARCED0007", LanguageManager.Get("ArchiveEditEmptyDirectory")); //ARCED0007
                string[] names = entries.Select(entry => Normalize(entry.Key ?? string.Empty).TrimEnd('/')).ToArray();
                ValidateEntries(names, roots);
                Dictionary<string, string> destinations = PlanDestinations(roots, names, prefix, action);
                long originalLength = new FileInfo(archivePath).Length;
                long outputUpperBound = 0;
                foreach (IArchiveEntry entry in entries)
                {
                    bool selected = roots.Any(root => IsWithin(Normalize(entry.Key ?? string.Empty), root));
                    if (action == ArchiveEditAction.Delete && selected) continue;
                    outputUpperBound = SaturatingAdd(outputUpperBound, Math.Max(entry.Size, 0));
                    if (action == ArchiveEditAction.Copy && selected)
                        outputUpperBound = SaturatingAdd(outputUpperBound, Math.Max(entry.Size, 0));
                }
                ResourcePreflight.Check(archivePath, SaturatingAdd(originalLength,
                    SaturatingAdd(Math.Max(originalLength, outputUpperBound), 64L * 1024 * 1024)),
                    128L * 1024 * 1024);

                await using FileStream targetFile = new(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 128 * 1024, true);
                WriterOptions options = new(type == FileDetector.FileType.Tar
                    ? SharpCompress.Common.CompressionType.None : SharpCompress.Common.CompressionType.LZMA2)
                { ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8, UTF8 = Encoding.UTF8 } };
                await using IAsyncWriter writer = await WriterFactory.OpenAsyncWriter(targetFile,
                    type == FileDetector.FileType.Tar ? ArchiveType.Tar : ArchiveType.SevenZip, options, token);
                int completed = 0;
                int total = Math.Max(entries.Length * (action == ArchiveEditAction.Copy ? 2 : 1), 1);
                foreach (IArchiveEntry entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) continue;
                    string name = Normalize(entry.Key ?? string.Empty);
                    string? root = roots.FirstOrDefault(candidate => IsWithin(name, candidate));
                    if (action == ArchiveEditAction.Delete && root is not null) continue;
                    string outputName = action == ArchiveEditAction.Move && root is not null
                        ? destinations[root] + name[root.Length..] : name;
                    await WriteEntryAsync(entry, writer, outputName, archivePath, type, token);
                    progress?.Report(new ArchiveProgress(++completed * 99 / total, outputName));
                    if (action == ArchiveEditAction.Copy && root is not null)
                    {
                        string copyName = destinations[root] + name[root.Length..];
                        await WriteEntryAsync(entry, writer, copyName, archivePath, type, token);
                        progress?.Report(new ArchiveProgress(++completed * 99 / total, copyName));
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if (Identity(archivePath) != sourceIdentity)
                throw new StageException("ARCED0008", LanguageManager.Get("ArchiveEditChanged")); //ARCED0008
            File.Replace(temporary, archivePath, backup, ignoreMetadataErrors: false);
            progress?.Report(new ArchiveProgress(100, string.Empty));
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task WriteEntryAsync(IArchiveEntry entry, IAsyncWriter writer,
        string name, string archivePath, FileDetector.FileType type, CancellationToken token)
    {
        if (type != FileDetector.FileType.Tar)
        {
            await using Stream input = entry.OpenEntryStream();
            await writer.WriteAsync(name, input, entry.LastModifiedTime ?? DateTime.Now, token);
            return;
        }

        // SharpCompress's TAR writer requires a seekable input to determine
        // its exact header size. Stage one member at a time in TEMP_D.
        long stagedBytes = SaturatingAdd(Math.Max(entry.Size, 0), 64L * 1024 * 1024);
        string staged = Path.Combine(TempDirectorySettings.GetDirectory(stagedBytes),
            "TangerineZipEditEntry_" + Guid.NewGuid().ToString("N"));
        try
        {
            ResourcePreflight.Check(staged, stagedBytes,
                128L * 1024 * 1024);
            await using (Stream input = entry.OpenEntryStream())
            await using (FileStream stagedOutput = new(staged, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 128 * 1024, true))
                await input.CopyToAsync(stagedOutput, token);
            ResourcePreflight.Check(archivePath, stagedBytes,
                128L * 1024 * 1024);
            await using FileStream stagedInput = new(staged, FileMode.Open, FileAccess.Read,
                FileShare.Read, 128 * 1024, true);
            await writer.WriteAsync(name, stagedInput, entry.LastModifiedTime ?? DateTime.Now, token);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    private static Dictionary<string, string> PlanDestinations(string[] roots, string[] existing,
        string prefix, ArchiveEditAction action)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (action == ArchiveEditAction.Delete) return result;
        HashSet<string> reserved = new(existing, StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (action == ArchiveEditAction.Move && prefix.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                throw new StageException("ARCED0005", LanguageManager.Get("ArchiveEditSameLocation")); //ARCED0005
            string originalParent = root.Contains('/') ? root[..(root.LastIndexOf('/') + 1)] : string.Empty;
            if (action == ArchiveEditAction.Move && prefix.Equals(originalParent, StringComparison.OrdinalIgnoreCase))
                throw new StageException("ARCED0005", LanguageManager.Get("ArchiveEditSameLocation")); //ARCED0005
            string leaf = root[(root.LastIndexOf('/') + 1)..];
            string candidate = prefix + leaf;
            int number = 2;
            while (reserved.Any(name => name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase)))
                candidate = prefix + WithNumber(leaf, number++);
            if (!IsSafeKey(candidate))
                throw new StageException("ARCED0003", LanguageManager.Get("ArchiveEditInvalidEntry")); //ARCED0003
            result[root] = candidate;
            reserved.Add(candidate);
        }
        return result;
    }

    private static string[] NormalizeRoots(IReadOnlyCollection<string> selection)
    {
        string[] roots = selection.Select(Normalize).Select(key => key.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key.Length).ToArray();
        if (roots.Any(key => !IsSafeKey(key)))
            throw new StageException("ARCED0003", LanguageManager.Get("ArchiveEditInvalidEntry")); //ARCED0003
        return roots.Where(key => !roots.Any(parent => parent.Length < key.Length &&
            key.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static void ValidateEntries(string[] names, string[] roots)
    {
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
            names.Any(name => !IsSafeKey(name)))
            throw new StageException("ARCED0003", LanguageManager.Get("ArchiveEditInvalidEntry")); //ARCED0003
        if (roots.Any(root => !names.Any(name => name.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))))
            throw new StageException("ARCED0004", LanguageManager.Get("ArchiveEditSelectedMissing")); //ARCED0004
    }

    private static string WithNumber(string leaf, int number)
    {
        int dot = leaf.LastIndexOf('.');
        return dot > 0 ? leaf[..dot] + $" ({number})" + leaf[dot..] : leaf + $" ({number})";
    }

    private static void WriteEntry(ZipArchiveEntry source, ZipArchive target, string name,
        ref long completed, long total, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool directory = source.FullName.EndsWith('/') || source.FullName.EndsWith('\\');
        if (directory && !name.EndsWith('/')) name += '/';
        ZipArchiveEntry output = target.CreateEntry(name, directory ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
        output.LastWriteTime = source.LastWriteTime;
        output.ExternalAttributes = source.ExternalAttributes;
        if (!directory)
        {
            using Stream input = source.Open();
            using Stream destination = output.Open();
            byte[] buffer = new byte[128 * 1024];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, read);
                completed += read;
                progress?.Report(new ArchiveProgress((int)Math.Clamp(completed * 99 / total, 0, 99), name));
            }
        }
    }

    private static long SumLengths(ZipArchiveEntry[] entries, ArchiveEditAction action, string[] roots)
    {
        long total = 0;
        foreach (ZipArchiveEntry entry in entries)
        {
            bool selected = roots.Any(root => IsWithin(Normalize(entry.FullName), root));
            if (action == ArchiveEditAction.Delete && selected) continue;
            total = SaturatingAdd(total, entry.Length);
            if (action == ArchiveEditAction.Copy && selected) total = SaturatingAdd(total, entry.Length);
        }
        return total;
    }

    private static bool IsWithin(string name, string root) =>
        name.TrimEnd('/').Equals(root, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => value.Replace('\\', '/');

    private static bool IsSafeKey(string value, bool allowEmpty = false) =>
        (allowEmpty && value.Length == 0) ||
        (value.Length > 0 && !value.StartsWith('/') && !value.Contains(':') && !value.Contains('\0') &&
         value.TrimEnd('/').Split('/').All(segment => segment is not ("" or "." or "..")));

    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
}
