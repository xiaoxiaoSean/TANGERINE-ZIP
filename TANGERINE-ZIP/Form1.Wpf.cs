namespace TANGERINE_ZIP;

public partial class Form1
{
    private readonly Menu mainMenu = new() { Background = WpfUi.Background, Foreground = WpfUi.Foreground };
    private readonly MenuItem TZIPToolStripMenuItem = new() { Header = "TZIP" };
    private readonly MenuItem OpenToolStripMenuItem = new();
    private readonly MenuItem extractToolStripMenuItem = new();
    private readonly MenuItem extractDirectlyALLToolStripMenuItem = new();
    private readonly MenuItem extractToFolderALLToolStripMenuItem = new();
    private readonly MenuItem extractDirectlySELECTEDToolStripMenuItem = new();
    private readonly MenuItem extractToAFolderSELECTEDToolStripMenuItem = new();
    private readonly MenuItem compressToolStripMenuItem = new();
    private readonly MenuItem compressSelectFileToolStripMenuItem = new();
    private readonly MenuItem SettingsToolStripMenuItem = new();
    private readonly MenuItem uninstallFileToolStripMenuItem = new();
    private readonly TextBlock mainTab = WpfUi.Text(string.Empty);
    private readonly ListBox fileBox = WpfUi.List(true);
    private readonly ProgressBar statusProgressBar = WpfUi.Progress();
    private readonly TextBlock statusLabel = WpfUi.Text(string.Empty);
    private readonly OpenFileDialog mainOpenFileDialog = new();
    private readonly SaveFileDialog mainSaveFileDialog = new();
    private readonly OpenFolderDialog mainFolderBrowserDialog = new();

    private void InitializeComponent()
    {
        WpfUi.Style(this);
        Icon = WpfUi.WindowIcon(typeof(Form1));
        Title = "TZIP";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        mainMenu.FontSize = FontSize;
        var root = WpfUi.Grid(0.75, 8.5, 0.75);
        mainMenu.Items.Add(TZIPToolStripMenuItem);
        mainMenu.Items.Add(OpenToolStripMenuItem);
        mainMenu.Items.Add(extractToolStripMenuItem);
        mainMenu.Items.Add(compressToolStripMenuItem);
        mainMenu.Items.Add(SettingsToolStripMenuItem);
        mainMenu.Items.Add(uninstallFileToolStripMenuItem);
        extractToolStripMenuItem.Items.Add(extractDirectlyALLToolStripMenuItem);
        extractToolStripMenuItem.Items.Add(extractToFolderALLToolStripMenuItem);
        extractToolStripMenuItem.Items.Add(extractDirectlySELECTEDToolStripMenuItem);
        extractToolStripMenuItem.Items.Add(extractToAFolderSELECTEDToolStripMenuItem);
        compressToolStripMenuItem.Items.Add(compressSelectFileToolStripMenuItem);
        TZIPToolStripMenuItem.Click += TZIPToolStripMenuItem_Click;
        OpenToolStripMenuItem.Click += OpenToolStripMenuItem_Click;
        extractDirectlyALLToolStripMenuItem.Click += extractDirectlyALLToolStripMenuItem_Click;
        extractToFolderALLToolStripMenuItem.Click += extractToFolderALLToolStripMenuItem_Click;
        extractDirectlySELECTEDToolStripMenuItem.Click += extractDirectlySELECTEDToolStripMenuItem_Click;
        extractToAFolderSELECTEDToolStripMenuItem.Click += extractToAFolderSELECTEDToolStripMenuItem_Click;
        compressSelectFileToolStripMenuItem.Click += compressSelectFileToolStripMenuItem_Click;
        SettingsToolStripMenuItem.Click += SettingsToolStripMenuItem_Click;
        uninstallFileToolStripMenuItem.Click += uninstallFileToolStripMenuItem_Click;
        WpfUi.Add(root, mainMenu, 0);

        var archiveView = WpfUi.Grid(0, 1);
        var header = new Border { Background = WpfUi.Surface, BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1) };
        header.Child = mainTab;
        mainTab.Margin = new Thickness(8);
        WpfUi.Add(archiveView, header, 0);
        WpfUi.Add(archiveView, fileBox, 1);
        fileBox.MouseDoubleClick += fileBox_DoubleClick;
        WpfUi.Add(root, archiveView, 1);

        var status = WpfUi.Grid(0);
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        statusProgressBar.Margin = new Thickness(4);
        WpfUi.Add(status, statusProgressBar, 0);
        WpfUi.Add(status, statusLabel, 0, 1);
        WpfUi.Add(root, status, 2);
        Content = root;
        Loaded += Form1_Load;
    }
}
