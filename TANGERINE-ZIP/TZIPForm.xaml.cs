namespace TANGERINE_ZIP;

public sealed partial class TZIPForm : Window
{
    public TZIPForm()
    {
        InitializeComponent();
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        Icon = WpfUi.WindowIcon(typeof(TZIPForm));
        Title = LanguageManager.Get("ApplicationTitle");
        informationText.Text = new System.ComponentModel.ComponentResourceManager(typeof(TZIPForm))
            .GetString("textBox1.Text") ?? string.Empty;
        closeButton.Content = LanguageManager.Get("Close");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
