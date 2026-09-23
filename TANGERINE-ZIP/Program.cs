using System.Globalization;

namespace TANGERINE_ZIP
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if ((args.Length is 2 or 3) &&
                (args[0] is Services.ContextMenuCertificateHelper.InstallSwitch or Services.ContextMenuCertificateHelper.RemoveSwitch))
            {
                Environment.ExitCode = Services.ContextMenuCertificateHelper.Execute(args);
                return;
            }
            if (args.Length == 1 && args[0] == Services.ArchiveWorker.Switch)
            {
                Environment.ExitCode = Services.ArchiveWorker.ExecuteAsync().GetAwaiter().GetResult();
                return;
            }
            var application = new System.Windows.Application();
            try
            {
                // Read the marker before creating any WPF window. A present
                // NO_MOUSE_EFFECT file means the effect is OFF.
                Services.MouseEffectSettings.Initialize();
            }
            catch (Exception exception)
            {
                Services.MouseEffectSettings.UseDisabledFallback();
                string stageCode = exception is Services.StageException stageException
                    ? stageException.StageCode : "PROGM0001";
                System.Windows.MessageBox.Show(
                    Tools.MessageTipGenerator.GenerateTip(stageCode, exception.Message),
                    LanguageManager.Get("ErrorTitle"),
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); //PROGM0001
            }
            while (true)
            {
                try
                {
                    // Both numeric files must be valid before any window is
                    // created, otherwise the shader could receive unsafe data.
                    Services.MouseEffectSettings.InitializeParameters();
                    break;
                }
                catch (Services.InvalidMouseEffectConfigurationException invalid)
                {
                    string prompt = string.Format(LanguageManager.Get("MouseConfigInvalidPrompt"),
                        invalid.FileName,
                        invalid.Minimum.ToString("0.##", CultureInfo.InvariantCulture),
                        invalid.Maximum.ToString("0.##", CultureInfo.InvariantCulture));
                    string message = Tools.MessageTipGenerator.GenerateTip(invalid.StageCode, prompt); //MECFG0001/MECFG0002
                    if (System.Windows.MessageBox.Show(message, LanguageManager.Get("MouseConfigInvalidTitle"),
                            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                            System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes)
                    {
                        // No means the user keeps the invalid file. Continuing
                        // would contradict the chosen configuration and is unsafe.
                        Environment.ExitCode = 1;
                        return;
                    }
                    try
                    {
                        // The dialog explicitly states that deletion is final.
                        // The next loop recreates only this missing file with
                        // its documented default; another invalid file gets
                        // its own separate confirmation.
                        Services.MouseEffectSettings.DeleteInvalidConfiguration(invalid);
                    }
                    catch (Exception exception)
                    {
                        ShowStartupError("PROGM0002", exception); //PROGM0002
                        Environment.ExitCode = 1;
                        return;
                    }
                }
                catch (Exception exception)
                {
                    ShowStartupError("PROGM0003", exception); //PROGM0003
                    Environment.ExitCode = 1;
                    return;
                }
            }
            if (args.Length >= 2 && args[0].StartsWith("--context-", StringComparison.Ordinal))
            {
                Services.ContextMenuCommandHandler.Run(args[0], args[1..]);
                return;
            }
            application.Run(new MainWindow());
        }

        private static void ShowStartupError(string fallbackStageCode, Exception exception)
        {
            string stageCode = exception is Services.StageException stageException
                ? stageException.StageCode : fallbackStageCode;
            System.Windows.MessageBox.Show(
                Tools.MessageTipGenerator.GenerateTip(stageCode, exception.Message),
                LanguageManager.Get("ErrorTitle"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); //PROGM0002/PROGM0003
        }
    }
}
