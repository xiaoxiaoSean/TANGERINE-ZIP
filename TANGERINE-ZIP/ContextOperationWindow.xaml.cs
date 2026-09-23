using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: CTOPW (ContextOperationWindow)
internal sealed partial class ContextOperationWindow : Window
{
    private readonly Func<IProgress<ArchiveProgress>, CancellationToken, Task> _operation;
    private readonly string? _extractionEngineNames;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _completed;

    public ContextOperationWindow()
    {
        InitializeComponent();
        _operation = (_, _) => Task.CompletedTask;
    }

    public ContextOperationWindow(string title, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation,
        string? extractionEngineNames = null)
    {
        _operation = operation;
        _extractionEngineNames = extractionEngineNames;
        InitializeComponent();
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        Title = title;
        WpfUi.SizeWindow(this, 0.5, 0.25);
        Closed += (_, _) => _cancellation.Dispose();
    }

    private async void ContextOperationWindow_Shown(object? sender, RoutedEventArgs e)
    {
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)) return;
        _actionButton.Content = LanguageManager.Get("StopWork");
        _statusText.Text = LanguageManager.Get("ContextOperationRunning");
        if (!string.IsNullOrWhiteSpace(_extractionEngineNames))
        {
            _engineText.Text = string.Format(LanguageManager.Get("ContextExtractionEngineStatus"), _extractionEngineNames);
            _engineText.Visibility = Visibility.Visible;
        }
        Progress<ArchiveProgress> progress = new(item =>
        {
            if (_completed) return;
            _operationProgressBar.Value = Math.Clamp(item.Percentage, 0, 100);
            _progressPercentText.Text = $"{_operationProgressBar.Value:0}%";
            if (!string.IsNullOrWhiteSpace(item.EntryKey)) _statusText.Text = item.EntryKey;
        });
        try
        {
            await _operation(progress, _cancellation.Token);
            _completed = true;
            _operationProgressBar.Value = 100;
            _progressPercentText.Text = "100%";
            _statusText.Text = LanguageManager.Get("ContextOperationCompleted");
            _actionButton.Content = LanguageManager.Get("Confirm");
            _ = Dispatcher.BeginInvoke(Close);
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = LanguageManager.Get("OperationCancelled");
            _completed = true;
            _actionButton.Content = LanguageManager.Get("Confirm");
        }
        catch (Exception exception)
        {
            string code = exception is StageException stage ? stage.StageCode : "CTOPW0001";
            MessageBox.Show(this, MessageTipGenerator.GenerateTip(code, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            _completed = true;
            _actionButton.Content = LanguageManager.Get("Confirm");
        }
    }

    private void ActionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_completed) { Close(); return; }
        if (!WpfUi.Confirm(this, LanguageManager.Get("StopWorkRisk"), LanguageManager.Get("StopWork"))) return;
        _actionButton.IsEnabled = false;
        _statusText.Text = LanguageManager.Get("StoppingWork");
        _cancellation.Cancel();
    }

    private void ContextOperationWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_completed) return;
        e.Cancel = true;
        ActionButton_Click(this, new RoutedEventArgs());
    }
}
