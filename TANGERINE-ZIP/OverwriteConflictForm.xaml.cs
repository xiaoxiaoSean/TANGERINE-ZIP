using TANGERINE_ZIP.Services;

namespace TANGERINE_ZIP;

internal sealed partial class OverwriteConflictForm : Window
{
    public ConflictChoice Choice { get; private set; } = ConflictChoice.Cancel;

    public OverwriteConflictForm() => InitializeComponent();

    public OverwriteConflictForm(ArchiveConflict conflict)
    {
        InitializeComponent();
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        Title = LanguageManager.Get("ConflictTitle");
        WpfUi.SizeWindow(this, 0.6, 0.38);
        promptText.Text = LanguageManager.Get("ConflictPrompt");
        detailText.Text = string.Format(LanguageManager.Get("ConflictFileDetails"), conflict.EntryKey, conflict.TargetPath);
        AddButton(thisYesButton, "ConflictThisYes", ConflictChoice.ThisYes);
        AddButton(thisNoButton, "ConflictThisNo", ConflictChoice.ThisNo);
        AddButton(allYesButton, "ConflictAllYes", ConflictChoice.AllYes);
        AddButton(allNoButton, "ConflictAllNo", ConflictChoice.AllNo);
        AddButton(cancelTaskButton, "ConflictCancelTask", ConflictChoice.Cancel);
    }

    private void AddButton(Button button, string key, ConflictChoice choice)
    {
        button.Content = LanguageManager.Get(key);
        button.Click += (_, _) => { Choice = choice; DialogResult = true; };
    }

    public static ConflictChoice Ask(Window? owner, ArchiveConflict conflict)
    {
        var form = new OverwriteConflictForm(conflict);
        if (owner is not null) form.Owner = owner;
        form.ShowDialog();
        return form.Choice;
    }
}
