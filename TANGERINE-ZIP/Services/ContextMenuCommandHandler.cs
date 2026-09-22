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
        ConcurrentDictionary<string, string?> passwords = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (!PasswordArchiveService.IsProtected(path)) continue;
            if (!ArchivePasswordForm.TryGetExtractionPassword(null, Path.GetFileName(path), null, out string? password)) return;
            passwords[path] = password;
        }
        using ContextOperationForm form = new(LanguageManager.Get("ContextExtractProgress"),
            (progress, token) => ExtractManyAsync(paths, passwords, policy, progress, token));
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
        if (!ArchivePasswordForm.TryGetCreationPassword(null, Path.GetFileName(dialog.FileName), out string? password)) return;
        using ContextOperationForm form = new(LanguageManager.Get("ContextCompressProgress"), (progress, token) =>
            new ArchiveWorkerClient().CreateAsync(paths, dialog.FileName, type, progress, token, password));
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

    private static async Task ExtractManyAsync(string[] paths, ConcurrentDictionary<string, string?> passwords,
        OverwritePolicy policy, IProgress<ArchiveProgress> progress, CancellationToken token)
    {
        using SemaphoreSlim throttle = new(Math.Max(1, Math.Min(Environment.ProcessorCount, 4)));
        using SemaphoreSlim passwordPromptLock = new(1, 1);
        ConcurrentDictionary<string, int> percentages = new(StringComparer.OrdinalIgnoreCase);
        Task[] tasks = paths.Select(async path =>
        {
            await throttle.WaitAsync(token);
            try
            {
                ArchiveWorkerClient client = new();
                passwords.TryGetValue(path, out string? password);
                NestedTarInfo nested;
                while (true)
                {
                    try
                    {
                        nested = await client.AnalyzeNestedTarAsync(path, token, password);
                        break;
                    }
                    catch (StageException exception) when (exception.StageCode is "PWDAR0001" or "PWDAR0002")
                    {
                        await passwordPromptLock.WaitAsync(token);
                        try
                        {
                            if (!ArchivePasswordForm.TryGetExtractionPassword(null, Path.GetFileName(path),
                                    MessageTipGenerator.GenerateTip(exception.StageCode, exception.Message), out password))
                                throw new OperationCanceledException(token);
                            passwords[path] = password;
                        }
                        finally { passwordPromptLock.Release(); }
                    }
                }
                string destination = Path.GetDirectoryName(path) ?? throw new StageException("CTXCM0005", LanguageManager.Get("ContextNoFiles")); //CTXCM0005
                Progress<ArchiveProgress> fileProgress = new(item =>
                {
                    percentages[path] = item.Percentage;
                    int overall = percentages.Values.Sum() / paths.Length;
                    progress.Report(new ArchiveProgress(overall, $"{Path.GetFileName(path)}: {item.EntryKey}"));
                });
                if (nested.FlattenAutomatically)
                    await client.ExtractNestedTarsAsync(path, nested.TarEntryKeys, destination, null, false, policy, fileProgress, token, password);
                else
                    await client.ExtractAsync(path, destination, null, policy, fileProgress, token, password);
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
