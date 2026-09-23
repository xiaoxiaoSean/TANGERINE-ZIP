namespace TANGERINE_ZIP;

public sealed partial class AboutWindow : Window
{
    private const double MouseWhiteThickenRadius = 125.0;

    public AboutWindow()
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.6, 0.6);
        Title = LanguageManager.Get("ApplicationTitle");
        // The about-page text is loaded from the same culture-specific ResX as the
        // rest of the WPF interface; XAML contains no hard-coded English paragraph.
        informationText.Text = LanguageManager.Get("AboutInformation");
        closeButton.Content = LanguageManager.Get("Close");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
