using System.Windows.Input;
using System.Windows.Interop;
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
    private HwndSource? _windowSource;
    private bool _hasUserZoomed;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

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

        // The viewport has no usable size in the constructor. Fit the decoded
        // image after layout, and attach a native hook because WPF does not
        // expose a dedicated horizontal-wheel routed event.
        Loaded += ImagePreviewWindow_Loaded;
        SourceInitialized += ImagePreviewWindow_SourceInitialized;
        Closed += ImagePreviewWindow_Closed;
    }

    private void ImagePreviewWindow_SourceInitialized(object? sender, EventArgs e)
    {
        RunInteraction(() =>
        {
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource?.AddHook(WindowMessageHook);
        }, "IMPVW0003"); //IMPVW0003
    }

    private void ImagePreviewWindow_Closed(object? sender, EventArgs e)
    {
        // Remove the native message hook and release any mouse capture so no
        // input remains attached to a window that is being destroyed.
        _windowSource?.RemoveHook(WindowMessageHook);
        if (_isPanning) previewImage.ReleaseMouseCapture();
    }

    private void ImagePreviewWindow_Loaded(object sender, RoutedEventArgs e) =>
        RunInteraction(FitToViewport, "IMPVW0004"); //IMPVW0004

    private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Resize the initial fit only until the user intentionally zooms.
        // Otherwise a window resize would discard the user's chosen scale.
        if (!_hasUserZoomed) RunInteraction(FitToViewport, "IMPVW0005"); //IMPVW0005
    }

    private void FitToViewport()
    {
        if (previewImage.Source is not BitmapSource bitmap ||
            imageScrollViewer.ViewportWidth <= 0 || imageScrollViewer.ViewportHeight <= 0)
            return;

        bool quarterTurn = Math.Abs(_rotation.Angle % 180) == 90;
        double imageWidth = quarterTurn ? bitmap.Height : bitmap.Width;
        double imageHeight = quarterTurn ? bitmap.Width : bitmap.Height;
        if (imageWidth <= 0 || imageHeight <= 0) return;

        // Never upscale a small source on open; large sources are reduced by
        // the smaller axis ratio so both dimensions fit inside the viewport.
        double fitScale = Math.Min(1.0, Math.Min(
            imageScrollViewer.ViewportWidth / imageWidth,
            imageScrollViewer.ViewportHeight / imageHeight));
        SetZoom(fitScale, userInitiated: false);
    }

    private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        RunInteraction(() =>
        {
            // Keep the image point under the pointer stationary while zooming.
            Point pointer = e.GetPosition(imageScrollViewer);
            double factor = Math.Pow(1.25, e.Delta / 120.0);
            ZoomAroundPointer(_scale.ScaleX * factor, pointer);
            e.Handled = true;
        }, "IMPVW0006"); //IMPVW0006
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam,
        IntPtr lParam, ref bool handled)
    {
        const int horizontalWheelMessage = 0x020E;
        if (message != horizontalWheelMessage || !IsMouseOver) return IntPtr.Zero;
        RunInteraction(() =>
        {
            // WM_MOUSEHWHEEL puts a signed delta in the high word of wParam.
            // Positive means right, so increase the horizontal scroll offset.
            short delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff));
            imageScrollViewer.ScrollToHorizontalOffset(
                imageScrollViewer.HorizontalOffset + delta / 120.0 * 48.0);
        }, "IMPVW0007"); //IMPVW0007
        handled = true;
        return IntPtr.Zero;
    }

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RunInteraction(() =>
        {
            _panStart = e.GetPosition(imageScrollViewer);
            _panStartHorizontalOffset = imageScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = imageScrollViewer.VerticalOffset;
            _isPanning = previewImage.CaptureMouse();
            if (_isPanning) e.Handled = true;
        }, "IMPVW0008"); //IMPVW0008
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        RunInteraction(() =>
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopPanning();
                return;
            }
            Point current = e.GetPosition(imageScrollViewer);
            imageScrollViewer.ScrollToHorizontalOffset(
                _panStartHorizontalOffset - (current.X - _panStart.X));
            imageScrollViewer.ScrollToVerticalOffset(
                _panStartVerticalOffset - (current.Y - _panStart.Y));
            e.Handled = true;
        }, "IMPVW0009"); //IMPVW0009
    }

    private void PreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        StopPanning();
        e.Handled = true;
    }

    private void PreviewImage_LostMouseCapture(object sender, MouseEventArgs e) => _isPanning = false;

    private void StopPanning()
    {
        _isPanning = false;
        if (previewImage.IsMouseCaptured) previewImage.ReleaseMouseCapture();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) =>
        RunInteraction(() => ZoomAroundPointer(_scale.ScaleX * 1.25,
            new Point(imageScrollViewer.ViewportWidth / 2, imageScrollViewer.ViewportHeight / 2)),
            "IMPVW0010"); //IMPVW0010

    private void ZoomOut_Click(object sender, RoutedEventArgs e) =>
        RunInteraction(() => ZoomAroundPointer(_scale.ScaleX / 1.25,
            new Point(imageScrollViewer.ViewportWidth / 2, imageScrollViewer.ViewportHeight / 2)),
            "IMPVW0011"); //IMPVW0011

    private void RotateLeft_Click(object sender, RoutedEventArgs e) =>
        RunInteraction(() => { _rotation.Angle -= 90; if (!_hasUserZoomed) FitToViewport(); },
            "IMPVW0012"); //IMPVW0012

    private void RotateRight_Click(object sender, RoutedEventArgs e) =>
        RunInteraction(() => { _rotation.Angle += 90; if (!_hasUserZoomed) FitToViewport(); },
            "IMPVW0013"); //IMPVW0013

    private void ZoomAroundPointer(double requestedScale, Point pointer)
    {
        double oldScale = _scale.ScaleX;
        double newScale = Math.Clamp(requestedScale, 0.001, 16.0);
        if (Math.Abs(newScale - oldScale) < double.Epsilon) return;
        double oldHorizontal = imageScrollViewer.HorizontalOffset;
        double oldVertical = imageScrollViewer.VerticalOffset;
        SetZoom(newScale, userInitiated: true);
        imageScrollViewer.UpdateLayout();
        double ratio = newScale / oldScale;
        imageScrollViewer.ScrollToHorizontalOffset((oldHorizontal + pointer.X) * ratio - pointer.X);
        imageScrollViewer.ScrollToVerticalOffset((oldVertical + pointer.Y) * ratio - pointer.Y);
    }

    private void SetZoom(double value, bool userInitiated)
    {
        _scale.ScaleX = _scale.ScaleY = value;
        if (userInitiated) _hasUserZoomed = true;
    }

    private void RunInteraction(Action action, string stageCode)
    {
        try { action(); }
        catch (Exception exception)
        {
            StopPanning();
            MessageBox.Show(this, MessageTipGenerator.GenerateTip(stageCode, exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //IMPVW0003-IMPVW0013
        }
    }

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
