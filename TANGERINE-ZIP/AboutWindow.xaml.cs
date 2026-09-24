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
        // Keep the original English details unchanged apart from replacing
        // both occurrences of the current application version.
        string aboutDetails = $"TZIP v{currentVersion}\nfounder:xiaoxiaoSean\nthanks all contributor\nthanks the author and all contributors  of rar\nthanks the author and all contributors of all nuget packages\n\nv{currentVersion}-1.0.0changelog:\nnew:compression password\nnew:first-layer right button menu for win11\nnew:quick window auto-close\nfix:cannot be extracted normally via right button menu";
        informationText.Text = aboutDetails;
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.6, 0.6);
        Title = LanguageManager.Get("ApplicationTitle");
        closeButton.Content = LanguageManager.Get("Close");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
