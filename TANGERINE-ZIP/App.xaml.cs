using System.Windows.Threading;
using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

/// <summary>
/// Standard WPF application. App.xaml generates the STA entry point and owns
/// the dispatcher; startup preparation must not create a second Application or
/// call Application.Run again. There is no console allocation on any entry path.
/// </summary>
public partial class App : Application
{
    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Configuration prompts can precede MainWindow. Keep the application
        // alive until startup explicitly opens that window or completes a helper.
        if (!await WpfStartup.StartAsync(this, e.Args)) Shutdown(Environment.ExitCode);
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Preserve the diagnostic stage of known failures. WPF owns the event
        // loop now, so report dispatcher exceptions through its standard event
        // rather than a catch around a manually invoked Application.Run.
        e.Handled = true;
        Environment.ExitCode = 1;
        string stage = e.Exception is StageException staged ? staged.StageCode : "PROGM0010";
        try
        {
            MessageBox.Show(MessageTipGenerator.GenerateTip(stage, e.Exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //PROGM0010
        }
        finally { Shutdown(1); }
    }
}
