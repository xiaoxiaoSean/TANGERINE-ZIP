using System.Collections.Concurrent;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: CTXCM
internal static class ContextMenuCommandHandler
{
    public static void Run(string action, string[] paths)
    {
        try
        {
            string[] validPaths = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (validPaths.Length == 0) throw new StageException("CTXCM0001", LanguageManager.Get("ContextNoFiles")); //CTXCM0001
            switch (action)
            {
                case "--context-extract": RunExtract(validPaths); break;
                case "--context-compress": RunCompress(validPaths); break;
                case "--context-open": RunOpen(validPaths); break;
                default: throw new StageException("CTXCM0002", LanguageManager.Get("ContextUnknownAction")); //CTXCM0002
            }
        }
        catch (Exception exception)
        {
            string stageCode = exception is StageException stageException ? stageException.StageCode : "CTXCM0003";
            MessageBox.Show(MessageTipGenerator.GenerateTip(stageCode, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //CTXCM0003
        }
    }

    private static void RunExtract(string[] paths)
    {
        FileDetector.FileType[] formats = paths.Select(FileDetector.DetectFileType).ToArray();
        if (formats.Any(format => !ArchiveCapabilities.CanOpen(format)))
            throw new StageException("CTXCM0004", LanguageManager.Get("ContextAllArchivesRequired")); //CTXCM0004
        string engineNames = string.Join(", ", formats.Select(GetExtractionEngineName)
            .Distinct(StringComparer.Ordinal));
        ConcurrentDictionary<string, string?> passwords = new(StringComparer.OrdinalIgnoreCase);
        using SemaphoreSlim conflictPromptLock = new(1, 1);
        bool? overwriteAll = null;
        async Task<ConflictChoice> ResolveConflictAsync(ArchiveConflict conflict, CancellationToken token)
        {
            await conflictPromptLock.WaitAsync(token);
            try
            {
                if (overwriteAll.HasValue) return overwriteAll.Value ? ConflictChoice.AllYes : ConflictChoice.AllNo;
                ConflictChoice choice = OverwriteConflictWindow.Ask(null, conflict);
                if (choice == ConflictChoice.AllYes) overwriteAll = true;
                if (choice == ConflictChoice.AllNo) overwriteAll = false;
                return choice;
            }
            finally { conflictPromptLock.Release(); }
        }
        ContextOperationWindow window = new(LanguageManager.Get("ContextExtractProgress"),
            (progress, token) => ExtractManyAsync(paths, passwords, ResolveConflictAsync, progress, token),
            engineNames);
        window.ShowDialog();
    }

    // Keep the label aligned with ArchiveService.ExtractAsync and its format-specific decoders.
    private static string GetExtractionEngineName(FileDetector.FileType format) => format switch
    {
        FileDetector.FileType.Iso => "DiscUtils",
        FileDetector.FileType.Wim => "ManagedWimLib",
        FileDetector.FileType.GZip => ".NET GZipStream",
        FileDetector.FileType.Lz4 => "K4os.Compression.LZ4",
        FileDetector.FileType.Xz => "Joveler.Compression.XZ",
        _ => "SharpCompress"
    };

    private static void RunCompress(string[] paths)
    {
        SaveFileDialog dialog = new()
        {
            Title = LanguageManager.Get("ContextSelectOutput"),
            Filter = LanguageManager.Get("CreateArchiveFilter"),
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Path.GetDirectoryName(paths[0])
        };
        if (dialog.ShowDialog() != true) return;
        string outputPath = dialog.FileName;
        FileDetector.FileType type = FileDetector.GetTypeFromCreateFilterIndex(dialog.FilterIndex);
        if (!ArchivePasswordWindow.TryGetCreationPassword(null, Path.GetFileName(outputPath), type, out string? password)) return;
        ContextOperationWindow window = new(LanguageManager.Get("ContextCompressProgress"), async (progress, token) =>
        {
            await new ArchiveWorkerClient().CreateAsync(paths, outputPath, type, progress, token, password);
            if (!File.Exists(outputPath))
                throw new StageException("CTXCM0006", string.Format(LanguageManager.Get("CompressionOutputMissing"), outputPath)); //CTXCM0006
        });
        window.ShowDialog();
    }

    private static void RunOpen(string[] paths)
    {
        if (paths.Length != 1)
        {
            MessageBox.Show(LanguageManager.Get("ContextOpenMultiple"), LanguageManager.Get("ApplicationTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new MainWindow(paths[0]).ShowDialog();
    }

    private static async Task ExtractManyAsync(string[] paths, ConcurrentDictionary<string, string?> passwords,
        Func<ArchiveConflict, CancellationToken, Task<ConflictChoice>> conflictResolver,
        IProgress<ArchiveProgress> progress, CancellationToken token)
    {
        using CancellationTokenSource batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationToken operationToken = batchCancellation.Token;
        using SemaphoreSlim throttle = new(Math.Max(1, Math.Min(Environment.ProcessorCount, 4)));
        using SemaphoreSlim passwordPromptLock = new(1, 1);
        ConcurrentDictionary<string, int> percentages = new(StringComparer.OrdinalIgnoreCase);
        Task[] tasks = paths.Select(async path =>
        {
            await throttle.WaitAsync(operationToken);
            try
            {
                ArchiveWorkerClient client = new();
                string parent = Path.GetDirectoryName(path) ?? throw new StageException("CTXCM0005", LanguageManager.Get("ContextNoFiles")); //CTXCM0005
                string destination = Path.Combine(parent, Path.GetFileNameWithoutExtension(path));
                progress.Report(new ArchiveProgress(0, string.Format(LanguageManager.Get("ContextDestinationStatus"), destination)));
                passwords.TryGetValue(path, out string? password);
                NestedTarInfo nested;
                while (true)
                {
                    try
                    {
                        nested = await client.AnalyzeNestedTarAsync(path, operationToken, password);
                        break;
                    }
                    catch (StageException exception) when (exception.StageCode is "PWDAR0001" or "PWDAR0002")
                    {
                        await passwordPromptLock.WaitAsync(operationToken);
                        try
                        {
                            if (!ArchivePasswordWindow.TryGetExtractionPassword(null, Path.GetFileName(path),
                                    MessageTipGenerator.GenerateTip(exception.StageCode, exception.Message), out password))
                            {
                                batchCancellation.Cancel();
                                throw new OperationCanceledException(operationToken);
                            }
                            passwords[path] = password;
                        }
                        finally { passwordPromptLock.Release(); }
                    }
                }
                Directory.CreateDirectory(destination);
                Progress<ArchiveProgress> fileProgress = new(item =>
                {
                    percentages[path] = item.Percentage;
                    int overall = percentages.Values.Sum() / paths.Length;
                    progress.Report(new ArchiveProgress(overall, $"{Path.GetFileName(path)}: {item.EntryKey}"));
                });
                async Task<ConflictChoice> ResolveBatchConflictAsync(ArchiveConflict conflict, CancellationToken conflictToken)
                {
                    ConflictChoice choice = await conflictResolver(conflict, conflictToken);
                    if (choice == ConflictChoice.Cancel) batchCancellation.Cancel();
                    return choice;
                }
                if (nested.FlattenAutomatically)
                    await client.ExtractNestedTarsAsync(path, nested.TarEntryKeys, destination, null, false, OverwritePolicy.Ask,
                        fileProgress, operationToken, password, ResolveBatchConflictAsync);
                else
                    await client.ExtractAsync(path, destination, null, OverwritePolicy.Ask, fileProgress, operationToken, password, ResolveBatchConflictAsync);
                percentages[path] = 100;
            }
            finally
            {
                throttle.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        progress.Report(new ArchiveProgress(100, string.Empty));
    }

}
