namespace TANGERINE_ZIP;

public sealed partial class AboutWindow : Window
{
    private const double MouseWhiteThickenRadius = 125.0;

    public AboutWindow()
    {
        InitializeComponent();
        string aboutD = "TZIP v1.1.0\nfounder:xiaoxiaoSean\nthanks all contributor\nthanks the author and all contributors  of rar\nthanks the author and all contributors of all nuget packages\n\nv1.1.0-1.0.0changelog:\nnew:compression password\nnew:first-layer right button menu for win11\nnew:quick window auto-close\nfix:cannot be extracted normally via right button menu";
        informationText.Text = aboutD;
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.6, 0.6);
        Title = LanguageManager.Get("ApplicationTitle");
        closeButton.Content = LanguageManager.Get("Close");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
