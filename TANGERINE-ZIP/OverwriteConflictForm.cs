using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: CNFFM
internal sealed class OverwriteConflictForm : Form
{
    public OverwriteConflictForm(ArchiveConflict conflict)
    {
        Text = LanguageManager.Get("ConflictTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(680, 250);

        Label explanation = new()
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12),
            Text = LanguageManager.Get("ConflictPrompt")
        };
        TextBox path = new()
        {
            Dock = DockStyle.Top,
            Height = 70,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Format(LanguageManager.Get("ConflictFileDetails"), conflict.EntryKey, conflict.TargetPath)
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        AddButton(buttons, "ConflictThisYes", ConflictChoice.ThisYes);
        AddButton(buttons, "ConflictThisNo", ConflictChoice.ThisNo);
        AddButton(buttons, "ConflictAllYes", ConflictChoice.AllYes);
        AddButton(buttons, "ConflictAllNo", ConflictChoice.AllNo);
        AddButton(buttons, "ConflictCancelTask", ConflictChoice.Cancel);
        Controls.Add(buttons);
        Controls.Add(path);
        Controls.Add(explanation);
        DarkTheme.Apply(this);
    }

    public ConflictChoice Choice { get; private set; } = ConflictChoice.Cancel;

    private void AddButton(Control parent, string resourceKey, ConflictChoice choice)
    {
        Button button = new() { Text = LanguageManager.Get(resourceKey), Width = 122, Height = 38, Margin = new Padding(4) };
        button.Click += (_, _) => { Choice = choice; DialogResult = DialogResult.OK; Close(); };
        parent.Controls.Add(button);
    }

    public static ConflictChoice Ask(IWin32Window? owner, ArchiveConflict conflict)
    {
        using OverwriteConflictForm form = new(conflict);
        if (owner is null) form.ShowDialog(); else form.ShowDialog(owner);
        return form.Choice;
    }
}
