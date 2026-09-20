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
            MessageBox.Show(MessageTipGenerator.GenerateTip(stageCode, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //CTXCM0003
        }
    }

    private static void RunExtract(string[] paths)
    {
        if (paths.Any(path => !ArchiveCapabilities.CanOpen(FileDetector.DetectFileType(path))))
            throw new StageException("CTXCM0004", LanguageManager.Get("ContextAllArchivesRequired")); //CTXCM0004
        OverwritePolicy policy = AskOverwritePolicy();
        if (policy == OverwritePolicy.Cancel) return;
        using ContextOperationForm form = new(LanguageManager.Get("ContextExtractProgress"), (progress, token) => ExtractManyAsync(paths, policy, progress, token));
        Application.Run(form);
    }

    private static void RunCompress(string[] paths)
    {
        using SaveFileDialog dialog = new()
        {
            Title = LanguageManager.Get("ContextSelectOutput"),
            Filter = LanguageManager.Get("CreateArchiveFilter"),
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Path.GetDirectoryName(paths[0])
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        FileDetector.FileType type = FileDetector.GetTypeFromCreateFilterIndex(dialog.FilterIndex);
        using ContextOperationForm form = new(LanguageManager.Get("ContextCompressProgress"), (progress, token) =>
            new ArchiveWorkerClient().CreateAsync(paths, dialog.FileName, type, progress, token));
        Application.Run(form);
    }

    private static void RunOpen(string[] paths)
    {
        if (paths.Length != 1)
        {
            MessageBox.Show(LanguageManager.Get("ContextOpenMultiple"), LanguageManager.Get("ApplicationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.Run(new Form1(paths[0]));
    }

    private static async Task ExtractManyAsync(string[] paths, OverwritePolicy policy, IProgress<ArchiveProgress> progress, CancellationToken token)
    {
        using SemaphoreSlim throttle = new(Math.Max(1, Math.Min(Environment.ProcessorCount, 4)));
        ConcurrentDictionary<string, int> percentages = new(StringComparer.OrdinalIgnoreCase);
        Task[] tasks = paths.Select(async path =>
        {
            await throttle.WaitAsync(token);
            try
            {
                ArchiveWorkerClient client = new();
                NestedTarInfo nested = await client.AnalyzeNestedTarAsync(path, token);
                string destination = Path.GetDirectoryName(path) ?? throw new StageException("CTXCM0005", LanguageManager.Get("ContextNoFiles")); //CTXCM0005
                Progress<ArchiveProgress> fileProgress = new(item =>
                {
                    percentages[path] = item.Percentage;
                    int overall = percentages.Values.Sum() / paths.Length;
                    progress.Report(new ArchiveProgress(overall, $"{Path.GetFileName(path)}: {item.EntryKey}"));
                });
                if (nested.FlattenAutomatically)
                    await client.ExtractNestedTarsAsync(path, nested.TarEntryKeys, destination, null, false, policy, fileProgress, token);
                else
                    await client.ExtractAsync(path, destination, null, policy, fileProgress, token);
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

    private static OverwritePolicy AskOverwritePolicy()
    {
        DialogResult result = MessageBox.Show(LanguageManager.Get("OverwritePolicyPrompt"), LanguageManager.Get("OverWriteOrNot"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result switch { DialogResult.Yes => OverwritePolicy.OverwriteAll, DialogResult.No => OverwritePolicy.SkipAll, _ => OverwritePolicy.Cancel };
    }
}
