using TANGERINE_ZIP.Resources;

namespace TANGERINE_ZIP;

public sealed class OverWriteOrNotForm : Window
{
    private readonly ListBox _choices = WpfUi.List();
    private bool _allOverwrite;
    private bool _allSkip;
    private int _output = -1;

    public OverWriteOrNotForm()
    {
        WpfUi.Style(this);
        Icon = WpfUi.WindowIcon(typeof(OverWriteOrNotForm));
        WpfUi.SizeWindow(this, 0.6, 0.55);
        Title = LanguageManager.Get("OverWriteOrNot");
        var root = WpfUi.Grid(2, 3);
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        WpfUi.Add(root, new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = WpfUi.Text(LanguageManager.Get("OverWriteText"))
        }, 0);
        WpfUi.Add(root, WpfUi.Logo(TZIPResource.TZIP), 0, 1);
        foreach (string key in new[] { "OverWrite1", "OverWrite2", "OverWrite3", "OverWrite4" })
            _choices.Items.Add(LanguageManager.Get(key));
        WpfUi.Add(root, _choices, 1);
        var execute = WpfUi.Button(LanguageManager.Get("Execute"));
        execute.Click += (_, _) => Execute();
        WpfUi.Add(root, execute, 1, 1);
        Content = root;
    }

    public void SyncBool(ref bool allOverWrite) => allOverWrite = _allOverwrite;
    public void SyncBool2(ref bool allSkip) => allSkip = _allSkip;
    public void GetResult(ref int input) => input = _output;

    private void Execute()
    {
        switch (_choices.SelectedIndex)
        {
            case 0: _output = 1; break;
            case 1: _output = 2; break;
            case 2: _output = -99; _allOverwrite = true; break;
            case 3: _output = -99; _allSkip = true; break;
            default:
                MessageBox.Show(this, LanguageManager.Get("Suggestion1") + LanguageManager.Get("OverWriteSuggestion1") + LanguageManager.Get("ErrorCode1") + "OWFSIES" + _choices.SelectedIndex,
                    LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
        }
        Close();
    }
}
