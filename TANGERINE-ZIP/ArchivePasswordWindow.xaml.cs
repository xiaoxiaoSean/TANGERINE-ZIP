using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

internal sealed partial class ArchivePasswordWindow : Window
{
    private readonly bool _creating;

    public ArchivePasswordWindow() => InitializeComponent();

    public ArchivePasswordWindow(string archiveName, bool creating, string? errorMessage = null, bool passwordSupported = true)
    {
        InitializeComponent();
        _creating = creating;
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        Width = SystemParameters.WorkArea.Width * 0.4;
        Title = LanguageManager.Get(creating ? "CreatePasswordTitle" : "EnterPasswordTitle");
        descriptionText.Text = string.Format(LanguageManager.Get(creating && !passwordSupported
            ? "PasswordFormatUnsupportedDescription" : creating ? "CreatePasswordDescription" : "EnterPasswordDescription"), archiveName);
        _enablePassword.Content = LanguageManager.Get("EnableArchivePassword");
        _enablePassword.IsChecked = passwordSupported;
        _enablePassword.IsEnabled = passwordSupported;
        _enablePassword.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        passwordText.Text = LanguageManager.Get("PasswordLabel");
        confirmationText.Text = LanguageManager.Get("ConfirmPasswordLabel");
        confirmationFields.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        _showPassword.Content = LanguageManager.Get("ShowPassword");
        confirmButton.Content = LanguageManager.Get("Confirm");
        cancelButton.Content = LanguageManager.Get("Cancel");
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            errorText.Text = errorMessage;
            errorText.Visibility = Visibility.Visible;
        }
        _enablePassword.Checked += (_, _) => UpdateEnabledState();
        _enablePassword.Unchecked += (_, _) => UpdateEnabledState();
        _showPassword.Checked += (_, _) => ShowPasswords(true);
        _showPassword.Unchecked += (_, _) => ShowPasswords(false);
        Loaded += (_, _) => _password.Focus();
        UpdateEnabledState();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowPasswords(bool show)
    {
        if (show) { _visiblePassword.Text = _password.Password; _visibleConfirmation.Text = _confirmButtonation.Password; }
        else { _password.Password = _visiblePassword.Text; _confirmButtonation.Password = _visibleConfirmation.Text; }
        _password.Visibility = _confirmButtonation.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _visiblePassword.Visibility = _visibleConfirmation.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private string EnteredPassword => _showPassword.IsChecked == true ? _visiblePassword.Text : _password.Password;
    private string EnteredConfirmation => _showPassword.IsChecked == true ? _visibleConfirmation.Text : _confirmButtonation.Password;
    public string? PasswordValue { get; private set; }

    private void UpdateEnabledState()
    {
        bool enabled = !_creating || _enablePassword.IsChecked == true;
        _password.IsEnabled = _visiblePassword.IsEnabled = _confirmButtonation.IsEnabled = _visibleConfirmation.IsEnabled = _showPassword.IsEnabled = enabled;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
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
        ArchivePasswordWindow window = new(archiveName, true, passwordSupported: ArchiveCapabilities.CanCreateWithPassword(type));
        if (owner is not null) window.Owner = owner;
        bool accepted = window.ShowDialog() == true;
        password = accepted ? window.PasswordValue : null;
        return accepted;
    }

    public static bool TryGetExtractionPassword(Window? owner, string archiveName, string? errorMessage, out string? password)
    {
        ArchivePasswordWindow window = new(archiveName, false, errorMessage);
        if (owner is not null) window.Owner = owner;
        bool accepted = window.ShowDialog() == true;
        password = accepted ? window.PasswordValue : null;
        return accepted;
    }
}
