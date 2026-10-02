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
        profilePanel.Visibility = _supportsAdvanced ? Visibility.Visible : Visibility.Collapsed;
        profileNameLabel.Text = LanguageManager.Get("ProfileNameLabel");
        savedProfilesLabel.Text = LanguageManager.Get("SavedProfilesLabel");
        saveProfileButton.Content = LanguageManager.Get("ProfileSaveButton");
        loadProfileButton.Content = LanguageManager.Get("ProfileLoadButton");
        deleteProfileButton.Content = LanguageManager.Get("ProfileDeleteButton");
        profileNote.Text = LanguageManager.Get("ProfileNoPasswordsNote");
        Title = LanguageManager.Get("CompressionOptionsTitle");
        descriptionText.Text = string.Format(LanguageManager.Get("CompressionOptionsDescription"), archiveName);
        enablePassword.Content = LanguageManager.Get("EnableArchivePassword");
        savedPasswordButton.Content = LanguageManager.Get("VaultChooseButton");
        enablePassword.IsEnabled = _supportsPassword;
        passwordRisk.Text = LanguageManager.Get("CompressionPasswordRisk");
        advancedCheck.Content = LanguageManager.Get("CompressionAdvanced");
        advancedCheck.IsEnabled = _supportsAdvanced;
        methodLabel.Text = LanguageManager.Get("CompressionMethod");
        defaultMethodItem.Content = LanguageManager.Get("CompressionDefaultMethod");
        methodRisk.Text = LanguageManager.Get("CompressionMethodRisk");
        levelLabel.Text = LanguageManager.Get("CompressionLevel");
        levelRisk.Text = LanguageManager.Get("CompressionLevelRisk");
        solidLabel.Text = LanguageManager.Get("SolidModeLabel");
        solidDefaultItem.Content = LanguageManager.Get("SolidModeDefault");
        solidOnItem.Content = LanguageManager.Get("SolidModeOn");
        solidOffItem.Content = LanguageManager.Get("SolidModeOff");
        solidLabel.Visibility = solidCombo.Visibility =
            type is FileDetector.FileType.SevenZip or FileDetector.FileType.Rar
                ? Visibility.Visible : Visibility.Collapsed;
        dictionaryLabel.Text = LanguageManager.Get("CompressionDictionary");
        dictionaryRisk.Text = LanguageManager.Get("CompressionDictionaryRisk");
        threadsLabel.Text = LanguageManager.Get("CompressionThreads");
        threadsRisk.Text = LanguageManager.Get("CompressionThreadsRisk");
        memoryLabel.Text = LanguageManager.Get("CompressionMemory");
        memoryRisk.Text = LanguageManager.Get("CompressionMemoryRisk");
        volumeLabel.Text = LanguageManager.Get("CompressionVolume");
        volumeRisk.Text = LanguageManager.Get("CompressionVolumeRisk");
        recoveryLabel.Text = LanguageManager.Get("RecoveryPercentLabel");
        recoveryLabel.Visibility = recoveryBox.Visibility =
            type == FileDetector.FileType.Rar ? Visibility.Visible : Visibility.Collapsed;
        excludeLabel.Text = LanguageManager.Get("ExcludePatternsLabel");
        excludeNote.Text = LanguageManager.Get("ExcludePatternsNote");
        selfExtractingCheck.Content = LanguageManager.Get("SfxOption");
        selfExtractingNote.Text = LanguageManager.Get("SfxNote");
        selfExtractingCheck.Visibility = selfExtractingNote.Visibility =
            type == FileDetector.FileType.SevenZip ? Visibility.Visible : Visibility.Collapsed;
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
        if (_supportsAdvanced) RefreshProfiles();
    }

    private void Password_Changed(object sender, RoutedEventArgs e) =>
        passwordBox.IsEnabled = savedPasswordButton.IsEnabled = enablePassword.IsChecked == true;

    private void SavedPassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PasswordVaultWindow vault = new(selecting: true) { Owner = this };
            if (vault.ShowDialog() == true && vault.SelectedPassword is string password)
                passwordBox.Password = password;
        }
        catch (Exception error) { ShowProfileError("COPTW0008", error); } //COPTW0008
    }

    private void Advanced_Checked(object sender, RoutedEventArgs e)
    {
        // Expanding is a one-way decision for this task. A new task resets it.
        advancedPanel.Visibility = Visibility.Visible;
        advancedCheck.IsEnabled = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Options = ReadOptions();
        if (Options is not null) DialogResult = true;
    }

    private CompressionOptions? ReadOptions()
    {
        bool advanced = advancedPanel.Visibility == Visibility.Visible;
        string? password = enablePassword.IsChecked == true ? passwordBox.Password : null;
        if (enablePassword.IsChecked == true && string.IsNullOrEmpty(password)) { Warn("ArchivePasswordRequired"); return null; }
        if (password is not null && password.Any(c => c is < ' ' or > '~')) { Warn("PasswordAsciiOnly"); return null; }
        if (!int.TryParse(dictionaryBox.Text, out int dictionary) || dictionary is < 1 or > 1024 ||
            !int.TryParse(threadsBox.Text, out int threads) || threads < 0 || threads > 128 ||
            !int.TryParse(memoryBox.Text, out int memory) || memory < 0 || memory > 1048576 ||
            !int.TryParse(volumeBox.Text, out int volume) || volume < 0 || volume > 1048576)
        { Warn("CompressionInvalidOptions"); return null; }
        string method = ((ComboBoxItem)methodCombo.SelectedItem).Tag.ToString()!;
        string solidMode = ((ComboBoxItem)solidCombo.SelectedItem).Tag.ToString()!;
        if (!int.TryParse(recoveryBox.Text, out int recoveryPercent) || recoveryPercent is < 0 or > 10)
        {
            ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"),
                MessageTipGenerator.GenerateTip("COPTW0002", LanguageManager.Get("RecoveryPercentInvalid"))); //COPTW0002
            return null;
        }
        string[] excludes = excludeBox.Text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (excludes.Any(pattern => pattern.Length > 260 || pattern.Contains('\r') || pattern.Contains('\n')))
        {
            ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"),
                MessageTipGenerator.GenerateTip("COPTW0003", LanguageManager.Get("ExcludePatternsInvalid"))); //COPTW0003
            return null;
        }
        if (advanced && (_type == FileDetector.FileType.Rar && levelCombo.SelectedIndex > 5 ||
            method == "LZMA2" && _type == FileDetector.FileType.Zip ||
            method == "Deflate" && _type == FileDetector.FileType.SevenZip))
        { Warn("CompressionInvalidOptions"); return null; }
        bool selfExtracting = advanced && selfExtractingCheck.IsChecked == true;
        if (selfExtracting && volume > 0)
        {
            ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"),
                MessageTipGenerator.GenerateTip("COPTW0001", LanguageManager.Get("SfxInvalidOptions"))); //COPTW0001
            return null;
        }
        return new CompressionOptions(password, advanced, levelCombo.SelectedIndex, method,
            dictionary, threads, memory, volume, selfExtracting,
            advanced ? solidMode : "Default", advanced && _type == FileDetector.FileType.Rar ? recoveryPercent : 0,
            advanced ? excludes : null);
    }

    private void RefreshProfiles()
    {
        profileCombo.Items.Clear();
        foreach (CompressionProfile profile in CompressionProfileStore.Load(_type))
            profileCombo.Items.Add(profile);
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CompressionOptions? options = ReadOptions();
            if (options is null) return;
            if (!options.Advanced) { Warn("ProfileEnableAdvanced"); return; }
            string name = profileNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl))
                throw new StageException("COPTW0004", LanguageManager.Get("ProfileInvalidName")); //COPTW0004
            await CompressionProfileStore.SaveAsync(new(name, _type,
                options with { Password = null }));
            RefreshProfiles();
            profileCombo.SelectedItem = profileCombo.Items.OfType<CompressionProfile>()
                .FirstOrDefault(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) { ShowProfileError("COPTW0005", error); } //COPTW0005
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (profileCombo.SelectedItem is not CompressionProfile profile)
            { Warn("ProfileSelectFirst"); return; }
            CompressionOptions value = profile.Options;
            advancedPanel.Visibility = Visibility.Visible;
            advancedCheck.IsEnabled = false;
            levelCombo.SelectedIndex = value.Level;
            methodCombo.SelectedItem = methodCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == value.Method);
            solidCombo.SelectedItem = solidCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == value.SolidMode);
            dictionaryBox.Text = value.DictionaryMiB.ToString();
            threadsBox.Text = value.Threads.ToString();
            memoryBox.Text = value.MemoryLimitMiB.ToString();
            volumeBox.Text = value.VolumeMiB.ToString();
            recoveryBox.Text = value.RecoveryPercent.ToString();
            excludeBox.Text = string.Join("; ", value.ExcludePatterns ?? []);
            selfExtractingCheck.IsChecked = value.SelfExtracting;
            profileNameBox.Text = profile.Name;
        }
        catch (Exception error) { ShowProfileError("COPTW0006", error); } //COPTW0006
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (profileCombo.SelectedItem is not CompressionProfile profile)
            { Warn("ProfileSelectFirst"); return; }
            if (ThemedPromptWindow.Ask(this, LanguageManager.Get("ProfileDeleteButton"),
                string.Format(LanguageManager.Get("ProfileDeleteConfirm"), profile.Name),
                (LanguageManager.Get("PromptYes"), MessageBoxResult.Yes),
                (LanguageManager.Get("PromptNo"), MessageBoxResult.No)) != MessageBoxResult.Yes) return;
            await CompressionProfileStore.DeleteAsync(_type, profile.Name);
            RefreshProfiles();
            profileNameBox.Clear();
        }
        catch (Exception error) { ShowProfileError("COPTW0007", error); } //COPTW0007
    }

    private void ShowProfileError(string fallback, Exception error) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ErrorTitle"), MessageTipGenerator.GenerateTip(
            error is StageException stage ? stage.StageCode : fallback, error.Message));

    private void Warn(string key) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ErrorTitle"), LanguageManager.Get(key));
}
