namespace TANGERINE_ZIP;

internal sealed partial class EncodingChoiceWindow : Window
{
    public string EncodingName { get; private set; } = "utf-8";

    public EncodingChoiceWindow()
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, 105);
        Title = LanguageManager.Get("EncodingMenu");
        descriptionText.Text = LanguageManager.Get("ToolSelectEncoding");
        confirmButton.Content = LanguageManager.Get("ToolReopen");
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        EncodingName = ((ComboBoxItem)encodingCombo.SelectedItem).Tag.ToString()!;
        DialogResult = true;
    }
}
