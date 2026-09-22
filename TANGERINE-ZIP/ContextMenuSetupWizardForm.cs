using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

internal enum ContextMenuSetupMode { Create, Delete }

// Stage head: CTXWZ
internal sealed class ContextMenuSetupWizardForm : Form
{
    private readonly ContextMenuSetupMode _mode;
    private readonly string _executablePath;
    private readonly Label _descriptionLabel = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly ListBox _progressLog = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _startButton = new() { Width = 140, Height = 36, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
    private readonly Button _cancelButton = new() { Width = 120, Height = 36, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
    private CancellationTokenSource? _cancellation;
    private bool _isBusy;

    internal ContextMenuSetupWizardForm(ContextMenuSetupMode mode, string executablePath)
    {
        _mode = mode;
        _executablePath = executablePath;
        Text = LanguageManager.Get(mode == ContextMenuSetupMode.Create ? "ContextWizardCreateTitle" : "ContextWizardDeleteTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 500);
        Padding = new Padding(20);

        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_startButton);
        layout.Controls.Add(_descriptionLabel, 0, 0);
        layout.Controls.Add(_statusLabel, 0, 1);
        layout.Controls.Add(_progressBar, 0, 2);
        layout.Controls.Add(_progressLog, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);

        _descriptionLabel.Text = LanguageManager.Get(mode == ContextMenuSetupMode.Create ? "ContextWizardCreateDescription" : "ContextWizardDeleteDescription");
        _startButton.Text = LanguageManager.Get(mode == ContextMenuSetupMode.Create ? "ContextWizardStartCreate" : "ContextWizardStartDelete");
        _cancelButton.Text = LanguageManager.Get("Cancel");
        _startButton.Click += StartButton_Click;
        _cancelButton.Click += CancelButton_Click;
        FormClosing += ContextMenuSetupWizardForm_FormClosing;
        Shown += (_, _) => ApplyTheme();
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (_isBusy) return;
        _isBusy = true;
        _cancellation = new CancellationTokenSource();
        _startButton.Enabled = false;
        _cancelButton.Text = LanguageManager.Get("StopWork");
        _progressLog.Items.Clear();
        _progressBar.Value = 0;
        try
        {
            Progress<ContextMenuProgress> progress = new(UpdateProgress);
            if (_mode == ContextMenuSetupMode.Create)
                await ContextMenuRegistrationService.CreateAsync(_executablePath, progress, _cancellation.Token);
            else
                await ContextMenuRegistrationService.DeleteAsync(_executablePath, progress, _cancellation.Token);
            string successKey = _mode == ContextMenuSetupMode.Create ? "ContextMenuModernCreated" : "ContextMenuDeleted";
            _statusLabel.Text = LanguageManager.Get(successKey);
            MessageBox.Show(this, LanguageManager.Get(successKey), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _isBusy = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = LanguageManager.Get("OperationCancelled");
            _progressLog.Items.Add(LanguageManager.Get("ContextProgressCancelled"));
        }
        catch (Exception exception)
        {
            _statusLabel.Text = LanguageManager.Get("ContextSetupIncomplete");
            ShowFailure("CTXWZ0001", exception); //CTXWZ0001
        }
        finally
        {
            _isBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
            if (!IsDisposed)
            {
                _startButton.Enabled = true;
                _cancelButton.Enabled = true;
                _cancelButton.Text = LanguageManager.Get("Close");
            }
        }
    }

    private void UpdateProgress(ContextMenuProgress progress)
    {
        if (!_isBusy || IsDisposed) return;
        _progressBar.Value = Math.Clamp(progress.Percentage, 0, 100);
        string message = LanguageManager.Get(progress.ResourceKey);
        if (!string.IsNullOrWhiteSpace(progress.Detail)) message = $"{message} {progress.Detail}";
        _statusLabel.Text = message;
        _progressLog.Items.Add($"{progress.Percentage,3}%  {message}");
        _progressLog.TopIndex = Math.Max(0, _progressLog.Items.Count - 1);
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (!_isBusy)
        {
            Close();
            return;
        }
        if (MessageBox.Show(this, LanguageManager.Get("ContextSetupCancelRisk"), LanguageManager.Get("StopWork"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _cancelButton.Enabled = false;
        _statusLabel.Text = LanguageManager.Get("StoppingWork");
        _cancellation?.Cancel();
    }

    private void ContextMenuSetupWizardForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_isBusy) return;
        e.Cancel = true;
        CancelButton_Click(sender, EventArgs.Empty);
    }

    private void ApplyTheme()
    {
        DarkTheme.Apply(this);
        BackColor = Color.Black;
        ForeColor = Color.WhiteSmoke;
        _descriptionLabel.BackColor = Color.Black;
        _descriptionLabel.ForeColor = Color.WhiteSmoke;
        _statusLabel.BackColor = Color.Black;
        _statusLabel.ForeColor = Color.WhiteSmoke;
        _progressLog.BackColor = Color.FromArgb(22, 22, 22);
        _progressLog.ForeColor = Color.WhiteSmoke;
        foreach (Button button in new[] { _startButton, _cancelButton })
        {
            button.BackColor = Color.FromArgb(38, 38, 38);
            button.ForeColor = Color.WhiteSmoke;
            button.FlatAppearance.BorderColor = Color.DimGray;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 58, 58);
        }
    }

    private void ShowFailure(string fallbackStageCode, Exception exception)
    {
        string stageCode = exception is StageException stageException ? stageException.StageCode : fallbackStageCode;
        MessageBox.Show(this, MessageTipGenerator.GenerateTip(stageCode, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //CTXWZ0002
    }
}
