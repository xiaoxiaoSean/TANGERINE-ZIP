using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

internal enum ContextMenuSetupMode { Create, Delete }

// Stage head: CMSPW (ContextMenuSetupWindow)
internal sealed partial class ContextMenuSetupWindow : Window
{
    private readonly ContextMenuSetupMode _mode;
    private readonly string _executablePath = string.Empty;
    private CancellationTokenSource? _cancellation;
    private bool _isBusy;

    public ContextMenuSetupWindow() => InitializeComponent();

    internal ContextMenuSetupWindow(ContextMenuSetupMode mode, string executablePath)
    {
        _mode = mode;
        _executablePath = executablePath;
        InitializeComponent();
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.65, 0.65);
        Title = LanguageManager.Get(mode == ContextMenuSetupMode.Create ? "ContextWizardCreateTitle" : "ContextWizardDeleteTitle");
        descriptionText.Text = LanguageManager.Get(mode == ContextMenuSetupMode.Create ? "ContextWizardCreateDescription" : "ContextWizardDeleteDescription");
        _startButton.Content = LanguageManager.Get(mode == ContextMenuSetupMode.Create ? "ContextWizardStartCreate" : "ContextWizardStartDelete");
        _cancelButton.Content = LanguageManager.Get("Cancel");
    }

    private async void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        _isBusy = true;
        _cancellation = new CancellationTokenSource();
        _startButton.IsEnabled = false;
        _cancelButton.Content = LanguageManager.Get("StopWork");
        _progressLogList.Items.Clear();
        _operationProgressBar.Value = 0;
        try
        {
            Progress<ContextMenuProgress> progress = new(UpdateProgress);
            if (_mode == ContextMenuSetupMode.Create)
                await ContextMenuRegistrationService.CreateAsync(_executablePath, progress, _cancellation.Token);
            else
                await ContextMenuRegistrationService.DeleteAsync(_executablePath, progress, _cancellation.Token);
            string key = _mode == ContextMenuSetupMode.Create ? "ContextMenuModernCreated" : "ContextMenuDeleted";
            _statusText.Text = LanguageManager.Get(key);
            MessageBox.Show(this, LanguageManager.Get(key), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            _isBusy = false;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = LanguageManager.Get("OperationCancelled");
            _progressLogList.Items.Add(LanguageManager.Get("ContextProgressCancelled"));
        }
        catch (Exception exception)
        {
            _statusText.Text = LanguageManager.Get("ContextSetupIncomplete");
            string code = exception is StageException stage ? stage.StageCode : "CMSPW0001";
            MessageBox.Show(this, MessageTipGenerator.GenerateTip(code, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
            _startButton.IsEnabled = true;
            _cancelButton.IsEnabled = true;
            _cancelButton.Content = LanguageManager.Get("Close");
        }
    }

    private void UpdateProgress(ContextMenuProgress progress)
    {
        if (!_isBusy) return;
        _operationProgressBar.Value = Math.Clamp(progress.Percentage, 0, 100);
        string message = LanguageManager.Get(progress.ResourceKey);
        if (!string.IsNullOrWhiteSpace(progress.Detail)) message = $"{message} {progress.Detail}";
        _statusText.Text = message;
        _progressLogList.Items.Add($"{progress.Percentage,3}%  {message}");
        _progressLogList.ScrollIntoView(_progressLogList.Items[^1]);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isBusy) { Close(); return; }
        if (!WpfUi.Confirm(this, LanguageManager.Get("ContextSetupCancelRisk"), LanguageManager.Get("StopWork"))) return;
        _cancelButton.IsEnabled = false;
        _statusText.Text = LanguageManager.Get("StoppingWork");
        _cancellation?.Cancel();
    }

    private void ContextMenuSetupWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isBusy) return;
        e.Cancel = true;
        CancelButton_Click(sender, new RoutedEventArgs());
    }
}
