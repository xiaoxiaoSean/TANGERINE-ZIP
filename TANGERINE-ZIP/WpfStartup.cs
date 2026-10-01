using System.Globalization;

namespace TANGERINE_ZIP
{
    internal static class WpfStartup
    {
        /// <summary>
        /// Prepare the standard WPF application, or finish a noninteractive helper.
        /// Return true only after showing the real main window; all other paths
        /// let App shut down with the existing command/helper exit code.
        /// </summary>
        internal static async Task<bool> StartAsync(System.Windows.Application application, string[] args)
        {
            if ((args.Length is 2 or 3) &&
                (args[0] is Services.ContextMenuCertificateHelper.InstallSwitch or Services.ContextMenuCertificateHelper.RemoveSwitch))
            {
                Environment.ExitCode = Services.ContextMenuCertificateHelper.Execute(args);
                return false;
            }
            if (args.Length == 1 && args[0] == Services.ArchiveWorker.Switch)
            {
                // App.Startup runs on the dispatcher. Await pipe/archive work so
                // its continuations can complete without blocking that dispatcher.
                Environment.ExitCode = await Services.ArchiveWorker.ExecuteAsync();
                return false;
            }
            // Console commands must run before WPF startup and its interactive configuration dialogs.
            if (Services.CommandLine.IsCommand(args))
            {
                Environment.ExitCode = await Services.CommandLine.RunAsync(args);
                return false;
            }
            bool isContextCommand = args.Length >= 2 &&
                args[0].StartsWith("--context-", StringComparison.Ordinal);
            // Context commands display several modal windows in sequence.
            // The password window may be the first and only open WPF window;
            // the default OnLastWindowClose would shut down the dispatcher
            // as soon as the user confirms it, before compression starts.
            application.ShutdownMode = isContextCommand
                    ? System.Windows.ShutdownMode.OnExplicitShutdown
                    : System.Windows.ShutdownMode.OnLastWindowClose;
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
                        return false;
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
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    ShowStartupError("PROGM0003", exception); //PROGM0003
                    Environment.ExitCode = 1;
                    return false;
                }
            }
            if (!InitializeAppearance())
            {
                Environment.ExitCode = 1;
                return false;
            }
            if (!InitializeTempDirectory(application))
            {
                Environment.ExitCode = 1;
                return false;
            }
            if (isContextCommand)
            {
                try { Services.ContextMenuCommandHandler.Run(args[0], args[1..]); }
                finally { application.Shutdown(); }
                return false;
            }
            try { Services.TempDirectorySettings.ClearOnFirstInstanceStartup(); }
            catch (Exception exception)
            {
                ShowStartupError("PROGM0009", exception); //PROGM0009
                Environment.ExitCode = 1;
                return false;
            }
            try
            {
                // App.xaml's generated entry point already owns Application.Run.
                // Assign the main window explicitly: an earlier configuration
                // prompt must not remain the application's main-window identity.
                application.MainWindow = new MainWindow(args.Length == 1 && File.Exists(args[0]) ? args[0] : null);
                application.MainWindow.Show();
                return true;
            }
            catch (Exception exception)
            {
                ShowStartupError("PROGM0010", exception); //PROGM0010
                Environment.ExitCode = 1;
                return false;
            }
        }

        private static bool InitializeAppearance()
        {
            while (true)
            {
                try
                {
                    Services.AppearanceSettings.Initialize();
                    return true;
                }
                catch (Services.InvalidColorConfigurationException invalid)
                {
                    string prompt = string.Format(LanguageManager.Get("SettingsColorInvalidPrompt"), invalid.FileName);
                    string message = Tools.MessageTipGenerator.GenerateTip(invalid.StageCode, prompt); //COLRS0001-COLRS0004
                    if (System.Windows.MessageBox.Show(message, LanguageManager.Get("SettingsColorInvalidTitle"),
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                        System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes)
                        return false;
                    try { Services.AppearanceSettings.DeleteInvalidConfiguration(invalid); }
                    catch (Exception exception)
                    {
                        ShowStartupError("PROGM0004", exception); //PROGM0004
                        return false;
                    }
                }
                catch (Services.ColorContrastException contrast)
                {
                    int accentIndex = contrast.AccentIndex;
                    string accentName = LanguageManager.Get(accentIndex == 0
                        ? "SettingsTextAccent" : "SettingsProgressAccent");
                    string backgroundName = LanguageManager.Get(accentIndex == 0
                        ? "SettingsWindowBackground" : "SettingsProgressBackground");
                    string prompt = string.Format(LanguageManager.Get("SettingsColorStartupContrast"),
                        accentName, backgroundName);
                    string message = Tools.MessageTipGenerator.GenerateTip(contrast.StageCode, prompt); //COLRS0009/COLRS0010
                    if (System.Windows.MessageBox.Show(message, LanguageManager.Get("SettingsColorInvalidTitle"),
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                        System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes)
                        return false;
                    try
                    {
                        using System.Windows.Forms.ColorDialog picker = new()
                        {
                            Color = ToDrawingColor(Services.AppearanceSettings.GetColor(accentIndex)),
                            FullOpen = true
                        };
                        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
                        Services.AppearanceSettings.SetColorAtStartup(accentIndex,
                            System.Windows.Media.Color.FromRgb(picker.Color.R, picker.Color.G, picker.Color.B));
                    }
                    catch (Services.ColorContrastException)
                    {
                        // The chosen color is still too close. Nothing was
                        // saved; show the same recovery choice again.
                    }
                    catch (Exception exception)
                    {
                        ShowStartupError("PROGM0005", exception); //PROGM0005
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    ShowStartupError("PROGM0006", exception); //PROGM0006
                    return false;
                }
            }
        }

        private static bool InitializeTempDirectory(System.Windows.Application application)
        {
            System.Windows.ShutdownMode previous = application.ShutdownMode;
            // The startup chooser is a WPF window shown before MainWindow.
            // Closing it must not shut down the application dispatcher.
            application.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            try
            {
                while (true)
                {
                    try { Services.TempDirectorySettings.Initialize(); return true; }
                    catch (Exception exception)
                    {
                        string stageCode = exception is Services.StageException stage
                            ? stage.StageCode : "PROGM0007";
                        ThemedPromptWindow.Inform(null, LanguageManager.Get("TempDirectoryTitle"),
                            Tools.MessageTipGenerator.GenerateTip(stageCode, exception.Message) +
                            Environment.NewLine + LanguageManager.Get("TempDirectoryChoosePrompt")); //PROGM0007
                        Microsoft.Win32.OpenFolderDialog folder = new()
                        {
                            Title = LanguageManager.Get("TempDirectoryChoosePrompt")
                        };
                        if (folder.ShowDialog() != true) return false;
                        try { Services.TempDirectorySettings.SetAsync(folder.FolderName).GetAwaiter().GetResult(); }
                        catch (Exception saveError)
                        {
                            string saveCode = saveError is Services.StageException saveStage
                                ? saveStage.StageCode : "PROGM0008";
                            ThemedPromptWindow.Inform(null, LanguageManager.Get("TempDirectoryTitle"),
                                Tools.MessageTipGenerator.GenerateTip(saveCode, saveError.Message)); //PROGM0008
                        }
                    }
                }
            }
            finally { application.ShutdownMode = previous; }
        }

        private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Color color) =>
            System.Drawing.Color.FromArgb(color.R, color.G, color.B);

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
