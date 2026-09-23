using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: F0001
public partial class Form1 : Window
{
    private readonly ArchiveWorkerClient _archiveService = new();
    private readonly RarToolService _rarToolService = new();
    private readonly OpenFileDialog _compressionSourceDialog = new();
    private readonly OpenFileDialog mainOpenFileDialog = new();
    private readonly SaveFileDialog mainSaveFileDialog = new();
    private readonly OpenFolderDialog mainFolderBrowserDialog = new();
    private readonly List<ArchiveEntryInfo> _archiveEntries = [];
    private readonly MenuItem _extractNestedTarMenuItem;
    private readonly MenuItem _stopWorkMenuItem;
    private readonly MenuItem _contextMenuToolStripMenuItem;
    private readonly MenuItem _createContextMenuToolStripMenuItem;
    private readonly MenuItem _deleteContextMenuToolStripMenuItem;
    private readonly string? _startupArchivePath;
    private CancellationTokenSource? _operationCancellation;
    private NestedTarInfo _nestedTarInfo = NestedTarInfo.None;
    private string _archivePath = string.Empty;
    private string? _archivePassword;
    private string _archiveCurrentDirectory = string.Empty;
    private bool _isBusy;

    public Form1() : this(null) { }

    public Form1(string? startupArchivePath)
    {
        InitializeComponent();
        Icon = WpfUi.WindowIcon(typeof(Form1));
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        mainMenu.FontSize = FontSize;
        _startupArchivePath = startupArchivePath;
        _compressionSourceDialog.Multiselect = true;
        _compressionSourceDialog.CheckFileExists = true;
        _compressionSourceDialog.RestoreDirectory = false;
        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(userProfilePath)) _compressionSourceDialog.InitialDirectory = userProfilePath;
        _stopWorkMenuItem = new MenuItem { Visibility = Visibility.Collapsed };
        _stopWorkMenuItem.Click += StopWorkMenuItem_Click;
        mainMenu.Items.Add(_stopWorkMenuItem);
        _contextMenuToolStripMenuItem = new MenuItem();
        _createContextMenuToolStripMenuItem = new MenuItem();
        _deleteContextMenuToolStripMenuItem = new MenuItem();
        _createContextMenuToolStripMenuItem.Click += CreateContextMenuToolStripMenuItem_Click;
        _deleteContextMenuToolStripMenuItem.Click += DeleteContextMenuToolStripMenuItem_Click;
        _contextMenuToolStripMenuItem.Items.Add(_createContextMenuToolStripMenuItem);
        _contextMenuToolStripMenuItem.Items.Add(_deleteContextMenuToolStripMenuItem);
        mainMenu.Items.Add(_contextMenuToolStripMenuItem);
        Closing += (_, args) =>
        {
            if (!_isBusy) return;
            args.Cancel = true;
            StopWorkMenuItem_Click(this, EventArgs.Empty);
        };
        _extractNestedTarMenuItem = new MenuItem();
        _extractNestedTarMenuItem.Click += ExtractNestedTarMenuItem_Click;
        extractToolStripMenuItem.Items.Add(new Separator());
        extractToolStripMenuItem.Items.Add(_extractNestedTarMenuItem);
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        try
        {
            ApplyLocalizedText();
            ConfigureEntryColors();
            SetArchiveControls(false);
            if (!string.IsNullOrWhiteSpace(_startupArchivePath))
                Dispatcher.BeginInvoke(async () => await OpenArchiveAsync(_startupArchivePath));
        }
        catch (Exception exception)
        {
            ShowException("F00010001", exception); //F00010001
        }
    }

    private void ApplyLocalizedText()
    {
        statusLabel.Text = LanguageManager.Get("readytext");
        OpenToolStripMenuItem.Header = LanguageManager.Get("openText");
        extractToolStripMenuItem.Header = LanguageManager.Get("extractText");
        compressToolStripMenuItem.Header = LanguageManager.Get("compressText");
        SettingsToolStripMenuItem.Header = LanguageManager.Get("settingsText");
        uninstallFileToolStripMenuItem.Header = LanguageManager.Get("uninstallFileText");
        mainTab.Text = LanguageManager.Get("mainTabText");
        extractDirectlyALLToolStripMenuItem.Header = LanguageManager.Get("extractDirectlyALLText");
        extractToFolderALLToolStripMenuItem.Header = LanguageManager.Get("extractToFolderALLText");
        extractDirectlySELECTEDToolStripMenuItem.Header = LanguageManager.Get("extractDirectlySELECTEDText");
        extractToAFolderSELECTEDToolStripMenuItem.Header = LanguageManager.Get("extractToAFolderSELECTEDText");
        compressSelectFileToolStripMenuItem.Header = LanguageManager.Get("SelectFilesToCompress");
        _compressionSourceDialog.Title = LanguageManager.Get("SelectFilesToCompress");
        _compressionSourceDialog.Filter = LanguageManager.Get("AllFilesFilter");
        _extractNestedTarMenuItem.Header = LanguageManager.Get("ExtractNestedTar");
        _stopWorkMenuItem.Header = LanguageManager.Get("StopWork");
        _contextMenuToolStripMenuItem.Header = LanguageManager.Get("ContextMenu");
        _createContextMenuToolStripMenuItem.Header = LanguageManager.Get("CreateContextMenu");
        _deleteContextMenuToolStripMenuItem.Header = LanguageManager.Get("DeleteContextMenu");
        mainOpenFileDialog.Title = LanguageManager.Get("SelectArchive");
        mainOpenFileDialog.Filter = LanguageManager.Get("ArchiveDialogFilter");
    }

    private void ConfigureEntryColors()
    {
        fileBox.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            foreach (string item in fileBox.Items.OfType<string>())
                if (fileBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
                    container.Foreground = FindEntry(item)?.IsDirectory == true
                        ? WpfUi.DirectoryForeground : WpfUi.Foreground;
        };
    }

    private async void OpenToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        if (mainOpenFileDialog.ShowDialog(this) != true) return;
        await OpenArchiveAsync(mainOpenFileDialog.FileName);
    }

    /// <summary>
    /// Opens an archive selected either from the application's file dialog or from Explorer's context menu.
    /// </summary>
    private async Task OpenArchiveAsync(string archivePath)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        string? password = null;
        string? passwordError = null;
        bool passwordProtected = false;
        while (true)
        {
            if (passwordProtected && !ArchivePasswordForm.TryGetExtractionPassword(
                    this, Path.GetFileName(archivePath), passwordError, out password))
            {
                UnloadArchive();
                return;
            }
            try
            {
                await RunOperationAsync(LanguageManager.Get("OpeningFile"), async (progress, token) =>
                {
                    FileDetector.FileType type = FileDetector.DetectFileType(archivePath);
                    if (!ArchiveCapabilities.CanOpen(type))
                        throw new StageException("F00010002", LanguageManager.Get("NotACompressedFile")); //F00010002
                    NestedTarInfo nestedTarInfo = await _archiveService.AnalyzeNestedTarAsync(archivePath, token, password, progress);
                    IReadOnlyList<ArchiveEntryInfo> entries = nestedTarInfo.FlattenAutomatically
                        ? await _archiveService.ListNestedTarAsync(archivePath, nestedTarInfo.TarEntryKeys[0], token, password)
                        : await _archiveService.ListAsync(archivePath, token, password);
                    _archivePath = archivePath;
                    _archivePassword = password;
                    _nestedTarInfo = nestedTarInfo;
                    _archiveCurrentDirectory = string.Empty;
                    _archiveEntries.Clear();
                    _archiveEntries.AddRange(entries);
                    mainTab.Text = LanguageManager.Get($"Format_{type}") + " " + LanguageManager.Get(type is FileDetector.FileType.Iso or FileDetector.FileType.Wim ? "ImageFile" : "CompressFile");
                    RefreshFileBox();
                    SetArchiveControls(true);
                });
                statusProgressBar.Value = 100;
                RefreshCurrentDirectoryStatus();
                return;
            }
            catch (OperationCanceledException) { statusLabel.Text = LanguageManager.Get("OperationCancelled"); return; }
            catch (StageException exception) when (exception.StageCode is "PWDAR0001" or "PWDAR0002")
            {
                passwordProtected = true;
                passwordError = MessageTipGenerator.GenerateTip(exception.StageCode, exception.Message); //PWDAR0002
            }
            catch (Exception exception)
            {
                UnloadArchive();
                ShowException("F00010003", exception); //F00010003
                return;
            }
        }
    }

    private void CreateContextMenuToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            string executablePath = Environment.ProcessPath ?? throw new StageException("F00010009", LanguageManager.Get("ContextExecutableMissing")); //F00010009
            ContextMenuSetupWizardForm wizard = new(ContextMenuSetupMode.Create, executablePath) { Owner = this };
            wizard.ShowDialog();
        }
        catch (Exception exception) { ShowException("F00010009", exception); } //F00010009
    }

    private void DeleteContextMenuToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            string executablePath = Environment.ProcessPath ?? throw new StageException("F00010010", LanguageManager.Get("ContextExecutableMissing")); //F00010010
            ContextMenuSetupWizardForm wizard = new(ContextMenuSetupMode.Delete, executablePath) { Owner = this };
            wizard.ShowDialog();
        }
        catch (Exception exception) { ShowException("F00010010", exception); } //F00010010
    }

    private async Task RunOperationAsync(string initialStatus, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation)
    {
        _isBusy = true;
        _operationCancellation = new CancellationTokenSource();
        CancellationTokenSource operationIdentity = _operationCancellation;
        _stopWorkMenuItem.Visibility = Visibility.Visible;
        _stopWorkMenuItem.IsEnabled = true;
        SetMenuEnabled(false);
        statusLabel.Text = initialStatus;
        statusProgressBar.Value = 0;
        Progress<ArchiveProgress> progress = new(item =>
        {
            // Reports can remain queued after completion or cancellation. Only the current job owns the UI.
            if (!ReferenceEquals(_operationCancellation, operationIdentity) || operationIdentity.IsCancellationRequested) return;
            statusProgressBar.Value = Math.Clamp(item.Percentage, 0, 100);
            statusLabel.Text = string.IsNullOrWhiteSpace(item.EntryKey) ? initialStatus : string.Format(LanguageManager.Get("ProgressStatus"), initialStatus, item.EntryKey, item.Percentage);
        });
        try { await operation(progress, _operationCancellation.Token); operationIdentity.Token.ThrowIfCancellationRequested(); }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _isBusy = false;
            _stopWorkMenuItem.Visibility = Visibility.Collapsed;
            SetMenuEnabled(true);
        }
    }

    private void StopWorkMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            CancellationTokenSource? current = _operationCancellation;
            if (current is null || current.IsCancellationRequested) return;
            bool answer = WpfUi.Confirm(this, LanguageManager.Get("StopWorkRisk"), LanguageManager.Get("StopWork"));
            // The modal confirmation pumps messages; the job may finish while the user reads it.
            if (!answer || !ReferenceEquals(current, _operationCancellation)) return;
            current.Cancel();
            _stopWorkMenuItem.IsEnabled = false;
            statusLabel.Text = LanguageManager.Get("StoppingWork");
        }
        catch (Exception exception) { ShowException("F00010008", exception); } //F00010008
    }

    private async Task ExtractAsync(bool selectedOnly, bool createArchiveFolder)
    {
        if (string.IsNullOrEmpty(_archivePath) || !File.Exists(_archivePath)) { ShowInformation("NoOpenedFile"); return; }
        IReadOnlyCollection<string>? selectedEntries = null;
        if (selectedOnly)
        {
            selectedEntries = GetSelectedArchiveEntries();
            if (selectedEntries.Count == 0) { ShowInformation("NoSelectedEntries"); return; }
        }
        mainFolderBrowserDialog.Title = LanguageManager.Get("SelectExtractFolderText");
        if (mainFolderBrowserDialog.ShowDialog(this) != true) return;
        string destination = mainFolderBrowserDialog.FolderName;
        if (createArchiveFolder) destination = Path.Combine(destination, Path.GetFileNameWithoutExtension(_archivePath));
        try
        {
            await RunOperationAsync(LanguageManager.Get("ExtractingText"), (progress, token) =>
                _nestedTarInfo.FlattenAutomatically
                    ? _archiveService.ExtractNestedTarsAsync(_archivePath, _nestedTarInfo.TarEntryKeys, destination, selectedEntries, false, OverwritePolicy.Ask, progress, token, _archivePassword,
                        (conflict, _) => Task.FromResult(OverwriteConflictForm.Ask(this, conflict)))
                    : _archiveService.ExtractAsync(_archivePath, destination, selectedEntries, OverwritePolicy.Ask, progress, token, _archivePassword,
                        (conflict, _) => Task.FromResult(OverwriteConflictForm.Ask(this, conflict))));
            statusProgressBar.Value = 100;
            statusLabel.Text = LanguageManager.Get("ExtractingCompleted");
        }
        catch (OperationCanceledException) { statusLabel.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("F00010004", exception); } //F00010004
    }

    private async void compressSelectFileToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        _compressionSourceDialog.FileName = string.Empty;
        if (_compressionSourceDialog.ShowDialog(this) != true) return;
        string[] sourcePaths = _compressionSourceDialog.FileNames;
        if (sourcePaths.Length == 0) return;
        mainSaveFileDialog.Title = LanguageManager.Get("SelectOutputArchive");
        mainSaveFileDialog.Filter = LanguageManager.Get("CreateArchiveFilter");
        mainSaveFileDialog.AddExtension = true;
        mainSaveFileDialog.OverwritePrompt = true;
        if (mainSaveFileDialog.ShowDialog(this) != true) return;
        FileDetector.FileType type = FileDetector.GetTypeFromCreateFilterIndex(mainSaveFileDialog.FilterIndex);
        if (!ArchivePasswordForm.TryGetCreationPassword(this, Path.GetFileName(mainSaveFileDialog.FileName), type, out string? password)) return;
        try
        {
            await RunOperationAsync(LanguageManager.Get("CompressingText"), (progress, token) =>
                _archiveService.CreateAsync(sourcePaths, mainSaveFileDialog.FileName, type, progress, token, password));
            statusProgressBar.Value = 100;
            statusLabel.Text = LanguageManager.Get("CompressionCompleted");
        }
        catch (OperationCanceledException) { statusLabel.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("F00010005", exception); } //F00010005
    }

    private async void ExtractNestedTarMenuItem_Click(object? sender, EventArgs e)
    {
        IReadOnlyList<string> tarEntries;
        if (_nestedTarInfo.FlattenAutomatically)
        {
            tarEntries = _nestedTarInfo.TarEntryKeys;
        }
        else
        {
            HashSet<string> selected = GetSelectedArchiveEntries().ToHashSet(StringComparer.OrdinalIgnoreCase);
            tarEntries = _nestedTarInfo.TarEntryKeys.Where(selected.Contains).ToArray();
            if (tarEntries.Count == 0)
            {
                ShowInformation("SelectNestedTarFiles");
                return;
            }
        }

        mainFolderBrowserDialog.Title = LanguageManager.Get("SelectExtractFolderText");
        if (mainFolderBrowserDialog.ShowDialog(this) != true) return;
        string destination = Path.Combine(mainFolderBrowserDialog.FolderName, Path.GetFileNameWithoutExtension(_archivePath));
        try
        {
            await RunOperationAsync(LanguageManager.Get("ExtractingNestedTar"), (progress, token) =>
                _archiveService.ExtractNestedTarsAsync(_archivePath, tarEntries, destination, null, true, OverwritePolicy.Ask, progress, token,
                    _archivePassword, (conflict, _) => Task.FromResult(OverwriteConflictForm.Ask(this, conflict))));
            statusProgressBar.Value = 100;
            statusLabel.Text = LanguageManager.Get("ExtractingCompleted");
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = LanguageManager.Get("OperationCancelled");
        }
        catch (Exception exception)
        {
            ShowException("F00010007", exception); //F00010007
        }
    }

    private void RefreshFileBox()
    {
        SortedDictionary<string, bool> children = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (ArchiveEntryInfo entry in _archiveEntries)
        {
            if (!entry.Key.StartsWith(_archiveCurrentDirectory, StringComparison.OrdinalIgnoreCase)) continue;
            string relative = entry.Key[_archiveCurrentDirectory.Length..].TrimEnd('/');
            if (string.IsNullOrEmpty(relative)) continue;
            int slash = relative.IndexOf('/');
            string name = slash >= 0 ? relative[..slash] : relative;
            children[name] = slash >= 0 || entry.IsDirectory;
        }
        fileBox.Items.Clear();
        if (!string.IsNullOrEmpty(_archiveCurrentDirectory)) fileBox.Items.Add(LanguageManager.Get("goToParentDirectoryText"));
        foreach ((string name, _) in children) fileBox.Items.Add(name);
        RefreshCurrentDirectoryStatus();
    }

    private ArchiveEntryInfo? FindEntry(string displayName)
    {
        string candidate = _archiveCurrentDirectory + displayName;
        return _archiveEntries.FirstOrDefault(entry => entry.Key.TrimEnd('/').Equals(candidate, StringComparison.OrdinalIgnoreCase) || entry.Key.StartsWith(candidate.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetSelectedArchiveEntries() => fileBox.SelectedItems.Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .Where(item => !string.IsNullOrEmpty(item) && item != LanguageManager.Get("goToParentDirectoryText"))
        .Select(item => FindEntry(item)?.IsDirectory == true ? (_archiveCurrentDirectory + item).TrimEnd('/') + "/" : _archiveCurrentDirectory + item)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void fileBox_DoubleClick(object? sender, EventArgs e)
    {
        string selected = fileBox.SelectedItem?.ToString() ?? string.Empty;
        if (selected == LanguageManager.Get("goToParentDirectoryText")) { GoToParentDirectory(); return; }
        if (FindEntry(selected)?.IsDirectory == true)
        {
            _archiveCurrentDirectory = (_archiveCurrentDirectory + selected).TrimEnd('/') + "/";
            RefreshFileBox();
        }
    }

    private void GoToParentDirectory()
    {
        string current = _archiveCurrentDirectory.TrimEnd('/');
        int slash = current.LastIndexOf('/');
        _archiveCurrentDirectory = slash < 0 ? string.Empty : current[..(slash + 1)];
        RefreshFileBox();
    }

    private void RefreshCurrentDirectoryStatus() => statusLabel.Text = string.Format(LanguageManager.Get("CurrentDirectoryFormat"), string.IsNullOrEmpty(_archiveCurrentDirectory) ? LanguageManager.Get("Root") : _archiveCurrentDirectory);
    private void SetArchiveControls(bool loaded) { uninstallFileToolStripMenuItem.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed; extractToolStripMenuItem.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed; _extractNestedTarMenuItem.Visibility = loaded && _nestedTarInfo.HasNestedTar ? Visibility.Visible : Visibility.Collapsed; }
    private void SetMenuEnabled(bool enabled) { OpenToolStripMenuItem.IsEnabled = enabled; extractToolStripMenuItem.IsEnabled = enabled; compressToolStripMenuItem.IsEnabled = enabled; uninstallFileToolStripMenuItem.IsEnabled = enabled; _extractNestedTarMenuItem.IsEnabled = enabled; _contextMenuToolStripMenuItem.IsEnabled = enabled; }

    private void UnloadArchive()
    {
        _archivePath = string.Empty; _archivePassword = null; _archiveCurrentDirectory = string.Empty; _nestedTarInfo = NestedTarInfo.None; _archiveEntries.Clear(); fileBox.Items.Clear();
        statusProgressBar.Value = 0; statusLabel.Text = LanguageManager.Get("readytext"); mainTab.Text = LanguageManager.Get("mainTabText"); SetArchiveControls(false);
    }

    private void uninstallFileToolStripMenuItem_Click(object sender, EventArgs e) => UnloadArchive();
    private void extractToolStripMenuItem_Click(object sender, EventArgs e) { }
    private async void extractDirectlyALLToolStripMenuItem_Click(object sender, EventArgs e) => await ExtractAsync(false, false);
    private async void extractToFolderALLToolStripMenuItem_Click(object sender, EventArgs e) => await ExtractAsync(false, true);
    private async void extractDirectlySELECTEDToolStripMenuItem_Click(object sender, EventArgs e) => await ExtractAsync(true, false);
    private async void extractToAFolderSELECTEDToolStripMenuItem_Click(object sender, EventArgs e) => await ExtractAsync(true, true);
    private void compressToolStripMenuItem_Click(object sender, EventArgs e) { }
    private void SettingsToolStripMenuItem_Click(object sender, EventArgs e) => ShowInformation("Unavailble1");
    private void ShowInformation(string resourceKey) => MessageBox.Show(this, LanguageManager.Get(resourceKey), LanguageManager.Get("ApplicationTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowException(string fallbackStageCode, Exception exception)
    {
        string stageCode = exception is StageException stageException ? stageException.StageCode : fallbackStageCode;
        MessageBox.Show(this, MessageTipGenerator.GenerateTip(stageCode, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //F00010006
    }

    private void TZIPToolStripMenuItem_Click(object sender, EventArgs e)
    {
        TZIPForm tzip=new TZIPForm { Owner = this };
        tzip.ShowDialog();
    }

}
