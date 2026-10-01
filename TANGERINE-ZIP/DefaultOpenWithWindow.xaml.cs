using System.ComponentModel;
using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: DAPWN (DefaultOpenWithWindow).
// Settings has no supported "user finished" callback. Use an explicit, resumable
// one-extension-at-a-time workflow; never launch a burst of Settings windows or
// report a Process.Start handoff as a successful change of default application.
internal sealed partial class DefaultOpenWithWindow : Window
{
    private const double MouseWhiteThickenRadius = 130;
    private Queue<string> _pendingExtensions = new();
    private readonly HashSet<string> _registeredExtensions = new(StringComparer.Ordinal);
    private string? _currentExtension;
    private string _executablePath = string.Empty;
    private bool _sequenceActive;
    private bool _operationBusy;
    private bool _useThisApp;
    private int _totalExtensions;
    private int _verifiedCount;
    private int _reviewedCount;
    private int _skippedCount;

    public DefaultOpenWithWindow()
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        FontSize = SystemFonts.MessageFontSize;
        WpfUi.SizeWindow(this, 0.65, 0.73);
        Title = LanguageManager.Get("DefaultOpenWithMenu");
        descriptionText.Text = LanguageManager.Get("DefaultAppsDescription") + Environment.NewLine +
            LanguageManager.Get("DefaultAppsIconHint");
        permissionHintText.Text = LanguageManager.Get("DefaultAppsPermissionHint");
        foreach (AssociationFormat format in DefaultAppAssociationService.Formats)
        {
            // Formats are technical names and extensions, shared across locales.
            // No disk image formats or unsupported suffixes enter this selection.
            formatChoicesPanel.Children.Add(new CheckBox
            {
                Content = $"{format.Label} ({string.Join(", ", format.Extensions)})",
                Tag = format, Margin = new Thickness(8), IsChecked = false
            });
        }
        ResetButtons();
    }

    private async void UseThisAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;
        try
        {
            if (!_sequenceActive) await StartSequenceAsync(useThisApp: true);
            else await CompleteCurrentAsync();
        }
        catch (Exception exception) { ShowError("DAPWN0001", exception); } //DAPWN0001
    }

    private async void ChooseDefaultAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;
        try
        {
            if (_sequenceActive) FinishSequence(cancelled: true);
            else await StartSequenceAsync(useThisApp: false);
        }
        catch (Exception exception) { ShowError("DAPWN0001", exception); } //DAPWN0001
    }

    private async Task StartSequenceAsync(bool useThisApp)
    {
        string[] selected = formatChoicesPanel.Children.OfType<CheckBox>()
            .Where(choice => choice.IsChecked == true)
            .SelectMany(choice => ((AssociationFormat)choice.Tag).Extensions).ToArray();
        if (selected.Length == 0)
            throw new StageException("DAPWN0002", LanguageManager.Get("DefaultAppsChooseFormats")); //DAPWN0002
        _executablePath = DefaultAppAssociationService.GetExecutablePath();
        _useThisApp = useThisApp;
        _pendingExtensions = new(selected);
        _totalExtensions = selected.Length;
        _registeredExtensions.Clear();
        _verifiedCount = _reviewedCount = _skippedCount = 0;
        _sequenceActive = true;
        formatChoicesPanel.IsEnabled = false;
        progressLogBox.Clear();
        await OpenNextAsync();
    }

    private async Task OpenNextAsync()
    {
        SetBusy(true);
        try
        {
            while (_sequenceActive && _pendingExtensions.TryDequeue(out string? extension))
            {
                _currentExtension = extension;
                bool retry;
                do
                {
                    retry = false;
                    try
                    {
                        currentFormatText.Text = string.Format(LanguageManager.Get("DefaultAppsPreparing"), extension);
                        if (!_registeredExtensions.Contains(extension))
                        {
                            // Registry registration can be slow under security software.
                            // Do it off the UI thread, but keep the window open until the
                            // transaction completes so no continuation targets a dead UI.
                            await Task.Run(() => DefaultAppAssociationService.RegisterHandler(extension, _executablePath));
                            _registeredExtensions.Add(extension);
                            Log(string.Format(LanguageManager.Get("DefaultAppsRegistered"), extension));
                        }
                        if (_useThisApp && IsThisAppDefault(extension))
                        {
                            _verifiedCount++;
                            Log(string.Format(LanguageManager.Get("DefaultAppsVerified"), extension));
                            break; // Already effective: no system confirmation needed.
                        }
                        LaunchCurrentSettings(extension);
                        ResetButtons();
                        return; // Wait for the explicit "check/next" button.
                    }
                    catch (Exception exception)
                    {
                        string diagnostic = FormatError("DAPWN0003", exception); //DAPWN0003
                        Log(extension + ": " + diagnostic);
                        MessageBoxResult choice = ThemedPromptWindow.Ask(this, Title, diagnostic,
                            (LanguageManager.Get("DefaultAppsRetry"), MessageBoxResult.Yes),
                            (LanguageManager.Get("DefaultAppsSkip"), MessageBoxResult.No),
                            (LanguageManager.Get("DefaultAppsStop"), MessageBoxResult.Cancel));
                        if (choice == MessageBoxResult.Yes) retry = true;
                        else if (choice == MessageBoxResult.No) _skippedCount++;
                        else FinishSequence(cancelled: true);
                    }
                } while (retry && _sequenceActive);
            }
            if (_sequenceActive) FinishSequence(cancelled: false);
        }
        finally { SetBusy(false); }
    }

    private bool IsThisAppDefault(string extension) =>
        string.Equals(DefaultAppAssociationService.QueryCurrentProgId(extension),
            DefaultAppAssociationService.GetProgId(extension), StringComparison.OrdinalIgnoreCase);

    private void LaunchCurrentSettings(string extension)
    {
        DefaultSettingsLaunch launch = DefaultAppAssociationService.OpenWindowsSettings(_useThisApp);
        foreach (StageException warning in launch.Warnings) Log(FormatError(warning.StageCode, warning));
        string instructionKey = launch.Uri.Contains("registeredAppUser=", StringComparison.Ordinal)
            ? "DefaultAppsAppPageInstruction"
            : OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                ? "DefaultAppsWindows11Instruction" : "DefaultAppsWindows10Instruction";
        currentFormatText.Text = string.Format(LanguageManager.Get("DefaultAppsCurrentFormat"),
            _totalExtensions - _pendingExtensions.Count, _totalExtensions, extension) + Environment.NewLine +
            string.Format(LanguageManager.Get(instructionKey), extension) + Environment.NewLine +
            LanguageManager.Get(_useThisApp ? "DefaultAppsTargetHint" : "DefaultAppsAnyAppHint");
        Log(string.Format(LanguageManager.Get("DefaultAppsSettingsOpened"), extension));
    }

    private async Task CompleteCurrentAsync()
    {
        if (_currentExtension is not string extension) return;
        if (_useThisApp)
        {
            if (IsThisAppDefault(extension))
            {
                _verifiedCount++;
                Log(string.Format(LanguageManager.Get("DefaultAppsVerified"), extension));
            }
            else
            {
                // Cancelling Settings or choosing another app is a normal outcome,
                // not an access-denied error and never a reason to request UAC.
                MessageBoxResult choice = ThemedPromptWindow.Ask(this, Title,
                    string.Format(LanguageManager.Get("DefaultAppsNotYetDefault"), extension),
                    (LanguageManager.Get("DefaultAppsReopen"), MessageBoxResult.Yes),
                    (LanguageManager.Get("DefaultAppsSkip"), MessageBoxResult.No),
                    (LanguageManager.Get("DefaultAppsStop"), MessageBoxResult.Cancel));
                if (choice == MessageBoxResult.Yes) { LaunchCurrentSettings(extension); return; }
                if (choice == MessageBoxResult.Cancel) { FinishSequence(cancelled: true); return; }
                _skippedCount++;
                Log(string.Format(LanguageManager.Get("DefaultAppsSkipped"), extension));
            }
        }
        else
        {
            // The generic flow accepts any user-selected app. Record the effective
            // ProgID for diagnostics, but count this as reviewed, not "set to TZIP".
            string current = DefaultAppAssociationService.QueryCurrentProgId(extension)
                ?? LanguageManager.Get("DefaultAppsNoDefault");
            _reviewedCount++;
            Log(string.Format(LanguageManager.Get("DefaultAppsReviewed"), extension, current));
        }
        _currentExtension = null;
        await OpenNextAsync();
    }

    private void FinishSequence(bool cancelled)
    {
        _sequenceActive = false;
        _currentExtension = null;
        _pendingExtensions.Clear();
        formatChoicesPanel.IsEnabled = true;
        currentFormatText.Text = LanguageManager.Get(cancelled ? "DefaultAppsStopped" : "DefaultAppsCompleted");
        Log(string.Format(LanguageManager.Get("DefaultAppsSummary"), _verifiedCount, _reviewedCount, _skippedCount));
        ResetButtons();
    }

    private void ResetButtons()
    {
        useThisAppButton.Content = LanguageManager.Get(!_sequenceActive ? "DefaultAppsUseThisApp" :
            _useThisApp ? "DefaultAppsCheckNext" : "DefaultAppsNext");
        chooseDefaultAppButton.Content = LanguageManager.Get(_sequenceActive ? "DefaultAppsStop" : "DefaultAppsChooseApp");
    }

    private void SetBusy(bool busy)
    {
        _operationBusy = busy;
        useThisAppButton.IsEnabled = chooseDefaultAppButton.IsEnabled = !busy;
    }

    private void Log(string message)
    {
        progressLogBox.AppendText(message + Environment.NewLine);
        progressLogBox.ScrollToEnd();
    }

    private static string FormatError(string fallbackCode, Exception exception) =>
        MessageTipGenerator.GenerateTip(exception is StageException stage ? stage.StageCode : fallbackCode, exception.Message);

    private void ShowError(string fallbackCode, Exception exception)
    {
        string message = FormatError(fallbackCode, exception);
        Log(message);
        ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"), message);
    }

    private void DefaultOpenWithWindow_Closing(object? sender, CancelEventArgs e)
    {
        // The user may close while waiting for Settings; remaining extensions are
        // simply abandoned. An in-progress registry write must finish/roll back.
        if (_operationBusy)
        {
            e.Cancel = true;
            currentFormatText.Text = LanguageManager.Get("DefaultAppsWaitForRegistration");
        }
    }
}
