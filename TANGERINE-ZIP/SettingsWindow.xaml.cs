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
        appearanceHeadingText.Text = LanguageManager.Get("SettingsAppearanceTab");
        appearanceDescriptionText.Text = LanguageManager.Get("SettingsAppearanceDescription");
        textAccentLabel.Text = LanguageManager.Get("SettingsTextAccent");
        windowBackgroundLabel.Text = LanguageManager.Get("SettingsWindowBackground");
        progressAccentLabel.Text = LanguageManager.Get("SettingsProgressAccent");
        progressBackgroundLabel.Text = LanguageManager.Get("SettingsProgressBackground");
        textAccentButton.Content = windowBackgroundButton.Content =
            progressAccentButton.Content = progressBackgroundButton.Content =
            LanguageManager.Get("SettingsChooseColor");
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
    }

    private async void SelectColor_Click(object sender, RoutedEventArgs e)
    {
        if (_colorSaveRunning) return;
        _colorSaveRunning = true;
        try
        {
            if (sender is not Button button ||
                !int.TryParse(button.Tag?.ToString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int index) || index is < 0 or > 3)
                throw new StageException("SETWN0004", LanguageManager.Get("SettingsColorInvalidSelection")); //SETWN0004

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
            await AppearanceSettings.SetColorAsync(index, chosen);
        }
        catch (ColorContrastException exception)
        {
            MessageBox.Show(this,
                MessageTipGenerator.GenerateTip(exception.StageCode, exception.Message),
                LanguageManager.Get("SettingsColorInvalidTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning); //COLRS0008
        }
        catch (Exception exception)
        {
            ShowSettingError("SETWN0005", exception); //SETWN0005
        }
        finally
        {
            _colorSaveRunning = false;
            RefreshColors();
        }
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
        await SaveRadiusChangesAsync();
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
        await SaveThicknessChangesAsync();
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
        if (IsVisible)
            MessageBox.Show(this, message, LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //SETWN0002/SETWN0003
        else
            MessageBox.Show(message, LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //SETWN0002/SETWN0003
    }

    private async void MouseEffectToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving) return;
        bool requestedState = mouseEffectToggle.IsChecked == true;
        _isSaving = true;
        mouseEffectToggle.IsEnabled = false;
        try
        {
            // Persist first; the service broadcasts only after the marker has
            // been verified, so all open windows switch to the same saved state.
            await MouseEffectSettings.SetEnabledAsync(requestedState);
        }
        catch (Exception exception)
        {
            string stageCode = exception is StageException stageException
                ? stageException.StageCode : "SETWN0001";
            MessageBox.Show(this, MessageTipGenerator.GenerateTip(stageCode, exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //SETWN0001
        }
        finally
        {
            // On failure, restore the button to the last committed value.
            RefreshToggle();
            mouseEffectToggle.IsEnabled = true;
            _isSaving = false;
        }
    }
}
