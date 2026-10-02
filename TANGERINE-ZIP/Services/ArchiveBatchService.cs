using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: ARBAT. Every batch item is staged independently. A completed
// item remains available if a later archive fails or the user cancels.
internal static class ArchiveBatchService
{
    public static async Task ExtractAsync(IReadOnlyList<string> sources, string destination,
        string? password, ArchiveWorkerClient worker, IProgress<ArchiveProgress>? progress,
        CancellationToken token, Func<ExtractionIssue, CancellationToken, Task<ExtractionAnswer>> issueResolver)
    {
        string[] outputPaths = Plan(sources, destination, extension: null);
        for (int index = 0; index < sources.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            string source = Path.GetFullPath(sources[index]);
            string final = outputPaths[index];
            string staging = final + ".tzip-" + Guid.NewGuid().ToString("N");
            Exception? operationError = null;
            try
            {
                // A unique sibling on the same volume lets Directory.Move
                // publish a complete extraction without exposing partial data.
                EnsurePrivateSibling(staging, destination);
                IProgress<ArchiveProgress> itemProgress = Scale(progress, index, sources.Count, source);
                await ExtractSourceAsync(worker, source, staging, password,
                    itemProgress, token, issueResolver);
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(final))
                    throw new StageException("ARBAT0003", LanguageManager.Get("BatchOutputExists")); //ARBAT0003
                Directory.Move(staging, final);
                progress?.Report(new ArchiveProgress((index + 1) * 100 / sources.Count, Path.GetFileName(source)));
            }
            catch (StageException error) { operationError = error; throw; }
            catch (OperationCanceledException error) { operationError = error; throw; }
            catch (Exception error)
            {
                operationError = error;
                throw new StageException("ARBAT0005", LanguageManager.Get("BatchExtractFailed"), error); //ARBAT0005
            }
            finally
            {
                try { CleanupPrivateDirectory(staging, destination); }
                catch (Exception cleanupError)
                {
                    if (operationError is not null) operationError.Data["CleanupError"] = cleanupError;
                    else throw new StageException("ARBAT0009", LanguageManager.Get("BatchCleanupFailed"), cleanupError); //ARBAT0009
                }
            }
        }
    }

    public static async Task ConvertAsync(IReadOnlyList<string> sources, string destination,
        FileDetector.FileType targetType, string? inputPassword, string? outputPassword, ArchiveWorkerClient worker,
        IProgress<ArchiveProgress>? progress, CancellationToken token,
        Func<ExtractionIssue, CancellationToken, Task<ExtractionAnswer>> issueResolver)
    {
        string extension = targetType switch
        {
            FileDetector.FileType.Zip => ".zip",
            FileDetector.FileType.SevenZip => ".7z",
            FileDetector.FileType.Tar => ".tar",
            _ => throw new StageException("ARBAT0004", LanguageManager.Get("BatchTargetUnsupported")) //ARBAT0004
        };
        string[] outputPaths = Plan(sources, destination, extension);
        string workspace = TempDirectorySettings.GetDirectory();
        for (int index = 0; index < sources.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            string source = Path.GetFullPath(sources[index]);
            string extraction = Path.Combine(workspace, "BatchConvert_" + Guid.NewGuid().ToString("N"));
            string final = outputPaths[index];
            string temporaryOutput = final + ".tzip-" + Guid.NewGuid().ToString("N") + ".tmp";
            Exception? operationError = null;
            try
            {
                EnsurePrivateChild(extraction, workspace);
                IProgress<ArchiveProgress> extracting = Scale(progress, index, sources.Count, source, 0, 50);
                await ExtractSourceAsync(worker, source, extraction, inputPassword,
                    extracting, token, issueResolver);
                string[] members = Directory.GetFileSystemEntries(extraction);
                if (members.Length == 0)
                    throw new StageException("ARBAT0006", LanguageManager.Get("BatchEmptyArchive")); //ARBAT0006
                IProgress<ArchiveProgress> compressing = Scale(progress, index, sources.Count, source, 50, 50);
                await worker.CreateAsync(members, temporaryOutput, targetType, compressing, token, outputPassword);
                token.ThrowIfCancellationRequested();
                if (!File.Exists(temporaryOutput) ||
                    FileDetector.DetectFileType(temporaryOutput) != targetType)
                    throw new StageException("ARBAT0007", LanguageManager.Get("BatchInvalidOutput")); //ARBAT0007
                File.Move(temporaryOutput, final, overwrite: false);
                progress?.Report(new ArchiveProgress((index + 1) * 100 / sources.Count, Path.GetFileName(source)));
            }
            catch (StageException error) { operationError = error; throw; }
            catch (OperationCanceledException error) { operationError = error; throw; }
            catch (Exception error)
            {
                operationError = error;
                throw new StageException("ARBAT0008", LanguageManager.Get("BatchConvertFailed"), error); //ARBAT0008
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
                    CleanupPrivateDirectory(extraction, workspace);
                }
                catch (Exception cleanupError)
                {
                    if (operationError is not null) operationError.Data["CleanupError"] = cleanupError;
                    else throw new StageException("ARBAT0009", LanguageManager.Get("BatchCleanupFailed"), cleanupError); //ARBAT0009
                }
            }
        }
    }

    private static string[] Plan(IReadOnlyList<string> sources, string destination, string? extension)
    {
        if (sources.Count == 0)
            throw new StageException("ARBAT0001", LanguageManager.Get("BatchNoArchives")); //ARBAT0001
        string root = Path.GetFullPath(destination);
        if (!Directory.Exists(root))
            throw new StageException("ARBAT0002", LanguageManager.Get("BatchDestinationMissing")); //ARBAT0002
        string[] result = new string[sources.Count];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < sources.Count; index++)
        {
            string source = Path.GetFullPath(sources[index]);
            if (!File.Exists(source) || !ArchiveCapabilities.CanOpen(FileDetector.DetectFileType(source)))
                throw new StageException("ARBAT0002", LanguageManager.Get("BatchSourceUnsupported")); //ARBAT0002
            string name = GetArchiveStem(source) + extension;
            if (string.IsNullOrWhiteSpace(name))
                throw new StageException("ARBAT0002", LanguageManager.Get("BatchSourceUnsupported")); //ARBAT0002
            if (!names.Add(name))
                throw new StageException("ARBAT0003", LanguageManager.Get("BatchDuplicateOutput")); //ARBAT0003
            result[index] = Path.Combine(root, name);
            if (File.Exists(result[index]) || Directory.Exists(result[index]))
                throw new StageException("ARBAT0003", LanguageManager.Get("BatchOutputExists")); //ARBAT0003
        }
        return result;
    }

    private static string GetArchiveStem(string path)
    {
        string name = Path.GetFileName(path);
        // Treat a compressed TAR's two suffixes as one archive extension so
        // bundle.tar.gz becomes bundle.zip or a folder named bundle.
        string[] compound = [".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz4", ".tar.zst",
            ".tar.zstd", ".tar.lz", ".tar.lzip"];
        string? suffix = compound.FirstOrDefault(value => name.EndsWith(value, StringComparison.OrdinalIgnoreCase));
        return suffix is null ? Path.GetFileNameWithoutExtension(name) : name[..^suffix.Length];
    }

    private static IProgress<ArchiveProgress> Scale(IProgress<ArchiveProgress>? target,
        int index, int total, string source, int phaseStart = 0, int phaseSpan = 100) =>
        new InlineProgress<ArchiveProgress>(item =>
        {
            int overall = (index * 100 + phaseStart + item.Percentage * phaseSpan / 100) / total;
            target?.Report(new ArchiveProgress(overall, Path.GetFileName(source) + ": " + item.EntryKey));
        });

    private static async Task ExtractSourceAsync(ArchiveWorkerClient worker, string source,
        string destination, string? password, IProgress<ArchiveProgress> progress,
        CancellationToken token, Func<ExtractionIssue, CancellationToken, Task<ExtractionAnswer>> issues)
    {
        // Batch jobs follow the same single-inner-TAR rule as the main view.
        // A .tar.gz converts its contained files rather than wrapping the TAR
        // file as one member of a new ZIP or 7z archive.
        NestedTarInfo nested = await worker.AnalyzeNestedTarAsync(source, token, password);
        if (nested.FlattenAutomatically)
            await worker.ExtractNestedTarsAsync(source, nested.TarEntryKeys, destination,
                null, false, OverwritePolicy.Ask, progress, token, password,
                (_, _) => Task.FromResult(ConflictChoice.Cancel), issues);
        else
            await worker.ExtractAsync(source, destination, null, OverwritePolicy.Ask,
                progress, token, password,
                (_, _) => Task.FromResult(ConflictChoice.Cancel), issues);
    }

    private static void EnsurePrivateSibling(string candidate, string parent) => EnsurePrivateChild(candidate, parent);

    private static void EnsurePrivateChild(string candidate, string parent)
    {
        string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string child = Path.GetFullPath(candidate);
        if (!child.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(child)?.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new StageException("ARBAT0010", LanguageManager.Get("BatchUnsafeTempPath")); //ARBAT0010
        Directory.CreateDirectory(child);
        if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
            throw new StageException("ARBAT0010", LanguageManager.Get("BatchUnsafeTempPath")); //ARBAT0010
    }

    private static void CleanupPrivateDirectory(string candidate, string parent)
    {
        string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string child = Path.GetFullPath(candidate);
        if (!child.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(child)?.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new StageException("ARBAT0010", LanguageManager.Get("BatchUnsafeTempPath")); //ARBAT0010
        if (Directory.Exists(child) && File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
            throw new StageException("ARBAT0010", LanguageManager.Get("BatchUnsafeTempPath")); //ARBAT0010
        if (Directory.Exists(child)) Directory.Delete(child, recursive: true);
    }
}
