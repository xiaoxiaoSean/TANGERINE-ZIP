
namespace TANGERINE_ZIP;

public sealed partial class OverwriteDecisionWindow : Window
{
    private bool _allOverwrite;
    private bool _allSkip;
    private int _output = -1;

    public OverwriteDecisionWindow()
    {
        InitializeComponent();
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.6, 0.55);
        Title = LanguageManager.Get("OverWriteOrNot");
        descriptionText.Text = LanguageManager.Get("OverWriteText");
        foreach (string key in new[] { "OverWrite1", "OverWrite2", "OverWrite3", "OverWrite4" })
            _choicesList.Items.Add(LanguageManager.Get(key));
        executeButton.Content = LanguageManager.Get("Execute");
    }

    public void SyncBool(ref bool allOverWrite) => allOverWrite = _allOverwrite;
    public void SyncBool2(ref bool allSkip) => allSkip = _allSkip;
    public void GetResult(ref int input) => input = _output;

    private void Execute_Click(object sender, RoutedEventArgs e) => Execute();

    private void Execute()
    {
        switch (_choicesList.SelectedIndex)
        {
            case 0: _output = 1; break;
            case 1: _output = 2; break;
            case 2: _output = -99; _allOverwrite = true; break;
            case 3: _output = -99; _allSkip = true; break;
            default:
                MessageBox.Show(this, LanguageManager.Get("Suggestion1") + LanguageManager.Get("OverWriteSuggestion1") + LanguageManager.Get("ErrorCode1") + "OWFSIES" + _choicesList.SelectedIndex,
                    LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
        }
        Close();
    }
}
