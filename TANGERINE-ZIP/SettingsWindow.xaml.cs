using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: SETWN (SettingsWindow)
internal sealed partial class SettingsWindow : Window
{
    private const double MouseWhiteThickenRadius = 125.0;
    private bool _isSaving;

    internal SettingsWindow()
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        Title = LanguageManager.Get("settingsText");
        basicTab.Header = LanguageManager.Get("SettingsBasicTab");
        basicHeadingText.Text = LanguageManager.Get("SettingsBasicTab");
        mouseEffectLabel.Text = LanguageManager.Get("SettingsMouseEffect");
        mouseEffectDescription.Text = LanguageManager.Get("SettingsMouseEffectDescription");
        MouseEffectSettings.Changed += OnMouseEffectChanged;
        Closed += (_, _) => MouseEffectSettings.Changed -= OnMouseEffectChanged;
        RefreshToggle();
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
