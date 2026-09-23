using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

internal sealed partial class ContextOperationWindow : Window
{
    private readonly Func<IProgress<ArchiveProgress>, CancellationToken, Task> _operation;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _completed;

    public ContextOperationWindow()
    {
        InitializeComponent();
        _operation = (_, _) => Task.CompletedTask;
    }

    public ContextOperationWindow(string title, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation)
    {
        _operation = operation;
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
        Progress<ArchiveProgress> progress = new(item =>
        {
            _operationProgressBar.Value = Math.Clamp(item.Percentage, 0, 100);
            if (!string.IsNullOrWhiteSpace(item.EntryKey)) _statusText.Text = item.EntryKey;
        });
        try
        {
            await _operation(progress, _cancellation.Token);
            _operationProgressBar.Value = 100;
            _statusText.Text = LanguageManager.Get("ContextOperationCompleted");
            _completed = true;
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
            string code = exception is StageException stage ? stage.StageCode : "CTXOP0001";
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
