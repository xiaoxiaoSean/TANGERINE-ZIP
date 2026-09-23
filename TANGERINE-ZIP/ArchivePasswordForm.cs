using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;
using System.Windows.Controls.Primitives;

namespace TANGERINE_ZIP;

internal sealed class ArchivePasswordForm : Window
{
    private readonly bool _creating;
    private readonly CheckBox _enablePassword = new();
    private readonly PasswordBox _password = new();
    private readonly PasswordBox _confirmation = new();
    private readonly TextBox _visiblePassword = new();
    private readonly TextBox _visibleConfirmation = new();
    private readonly CheckBox _showPassword = new();

    public ArchivePasswordForm(string archiveName, bool creating, string? errorMessage = null, bool passwordSupported = true)
    {
        _creating = creating;
        WpfUi.Style(this);
        Title = LanguageManager.Get(creating ? "CreatePasswordTitle" : "EnterPasswordTitle");
        SizeToContent = SizeToContent.Height;
        Width = SystemParameters.WorkArea.Width * 0.4;
        ShowInTaskbar = false;
        var fields = new StackPanel { Margin = new Thickness(18) };
        fields.Children.Add(WpfUi.Text(string.Format(LanguageManager.Get(creating && !passwordSupported
            ? "PasswordFormatUnsupportedDescription" : creating ? "CreatePasswordDescription" : "EnterPasswordDescription"), archiveName)));
        _enablePassword.Content = LanguageManager.Get("EnableArchivePassword");
        _enablePassword.IsChecked = passwordSupported;
        _enablePassword.IsEnabled = passwordSupported;
        _enablePassword.Foreground = WpfUi.Foreground;
        if (creating) fields.Children.Add(_enablePassword);
        fields.Children.Add(WpfUi.Text(LanguageManager.Get("PasswordLabel")));
        fields.Children.Add(PasswordField(_password, _visiblePassword));
        if (creating)
        {
            fields.Children.Add(WpfUi.Text(LanguageManager.Get("ConfirmPasswordLabel")));
            fields.Children.Add(PasswordField(_confirmation, _visibleConfirmation));
        }
        _showPassword.Content = LanguageManager.Get("ShowPassword");
        _showPassword.Foreground = WpfUi.Foreground;
        _showPassword.Margin = new Thickness(4, 10, 4, 4);
        fields.Children.Add(_showPassword);
        if (!string.IsNullOrWhiteSpace(errorMessage))
            fields.Children.Add(new TextBlock { Text = errorMessage, Foreground = WpfUi.ErrorForeground, TextWrapping = TextWrapping.Wrap });
        var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        var confirm = WpfUi.Button(LanguageManager.Get("Confirm"));
        var cancel = WpfUi.Button(LanguageManager.Get("Cancel"));
        confirm.IsDefault = true;
        cancel.IsCancel = true;
        confirm.Click += Confirm_Click;
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        fields.Children.Add(buttons);
        Content = fields;
        _enablePassword.Checked += (_, _) => UpdateEnabledState();
        _enablePassword.Unchecked += (_, _) => UpdateEnabledState();
        _showPassword.Checked += (_, _) => ShowPasswords(true);
        _showPassword.Unchecked += (_, _) => ShowPasswords(false);
        Loaded += (_, _) => _password.Focus();
        UpdateEnabledState();
    }

    private static Grid PasswordField(PasswordBox hidden, TextBox visible)
    {
        var grid = new Grid { Margin = new Thickness(4) };
        hidden.Background = visible.Background = WpfUi.Surface;
        hidden.Foreground = visible.Foreground = WpfUi.Foreground;
        visible.Visibility = Visibility.Collapsed;
        grid.Children.Add(hidden);
        grid.Children.Add(visible);
        return grid;
    }

    private void ShowPasswords(bool show)
    {
        if (show) { _visiblePassword.Text = _password.Password; _visibleConfirmation.Text = _confirmation.Password; }
        else { _password.Password = _visiblePassword.Text; _confirmation.Password = _visibleConfirmation.Text; }
        _password.Visibility = _confirmation.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _visiblePassword.Visibility = _visibleConfirmation.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private string EnteredPassword => _showPassword.IsChecked == true ? _visiblePassword.Text : _password.Password;
    private string EnteredConfirmation => _showPassword.IsChecked == true ? _visibleConfirmation.Text : _confirmation.Password;
    public string? PasswordValue { get; private set; }

    private void UpdateEnabledState()
    {
        bool enabled = !_creating || _enablePassword.IsChecked == true;
        _password.IsEnabled = _visiblePassword.IsEnabled = _confirmation.IsEnabled = _visibleConfirmation.IsEnabled = _showPassword.IsEnabled = enabled;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_creating && _enablePassword.IsChecked != true) { PasswordValue = null; DialogResult = true; return; }
        if (string.IsNullOrEmpty(EnteredPassword)) { Warn("PWDFM0001", "ArchivePasswordRequired"); return; }
        if (_creating && EnteredPassword.Any(character => character is < ' ' or > '~')) { Warn("PWDFM0003", "PasswordAsciiOnly"); return; }
        if (_creating && !string.Equals(EnteredPassword, EnteredConfirmation, StringComparison.Ordinal)) { Warn("PWDFM0002", "PasswordMismatch"); return; }
        PasswordValue = EnteredPassword;
        DialogResult = true;
    }

    private void Warn(string code, string key) => MessageBox.Show(this,
        MessageTipGenerator.GenerateTip(code, LanguageManager.Get(key)), LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool TryGetCreationPassword(Window? owner, string archiveName, FileDetector.FileType type, out string? password)
    {
        ArchivePasswordForm form = new(archiveName, true, passwordSupported: ArchiveCapabilities.CanCreateWithPassword(type));
        if (owner is not null) form.Owner = owner;
        bool accepted = form.ShowDialog() == true;
        password = accepted ? form.PasswordValue : null;
        return accepted;
    }

    public static bool TryGetExtractionPassword(Window? owner, string archiveName, string? errorMessage, out string? password)
    {
        ArchivePasswordForm form = new(archiveName, false, errorMessage);
        if (owner is not null) form.Owner = owner;
        bool accepted = form.ShowDialog() == true;
        password = accepted ? form.PasswordValue : null;
        return accepted;
    }
}
