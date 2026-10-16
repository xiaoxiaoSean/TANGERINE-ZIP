using TANGERINE_ZIP.Services;

namespace TANGERINE_ZIP.Tools;

/// <summary>
/// Single static entry point for diagnostic dialogs. Callers supply the
/// boundary's fallback stage; a StageException keeps its original stage.
/// </summary>
internal static class ErrorDialog
{
    internal static void Show(Window? owner, string fallbackStageCode, Exception exception)
    {
        string stageCode = exception is StageException staged ? staged.StageCode : fallbackStageCode;
        Show(owner, stageCode, exception.Message, exception);
    }

    internal static void Show(Window? owner, string stageCode, string detail, Exception? exception = null) =>
        ShowFormatted(owner, MessageTipGenerator.GenerateTip(stageCode, detail, exception));

    internal static void ShowFormatted(Window? owner, string message) =>
        new ErrorDialogWindow(owner, message).ShowDialog();
}
