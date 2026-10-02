using System.Text;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: ACOMW. The editor only collects text; the worker performs the
// atomic ZIP update after this dialog has closed.
internal sealed partial class ArchiveCommentWindow : Window
{
    public ArchiveCommentWindow(string archiveName, string initialComment)
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.48, 0.48);
        Title = LanguageManager.Get("CommentMenu");
        descriptionText.Text = string.Format(LanguageManager.Get("CommentDescription"), archiveName);
        commentBox.Text = initialComment;
        saveButton.Content = LanguageManager.Get("CommentSave");
        cancelButton.Content = LanguageManager.Get("Cancel");
    }

    public string CommentText => commentBox.Text;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Encoding.UTF8.GetByteCount(commentBox.Text) > ushort.MaxValue)
        {
            ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"),
                MessageTipGenerator.GenerateTip("ACOMW0001", LanguageManager.Get("CommentTooLong"))); //ACOMW0001
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
