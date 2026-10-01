using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

internal sealed partial class CompressionOptionsWindow : Window
{
    private readonly bool _supportsAdvanced;
    private readonly bool _supportsPassword;
    private readonly FileDetector.FileType _type;
    public CompressionOptions? Options { get; private set; }

    public CompressionOptionsWindow(string archiveName, FileDetector.FileType type)
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.6, 0.8);
        FontSize = SystemFonts.MessageFontSize;
        MouseWhiteThickening.Attach(this, 105);
        _supportsAdvanced = type is FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Rar;
        _supportsPassword = ArchiveCapabilities.CanCreateWithPassword(type);
        _type = type;
        Title = LanguageManager.Get("CompressionOptionsTitle");
        descriptionText.Text = string.Format(LanguageManager.Get("CompressionOptionsDescription"), archiveName);
        enablePassword.Content = LanguageManager.Get("EnableArchivePassword");
        enablePassword.IsEnabled = _supportsPassword;
        passwordRisk.Text = LanguageManager.Get("CompressionPasswordRisk");
        advancedCheck.Content = LanguageManager.Get("CompressionAdvanced");
        advancedCheck.IsEnabled = _supportsAdvanced;
        methodLabel.Text = LanguageManager.Get("CompressionMethod");
        methodRisk.Text = LanguageManager.Get("CompressionMethodRisk");
        levelLabel.Text = LanguageManager.Get("CompressionLevel");
        levelRisk.Text = LanguageManager.Get("CompressionLevelRisk");
        dictionaryLabel.Text = LanguageManager.Get("CompressionDictionary");
        dictionaryRisk.Text = LanguageManager.Get("CompressionDictionaryRisk");
        threadsLabel.Text = LanguageManager.Get("CompressionThreads");
        threadsRisk.Text = LanguageManager.Get("CompressionThreadsRisk");
        memoryLabel.Text = LanguageManager.Get("CompressionMemory");
        memoryRisk.Text = LanguageManager.Get("CompressionMemoryRisk");
        volumeLabel.Text = LanguageManager.Get("CompressionVolume");
        volumeRisk.Text = LanguageManager.Get("CompressionVolumeRisk");
        confirmButton.Content = LanguageManager.Get("Confirm");
        cancelButton.Content = LanguageManager.Get("Cancel");
        if (type == FileDetector.FileType.Zip) methodCombo.SelectedIndex = 2;
        if (type == FileDetector.FileType.Rar)
        {
            methodCombo.Items.RemoveAt(2);
            methodCombo.Items.RemoveAt(1);
            for (int index = 9; index > 5; index--) levelCombo.Items.RemoveAt(index);
        }
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "ADVANCED_MENU_ON")) &&
            new FileInfo(Path.Combine(AppContext.BaseDirectory, "ADVANCED_MENU_ON")).Length == 0)
        {
            advancedCheck.Visibility = Visibility.Collapsed;
            advancedPanel.Visibility = _supportsAdvanced ? Visibility.Visible : Visibility.Collapsed;
        }
        Password_Changed(this, new RoutedEventArgs());
    }

    private void Password_Changed(object sender, RoutedEventArgs e) =>
        passwordBox.IsEnabled = enablePassword.IsChecked == true;

    private void Advanced_Checked(object sender, RoutedEventArgs e)
    {
        // Expanding is a one-way decision for this task. A new task resets it.
        advancedPanel.Visibility = Visibility.Visible;
        advancedCheck.IsEnabled = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        bool advanced = advancedPanel.Visibility == Visibility.Visible;
        string? password = enablePassword.IsChecked == true ? passwordBox.Password : null;
        if (enablePassword.IsChecked == true && string.IsNullOrEmpty(password)) { Warn("ArchivePasswordRequired"); return; }
        if (password is not null && password.Any(c => c is < ' ' or > '~')) { Warn("PasswordAsciiOnly"); return; }
        if (!int.TryParse(dictionaryBox.Text, out int dictionary) || dictionary is < 1 or > 1024 ||
            !int.TryParse(threadsBox.Text, out int threads) || threads < 0 || threads > 128 ||
            !int.TryParse(memoryBox.Text, out int memory) || memory < 0 || memory > 1048576 ||
            !int.TryParse(volumeBox.Text, out int volume) || volume < 0 || volume > 1048576)
        { Warn("CompressionInvalidOptions"); return; }
        string method = ((ComboBoxItem)methodCombo.SelectedItem).Content.ToString()!;
        if (advanced && (_type == FileDetector.FileType.Rar && levelCombo.SelectedIndex > 5 ||
            method == "LZMA2" && _type == FileDetector.FileType.Zip ||
            method == "Deflate" && _type == FileDetector.FileType.SevenZip))
        { Warn("CompressionInvalidOptions"); return; }
        Options = new CompressionOptions(password, advanced, levelCombo.SelectedIndex, method,
            dictionary, threads, memory, volume);
        DialogResult = true;
    }

    private void Warn(string key) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ErrorTitle"), LanguageManager.Get(key));
}
