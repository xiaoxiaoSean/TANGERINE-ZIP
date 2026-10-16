namespace TANGERINE_ZIP;

/// <summary>
/// Dedicated diagnostic window. Both dimensions are fractions of the current
/// work area, so translated messages and display scaling remain usable.
/// </summary>
internal sealed partial class ErrorDialogWindow : Window
{
    internal ErrorDialogWindow(Window? owner, string message)
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.46, 0.48);
        MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        Title = LanguageManager.Get("ErrorTitle");
        diagnosticText.Text = message;
        closeButton.Content = LanguageManager.Get("Confirm");
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = owner is null;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
