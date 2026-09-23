using TANGERINE_ZIP.Services;

namespace TANGERINE_ZIP;

internal sealed class OverwriteConflictForm : Window
{
    public ConflictChoice Choice { get; private set; } = ConflictChoice.Cancel;

    public OverwriteConflictForm(ArchiveConflict conflict)
    {
        WpfUi.Style(this);
        Title = LanguageManager.Get("ConflictTitle");
        WpfUi.SizeWindow(this, 0.6, 0.38);
        ShowInTaskbar = false;
        var root = WpfUi.Grid(2, 2, 1);
        root.Margin = new Thickness(18);
        WpfUi.Add(root, WpfUi.Text(LanguageManager.Get("ConflictPrompt")), 0);
        WpfUi.Add(root, new TextBox
        {
            Text = string.Format(LanguageManager.Get("ConflictFileDetails"), conflict.EntryKey, conflict.TargetPath),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = WpfUi.Surface,
            Foreground = WpfUi.Foreground, Margin = new Thickness(4)
        }, 1);
        var buttons = new UniformGrid { Rows = 1, Margin = new Thickness(0, 12, 0, 0) };
        AddButton(buttons, "ConflictThisYes", ConflictChoice.ThisYes);
        AddButton(buttons, "ConflictThisNo", ConflictChoice.ThisNo);
        AddButton(buttons, "ConflictAllYes", ConflictChoice.AllYes);
        AddButton(buttons, "ConflictAllNo", ConflictChoice.AllNo);
        AddButton(buttons, "ConflictCancelTask", ConflictChoice.Cancel);
        WpfUi.Add(root, buttons, 2);
        Content = root;
    }

    private void AddButton(Panel parent, string key, ConflictChoice choice)
    {
        var button = WpfUi.Button(LanguageManager.Get(key));
        button.Click += (_, _) => { Choice = choice; DialogResult = true; };
        parent.Children.Add(button);
    }

    public static ConflictChoice Ask(Window? owner, ArchiveConflict conflict)
    {
        var form = new OverwriteConflictForm(conflict);
        if (owner is not null) form.Owner = owner;
        form.ShowDialog();
        return form.Choice;
    }
}
