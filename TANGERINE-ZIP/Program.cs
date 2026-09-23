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
            if (args.Length >= 2 && args[0].StartsWith("--context-", StringComparison.Ordinal))
            {
                Services.ContextMenuCommandHandler.Run(args[0], args[1..]);
                return;
            }
            application.Run(new MainWindow());
        }
    }
}
