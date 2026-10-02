using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: BOPTW. The dialog only validates user choices; batch execution
// and its cancellation are owned by the main window's operation coordinator.
internal sealed partial class ArchiveBatchOptionsWindow : Window
{
    private readonly bool _conversion;

    public ArchiveBatchOptionsWindow(bool conversion)
    {
        InitializeComponent();
        _conversion = conversion;
        Title = LanguageManager.Get(conversion ? "BatchConvertMenu" : "BatchExtractMenu");
        descriptionText.Text = LanguageManager.Get(conversion ? "BatchConvertDescription" : "BatchExtractDescription");
        formatLabel.Text = LanguageManager.Get("BatchTargetFormat");
        formatLabel.Visibility = formatCombo.Visibility = conversion ? Visibility.Visible : Visibility.Collapsed;
        formatCombo.Items.Add(new FormatChoice(FileDetector.FileType.Zip, LanguageManager.Get("Format_Zip")));
        formatCombo.Items.Add(new FormatChoice(FileDetector.FileType.SevenZip, LanguageManager.Get("Format_SevenZip")));
        formatCombo.Items.Add(new FormatChoice(FileDetector.FileType.Tar, LanguageManager.Get("Format_Tar")));
        formatCombo.SelectedIndex = 0;
        inputPasswordLabel.Text = LanguageManager.Get("BatchInputPassword");
        outputPasswordLabel.Text = LanguageManager.Get("BatchOutputPassword");
        outputPasswordConfirmLabel.Text = LanguageManager.Get("BatchOutputPasswordConfirm");
        encryptionNote.Text = LanguageManager.Get("BatchEncryptionNote");
        outputPasswordLabel.Visibility = outputPasswordBox.Visibility =
            outputPasswordConfirmLabel.Visibility = outputPasswordConfirmBox.Visibility =
            encryptionNote.Visibility = conversion ? Visibility.Visible : Visibility.Collapsed;
        confirmButton.Content = LanguageManager.Get("Confirm");
        cancelButton.Content = LanguageManager.Get("Cancel");
    }

    public FileDetector.FileType TargetType { get; private set; } = FileDetector.FileType.Zip;
    public string? InputPassword { get; private set; }
    public string? OutputPassword { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        TargetType = _conversion && formatCombo.SelectedItem is FormatChoice choice
            ? choice.Type : FileDetector.FileType.Zip;
        InputPassword = string.IsNullOrEmpty(inputPasswordBox.Password) ? null : inputPasswordBox.Password;
        OutputPassword = string.IsNullOrEmpty(outputPasswordBox.Password) ? null : outputPasswordBox.Password;
        if (_conversion && OutputPassword is not null)
        {
            if (TargetType == FileDetector.FileType.Tar)
            { Warn("BOPTW0001", "BatchTarPasswordUnsupported"); return; } //BOPTW0001
            if (OutputPassword.Any(character => character is < ' ' or > '~'))
            { Warn("BOPTW0002", "PasswordAsciiOnly"); return; } //BOPTW0002
            if (OutputPassword != outputPasswordConfirmBox.Password)
            { Warn("BOPTW0003", "PasswordMismatch"); return; } //BOPTW0003
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Warn(string stageCode, string key) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ErrorTitle"),
        MessageTipGenerator.GenerateTip(stageCode, LanguageManager.Get(key)));

    private sealed record FormatChoice(FileDetector.FileType Type, string Name)
    {
        public override string ToString() => Name;
    }
}
