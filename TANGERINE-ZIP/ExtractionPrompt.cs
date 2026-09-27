using TANGERINE_ZIP.Services;

namespace TANGERINE_ZIP;

internal static class ExtractionPrompt
{
    public static Task<ExtractionAnswer> AskAsync(Window? owner, ExtractionIssue issue, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (issue.Kind == ExtractionIssueKind.PathTraversal)
        {
            ThemedPromptWindow.Inform(owner, LanguageManager.Get("UnsafeArchivePath"),
                string.Format(LanguageManager.Get("TraversalChooseFolder"), issue.EntryKey));
            OpenFolderDialog folder = new() { Title = LanguageManager.Get("TraversalChooseFolderTitle") };
            return Task.FromResult(folder.ShowDialog(owner) == true
                ? new ExtractionAnswer(ExtractionDecision.Redirect, folder.FolderName)
                : new ExtractionAnswer(ExtractionDecision.Stop));
        }
        string message = issue.Kind == ExtractionIssueKind.SuspiciousSize
            ? string.Format(LanguageManager.Get("BombPrompt"), issue.EntryKey, issue.Detail)
            : string.Format(LanguageManager.Get("ExtractFailurePrompt"), issue.EntryKey, issue.Detail);
        MessageBoxResult answer = ThemedPromptWindow.Ask(owner, LanguageManager.Get("extractText"), message,
            (LanguageManager.Get(issue.Kind == ExtractionIssueKind.SuspiciousSize ? "PromptContinue" : "PromptRetry"), MessageBoxResult.Yes),
            (LanguageManager.Get("PromptSkip"), MessageBoxResult.No),
            (LanguageManager.Get("PromptStop"), MessageBoxResult.Cancel));
        return Task.FromResult(new ExtractionAnswer(answer switch
        {
            MessageBoxResult.Yes => issue.Kind == ExtractionIssueKind.SuspiciousSize ? ExtractionDecision.Continue : ExtractionDecision.Retry,
            MessageBoxResult.No => ExtractionDecision.Skip,
            _ => ExtractionDecision.Stop
        }));
    }
}
