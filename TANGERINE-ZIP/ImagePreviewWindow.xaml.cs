using System.Windows.Media.Imaging;
using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: IMPVW
internal sealed partial class ImagePreviewWindow : Window
{
    private const double MouseWhiteThickenRadius = 110.0;
    private readonly PreviewPayload _payload;
    private readonly string _sourceArchivePath;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _rotation = new();

    internal ImagePreviewWindow(PreviewPayload payload, string sourceArchivePath, long memoryLimit)
    {
        _payload = payload;
        _sourceArchivePath = sourceArchivePath;
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        WpfUi.SizeWindow(this, 0.72, 0.78);
        Title = string.Format(LanguageManager.Get("PreviewWindowTitle"), Path.GetFileName(payload.EntryKey));
        zoomInButton.Content = LanguageManager.Get("PreviewZoomIn");
        zoomOutButton.Content = LanguageManager.Get("PreviewZoomOut");
        rotateLeftButton.Content = LanguageManager.Get("PreviewRotateLeft");
        rotateRightButton.Content = LanguageManager.Get("PreviewRotateRight");
        saveAsButton.Content = LanguageManager.Get("PreviewSaveAs");

        // Check decoded pixel storage before WPF allocates the full bitmap.
        // A small compressed image can otherwise expand far beyond the
        // decompressed-file limit and exhaust the machine's memory.
        using (MemoryStream inspect = new(payload.Buffer, 0, payload.Length, writable: false))
        {
            BitmapDecoder decoder = BitmapDecoder.Create(inspect, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            BitmapFrame frame = decoder.Frames[0];
            long decodedBytes = checked((long)frame.PixelWidth * frame.PixelHeight * 4);
            if (decodedBytes > memoryLimit - payload.Length)
                throw new StageException("IMPVW0002", LanguageManager.Get("PreviewMemoryLimitExceeded")); //IMPVW0002
        }
        // OnLoad completes decoding while the original in-memory stream is
        // alive, so WPF never needs a temporary file or a later archive read.
        using MemoryStream source = new(payload.Buffer, 0, payload.Length, writable: false);
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = source;
        bitmap.EndInit();
        bitmap.Freeze();
        previewImage.Source = bitmap;
        TransformGroup transforms = new();
        transforms.Children.Add(_scale);
        transforms.Children.Add(_rotation);
        previewImage.LayoutTransform = transforms;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Min(_scale.ScaleX * 1.25, 16));
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Max(_scale.ScaleX / 1.25, 0.05));
    private void RotateLeft_Click(object sender, RoutedEventArgs e) => _rotation.Angle -= 90;
    private void RotateRight_Click(object sender, RoutedEventArgs e) => _rotation.Angle += 90;

    private void SetZoom(double value) => _scale.ScaleX = _scale.ScaleY = value;

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        saveAsButton.IsEnabled = false;
        try { await PreviewSaveService.SaveAsAsync(this, _payload, _sourceArchivePath); }
        catch (Exception exception)
        {
            string code = exception is StageException stage ? stage.StageCode : "IMPVW0001";
            MessageBox.Show(this, MessageTipGenerator.GenerateTip(code, exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //IMPVW0001
        }
        finally { saveAsButton.IsEnabled = true; }
    }
}
