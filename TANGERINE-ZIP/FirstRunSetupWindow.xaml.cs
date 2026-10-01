using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: FRSYS. Both integration services already own their Windows 10/11
// handling. This coordinator adds one optional first-run action, progress,
// cancellation between extensions, and independent reporting of each failure.
internal sealed partial class FirstRunSetupWindow : Window
{
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _contextPhase;
    private bool _exitRequested;

    internal FirstRunSetupWindow()
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.57, 0.57);
        Title = LanguageManager.Get("FirstRunSetupTitle");
        descriptionText.Text = LanguageManager.Get("FirstRunSetupDescription");
        permissionHintText.Text = LanguageManager.Get("ContextUacConsentHint") + Environment.NewLine +
            LanguageManager.Get("DefaultAppsPermissionHint");
        startButton.Content = LanguageManager.Get("FirstRunSetupStart");
        exitButton.Content = LanguageManager.Get("FirstRunSetupSkip");
        Closing += (_, e) =>
        {
            if (!_busy) return;
            _exitRequested = true;
            _cancellation?.Cancel();
            e.Cancel = true;
        };
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        _exitRequested = false;
        _cancellation = new CancellationTokenSource();
        startButton.IsEnabled = false;
        exitButton.Content = LanguageManager.Get("StopWork");
        progressLog.Items.Clear();
        setupProgress.Value = 0;
        bool failed = false;
        bool completed = false;
        try
        {
            string executable = await Task.Run(DefaultAppAssociationService.GetExecutablePath);
            statusText.Text = LanguageManager.Get("FirstRunSetupContext");
            _contextPhase = true;
            IProgress<ContextMenuProgress> contextProgress = new Progress<ContextMenuProgress>(item =>
            {
                if (!_contextPhase) return;
                setupProgress.Value = Math.Max(setupProgress.Value, item.Percentage * 0.5);
                statusText.Text = LanguageManager.Get(item.ResourceKey);
                if (!string.IsNullOrWhiteSpace(item.Detail)) progressLog.Items.Add(item.Detail);
            });
            try
            {
                await ContextMenuRegistrationService.CreateAsync(executable, contextProgress, _cancellation.Token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                failed = true;
                AddError("FRSYS0001", exception); //FRSYS0001
            }
            _contextPhase = false;
            string[] extensions = DefaultAppAssociationService.Formats.SelectMany(format => format.Extensions).ToArray();
            for (int index = 0; index < extensions.Length; index++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                string extension = extensions[index];
                statusText.Text = string.Format(LanguageManager.Get("FirstRunSetupDefaults"), index + 1, extensions.Length, extension);
                try
                {
                    // The OS association setter is synchronous; isolate it
                    // from the dispatcher and check cancellation between files.
                    await Task.Run(() => DefaultAppAssociationService.SetThisAppDefault(extension, executable),
                        _cancellation.Token);
                    progressLog.Items.Add(string.Format(LanguageManager.Get("DefaultAppsVerified"), extension));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    failed = true;
                    AddError("FRSYS0002", exception, extension); //FRSYS0002
                }
                setupProgress.Value = 50 + (index + 1) * 50.0 / extensions.Length;
            }
            statusText.Text = LanguageManager.Get(failed ? "FirstRunSetupFailed" : "FirstRunSetupCompleted");
            completed = !failed;
        }
        catch (OperationCanceledException)
        {
            statusText.Text = LanguageManager.Get("OperationCancelled");
        }
        catch (Exception exception)
        {
            statusText.Text = LanguageManager.Get("FirstRunSetupFailed");
            AddError("FRSYS0003", exception); //FRSYS0003
        }
        finally
        {
            _busy = false;
            _contextPhase = false;
            _cancellation?.Dispose();
            _cancellation = null;
            startButton.IsEnabled = true;
            exitButton.Content = LanguageManager.Get("FirstRunSetupSkip");
        }
        if (_exitRequested) DialogResult = false;
        else if (completed) DialogResult = true;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { _exitRequested = true; _cancellation?.Cancel(); return; }
        DialogResult = false;
    }

    private void AddError(string code, Exception exception, string? extension = null)
    {
        string stage = exception is StageException staged ? staged.StageCode : code;
        progressLog.Items.Add((extension is null ? "" : extension + ": ") +
            MessageTipGenerator.GenerateTip(stage, exception.Message));
    }
}
