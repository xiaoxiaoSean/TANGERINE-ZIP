
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: OVDWN (OverwriteDecisionWindow)
public sealed partial class OverwriteDecisionWindow : Window
{
    private const double MouseWhiteThickenRadius = 115.0;
    private bool _allOverwrite;
    private bool _allSkip;
    private int _output = -1;

    public OverwriteDecisionWindow()
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
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
                string detail = LanguageManager.Get("Suggestion1") + LanguageManager.Get("OverWriteSuggestion1");
                MessageBox.Show(this, MessageTipGenerator.GenerateTip("OVDWN0001", detail), //OVDWN0001
                    LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
        }
        Close();
    }
}
