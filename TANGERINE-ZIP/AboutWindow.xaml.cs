using TANGERINE_ZIP.Services;

namespace TANGERINE_ZIP;

// Stage head: ABTWN (AboutWindow)
public sealed partial class AboutWindow : Window
{
    private const double MouseWhiteThickenRadius = 125.0;

    public AboutWindow()
    {
        InitializeComponent();
        // Read the compiled assembly version rather than duplicating a
        // literal. The project Version property also supplies Windows file
        // properties, so this text cannot drift from the published EXE.
        string currentVersion = typeof(AboutWindow).Assembly.GetName().Version?.ToString(4)
            ?? throw new StageException("ABTWN0001", LanguageManager.Get("VersionUnavailable")); //ABTWN0001
        informationText.Text = string.Format(LanguageManager.Get("AboutDetails"), currentVersion);
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.6, 0.6);
        Title = LanguageManager.Get("ApplicationTitle");
        closeButton.Content = LanguageManager.Get("Close");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
