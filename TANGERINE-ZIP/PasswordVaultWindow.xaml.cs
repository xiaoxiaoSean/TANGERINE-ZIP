using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: PVLWN. Passwords never appear in the list and are only read
// from Credential Manager when the user explicitly chooses Use.
internal sealed partial class PasswordVaultWindow : Window
{
    private readonly bool _selecting;

    public PasswordVaultWindow(bool selecting)
    {
        InitializeComponent();
        _selecting = selecting;
        Title = LanguageManager.Get("VaultTitle");
        descriptionText.Text = LanguageManager.Get("VaultDescription");
        savedLabel.Text = LanguageManager.Get("VaultSavedLabel");
        nameLabel.Text = LanguageManager.Get("VaultNameLabel");
        passwordLabel.Text = LanguageManager.Get("VaultPasswordLabel");
        saveButton.Content = LanguageManager.Get("VaultSaveButton");
        useButton.Content = LanguageManager.Get("VaultUseButton");
        useButton.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
        deleteButton.Content = LanguageManager.Get("VaultDeleteButton");
        closeButton.Content = LanguageManager.Get("Close");
        RefreshNames();
    }

    public string? SelectedPassword { get; private set; }

    private void RefreshNames()
    {
        savedCombo.Items.Clear();
        foreach (string name in PasswordVaultService.ListNames()) savedCombo.Items.Add(name);
    }

    private void SavedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (savedCombo.SelectedItem is string name) nameBox.Text = name;
        // Selection does not populate the PasswordBox: a secret is retrieved
        // only when Use is clicked, never during casual list browsing.
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string name = nameBox.Text.Trim();
            if (PasswordVaultService.ListNames().Contains(name, StringComparer.OrdinalIgnoreCase) &&
                ThemedPromptWindow.Ask(this, LanguageManager.Get("VaultTitle"),
                    string.Format(LanguageManager.Get("VaultReplaceConfirm"), name),
                    (LanguageManager.Get("PromptYes"), MessageBoxResult.Yes),
                    (LanguageManager.Get("PromptNo"), MessageBoxResult.No)) != MessageBoxResult.Yes) return;
            PasswordVaultService.Save(name, passwordBox.Password);
            passwordBox.Clear();
            RefreshNames();
            savedCombo.SelectedItem = name;
        }
        catch (Exception error) { ShowError("PVLWN0001", error); } //PVLWN0001
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (savedCombo.SelectedItem is not string name)
            { ShowInformation("VaultSelectFirst"); return; }
            SelectedPassword = PasswordVaultService.Read(name);
            DialogResult = true;
        }
        catch (Exception error) { ShowError("PVLWN0002", error); } //PVLWN0002
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (savedCombo.SelectedItem is not string name)
            { ShowInformation("VaultSelectFirst"); return; }
            if (ThemedPromptWindow.Ask(this, LanguageManager.Get("VaultTitle"),
                string.Format(LanguageManager.Get("VaultDeleteConfirm"), name),
                (LanguageManager.Get("PromptYes"), MessageBoxResult.Yes),
                (LanguageManager.Get("PromptNo"), MessageBoxResult.No)) != MessageBoxResult.Yes) return;
            PasswordVaultService.Delete(name);
            nameBox.Clear();
            passwordBox.Clear();
            RefreshNames();
        }
        catch (Exception error) { ShowError("PVLWN0003", error); } //PVLWN0003
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowInformation(string key) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("VaultTitle"), LanguageManager.Get(key));

    private void ShowError(string fallback, Exception error) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ErrorTitle"), MessageTipGenerator.GenerateTip(
            error is StageException stage ? stage.StageCode : fallback, error.Message));
}
