using System.ComponentModel;
using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: DAPWN (DefaultOpenWithWindow).
// The first action chooses the detected hash implementation and verifies each
// effective result without a per-format Settings interaction. The second
// action remains the explicit, user-directed Windows Settings workflow.
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
        selectAllButton.Content = LanguageManager.Get("DefaultAppsSelectAll");
        selectNoneButton.Content = LanguageManager.Get("DefaultAppsSelectNone");
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

    private void SelectAllButton_Click(object sender, RoutedEventArgs e) => SetSelection(true);

    private void SelectNoneButton_Click(object sender, RoutedEventArgs e) => SetSelection(false);

    private void SetSelection(bool selected)
    {
        // The running sequence is a snapshot. Lock both shortcuts and individual
        // choices until it ends, so changing the visible selection cannot imply
        // that queued default-association operations have been added or removed.
        if (_sequenceActive || _operationBusy) return;
        try
        {
            foreach (CheckBox choice in formatChoicesPanel.Children.OfType<CheckBox>())
                choice.IsChecked = selected;
        }
        catch (Exception exception) { ShowError("DAPWN0004", exception); } //DAPWN0004
    }

    private async void UseThisAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationBusy) return;
        try
        {
            if (!_sequenceActive) await StartSequenceAsync(useThisApp: true);
            else if (_useThisApp) FinishSequence(cancelled: true);
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
        selectionButtonsPanel.IsEnabled = false;
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
                        if (_useThisApp)
                        {
                            currentFormatText.Text = string.Format(LanguageManager.Get("DefaultAppsAutomaticSetting"),
                                _totalExtensions - _pendingExtensions.Count, _totalExtensions, extension);
                            await Task.Run(() => DefaultAppAssociationService.SetThisAppDefault(extension, _executablePath));
                            _verifiedCount++;
                            Log(string.Format(LanguageManager.Get("DefaultAppsVerified"), extension));
                            break; // Continue automatically when Windows accepts the result.
                        }
                        if (!_registeredExtensions.Contains(extension))
                        {
                            // Registry registration can be slow under security software.
                            // Do it off the UI thread, but keep the window open until the
                            // transaction completes so no continuation targets a dead UI.
                            await Task.Run(() => DefaultAppAssociationService.RegisterHandler(extension, _executablePath));
                            _registeredExtensions.Add(extension);
                            Log(string.Format(LanguageManager.Get("DefaultAppsRegistered"), extension));
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

    private void LaunchCurrentSettings(string extension)
    {
        // Only the explicit choose-any-app workflow launches Settings.
        DefaultSettingsLaunch launch = DefaultAppAssociationService.OpenWindowsSettings(useThisApp: false);
        foreach (StageException warning in launch.Warnings) Log(FormatError(warning.StageCode, warning));
        string instructionKey = launch.Uri.Contains("registeredAppUser=", StringComparison.Ordinal)
            ? "DefaultAppsAppPageInstruction"
            : OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                ? "DefaultAppsWindows11Instruction" : "DefaultAppsWindows10Instruction";
        currentFormatText.Text = string.Format(LanguageManager.Get("DefaultAppsCurrentFormat"),
            _totalExtensions - _pendingExtensions.Count, _totalExtensions, extension) + Environment.NewLine +
            string.Format(LanguageManager.Get(instructionKey), extension) + Environment.NewLine +
            LanguageManager.Get("DefaultAppsAnyAppHint");
        Log(string.Format(LanguageManager.Get("DefaultAppsSettingsOpened"), extension));
    }

    private async Task CompleteCurrentAsync()
    {
        if (_currentExtension is not string extension) return;
        if (_useThisApp) throw new InvalidOperationException("Automatic association cannot enter Settings confirmation.");
        string current = DefaultAppAssociationService.QueryCurrentProgId(extension)
            ?? LanguageManager.Get("DefaultAppsNoDefault");
        _reviewedCount++;
        Log(string.Format(LanguageManager.Get("DefaultAppsReviewed"), extension, current));
        _currentExtension = null;
        await OpenNextAsync();
    }

    private void FinishSequence(bool cancelled)
    {
        _sequenceActive = false;
        _currentExtension = null;
        _pendingExtensions.Clear();
        formatChoicesPanel.IsEnabled = true;
        selectionButtonsPanel.IsEnabled = true;
        currentFormatText.Text = LanguageManager.Get(cancelled ? "DefaultAppsStopped" : "DefaultAppsCompleted");
        Log(string.Format(LanguageManager.Get("DefaultAppsSummary"), _verifiedCount, _reviewedCount, _skippedCount));
        ResetButtons();
    }

    private void ResetButtons()
    {
        useThisAppButton.Content = LanguageManager.Get(!_sequenceActive || _useThisApp ? "DefaultAppsUseThisApp" : "DefaultAppsNext");
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
