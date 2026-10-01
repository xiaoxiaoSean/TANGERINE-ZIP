using System.Globalization;
using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: SETWN (SettingsWindow)
internal sealed partial class SettingsWindow : Window
{
    private const double MouseWhiteThickenRadius = 125.0;
    private bool _isSaving;
    private bool _pageReady;
    private bool _updatingSliders;
    private bool _radiusSaveRunning;
    private bool _thicknessSaveRunning;
    private bool _colorSaveRunning;
    private bool _radiusResetRunning;
    private bool _thicknessResetRunning;
    private Task _radiusSaveTask = Task.CompletedTask;
    private Task _thicknessSaveTask = Task.CompletedTask;

    internal SettingsWindow()
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        // The window size follows the available work area; individual controls
        // are sized by Grid star/Auto columns rather than fixed pixel widths.
        WpfUi.SizeWindow(this, 0.62, 0.56);
        Title = LanguageManager.Get("settingsText");
        basicTab.Header = LanguageManager.Get("SettingsBasicTab");
        basicHeadingText.Text = LanguageManager.Get("SettingsBasicTab");
        appearanceTab.Header = LanguageManager.Get("SettingsAppearanceTab");
        moreTab.Header = LanguageManager.Get("SettingsMoreTab");
        moreHeadingText.Text = LanguageManager.Get("TempDirectoryTitle");
        tempDirectoryDescription.Text = LanguageManager.Get("TempDirectoryDescription");
        tempDirectoryChooseButton.Content = LanguageManager.Get("TempDirectoryChange");
        appearanceHeadingText.Text = LanguageManager.Get("SettingsAppearanceTab");
        appearanceDescriptionText.Text = LanguageManager.Get("SettingsAppearanceDescription");
        textAccentLabel.Text = LanguageManager.Get("SettingsTextAccent");
        windowBackgroundLabel.Text = LanguageManager.Get("SettingsWindowBackground");
        progressAccentLabel.Text = LanguageManager.Get("SettingsProgressAccent");
        progressBackgroundLabel.Text = LanguageManager.Get("SettingsProgressBackground");
        archiveSelectionLabel.Text = LanguageManager.Get("SettingsArchiveSelection");
        textAccentButton.Content = windowBackgroundButton.Content =
            progressAccentButton.Content = progressBackgroundButton.Content = archiveSelectionButton.Content =
            LanguageManager.Get("SettingsChooseColor");
        mouseEffectResetButton.Content = radiusResetButton.Content =
            thicknessResetButton.Content = textAccentResetButton.Content =
            windowBackgroundResetButton.Content = progressAccentResetButton.Content =
            progressBackgroundResetButton.Content = archiveSelectionResetButton.Content = LanguageManager.Get("SettingsRestoreDefault");
        mouseEffectLabel.Text = LanguageManager.Get("SettingsMouseEffect");
        mouseEffectDescription.Text = LanguageManager.Get("SettingsMouseEffectDescription");
        radiusLabel.Text = LanguageManager.Get("SettingsMouseRadius");
        radiusDescription.Text = LanguageManager.Get("SettingsMouseRadiusDescription");
        thicknessLabel.Text = LanguageManager.Get("SettingsMouseThickness");
        thicknessDescription.Text = LanguageManager.Get("SettingsMouseThicknessDescription");
        radiusSlider.Minimum = MouseEffectSettings.MinimumRadius;
        radiusSlider.Maximum = MouseEffectSettings.MaximumRadius;
        thicknessSlider.Minimum = MouseEffectSettings.MinimumThickness;
        thicknessSlider.Maximum = MouseEffectSettings.MaximumThickness;
        radiusSlider.Value = MouseEffectSettings.Radius;
        thicknessSlider.Value = MouseEffectSettings.Thickness;
        _pageReady = true;
        MouseEffectSettings.Changed += OnMouseEffectChanged;
        MouseEffectSettings.ParametersChanged += OnParametersChanged;
        AppearanceSettings.Changed += RefreshColors;
        Closed += (_, _) =>
        {
            MouseEffectSettings.Changed -= OnMouseEffectChanged;
            MouseEffectSettings.ParametersChanged -= OnParametersChanged;
            AppearanceSettings.Changed -= RefreshColors;
        };
        RefreshToggle();
        RefreshParameterText();
        RefreshColors();
        RefreshTempDirectory();
    }

    private void RefreshTempDirectory()
    {
        string path = TempDirectorySettings.CurrentPath;
        tempDirectoryPathText.Text = string.Format(LanguageManager.Get("TempDirectoryCurrent"), path);
        try
        {
            TempDirectorySettings.GetDirectory();
            string root = Path.GetPathRoot(path)!;
            long free = new DriveInfo(root).AvailableFreeSpace;
            tempDirectoryAvailableText.Text = string.Format(LanguageManager.Get("TempDirectoryAvailable"),
                ResourcePreflight.FormatBytes(free));
        }
        catch
        {
            tempDirectoryAvailableText.Text = LanguageManager.Get("TempDirectoryUnavailable");
        }
    }

    private async void ChooseTempDirectory_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFolderDialog folder = new()
        {
            Title = LanguageManager.Get("TempDirectoryChoosePrompt"),
            InitialDirectory = TempDirectorySettings.CurrentPath
        };
        if (folder.ShowDialog(this) != true) return;
        tempDirectoryChooseButton.IsEnabled = false;
        try
        {
            await TempDirectorySettings.SetAsync(folder.FolderName);
            RefreshTempDirectory();
        }
        catch (Exception exception)
        {
            string code = exception is StageException stage ? stage.StageCode : "SETWN0010";
            ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"),
                MessageTipGenerator.GenerateTip(code, exception.Message)); //SETWN0010
        }
        finally { tempDirectoryChooseButton.IsEnabled = true; }
    }

    private void RefreshColors()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshColors);
            return;
        }
        textAccentValue.Text = AppearanceSettings.Format(AppearanceSettings.GetColor(0));
        windowBackgroundValue.Text = AppearanceSettings.Format(AppearanceSettings.GetColor(1));
        progressAccentValue.Text = AppearanceSettings.Format(AppearanceSettings.GetColor(2));
        progressBackgroundValue.Text = AppearanceSettings.Format(AppearanceSettings.GetColor(3));
        archiveSelectionValue.Text = AppearanceSettings.Format(AppearanceSettings.GetColor(4));
    }

    private async void SelectColor_Click(object sender, RoutedEventArgs e)
    {
        if (_colorSaveRunning) return;
        _colorSaveRunning = true;
        SetColorControlsEnabled(false);
        try
        {
            int index = ReadColorIndex(sender);

            System.Windows.Media.Color current = AppearanceSettings.GetColor(index);
            using System.Windows.Forms.ColorDialog picker = new()
            {
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
                FullOpen = true
            };
            if (picker.ShowDialog(new ColorDialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle))
                != System.Windows.Forms.DialogResult.OK) return;

            System.Windows.Media.Color chosen = System.Windows.Media.Color.FromRgb(
                picker.Color.R, picker.Color.G, picker.Color.B);
            BeginColorProgress();
            await AppearanceSettings.SetColorAsync(index, chosen);
            CompleteColorProgress();
        }
        catch (ColorContrastException exception)
        {
            FailColorProgress();
            ThemedPromptWindow.Inform(this, LanguageManager.Get("SettingsColorInvalidTitle"),
                MessageTipGenerator.GenerateTip(exception.StageCode, exception.Message)); //COLRS0008
        }
        catch (Exception exception)
        {
            FailColorProgress();
            ShowSettingError("SETWN0005", exception); //SETWN0005
        }
        finally
        {
            _colorSaveRunning = false;
            SetColorControlsEnabled(true);
            RefreshColors();
        }
    }

    private async void ResetColor_Click(object sender, RoutedEventArgs e)
    {
        if (_colorSaveRunning) return;
        _colorSaveRunning = true;
        SetColorControlsEnabled(false);
        try
        {
            int index = ReadColorIndex(sender);
            System.Windows.Media.Color defaultColor = AppearanceSettings.GetDefaultColor(index);
            if (AppearanceSettings.GetColor(index) == defaultColor) return;
            BeginColorProgress();

            try { await AppearanceSettings.SetColorAsync(index, defaultColor); }
            catch (ColorContrastException contrast)
            {
                // An individual reset can make a currently inverted color
                // pair unreadable. Change neither file unless the user
                // explicitly approves resetting the whole pair together.
                string prompt = MessageTipGenerator.GenerateTip(contrast.StageCode,
                    LanguageManager.Get("SettingsResetPairPrompt")); //COLRS0008
                if (ThemedPromptWindow.Ask(this, LanguageManager.Get("SettingsColorInvalidTitle"), prompt,
                    (LanguageManager.Get("PromptYes"), MessageBoxResult.Yes),
                    (LanguageManager.Get("PromptNo"), MessageBoxResult.No)) == MessageBoxResult.Yes)
                    await AppearanceSettings.ResetPairAsync(index < 2 ? 0 : 2);
                else
                {
                    colorSaveProgress.Visibility = Visibility.Collapsed;
                    colorSaveStatus.Text = LanguageManager.Get("SettingsColorUnchanged");
                    return;
                }
            }
            CompleteColorProgress();
        }
        catch (Exception exception)
        {
            FailColorProgress();
            ShowSettingError("SETWN0006", exception); //SETWN0006
        }
        finally
        {
            _colorSaveRunning = false;
            SetColorControlsEnabled(true);
            RefreshColors();
        }
    }

    private static int ReadColorIndex(object sender)
    {
        if (sender is not Button button ||
            !int.TryParse(button.Tag?.ToString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out int index) || index is < 0 or > 4)
            throw new StageException("SETWN0004", LanguageManager.Get("SettingsColorInvalidSelection")); //SETWN0004
        return index;
    }

    private void SetColorControlsEnabled(bool enabled)
    {
        textAccentButton.IsEnabled = windowBackgroundButton.IsEnabled =
            progressAccentButton.IsEnabled = progressBackgroundButton.IsEnabled = archiveSelectionButton.IsEnabled =
            textAccentResetButton.IsEnabled = windowBackgroundResetButton.IsEnabled =
            progressAccentResetButton.IsEnabled = progressBackgroundResetButton.IsEnabled =
            archiveSelectionResetButton.IsEnabled = enabled;
    }

    private void BeginColorProgress()
    {
        colorSaveStatus.Text = LanguageManager.Get("SettingsColorSaving");
        colorSaveProgress.Value = 0;
        colorSaveProgress.IsIndeterminate = true;
        colorSaveProgress.Visibility = Visibility.Visible;
    }

    private void CompleteColorProgress()
    {
        colorSaveProgress.IsIndeterminate = false;
        colorSaveProgress.Value = 100;
        colorSaveStatus.Text = LanguageManager.Get("SettingsColorSaved");
    }

    private void FailColorProgress()
    {
        colorSaveProgress.IsIndeterminate = false;
        colorSaveProgress.Value = 0;
        colorSaveStatus.Text = LanguageManager.Get("SettingsColorSaveFailedStatus");
    }

    private sealed class ColorDialogOwner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }

    private void OnMouseEffectChanged(bool enabled)
    {
        // Setting changes are normally raised on the WPF dispatcher. Marshaling
        // here also keeps the page safe if another caller changes the setting.
        if (Dispatcher.CheckAccess()) RefreshToggle();
        else Dispatcher.BeginInvoke(RefreshToggle);
    }

    private void RefreshToggle()
    {
        mouseEffectToggle.IsChecked = MouseEffectSettings.IsEnabled;
        mouseEffectToggle.Content = LanguageManager.Get(
            MouseEffectSettings.IsEnabled ? "SettingsMouseEffectOn" : "SettingsMouseEffectOff");
    }

    private void OnParametersChanged(double radius, double thickness)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnParametersChanged(radius, thickness));
            return;
        }
        _updatingSliders = true;
        try
        {
            // Do not move a thumb out from under the user's pointer while a
            // drag is still being saved. The final state is restored below.
            if (!_radiusSaveRunning) radiusSlider.Value = radius;
            if (!_thicknessSaveRunning) thicknessSlider.Value = thickness;
            RefreshParameterText();
        }
        finally { _updatingSliders = false; }
    }

    private void RefreshParameterText()
    {
        radiusValueText.Text = string.Format(CultureInfo.CurrentCulture,
            LanguageManager.Get("SettingsMouseRadiusValue"), Math.Round(radiusSlider.Value));
        thicknessValueText.Text = string.Format(CultureInfo.CurrentCulture,
            LanguageManager.Get("SettingsMouseThicknessValue"), Math.Round(thicknessSlider.Value, 2));
    }

    private async void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_pageReady || _updatingSliders) return;
        RefreshParameterText();
        if (_radiusSaveTask.IsCompleted) _radiusSaveTask = SaveRadiusChangesAsync();
        await _radiusSaveTask;
    }

    private async Task SaveRadiusChangesAsync()
    {
        if (_radiusSaveRunning) return;
        _radiusSaveRunning = true;
        try
        {
            while (true)
            {
                // Coalesce drag events and write only the most recent value.
                // The continuation stays on the WPF dispatcher; disk I/O is
                // performed asynchronously inside MouseEffectSettings.
                await Task.Delay(120);
                double requested = Math.Round(radiusSlider.Value);
                if (Math.Abs(requested - MouseEffectSettings.Radius) > 0.001)
                    await MouseEffectSettings.SetRadiusAsync(requested);
                if (Math.Abs(Math.Round(radiusSlider.Value) - requested) <= 0.001) break;
            }
        }
        catch (Exception exception)
        {
            ShowSettingError("SETWN0002", exception); //SETWN0002
        }
        finally
        {
            _radiusSaveRunning = false;
            _updatingSliders = true;
            radiusSlider.Value = MouseEffectSettings.Radius;
            _updatingSliders = false;
            RefreshParameterText();
        }
    }

    private async void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_pageReady || _updatingSliders) return;
        RefreshParameterText();
        if (_thicknessSaveTask.IsCompleted) _thicknessSaveTask = SaveThicknessChangesAsync();
        await _thicknessSaveTask;
    }

    private async Task SaveThicknessChangesAsync()
    {
        if (_thicknessSaveRunning) return;
        _thicknessSaveRunning = true;
        try
        {
            while (true)
            {
                await Task.Delay(120);
                double requested = Math.Round(thicknessSlider.Value, 2);
                if (Math.Abs(requested - MouseEffectSettings.Thickness) > 0.001)
                    await MouseEffectSettings.SetThicknessAsync(requested);
                if (Math.Abs(Math.Round(thicknessSlider.Value, 2) - requested) <= 0.001) break;
            }
        }
        catch (Exception exception)
        {
            ShowSettingError("SETWN0003", exception); //SETWN0003
        }
        finally
        {
            _thicknessSaveRunning = false;
            _updatingSliders = true;
            thicknessSlider.Value = MouseEffectSettings.Thickness;
            _updatingSliders = false;
            RefreshParameterText();
        }
    }

    private void ShowSettingError(string fallbackStageCode, Exception exception)
    {
        string stageCode = exception is StageException stageException
            ? stageException.StageCode : fallbackStageCode;
        string message = MessageTipGenerator.GenerateTip(stageCode, exception.Message);
        ThemedPromptWindow.Inform(IsVisible ? this : null, LanguageManager.Get("ErrorTitle"),
            message); //SETWN0002/SETWN0003/SETWN0005-SETWN0009
    }

    private async void MouseEffectToggle_Click(object sender, RoutedEventArgs e) =>
        await SaveMouseEffectAsync(mouseEffectToggle.IsChecked == true, "SETWN0001"); //SETWN0001

    private async void MouseEffectReset_Click(object sender, RoutedEventArgs e) =>
        await SaveMouseEffectAsync(MouseEffectSettings.DefaultEnabled, "SETWN0007"); //SETWN0007

    private async Task SaveMouseEffectAsync(bool requestedState, string fallbackStageCode)
    {
        if (_isSaving) return;
        _isSaving = true;
        mouseEffectToggle.IsEnabled = false;
        mouseEffectResetButton.IsEnabled = false;
        try
        {
            // Persist first; the service broadcasts only after the marker has
            // been verified, so all open windows switch to the same saved state.
            await MouseEffectSettings.SetEnabledAsync(requestedState);
        }
        catch (Exception exception)
        {
            ShowSettingError(fallbackStageCode, exception); //SETWN0001/SETWN0007
        }
        finally
        {
            // On failure, restore the button to the last committed value.
            RefreshToggle();
            mouseEffectToggle.IsEnabled = true;
            mouseEffectResetButton.IsEnabled = true;
            _isSaving = false;
        }
    }

    private async void RadiusReset_Click(object sender, RoutedEventArgs e)
    {
        if (_radiusResetRunning) return;
        _radiusResetRunning = true;
        radiusSlider.IsEnabled = radiusResetButton.IsEnabled = false;
        try
        {
            _updatingSliders = true;
            radiusSlider.Value = MouseEffectSettings.DefaultRadius;
            _updatingSliders = false;
            await _radiusSaveTask;
            if (Math.Abs(MouseEffectSettings.Radius - MouseEffectSettings.DefaultRadius) > 0.001)
                await MouseEffectSettings.SetRadiusAsync(MouseEffectSettings.DefaultRadius);
        }
        catch (Exception exception) { ShowSettingError("SETWN0008", exception); } //SETWN0008
        finally
        {
            _radiusResetRunning = false;
            _updatingSliders = true;
            radiusSlider.Value = MouseEffectSettings.Radius;
            _updatingSliders = false;
            radiusSlider.IsEnabled = radiusResetButton.IsEnabled = true;
            RefreshParameterText();
        }
    }

    private async void ThicknessReset_Click(object sender, RoutedEventArgs e)
    {
        if (_thicknessResetRunning) return;
        _thicknessResetRunning = true;
        thicknessSlider.IsEnabled = thicknessResetButton.IsEnabled = false;
        try
        {
            _updatingSliders = true;
            thicknessSlider.Value = MouseEffectSettings.DefaultThickness;
            _updatingSliders = false;
            await _thicknessSaveTask;
            if (Math.Abs(MouseEffectSettings.Thickness - MouseEffectSettings.DefaultThickness) > 0.001)
                await MouseEffectSettings.SetThicknessAsync(MouseEffectSettings.DefaultThickness);
        }
        catch (Exception exception) { ShowSettingError("SETWN0009", exception); } //SETWN0009
        finally
        {
            _thicknessResetRunning = false;
            _updatingSliders = true;
            thicknessSlider.Value = MouseEffectSettings.Thickness;
            _updatingSliders = false;
            thicknessSlider.IsEnabled = thicknessResetButton.IsEnabled = true;
            RefreshParameterText();
        }
    }
}
