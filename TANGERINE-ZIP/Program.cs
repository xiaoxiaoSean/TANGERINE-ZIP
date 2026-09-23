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
            if (args.Length == 2 && args[0] is Services.ContextMenuCertificateHelper.InstallSwitch or Services.ContextMenuCertificateHelper.RemoveSwitch)
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
            if (args.Length >= 2 && args[0].StartsWith("--context-", StringComparison.Ordinal))
            {
                Services.ContextMenuCommandHandler.Run(args[0], args[1..]);
                return;
            }
            application.Run(new MainWindow());
        }
    }
}
