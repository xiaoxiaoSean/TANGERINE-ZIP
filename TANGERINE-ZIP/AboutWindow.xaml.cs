namespace TANGERINE_ZIP;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.6, 0.6);
        Title = LanguageManager.Get("ApplicationTitle");
        closeButton.Content = LanguageManager.Get("Close");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
