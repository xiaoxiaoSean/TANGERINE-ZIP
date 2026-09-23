using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

internal sealed class ContextOperationForm : Window
{
    private readonly Func<IProgress<ArchiveProgress>, CancellationToken, Task> _operation;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProgressBar _progressBar = WpfUi.Progress();
    private readonly TextBlock _statusLabel = WpfUi.Text(string.Empty);
    private readonly Button _actionButton = WpfUi.Button(string.Empty);
    private bool _completed;

    public ContextOperationForm(string title, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation)
    {
        _operation = operation;
        WpfUi.Style(this);
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WpfUi.SizeWindow(this, 0.5, 0.25);
        var root = WpfUi.Grid(0.2, 1, 0.5);
        root.Margin = new Thickness(16);
        WpfUi.Add(root, _progressBar, 0);
        WpfUi.Add(root, _statusLabel, 1);
        WpfUi.Add(root, _actionButton, 2);
        Content = root;
        _actionButton.Click += ActionButton_Click;
        Loaded += ContextOperationForm_Shown;
        Closing += ContextOperationForm_Closing;
        Closed += (_, _) => _cancellation.Dispose();
    }

    private async void ContextOperationForm_Shown(object? sender, RoutedEventArgs e)
    {
        _actionButton.Content = LanguageManager.Get("StopWork");
        _statusLabel.Text = LanguageManager.Get("ContextOperationRunning");
        Progress<ArchiveProgress> progress = new(item =>
        {
            _progressBar.Value = Math.Clamp(item.Percentage, 0, 100);
            if (!string.IsNullOrWhiteSpace(item.EntryKey)) _statusLabel.Text = item.EntryKey;
        });
        try
        {
            await _operation(progress, _cancellation.Token);
            _progressBar.Value = 100;
            _statusLabel.Text = LanguageManager.Get("ContextOperationCompleted");
            _completed = true;
            _actionButton.Content = LanguageManager.Get("Confirm");
            _ = Dispatcher.BeginInvoke(Close);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = LanguageManager.Get("OperationCancelled");
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
        _statusLabel.Text = LanguageManager.Get("StoppingWork");
        _cancellation.Cancel();
    }

    private void ContextOperationForm_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_completed) return;
        e.Cancel = true;
        ActionButton_Click(this, new RoutedEventArgs());
    }
}
