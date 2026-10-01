using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;
using System.Windows.Input;

namespace TANGERINE_ZIP;

// Stage head: MAINW (MainWindow)
public partial class MainWindow : Window
{
    private const double MouseWhiteThickenRadius = 140.0;
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
    private CancellationTokenSource? _searchCancellation;
    private (long Length, DateTime LastWriteUtc)? _loadedArchiveIdentity;
    private ArchiveClipboard? _archiveClipboard;
    private ExplorerArchiveClipboard? _explorerClipboard;
    private string? _clipboardStaging;

    private sealed record ArchiveClipboard(string ArchivePath, string[] Keys, bool Cut,
        (long Length, DateTime LastWriteUtc) SourceIdentity);

    private sealed record ArchiveEntryRow(string Name, string Key, bool IsDirectory,
        string OriginalSize, string CompressedSize)
    {
        public override string ToString() => Name;
    }

    public MainWindow() : this(null) { }

    public MainWindow(string? startupArchivePath)
    {
        InitializeComponent();
        // ContextMenu owns a separate popup presentation source. Refresh its local
        // resources when settings change and before each open so it never keeps
        // brushes from the previous appearance profile.
        AppearanceSettings.Changed += RefreshArchiveContextMenuTheme;
        archiveEntriesList.ContextMenu!.Opened += (_, _) => RefreshArchiveContextMenuTheme();
        Closed += (_, _) =>
        {
            AppearanceSettings.Changed -= RefreshArchiveContextMenuTheme;
            // The process-owned OLE data object can no longer serve Explorer
            // after this window closes, so its staged payload can be released.
            if (_clipboardStaging is not null) CleanupClipboardStaging(_clipboardStaging);
        };
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
        FontSize = SystemFonts.MessageFontSize;
        // Scale from the work area and keep the startup window's long-to-short ratio at sqrt(2):1.
        double initialHeight = SystemParameters.WorkArea.Height * 0.43;
        Height = initialHeight;
        Width = initialHeight * Math.Sqrt(2);
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
            ShowException("MAINW0001", exception); //MAINW0001
        }
    }

    private void ApplyLocalizedText()
    {
        operationStatusText.Text = LanguageManager.Get("readytext");
        openArchiveMenuItem.Header = LanguageManager.Get("openText");
        previewEntryMenuItem.Header = LanguageManager.Get("PreviewSelectedEntry");
        extractMenuItem.Header = LanguageManager.Get("extractText");
        compressMenuItem.Header = LanguageManager.Get("compressText");
        settingsMenuItem.Header = LanguageManager.Get("settingsText");
        unloadArchiveMenuItem.Header = LanguageManager.Get("uninstallFileText");
        archiveHeaderText.Text = LanguageManager.Get("ArchiveHeaderText");
        extractAllHereMenuItem.Header = LanguageManager.Get("extractDirectlyALLText");
        extractAllToFolderMenuItem.Header = LanguageManager.Get("extractToFolderALLText");
        extractToArchiveLocationMenuItem.Header = LanguageManager.Get("ExtractToArchiveLocation");
        extractSelectedHereMenuItem.Header = LanguageManager.Get("extractDirectlySELECTEDText");
        extractSelectedToFolderMenuItem.Header = LanguageManager.Get("extractToAFolderSELECTEDText");
        compressFilesMenuItem.Header = LanguageManager.Get("SelectFilesToCompress");
        _sourceFilesDialog.Title = LanguageManager.Get("SelectFilesToCompress");
        _sourceFilesDialog.Filter = LanguageManager.Get("AllFilesFilter");
        extractNestedTarMenuItem.Header = LanguageManager.Get("ExtractNestedTar");
        stopWorkMenuItem.Header = LanguageManager.Get("StopWork");
        systemSettingsMenuItem.Header = LanguageManager.Get("SystemSettingsMenu");
        defaultOpenWithMenuItem.Header = LanguageManager.Get("DefaultOpenWithMenu");
        createContextMenuItem.Header = LanguageManager.Get("CreateContextMenu");
        deleteContextMenuItem.Header = LanguageManager.Get("DeleteContextMenu");
        archiveOpenDialog.Title = LanguageManager.Get("SelectArchive");
        archiveOpenDialog.Filter = LanguageManager.Get("ArchiveDialogFilter");
        toolsMenuItem.Header = LanguageManager.Get("ToolsMenu");
        integrityMenuItem.Header = LanguageManager.Get("IntegrityMenu");
        repairMenuItem.Header = LanguageManager.Get("RepairMenu");
        hashMenuItem.Header = LanguageManager.Get("HashMenu");
        encodingMenuItem.Header = LanguageManager.Get("EncodingMenu");
        nameColumn.Header = LanguageManager.Get("ListName");
        originalSizeColumn.Header = LanguageManager.Get("ListOriginalSize");
        compressedSizeColumn.Header = LanguageManager.Get("ListCompressedSize");
        extractEntryContextMenu.Header = LanguageManager.Get("ExtractEntryToFolder");
        entryDetailsContextMenu.Header = LanguageManager.Get("EntryDetails");
        copyEntryContextMenu.Header = LanguageManager.Get("ArchiveEditCopy");
        cutEntryContextMenu.Header = LanguageManager.Get("ArchiveEditCut");
        pasteEntryContextMenu.Header = LanguageManager.Get("ArchiveEditPaste");
        deleteEntryContextMenu.Header = LanguageManager.Get("ArchiveEditDelete");
        searchBox.ToolTip = LanguageManager.Get("SearchEntries");
    }

    private void ConfigureEntryColors()
    {
        archiveEntriesList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            foreach (ArchiveEntryRow item in archiveEntriesList.Items.OfType<ArchiveEntryRow>())
                if (archiveEntriesList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
                {
                    // Keep entries on the shared text brush even after a live
                    // appearance change. Weight distinguishes directories
                    // without risking an unreadable hard-coded highlight.
                    container.SetResourceReference(Control.ForegroundProperty, "ForegroundBrush");
                    container.FontWeight = item.IsDirectory
                        ? FontWeights.SemiBold : FontWeights.Normal;
                }
        };
    }

    private void RefreshArchiveContextMenuTheme()
    {
        ContextMenu? menu = archiveEntriesList.ContextMenu;
        if (menu is null || Application.Current is null) return;
        ResourceDictionary shared = Application.Current.Resources;
        foreach (string key in new[] { "SurfaceBrush", "ForegroundBrush", "BorderBrush", "ButtonBrush" })
            menu.Resources[key] = shared[key];
        menu.Background = (Brush)shared["SurfaceBrush"];
        menu.Foreground = (Brush)shared["ForegroundBrush"];
        menu.BorderBrush = (Brush)shared["BorderBrush"];
        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            item.Background = (Brush)shared["SurfaceBrush"];
            item.Foreground = (Brush)shared["ForegroundBrush"];
        }
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
                        throw new StageException("MAINW0002", LanguageManager.Get("NotACompressedFile")); //MAINW0002
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
                    _loadedArchiveIdentity = GetArchiveIdentity(archivePath);
                    searchBox.Clear();
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
                ShowException("MAINW0003", exception); //MAINW0003
                return;
            }
        }
    }

    private void CreateContextMenu_Click(object? sender, EventArgs e)
    {
        try
        {
            string executablePath = Environment.ProcessPath ?? throw new StageException("MAINW0009", LanguageManager.Get("ContextExecutableMissing")); //MAINW0009
            ContextMenuSetupWindow wizard = new(ContextMenuSetupMode.Create, executablePath) { Owner = this };
            wizard.ShowDialog();
        }
        catch (Exception exception) { ShowException("MAINW0009", exception); } //MAINW0009
    }

    private void DefaultOpenWithMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Keep system integration separate from archive processing. Registration
            // applies to this Windows account; Windows owns the final default choice.
            DefaultOpenWithWindow window = new() { Owner = this };
            window.ShowDialog();
        }
        catch (Exception exception) { ShowException("MAINW0026", exception); } //MAINW0026
    }

    private void DeleteContextMenu_Click(object? sender, EventArgs e)
    {
        try
        {
            string executablePath = Environment.ProcessPath ?? throw new StageException("MAINW0010", LanguageManager.Get("ContextExecutableMissing")); //MAINW0010
            ContextMenuSetupWindow wizard = new(ContextMenuSetupMode.Delete, executablePath) { Owner = this };
            wizard.ShowDialog();
        }
        catch (Exception exception) { ShowException("MAINW0010", exception); } //MAINW0010
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
        catch (Exception exception) { ShowException("MAINW0008", exception); } //MAINW0008
    }

    private async Task ExtractAsync(bool selectedOnly, bool createArchiveFolder, bool useArchiveDirectory = false)
    {
        try
        {
            if (string.IsNullOrEmpty(_archivePath) || !File.Exists(_archivePath)) { ShowInformation("NoOpenedFile"); return; }
            IReadOnlyCollection<string>? selectedEntries = null;
            if (selectedOnly)
            {
                selectedEntries = GetSelectedArchiveEntries();
                if (selectedEntries.Count == 0) { ShowInformation("NoSelectedEntries"); return; }
            }
            string destination;
            if (useArchiveDirectory)
            {
                // Direct extraction targets the archive's parent itself; the
                // existing "to folder" command still creates a named subfolder.
                destination = Path.GetDirectoryName(Path.GetFullPath(_archivePath))
                    ?? throw new StageException("MAINW0014", LanguageManager.Get("ExtractArchiveLocationMissing")); //MAINW0014
            }
            else
            {
                destinationFolderDialog.Title = LanguageManager.Get("SelectExtractFolderText");
                if (destinationFolderDialog.ShowDialog(this) != true) return;
                destination = destinationFolderDialog.FolderName;
            }
            if (createArchiveFolder) destination = Path.Combine(destination, Path.GetFileNameWithoutExtension(_archivePath));
            Task<ConflictChoice> ResolveConflictAsync(ArchiveConflict conflict, CancellationToken _)
            {
                if (Path.GetFullPath(conflict.TargetPath).Equals(Path.GetFullPath(_archivePath), StringComparison.OrdinalIgnoreCase))
                    throw new StageException("MAINW0018", LanguageManager.Get("ExtractWouldOverwriteArchive")); //MAINW0018
                return Task.FromResult(OverwriteConflictWindow.Ask(this, conflict));
            }
            await RunOperationAsync(LanguageManager.Get("ExtractingText"), (progress, token) =>
                _nestedTarInfo.FlattenAutomatically
                    ? _archiveService.ExtractNestedTarsAsync(_archivePath, _nestedTarInfo.TarEntryKeys, destination, selectedEntries, false, OverwritePolicy.Ask, progress, token, _archivePassword,
                        ResolveConflictAsync, (issue, issueToken) => ExtractionPrompt.AskAsync(this, issue, issueToken))
                    : _archiveService.ExtractAsync(_archivePath, destination, selectedEntries, OverwritePolicy.Ask, progress, token, _archivePassword,
                        ResolveConflictAsync, (issue, issueToken) => ExtractionPrompt.AskAsync(this, issue, issueToken)));
            operationProgressBar.Value = 100;
            operationStatusText.Text = LanguageManager.Get("ExtractingCompleted");
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("MAINW0004", exception); } //MAINW0004
    }

    private async void CompressFilesMenu_Click(object sender, EventArgs e)
    {
        try
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
            string outputPath = archiveSaveDialog.FileName;
            FileDetector.FileType type = FileDetector.GetTypeFromCreateFilterIndex(archiveSaveDialog.FilterIndex);
            CompressionOptionsWindow optionsWindow = new(Path.GetFileName(outputPath), type) { Owner = this };
            if (optionsWindow.ShowDialog() != true) return;
            CompressionOptions options = optionsWindow.Options!;
            await RunOperationAsync(LanguageManager.Get("CompressingText"), (progress, token) =>
                _archiveService.CreateAsync(sourcePaths, outputPath, type, progress, token, options.Password, options));
            // Never display completion when the worker returned success but the
            // expected archive is missing from the exact path chosen by the user.
            if (!ArchiveOutput.Exists(outputPath))
                throw new StageException("MAINW0012", string.Format(LanguageManager.Get("CompressionOutputMissing"), outputPath)); //MAINW0012
            operationProgressBar.Value = 100;
            operationStatusText.Text = LanguageManager.Get("CompressionCompleted");
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("MAINW0005", exception); } //MAINW0005
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
            ShowException("MAINW0007", exception); //MAINW0007
        }
    }

    private void RefreshArchiveEntriesList()
    {
        if (!string.IsNullOrWhiteSpace(searchBox.Text)) { _ = SearchEntriesAsync(searchBox.Text); return; }
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
        if (!string.IsNullOrEmpty(_archiveCurrentDirectory))
            archiveEntriesList.Items.Add(new ArchiveEntryRow(LanguageManager.Get("goToParentDirectoryText"), "..", true, "", ""));
        foreach ((string name, bool directory) in children)
        {
            string key = _archiveCurrentDirectory + name;
            ArchiveEntryInfo? entry = _archiveEntries.FirstOrDefault(item =>
                item.Key.TrimEnd('/').Equals(key, StringComparison.OrdinalIgnoreCase));
            archiveEntriesList.Items.Add(new ArchiveEntryRow(name, directory ? key + "/" : key, directory,
                directory || entry is null ? "" : entry.SizeKnown ? ResourcePreflight.FormatBytes(entry.Size) : LanguageManager.Get("EntryDetailsUnknown"),
                directory || entry?.CompressedSize is null ? LanguageManager.Get("UnknownCompressedSize") : ResourcePreflight.FormatBytes(entry.CompressedSize.Value)));
        }
        RefreshCurrentDirectoryStatus();
    }

    private bool IsDirectoryEntry(string displayName)
    {
        string candidate = _archiveCurrentDirectory + displayName;
        string prefix = candidate.TrimEnd('/') + "/";
        // ZIP/TAR archives may omit explicit directory entries. An entry
        // beneath this prefix still makes the displayed item a folder.
        return _archiveEntries.Any(entry =>
            entry.IsDirectory && entry.Key.TrimEnd('/').Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetSelectedArchiveEntries() => archiveEntriesList.SelectedItems.OfType<ArchiveEntryRow>()
        .Select(item => item.Key)
        .Where(item => !string.IsNullOrEmpty(item) && item != "..")
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private async void ArchiveEntriesList_MouseDoubleClick(object? sender, EventArgs e)
    {
        ArchiveEntryRow? row = archiveEntriesList.SelectedItem as ArchiveEntryRow;
        string selected = row?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(selected)) return;
        if (selected == LanguageManager.Get("goToParentDirectoryText")) { GoToParentDirectory(); return; }
        if (row!.IsDirectory)
        {
            _archiveCurrentDirectory = row.Key.TrimEnd('/') + "/";
            searchBox.Clear();
            RefreshArchiveEntriesList();
            return;
        }
        await PreviewSelectedEntryAsync();
    }

    private async void PreviewEntryMenuItem_Click(object sender, RoutedEventArgs e) => await PreviewSelectedEntryAsync();

    private async Task PreviewSelectedEntryAsync()
    {
        try
        {
            if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
            if (string.IsNullOrWhiteSpace(_archivePath)) { ShowInformation("NoOpenedFile"); return; }
            string selected = archiveEntriesList.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selected) || selected == LanguageManager.Get("goToParentDirectoryText"))
            { ShowInformation("NoSelectedEntries"); return; }
            string key = (archiveEntriesList.SelectedItem as ArchiveEntryRow)?.Key ?? _archiveCurrentDirectory + selected;
            ArchiveEntryInfo entry = _archiveEntries.FirstOrDefault(item => !item.IsDirectory &&
                item.Key.TrimEnd('/').Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw new StageException("MAINW0013", LanguageManager.Get("PreviewEntryMissing")); //MAINW0013
            long memoryLimit = PreviewMemoryBudget.GetLimit();
            PreviewPayload? payload = null;
            await RunOperationAsync(LanguageManager.Get("PreviewLoading"), async (progress, token) =>
                payload = await new ArchiveService().ReadPreviewEntryAsync(_archivePath, entry.Key, entry.Size,
                    _nestedTarInfo, memoryLimit, progress, token, _archivePassword));
            if (payload is null)
                throw new StageException("MAINW0015", LanguageManager.Get("PreviewEntryMissing")); //MAINW0015
            using MemoryStream detectionStream = new(payload.Buffer, 0, payload.Length, writable: false);
            FileDetector.FileType contentType = FileDetector.DetectFileType(detectionStream);
            Window preview;
            if (contentType is FileDetector.FileType.Png or FileDetector.FileType.Jpeg or
                FileDetector.FileType.Gif or FileDetector.FileType.Bmp or FileDetector.FileType.Tiff or
                FileDetector.FileType.WebP or FileDetector.FileType.Ico)
                preview = new ImagePreviewWindow(payload, _archivePath, memoryLimit);
            else if (FileDetector.TryDecodeText(payload.Buffer.AsSpan(0, payload.Length), out string text))
                preview = new TextPreviewWindow(payload, text, _archivePath);
            else
                throw new StageException("MAINW0016", LanguageManager.Get("PreviewUnsupportedType")); //MAINW0016
            operationStatusText.Text = LanguageManager.Get("PreviewReady");
            preview.Owner = this;
            preview.ShowDialog();
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("MAINW0017", exception); } //MAINW0017
    }

    private void GoToParentDirectory()
    {
        string current = _archiveCurrentDirectory.TrimEnd('/');
        int slash = current.LastIndexOf('/');
        _archiveCurrentDirectory = slash < 0 ? string.Empty : current[..(slash + 1)];
        RefreshArchiveEntriesList();
    }

    private void RefreshCurrentDirectoryStatus() => operationStatusText.Text = string.Format(LanguageManager.Get("CurrentDirectoryFormat"), string.IsNullOrEmpty(_archiveCurrentDirectory) ? LanguageManager.Get("Root") : _archiveCurrentDirectory);

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(searchBox.Text))
        {
            _searchCancellation?.Cancel();
            _searchCancellation = null;
            RefreshArchiveEntriesList();
        }
        else _ = SearchEntriesAsync(searchBox.Text);
    }

    private async Task SearchEntriesAsync(string query)
    {
        _searchCancellation?.Cancel();
        CancellationTokenSource cancellation = new();
        _searchCancellation = cancellation;
        ArchiveEntryInfo[] snapshot = _archiveEntries.ToArray();
        try
        {
            // PLINQ searches independent entry names on multiple pool threads. The UI is
            // updated once on the dispatcher, and stale searches cannot replace newer text.
            ArchiveEntryInfo[] matches = await Task.Run(() => snapshot.AsParallel()
                .WithCancellation(cancellation.Token)
                .Where(item => item.Key.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(item => item.Key).ToArray(), cancellation.Token);
            if (!ReferenceEquals(_searchCancellation, cancellation)) return;
            archiveEntriesList.Items.Clear();
            foreach (ArchiveEntryInfo entry in matches)
                archiveEntriesList.Items.Add(new ArchiveEntryRow(entry.Key, entry.Key, entry.IsDirectory,
                    entry.IsDirectory ? "" : entry.SizeKnown ? ResourcePreflight.FormatBytes(entry.Size) : LanguageManager.Get("EntryDetailsUnknown"),
                    entry.CompressedSize is null ? LanguageManager.Get("UnknownCompressedSize") : ResourcePreflight.FormatBytes(entry.CompressedSize.Value)));
        }
        catch (OperationCanceledException) { }
    }

    private async void ExtractEntryContext_Click(object sender, RoutedEventArgs e) => await ExtractAsync(true, false);

    private void ArchiveEntriesList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ModifierKeys modifiers = e.KeyboardDevice.Modifiers;
        if (modifiers == ModifierKeys.Control && e.Key is Key.C or Key.X or Key.V)
        {
            e.Handled = true;
            if (e.Key == Key.V) _ = PasteArchiveEntriesAsync();
            else _ = CopyArchiveEntriesAsync(e.Key == Key.X);
        }
        else if (modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            e.Handled = true;
            _ = DeleteArchiveEntriesAsync();
        }
    }

    private async void CopyEntryContext_Click(object sender, RoutedEventArgs e) => await CopyArchiveEntriesAsync(false);
    private async void CutEntryContext_Click(object sender, RoutedEventArgs e) => await CopyArchiveEntriesAsync(true);
    private async void PasteEntryContext_Click(object sender, RoutedEventArgs e) => await PasteArchiveEntriesAsync();
    private async void DeleteEntryContext_Click(object sender, RoutedEventArgs e) => await DeleteArchiveEntriesAsync();

    private static (long Length, DateTime LastWriteUtc) GetArchiveIdentity(string path)
    {
        FileInfo file = new(path);
        file.Refresh();
        return (file.Length, file.LastWriteTimeUtc);
    }

    private bool CanEditOpenedArchive()
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return false; }
        if (string.IsNullOrEmpty(_archivePath) || !File.Exists(_archivePath))
        { ShowInformation("NoOpenedFile"); return false; }
        if (_nestedTarInfo.FlattenAutomatically ||
            !ArchiveEditService.CanEdit(FileDetector.DetectFileType(_archivePath)))
        { ShowInformation("ArchiveEditUnsupported"); return false; }
        if (_loadedArchiveIdentity != GetArchiveIdentity(_archivePath))
        { ShowInformation("ArchiveEditChanged"); return false; }
        return true;
    }

    private async Task CopyArchiveEntriesAsync(bool cut)
    {
        try
        {
            if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
            if (string.IsNullOrEmpty(_archivePath) || !File.Exists(_archivePath))
            { ShowInformation("NoOpenedFile"); return; }
            if (_nestedTarInfo.FlattenAutomatically ||
                (cut && !ArchiveEditService.CanEdit(FileDetector.DetectFileType(_archivePath))))
            { ShowInformation("ArchiveEditUnsupported"); return; }
            if (_loadedArchiveIdentity != GetArchiveIdentity(_archivePath))
            { ShowInformation("ArchiveEditChanged"); return; }
            string[] selected = GetSelectedArchiveEntries().ToArray();
            if (selected.Length == 0) { ShowInformation("NoSelectedEntries"); return; }
            string archive = _archivePath;
            (long Length, DateTime LastWriteUtc) identity = GetArchiveIdentity(archive);
            string temporaryRoot = TempDirectorySettings.GetDirectory();
            long bytes = _archiveEntries.Where(entry => !entry.IsDirectory && selected.Any(key =>
                    entry.Key.Equals(key.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                    entry.Key.StartsWith(key.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
                .Aggregate(0L, (total, entry) => total > long.MaxValue - Math.Max(entry.Size, 0)
                    ? long.MaxValue : total + Math.Max(entry.Size, 0));
            ResourcePreflight.Check(temporaryRoot, bytes > long.MaxValue - 64L * 1024 * 1024
                ? long.MaxValue : bytes + 64L * 1024 * 1024, 128L * 1024 * 1024);
            string staging = Path.Combine(temporaryRoot, "TangerineZipClipboard", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                await RunOperationAsync(LanguageManager.Get("ArchiveEditPreparingClipboard"),
                    (progress, token) => _archiveService.ExtractAsync(archive, staging, selected,
                        OverwritePolicy.SkipAll, progress, token, _archivePassword,
                        issueResolver: (issue, issueToken) => issue.Kind == ExtractionIssueKind.PathTraversal
                            ? Task.FromResult(new ExtractionAnswer(ExtractionDecision.Stop))
                            : ExtractionPrompt.AskAsync(this, issue, issueToken)));
                if (identity != GetArchiveIdentity(archive))
                    throw new StageException("MAINW0021", LanguageManager.Get("ArchiveEditChanged")); //MAINW0021
                string[] stagedPaths = selected.Select(key =>
                {
                    string relative = key.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
                    string full = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!full.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) || (!File.Exists(full) && !Directory.Exists(full)))
                        throw new StageException("MAINW0022", LanguageManager.Get("ArchiveEditClipboardStageFailed")); //MAINW0022
                    return full;
                }).ToArray();
                ExplorerArchiveClipboard replacement = ExplorerArchiveClipboard.PutFiles(stagedPaths, cut,
                    () => Dispatcher.BeginInvoke(async () => await FinishExplorerCutAsync(
                        new ArchiveClipboard(archive, selected, true, identity), stagedPaths, staging)));
                string? previousStaging = _clipboardStaging;
                _explorerClipboard = replacement;
                _clipboardStaging = staging;
                _archiveClipboard = new(archive, selected, cut, identity);
                if (previousStaging is not null) CleanupClipboardStaging(previousStaging);
            }
            catch
            {
                // Only a failed staging session can be removed immediately. A
                // successful session remains available for later Explorer pastes.
                CleanupClipboardStaging(staging);
                throw;
            }
            operationStatusText.Text = string.Format(LanguageManager.Get(cut ? "ArchiveEditCutReady" :
                "ArchiveEditCopyReady"), selected.Length);
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("MAINW0018", exception); } //MAINW0018
    }

    private async Task FinishExplorerCutAsync(ArchiveClipboard clipboard, string[] stagedPaths, string staging)
    {
        try
        {
            // A shell callback may arrive while another job holds the archive.
            // Wait for that job, then recheck identity before changing anything.
            while (_isBusy) await Task.Delay(100);
            // If Explorer skipped even one file, retain every original member.
            if (stagedPaths.Any(path => File.Exists(path) || Directory.Exists(path))) return;
            if (!File.Exists(clipboard.ArchivePath) ||
                clipboard.SourceIdentity != GetArchiveIdentity(clipboard.ArchivePath)) return;
            string backup = string.Empty;
            await RunOperationAsync(LanguageManager.Get("ArchiveEditProgress"), async (progress, token) =>
                backup = await _archiveService.EditAsync(clipboard.ArchivePath, clipboard.Keys,
                    string.Empty, ArchiveEditAction.Delete, progress, token));
            _archiveClipboard = null;
            CleanupClipboardStaging(staging);
            if (string.Equals(_clipboardStaging, staging, StringComparison.OrdinalIgnoreCase))
                _clipboardStaging = null;
            if (_archivePath.Equals(clipboard.ArchivePath, StringComparison.OrdinalIgnoreCase))
                await ReloadEditedArchiveAsync(clipboard.ArchivePath, _archiveCurrentDirectory, backup);
        }
        catch (Exception exception) { ShowException("MAINW0023", exception); } //MAINW0023
    }

    private void CleanupClipboardStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        catch (Exception exception)
        {
            // A failed deletion must be visible, yet must not turn a completed
            // clipboard or archive operation into an apparent write failure.
            string message = MessageTipGenerator.GenerateTip("MAINW0024", exception.Message);
            ThemedPromptWindow.Inform(IsLoaded ? this : null,
                LanguageManager.Get("ErrorTitle"), message); //MAINW0024
        }
    }

    private async Task PasteArchiveEntriesAsync()
    {
        try
        {
            if (!CanEditOpenedArchive()) return;
            ArchiveClipboard? clipboard = _archiveClipboard;
            if (clipboard is null) { ShowInformation("ArchiveEditClipboardEmpty"); return; }
            if (!clipboard.ArchivePath.Equals(_archivePath, StringComparison.OrdinalIgnoreCase) ||
                clipboard.SourceIdentity != GetArchiveIdentity(_archivePath))
            { _archiveClipboard = null; ShowInformation("ArchiveEditChanged"); return; }
            string path = _archivePath;
            string directory = _archiveCurrentDirectory;
            string backup = string.Empty;
            await RunOperationAsync(LanguageManager.Get("ArchiveEditProgress"), async (progress, token) =>
                backup = await _archiveService.EditAsync(path, clipboard.Keys, directory,
                    clipboard.Cut ? ArchiveEditAction.Move : ArchiveEditAction.Copy, progress, token));
            _archiveClipboard = clipboard.Cut ? null : clipboard with { SourceIdentity = GetArchiveIdentity(path) };
            await ReloadEditedArchiveAsync(path, directory, backup);
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("MAINW0019", exception); } //MAINW0019
    }

    private async Task DeleteArchiveEntriesAsync()
    {
        try
        {
            if (!CanEditOpenedArchive()) return;
            string[] selected = GetSelectedArchiveEntries().ToArray();
            if (selected.Length == 0) { ShowInformation("NoSelectedEntries"); return; }
            if (ThemedPromptWindow.Ask(this, LanguageManager.Get("ArchiveEditDelete"),
                string.Format(LanguageManager.Get("ArchiveEditDeleteConfirm"), selected.Length),
                (LanguageManager.Get("PromptYes"), MessageBoxResult.Yes),
                (LanguageManager.Get("PromptNo"), MessageBoxResult.No)) != MessageBoxResult.Yes) return;
            string path = _archivePath;
            string directory = _archiveCurrentDirectory;
            string backup = string.Empty;
            await RunOperationAsync(LanguageManager.Get("ArchiveEditProgress"), async (progress, token) =>
                backup = await _archiveService.EditAsync(path, selected, directory,
                    ArchiveEditAction.Delete, progress, token));
            _archiveClipboard = null;
            await ReloadEditedArchiveAsync(path, directory, backup);
        }
        catch (OperationCanceledException) { operationStatusText.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("MAINW0020", exception); } //MAINW0020
    }

    private async Task ReloadEditedArchiveAsync(string path, string directory, string backup)
    {
        await OpenArchiveAsync(path);
        if (!string.IsNullOrEmpty(_archivePath))
        {
            _archiveCurrentDirectory = directory;
            RefreshArchiveEntriesList();
            operationStatusText.Text = string.Format(LanguageManager.Get("ArchiveEditCompleted"), backup);
        }
    }

    private void EntryDetailsContext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (archiveEntriesList.SelectedItem is not ArchiveEntryRow row || row.Key == "..") return;
            ArchiveEntryInfo? entry = _archiveEntries.FirstOrDefault(item =>
                item.Key.Equals(row.Key, StringComparison.OrdinalIgnoreCase));
            string unknown = LanguageManager.Get("EntryDetailsUnknown");
            string originalSize = entry is null || !entry.SizeKnown ? unknown : ResourcePreflight.FormatBytes(entry.Size);
            string compressedSize = entry?.CompressedSize is long compressed
                ? ResourcePreflight.FormatBytes(compressed) : unknown;
            // Ratios use the selected member's bytes, never the total archive size.
            // Negative savings are valid when compression adds overhead to a small file.
            string ratio = entry is { IsDirectory: false, SizeKnown: true, Size: > 0, CompressedSize: >= 0 }
                ? (100d * (1d - (double)entry.CompressedSize.Value / entry.Size)).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + "%"
                : unknown;
            string saved = entry is { IsDirectory: false, SizeKnown: true, CompressedSize: >= 0 }
                ? (entry.Size - entry.CompressedSize.Value).ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " B"
                : unknown;
            string method = entry?.CompressionMethod is { Length: > 0 } name
                ? name == "None" ? LanguageManager.Get("EntryDetailsStored") : name : unknown;
            string encrypted = entry?.IsEncrypted is bool isEncrypted
                ? LanguageManager.Get(isEncrypted ? "EntryDetailsYes" : "EntryDetailsNo") : unknown;
            string crc = entry?.Crc is long value ? value.ToString("X8") : unknown;
            string modified = entry?.LastModifiedTime?.ToString("G", System.Globalization.CultureInfo.CurrentCulture) ?? unknown;
            string kind = LanguageManager.Get(row.IsDirectory ? "EntryDetailsDirectory" : "EntryDetailsFile");
            string format = string.IsNullOrEmpty(_archivePath) ? unknown :
                LanguageManager.Get($"Format_{FileDetector.DetectFileType(_archivePath)}");
            ThemedPromptWindow.Inform(this, LanguageManager.Get("EntryDetails"),
                $"{LanguageManager.Get("ListName")}: {row.Key}\n" +
                $"{LanguageManager.Get("EntryDetailsKind")}: {kind}\n" +
                $"{LanguageManager.Get("EntryDetailsFormat")}: {format}\n" +
                $"{LanguageManager.Get("ListOriginalSize")}: {originalSize}\n" +
                $"{LanguageManager.Get("ListCompressedSize")}: {compressedSize}\n" +
                $"{LanguageManager.Get("EntryDetailsMethod")}: {method}\n" +
                $"{LanguageManager.Get("EntryDetailsRatio")}: {ratio}\n" +
                $"{LanguageManager.Get("EntryDetailsSaved")}: {saved}\n" +
                $"{LanguageManager.Get("EntryDetailsModified")}: {modified}\n" +
                $"{LanguageManager.Get("EntryDetailsEncrypted")}: {encrypted}\n" +
                $"CRC32: {crc}");
        }
        catch (Exception exception) { ShowException("MAINW0025", exception); } //MAINW0025
    }

    private void IntegrityMenu_Click(object sender, RoutedEventArgs e) => new ArchiveToolsWindow(ArchiveToolKind.Integrity, _archivePath, _archivePassword) { Owner = this }.ShowDialog();
    private void RepairMenu_Click(object sender, RoutedEventArgs e) => new ArchiveToolsWindow(ArchiveToolKind.Repair, _archivePath, _archivePassword) { Owner = this }.ShowDialog();
    private void HashMenu_Click(object sender, RoutedEventArgs e) => new ArchiveToolsWindow(ArchiveToolKind.Hash, _archivePath) { Owner = this }.ShowDialog();

    private async void EncodingMenu_Click(object sender, RoutedEventArgs e)
    {
        EncodingChoiceWindow dialog = new() { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _archiveService.EntryEncodingName = dialog.EncodingName;
        if (!string.IsNullOrEmpty(_archivePath)) await OpenArchiveAsync(_archivePath);
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0 || !File.Exists(files[0])) return;
        string path = files[0];
        if (_isBusy)
        {
            if (ThemedPromptWindow.Ask(this, Title, LanguageManager.Get("DropBusyPrompt"),
                    (LanguageManager.Get("PromptYes"), MessageBoxResult.Yes),
                    (LanguageManager.Get("PromptNo"), MessageBoxResult.No)) == MessageBoxResult.Yes) StartAnotherInstance(path);
            return;
        }
        if (!string.IsNullOrEmpty(_archivePath))
        {
            MessageBoxResult choice = ThemedPromptWindow.Ask(this, Title, LanguageManager.Get("DropLoadedPrompt"),
                (LanguageManager.Get("PromptUnload"), MessageBoxResult.Yes),
                (LanguageManager.Get("PromptNewInstance"), MessageBoxResult.No),
                (LanguageManager.Get("Cancel"), MessageBoxResult.Cancel));
            if (choice == MessageBoxResult.Cancel) return;
            if (choice == MessageBoxResult.No) { StartAnotherInstance(path); return; }
            UnloadArchive();
        }
        await OpenArchiveAsync(path);
    }

    private void ArchiveEntriesList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Empty GridViews can retain the initial, untranslated header's measured
        // width. Allocate columns from the live viewport so headers and entries
        // remain visible without fixed pixel widths or search-box spacer offsets.
        if (nameColumn is null || originalSizeColumn is null || compressedSizeColumn is null) return;
        double available = Math.Max(0, e.NewSize.Width - SystemParameters.VerticalScrollBarWidth -
            archiveEntriesList.BorderThickness.Left - archiveEntriesList.BorderThickness.Right);
        nameColumn.Width = available * 0.5;
        originalSizeColumn.Width = available * 0.25;
        compressedSizeColumn.Width = available * 0.25;
    }

    private static void StartAnotherInstance(string path)
    {
        // Do not allocate a console when opening another archive in its own GUI
        // instance. CreateNoWindow does not hide Windows-subsystem WPF windows.
        System.Diagnostics.ProcessStartInfo start = new(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(path);
        System.Diagnostics.Process.Start(start);
    }
    private void SetArchiveControls(bool loaded) { unloadArchiveMenuItem.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed; previewEntryMenuItem.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed; extractMenuItem.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed; extractNestedTarMenuItem.Visibility = loaded && _nestedTarInfo.HasNestedTar ? Visibility.Visible : Visibility.Collapsed; }
    private void SetMenuEnabled(bool enabled) { openArchiveMenuItem.IsEnabled = enabled; previewEntryMenuItem.IsEnabled = enabled; extractMenuItem.IsEnabled = enabled; compressMenuItem.IsEnabled = enabled; unloadArchiveMenuItem.IsEnabled = enabled; extractNestedTarMenuItem.IsEnabled = enabled; systemSettingsMenuItem.IsEnabled = enabled; }

    private void UnloadArchive()
    {
        _archivePath = string.Empty; _archivePassword = null; _archiveCurrentDirectory = string.Empty; _nestedTarInfo = NestedTarInfo.None; _loadedArchiveIdentity = null; _archiveClipboard = null; _archiveEntries.Clear(); searchBox.Clear(); archiveEntriesList.Items.Clear();
        operationProgressBar.Value = 0; operationStatusText.Text = LanguageManager.Get("readytext"); archiveHeaderText.Text = LanguageManager.Get("ArchiveHeaderText"); SetArchiveControls(false);
    }

    private void UnloadArchiveMenu_Click(object sender, EventArgs e) => UnloadArchive();
    private async void ExtractAllHereMenu_Click(object sender, EventArgs e) => await ExtractAsync(false, false);
    private async void ExtractAllToFolderMenu_Click(object sender, EventArgs e) => await ExtractAsync(false, true);
    private async void ExtractToArchiveLocationMenu_Click(object sender, EventArgs e) => await ExtractAsync(false, false, true);
    private async void ExtractSelectedHereMenu_Click(object sender, EventArgs e) => await ExtractAsync(true, false);
    private async void ExtractSelectedToFolderMenu_Click(object sender, EventArgs e) => await ExtractAsync(true, true);
    private void SettingsMenu_Click(object sender, EventArgs e)
    {
        try
        {
            SettingsWindow settingsWindow = new() { Owner = this };
            settingsWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            ShowException("MAINW0011", exception); //MAINW0011
        }
    }
    private void ShowInformation(string resourceKey) => ThemedPromptWindow.Inform(this,
        LanguageManager.Get("ApplicationTitle"), LanguageManager.Get(resourceKey));

    private void ShowException(string fallbackStageCode, Exception exception)
    {
        string stageCode = exception is StageException stageException ? stageException.StageCode : fallbackStageCode;
        ThemedPromptWindow.Inform(this, LanguageManager.Get("ErrorTitle"),
            MessageTipGenerator.GenerateTip(stageCode, exception.Message)); //MAINW0006
    }

    private void AboutMenu_Click(object sender, EventArgs e)
    {
        try
        {
            AboutWindow aboutWindow = new() { Owner = this };
            aboutWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            ShowException("MAINW0019", exception); //MAINW0019
        }
    }

}
