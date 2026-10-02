using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: SFXWN. This dialog collects paths only. The main window owns the
// cancellable worker operation after the dialog closes.
internal sealed partial class ArchiveSfxWindow : Window
{
    private readonly string? _currentArchive;

    public ArchiveSfxWindow(string? currentArchive)
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.66, 0.52);
        FontSize = SystemFonts.MessageFontSize;
        MouseWhiteThickening.Attach(this, 105);
        _currentArchive = currentArchive;
        Title = LanguageManager.Get("SfxToolMenu");
        descriptionText.Text = LanguageManager.Get("SfxToolDescription");
        sourceLabel.Text = LanguageManager.Get("SfxSourceLabel");
        chooseSourceButton.Content = LanguageManager.Get("SfxChooseSource");
        useCurrentButton.Content = LanguageManager.Get("SfxUseCurrent");
        passwordLabel.Text = LanguageManager.Get("SfxInputPassword");
        outputLabel.Text = LanguageManager.Get("SfxOutputLabel");
        chooseOutputButton.Content = LanguageManager.Get("SfxChooseOutput");
        formatNote.Text = LanguageManager.Get("SfxFormatNote");
        createButton.Content = LanguageManager.Get("SfxCreateButton");
        cancelButton.Content = LanguageManager.Get("Cancel");
    }

    public string SourcePath { get; private set; } = string.Empty;
    public string OutputPath { get; private set; } = string.Empty;
    public string? InputPassword { get; private set; }

    private void ChooseSource_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenFileDialog dialog = new()
            {
                Title = LanguageManager.Get("SfxChooseSource"),
                Filter = LanguageManager.Get("ArchiveDialogFilter"),
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true) sourceBox.Text = dialog.FileName;
        }
        catch (Exception error) { ShowError("SFXWN0001", error); } //SFXWN0001
    }

    private void UseCurrent_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Keep this button usable even when the main window has no archive.
            // Its explicit message teaches the user how to recover.
            if (string.IsNullOrWhiteSpace(_currentArchive) || !File.Exists(_currentArchive))
                throw new StageException("SFXWN0002", LanguageManager.Get("SfxNoCurrentArchive")); //SFXWN0002
            sourceBox.Text = _currentArchive;
        }
        catch (Exception error) { ShowError("SFXWN0002", error); } //SFXWN0002
    }

    private void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string sourceName = Path.GetFileNameWithoutExtension(sourceBox.Text);
            SaveFileDialog dialog = new()
            {
                Title = LanguageManager.Get("SfxChooseOutput"),
                Filter = LanguageManager.Get("SfxExeFilter"),
                DefaultExt = ".exe",
                AddExtension = true,
                FileName = string.IsNullOrWhiteSpace(sourceName) ? string.Empty : sourceName + ".exe",
                // Publication is create-only. A conflicting path receives a
                // StageCode warning instead of an overwrite promise.
                OverwritePrompt = false
            };
            if (dialog.ShowDialog(this) == true) outputBox.Text = dialog.FileName;
        }
        catch (Exception error) { ShowError("SFXWN0003", error); } //SFXWN0003
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceBox.Text) || !File.Exists(sourceBox.Text) ||
                !ArchiveCapabilities.CanOpen(FileDetector.DetectFileType(sourceBox.Text)))
                throw new StageException("SFXWN0004", LanguageManager.Get("SfxSourceUnsupported")); //SFXWN0004
            if (string.IsNullOrWhiteSpace(outputBox.Text) ||
                !outputBox.Text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new StageException("SFXWN0005", LanguageManager.Get("SfxOutputInvalid")); //SFXWN0005
            if (File.Exists(outputBox.Text) || Directory.Exists(outputBox.Text))
                throw new StageException("SFXWN0006", LanguageManager.Get("SfxOutputExists")); //SFXWN0006
            SourcePath = Path.GetFullPath(sourceBox.Text);
            OutputPath = Path.GetFullPath(outputBox.Text);
            InputPassword = string.IsNullOrEmpty(passwordBox.Password) ? null : passwordBox.Password;
            DialogResult = true;
        }
        catch (Exception error) { ShowError("SFXWN0007", error); } //SFXWN0007
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string fallback, Exception error) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ErrorTitle"), MessageTipGenerator.GenerateTip(
            error is StageException staged ? staged.StageCode : fallback,
            error is StageException ? error.Message : LanguageManager.Get("SfxDialogFailed")));
}
