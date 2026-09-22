using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: CTXOP
internal sealed class ContextOperationForm : Form
{
    private readonly Func<IProgress<ArchiveProgress>, CancellationToken, Task> _operation;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Top, Height = 24, Minimum = 0, Maximum = 100 };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button _actionButton = new() { Dock = DockStyle.Bottom, Height = 38 };
    private bool _completed;

    public ContextOperationForm(string title, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation)
    {
        _operation = operation;
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(620, 100);
        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        Controls.Add(_actionButton);
        _actionButton.Click += ActionButton_Click;
        Shown += ContextOperationForm_Shown;
        FormClosing += ContextOperationForm_FormClosing;
    }

    private async void ContextOperationForm_Shown(object? sender, EventArgs e)
    {
        DarkTheme.Apply(this);
        _actionButton.Text = LanguageManager.Get("StopWork");
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
            _actionButton.Text = LanguageManager.Get("Confirm");
            // Explorer operations close themselves only after the awaited operation has completed without error.
            BeginInvoke(Close);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = LanguageManager.Get("OperationCancelled");
            _completed = true;
            _actionButton.Text = LanguageManager.Get("Confirm");
        }
        catch (Exception exception)
        {
            string stageCode = exception is StageException stageException ? stageException.StageCode : "CTXOP0001";
            MessageBox.Show(this, MessageTipGenerator.GenerateTip(stageCode, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //CTXOP0001
            _completed = true;
            _actionButton.Text = LanguageManager.Get("Confirm");
        }
    }

    private void ActionButton_Click(object? sender, EventArgs e)
    {
        if (_completed)
        {
            Close();
            return;
        }
        if (MessageBox.Show(this, LanguageManager.Get("StopWorkRisk"), LanguageManager.Get("StopWork"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _actionButton.Enabled = false;
        _statusLabel.Text = LanguageManager.Get("StoppingWork");
        _cancellation.Cancel();
    }

    private void ContextOperationForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_completed) return;
        e.Cancel = true;
        ActionButton_Click(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cancellation.Dispose();
        base.Dispose(disposing);
    }
}
