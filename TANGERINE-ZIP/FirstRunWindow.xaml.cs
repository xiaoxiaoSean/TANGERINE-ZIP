using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: FIRUN. This dialog is created only when no configuration marker
// exists. All layout columns scale with the window and the window size is a
// fraction of the current work area, not a fixed control dimension.
internal sealed partial class FirstRunWindow : Window
{
    private bool _busy;

    internal FirstRunWindow()
    {
        InitializeComponent();
        WpfUi.SizeWindow(this, 0.50, 0.42);
        Title = headingText.Text = LanguageManager.Get("FirstRunTitle");
        descriptionText.Text = LanguageManager.Get("FirstRunDescription");
        agreeButton.Content = LanguageManager.Get("PromptYes");
        declineButton.Content = LanguageManager.Get("PromptNo");
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
    }

    private async void AgreeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        agreeButton.IsEnabled = declineButton.IsEnabled = false;
        bool completed = false;
        try
        {
            statusText.Text = LanguageManager.Get("FirstRunCreating");
            setupProgress.Value = 10;
            // Mouse parameters use their existing create-if-missing parser.
            // Running this disk work away from the dispatcher keeps progress
            // visible while the configuration files are written and checked.
            await Task.Run(() =>
            {
                MouseEffectSettings.Initialize();
                MouseEffectSettings.InitializeParameters();
            });
            setupProgress.Value = 45;
            await AppearanceSettings.InitializeAsync();
            setupProgress.Value = 70;
            statusText.Text = LanguageManager.Get("FirstRunChooseTemp");
            OpenFolderDialog folder = new() { Title = LanguageManager.Get("FirstRunChooseTemp") };
            if (folder.ShowDialog(this) != true)
            {
                agreeButton.IsEnabled = declineButton.IsEnabled = true;
                return;
            }
            await Task.Run(() => TempDirectorySettings.SetAsync(folder.FolderName));
            setupProgress.Value = 100;
            statusText.Text = LanguageManager.Get("FirstRunReady");
            completed = true;
        }
        catch (Exception exception)
        {
            string code = exception switch
            {
                StageException staged => staged.StageCode,
                InvalidColorConfigurationException invalidColor => invalidColor.StageCode,
                InvalidMouseEffectConfigurationException invalidMouse => invalidMouse.StageCode,
                _ => "FIRUN0001"
            };
            ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"),
                MessageTipGenerator.GenerateTip(code, exception.Message)); //FIRUN0001
            agreeButton.IsEnabled = declineButton.IsEnabled = true;
        }
        finally { _busy = false; }
        if (completed) DialogResult = true;
    }

    private void DeclineButton_Click(object sender, RoutedEventArgs e)
    {
        try { ThemedPromptWindow.Inform(this, Title, LanguageManager.Get("FirstRunDeclineAdvice")); }
        catch (Exception exception)
        {
            MessageBox.Show(MessageTipGenerator.GenerateTip("FIRUN0002", exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //FIRUN0002
        }
        finally { DialogResult = false; }
    }
}
