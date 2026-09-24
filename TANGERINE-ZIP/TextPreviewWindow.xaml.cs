using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: TXPVW
internal sealed partial class TextPreviewWindow : Window
{
    private const double MouseWhiteThickenRadius = 110.0;
    private readonly PreviewPayload _payload;
    private readonly string _sourceArchivePath;

    internal TextPreviewWindow(PreviewPayload payload, string text, string sourceArchivePath)
    {
        _payload = payload;
        _sourceArchivePath = sourceArchivePath;
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        WpfUi.SizeWindow(this, 0.72, 0.78);
        Title = string.Format(LanguageManager.Get("PreviewWindowTitle"), Path.GetFileName(payload.EntryKey));
        selectAllButton.Content = LanguageManager.Get("PreviewSelectAll");
        copyButton.Content = LanguageManager.Get("PreviewCopy");
        zoomInButton.Content = LanguageManager.Get("PreviewZoomIn");
        zoomOutButton.Content = LanguageManager.Get("PreviewZoomOut");
        saveAsButton.Content = LanguageManager.Get("PreviewSaveAs");
        previewText.FontSize = SystemFonts.MessageFontSize;
        previewText.Text = text;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => previewText.SelectAll();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { previewText.Copy(); }
        catch (Exception exception) { ShowError("TXPVW0001", exception); } //TXPVW0001
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) =>
        previewText.FontSize = Math.Min(previewText.FontSize * 1.2, SystemFonts.MessageFontSize * 6);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) =>
        previewText.FontSize = Math.Max(previewText.FontSize / 1.2, SystemFonts.MessageFontSize * 0.6);

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        saveAsButton.IsEnabled = false;
        try { await PreviewSaveService.SaveAsAsync(this, _payload, _sourceArchivePath); }
        catch (Exception exception) { ShowError("TXPVW0002", exception); } //TXPVW0002
        finally { saveAsButton.IsEnabled = true; }
    }

    private void ShowError(string fallback, Exception exception)
    {
        string code = exception is StageException stage ? stage.StageCode : fallback;
        MessageBox.Show(this, MessageTipGenerator.GenerateTip(code, exception.Message),
            LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //TXPVW0001/TXPVW0002
    }
}
