using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: F0001
public partial class MainWindow : Window
{
    private readonly ArchiveWorkerClient _archiveService = new();
    private readonly OpenFileDialog _sourceFilesDialog = new();
    private readonly OpenFileDialog archiveOpenDialog = new();
    private readonly SaveFileDialog archiveSaveDialog = new();
    private readonly OpenFolderDialog destinationFolderDialog = new();
    private readonly List<ArchiveEntryInfo> _archiveEntries = [];
    private readonly string? _startupArchivePath;
    private CancellationTokenSource? _operationCancellation;
    private NestedTarInfo _nestedTarInfo = NestedTarInfo.None;
    private string _archivePath = string.Empty;
    private string? _archivePassword;
    private string _archiveCurrentDirectory = string.Empty;
    private bool _isBusy;

    public MainWindow() : this(null) { }

    public MainWindow(string? startupArchivePath)
    {
        InitializeComponent();
        FontSize = SystemFonts.MessageFontSize;
        WpfUi.SizeWindow(this, 0.38, 0.38);
        _startupArchivePath = startupArchivePath;
        _sourceFilesDialog.Multiselect = true;
        _sourceFilesDialog.CheckFileExists = true;
        _sourceFilesDialog.RestoreDirectory = false;
        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(userProfilePath)) _sourceFilesDialog.InitialDirectory = userProfilePath;
        Closing += (_, args) =>
        {
            if (!_isBusy) return;
            args.Cancel = true;
            StopWorkMenuItem_Click(this, EventArgs.Empty);
        };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
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
        operationStatusText.Text = LanguageManager.Get("readytext");
        openArchiveMenuItem.Header = LanguageManager.Get("openText");
        extractMenuItem.Header = LanguageManager.Get("extractText");
        compressMenuItem.Header = LanguageManager.Get("compressText");
        settingsMenuItem.Header = LanguageManager.Get("settingsText");
        unloadArchiveMenuItem.Header = LanguageManager.Get("uninstallFileText");
        archiveHeaderText.Text = LanguageManager.Get("ArchiveHeaderText");
        extractAllHereMenuItem.Header = LanguageManager.Get("extractDirectlyALLText");
        extractAllToFolderMenuItem.Header = LanguageManager.Get("extractToFolderALLText");
        extractSelectedHereMenuItem.Header = LanguageManager.Get("extractDirectlySELECTEDText");
        extractSelectedToFolderMenuItem.Header = LanguageManager.Get("extractToAFolderSELECTEDText");
        compressFilesMenuItem.Header = LanguageManager.Get("SelectFilesToCompress");
        _sourceFilesDialog.Title = LanguageManager.Get("SelectFilesToCompress");
        _sourceFilesDialog.Filter = LanguageManager.Get("AllFilesFilter");
        extractNestedTarMenuItem.Header = LanguageManager.Get("ExtractNestedTar");
        stopWorkMenuItem.Header = LanguageManager.Get("StopWork");
        contextMenuItem.Header = LanguageManager.Get("ContextMenu");
        createContextMenuItem.Header = LanguageManager.Get("CreateContextMenu");
        deleteContextMenuItem.Header = LanguageManager.Get("DeleteContextMenu");
        archiveOpenDialog.Title = LanguageManager.Get("SelectArchive");
        archiveOpenDialog.Filter = LanguageManager.Get("ArchiveDialogFilter");
    }

    private void ConfigureEntryColors()
    {
        archiveEntriesList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            foreach (string item in archiveEntriesList.Items.OfType<string>())
                if (archiveEntriesList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
                    container.Foreground = FindEntry(item)?.IsDirectory == true
                        ? WpfUi.DirectoryForeground : WpfUi.Foreground;
        };
    }

    private async void OpenArchiveMenu_Click(object sender, EventArgs e)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        if (archiveOpenDialog.ShowDialog(this) != true) return;
        await OpenArchiveAsync(archiveOpenDialog.FileName);
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
            if (passwordProtected && !ArchivePasswordWindow.TryGetExtractionPassword(
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
                    archiveHeaderText.Text = LanguageManager.Get($"Format_{type}") + " " + LanguageManager.Get(type is FileDetector.FileType.Iso or FileDetector.FileType.Wim ? "ImageFile" : "CompressFile");
                    RefreshArchiveEntriesList();
                    SetArchiveControls(true);
                });
                operationProgressBar.Value = 100;
                RefreshCurrentDirectoryStatus();
                return;
            }
            catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); return; }
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

    private void CreateContextMenu_Click(object? sender, EventArgs e)
    {
        try
        {
            string executablePath = Environment.ProcessPath ?? throw new StageException("F00010009", LanguageManager.Get("ContextExecutableMissing")); //F00010009
            ContextMenuSetupWindow wizard = new(ContextMenuSetupMode.Create, executablePath) { Owner = this };
            wizard.ShowDialog();
        }
        catch (Exception exception) { ShowException("F00010009", exception); } //F00010009
    }

    private void DeleteContextMenu_Click(object? sender, EventArgs e)
    {
        try
        {
            string executablePath = Environment.ProcessPath ?? throw new StageException("F00010010", LanguageManager.Get("ContextExecutableMissing")); //F00010010
            ContextMenuSetupWindow wizard = new(ContextMenuSetupMode.Delete, executablePath) { Owner = this };
            wizard.ShowDialog();
        }
        catch (Exception exception) { ShowException("F00010010", exception); } //F00010010
    }

    private async Task RunOperationAsync(string initialStatus, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation)
    {
        _isBusy = true;
        _operationCancellation = new CancellationTokenSource();
        CancellationTokenSource operationIdentity = _operationCancellation;
        stopWorkMenuItem.Visibility = Visibility.Visible;
        stopWorkMenuItem.IsEnabled = true;
        SetMenuEnabled(false);
        operationStatusText.Text = initialStatus;
        operationProgressBar.Value = 0;
        Progress<ArchiveProgress> progress = new(item =>
        {
            // Reports can remain queued after completion or cancellation. Only the current job owns the UI.
            if (!ReferenceEquals(_operationCancellation, operationIdentity) || operationIdentity.IsCancellationRequested) return;
            operationProgressBar.Value = Math.Clamp(item.Percentage, 0, 100);
            operationStatusText.Text = string.IsNullOrWhiteSpace(item.EntryKey) ? initialStatus : string.Format(LanguageManager.Get("ProgressStatus"), initialStatus, item.EntryKey, item.Percentage);
        });
        try { await operation(progress, _operationCancellation.Token); operationIdentity.Token.ThrowIfCancellationRequested(); }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _isBusy = false;
            stopWorkMenuItem.Visibility = Visibility.Collapsed;
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
            stopWorkMenuItem.IsEnabled = false;
            operationStatusText.Text = LanguageManager.Get("StoppingWork");
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
        destinationFolderDialog.Title = LanguageManager.Get("SelectExtractFolderText");
        if (destinationFolderDialog.ShowDialog(this) != true) return;
        string destination = destinationFolderDialog.FolderName;
        if (createArchiveFolder) destination = Path.Combine(destination, Path.GetFileNameWithoutExtension(_archivePath));
        try
        {
            await RunOperationAsync(LanguageManager.Get("ExtractingText"), (progress, token) =>
                _nestedTarInfo.FlattenAutomatically
                    ? _archiveService.ExtractNestedTarsAsync(_archivePath, _nestedTarInfo.TarEntryKeys, destination, selectedEntries, false, OverwritePolicy.Ask, progress, token, _archivePassword,
                        (conflict, _) => Task.FromResult(OverwriteConflictWindow.Ask(this, conflict)))
                    : _archiveService.ExtractAsync(_archivePath, destination, selectedEntries, OverwritePolicy.Ask, progress, token, _archivePassword,
                        (conflict, _) => Task.FromResult(OverwriteConflictWindow.Ask(this, conflict))));
            operationProgressBar.Value = 100;
            operationStatusText.Text = LanguageManager.Get("ExtractingCompleted");
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("F00010004", exception); } //F00010004
    }

    private async void CompressFilesMenu_Click(object sender, EventArgs e)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        _sourceFilesDialog.FileName = string.Empty;
        if (_sourceFilesDialog.ShowDialog(this) != true) return;
        string[] sourcePaths = _sourceFilesDialog.FileNames;
        if (sourcePaths.Length == 0) return;
        archiveSaveDialog.Title = LanguageManager.Get("SelectOutputArchive");
        archiveSaveDialog.Filter = LanguageManager.Get("CreateArchiveFilter");
        archiveSaveDialog.AddExtension = true;
        archiveSaveDialog.OverwritePrompt = true;
        if (archiveSaveDialog.ShowDialog(this) != true) return;
        FileDetector.FileType type = FileDetector.GetTypeFromCreateFilterIndex(archiveSaveDialog.FilterIndex);
        if (!ArchivePasswordWindow.TryGetCreationPassword(this, Path.GetFileName(archiveSaveDialog.FileName), type, out string? password)) return;
        try
        {
            await RunOperationAsync(LanguageManager.Get("CompressingText"), (progress, token) =>
                _archiveService.CreateAsync(sourcePaths, archiveSaveDialog.FileName, type, progress, token, password));
            operationProgressBar.Value = 100;
            operationStatusText.Text = LanguageManager.Get("CompressionCompleted");
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
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

        destinationFolderDialog.Title = LanguageManager.Get("SelectExtractFolderText");
        if (destinationFolderDialog.ShowDialog(this) != true) return;
        string destination = Path.Combine(destinationFolderDialog.FolderName, Path.GetFileNameWithoutExtension(_archivePath));
        try
        {
            await RunOperationAsync(LanguageManager.Get("ExtractingNestedTar"), (progress, token) =>
                _archiveService.ExtractNestedTarsAsync(_archivePath, tarEntries, destination, null, true, OverwritePolicy.Ask, progress, token,
                    _archivePassword, (conflict, _) => Task.FromResult(OverwriteConflictWindow.Ask(this, conflict))));
            operationProgressBar.Value = 100;
            operationStatusText.Text = LanguageManager.Get("ExtractingCompleted");
        }
        catch (OperationCanceledException)
        {
            operationStatusText.Text = LanguageManager.Get("OperationCancelled");
        }
        catch (Exception exception)
        {
            ShowException("F00010007", exception); //F00010007
        }
    }

    private void RefreshArchiveEntriesList()
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
        archiveEntriesList.Items.Clear();
        if (!string.IsNullOrEmpty(_archiveCurrentDirectory)) archiveEntriesList.Items.Add(LanguageManager.Get("goToParentDirectoryText"));
        foreach ((string name, _) in children) archiveEntriesList.Items.Add(name);
        RefreshCurrentDirectoryStatus();
    }

    private ArchiveEntryInfo? FindEntry(string displayName)
    {
        string candidate = _archiveCurrentDirectory + displayName;
        return _archiveEntries.FirstOrDefault(entry => entry.Key.TrimEnd('/').Equals(candidate, StringComparison.OrdinalIgnoreCase) || entry.Key.StartsWith(candidate.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetSelectedArchiveEntries() => archiveEntriesList.SelectedItems.Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .Where(item => !string.IsNullOrEmpty(item) && item != LanguageManager.Get("goToParentDirectoryText"))
        .Select(item => FindEntry(item)?.IsDirectory == true ? (_archiveCurrentDirectory + item).TrimEnd('/') + "/" : _archiveCurrentDirectory + item)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void ArchiveEntriesList_MouseDoubleClick(object? sender, EventArgs e)
    {
        string selected = archiveEntriesList.SelectedItem?.ToString() ?? string.Empty;
        if (selected == LanguageManager.Get("goToParentDirectoryText")) { GoToParentDirectory(); return; }
        if (FindEntry(selected)?.IsDirectory == true)
        {
            _archiveCurrentDirectory = (_archiveCurrentDirectory + selected).TrimEnd('/') + "/";
            RefreshArchiveEntriesList();
        }
    }

    private void GoToParentDirectory()
    {
        string current = _archiveCurrentDirectory.TrimEnd('/');
        int slash = current.LastIndexOf('/');
        _archiveCurrentDirectory = slash < 0 ? string.Empty : current[..(slash + 1)];
        RefreshArchiveEntriesList();
    }

    private void RefreshCurrentDirectoryStatus() => operationStatusText.Text = string.Format(LanguageManager.Get("CurrentDirectoryFormat"), string.IsNullOrEmpty(_archiveCurrentDirectory) ? LanguageManager.Get("Root") : _archiveCurrentDirectory);
    private void SetArchiveControls(bool loaded) { unloadArchiveMenuItem.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed; extractMenuItem.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed; extractNestedTarMenuItem.Visibility = loaded && _nestedTarInfo.HasNestedTar ? Visibility.Visible : Visibility.Collapsed; }
    private void SetMenuEnabled(bool enabled) { openArchiveMenuItem.IsEnabled = enabled; extractMenuItem.IsEnabled = enabled; compressMenuItem.IsEnabled = enabled; unloadArchiveMenuItem.IsEnabled = enabled; extractNestedTarMenuItem.IsEnabled = enabled; contextMenuItem.IsEnabled = enabled; }

    private void UnloadArchive()
    {
        _archivePath = string.Empty; _archivePassword = null; _archiveCurrentDirectory = string.Empty; _nestedTarInfo = NestedTarInfo.None; _archiveEntries.Clear(); archiveEntriesList.Items.Clear();
        operationProgressBar.Value = 0; operationStatusText.Text = LanguageManager.Get("readytext"); archiveHeaderText.Text = LanguageManager.Get("ArchiveHeaderText"); SetArchiveControls(false);
    }

    private void UnloadArchiveMenu_Click(object sender, EventArgs e) => UnloadArchive();
    private async void ExtractAllHereMenu_Click(object sender, EventArgs e) => await ExtractAsync(false, false);
    private async void ExtractAllToFolderMenu_Click(object sender, EventArgs e) => await ExtractAsync(false, true);
    private async void ExtractSelectedHereMenu_Click(object sender, EventArgs e) => await ExtractAsync(true, false);
    private async void ExtractSelectedToFolderMenu_Click(object sender, EventArgs e) => await ExtractAsync(true, true);
    private void SettingsMenu_Click(object sender, EventArgs e) => ShowInformation("Unavailble1");
    private void ShowInformation(string resourceKey) => MessageBox.Show(this, LanguageManager.Get(resourceKey), LanguageManager.Get("ApplicationTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowException(string fallbackStageCode, Exception exception)
    {
        string stageCode = exception is StageException stageException ? stageException.StageCode : fallbackStageCode;
        MessageBox.Show(this, MessageTipGenerator.GenerateTip(stageCode, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error); //F00010006
    }

    private void AboutMenu_Click(object sender, EventArgs e)
    {
        AboutWindow aboutWindow = new() { Owner = this };
        aboutWindow.ShowDialog();
    }

}
