using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;
using TANGERINE_ZIP.Tools.LightTool;

namespace TANGERINE_ZIP;

// Stage head: F0001
public partial class Form1 : Form
{
    private readonly ArchiveWorkerClient _archiveService = new();
    private readonly RarToolService _rarToolService = new();
    private readonly OpenFileDialog _compressionSourceDialog = new();
    private readonly List<ArchiveEntryInfo> _archiveEntries = [];
#if ENABLE_LIGHT
    private TangerineLightOverlay? _lightOverlay;
    private System.Windows.Forms.Timer? _fileBoxScrollTimer;
    private float _normalEdgeStrength;
#endif
    private readonly ToolStripMenuItem _extractNestedTarMenuItem;
    private readonly ToolStripMenuItem _stopWorkMenuItem;
    private readonly ToolStripMenuItem _contextMenuToolStripMenuItem;
    private readonly ToolStripMenuItem _createContextMenuToolStripMenuItem;
    private readonly ToolStripMenuItem _deleteContextMenuToolStripMenuItem;
    private readonly string? _startupArchivePath;
    private CancellationTokenSource? _operationCancellation;
    private NestedTarInfo _nestedTarInfo = NestedTarInfo.None;
    private string _archivePath = string.Empty;
    private string _archiveCurrentDirectory = string.Empty;
    private bool _isBusy;

    public Form1(string? startupArchivePath = null)
    {
        InitializeComponent();
        _startupArchivePath = startupArchivePath;
        _compressionSourceDialog.Multiselect = true;
        _compressionSourceDialog.CheckFileExists = true;
        _compressionSourceDialog.CheckPathExists = true;
        _compressionSourceDialog.RestoreDirectory = false;
        // The classic native dialog avoids Explorer shell extensions and unavailable recent locations.
        _compressionSourceDialog.AutoUpgradeEnabled = false;
        _compressionSourceDialog.ClientGuid = new Guid("D198A293-BAA6-4F93-91F5-7F587C667D84");
        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(userProfilePath)) _compressionSourceDialog.InitialDirectory = userProfilePath;
        _stopWorkMenuItem = new ToolStripMenuItem { Visible = false };
        _stopWorkMenuItem.Click += StopWorkMenuItem_Click;
        mainMenu.Items.Add(_stopWorkMenuItem);
        _contextMenuToolStripMenuItem = new ToolStripMenuItem();
        _createContextMenuToolStripMenuItem = new ToolStripMenuItem();
        _deleteContextMenuToolStripMenuItem = new ToolStripMenuItem();
        _createContextMenuToolStripMenuItem.Click += CreateContextMenuToolStripMenuItem_Click;
        _deleteContextMenuToolStripMenuItem.Click += DeleteContextMenuToolStripMenuItem_Click;
        _contextMenuToolStripMenuItem.DropDownItems.AddRange([_createContextMenuToolStripMenuItem, _deleteContextMenuToolStripMenuItem]);
        mainMenu.Items.Add(_contextMenuToolStripMenuItem);
        FormClosing += (_, args) =>
        {
            if (!_isBusy) return;
            args.Cancel = true;
            StopWorkMenuItem_Click(this, EventArgs.Empty);
        };
        FormClosed += (_, _) => _compressionSourceDialog.Dispose();
        _extractNestedTarMenuItem = new ToolStripMenuItem();
        _extractNestedTarMenuItem.Click += ExtractNestedTarMenuItem_Click;
        extractToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
        extractToolStripMenuItem.DropDownItems.Add(_extractNestedTarMenuItem);
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        try
        {
            ApplyLocalizedText();
            DarkTheme.Apply(this);
#if ENABLE_LIGHT
            ConfigureLightEffect();
#endif
            ConfigureEntryColors();
            SetArchiveControls(false);
            if (!string.IsNullOrWhiteSpace(_startupArchivePath))
                BeginInvoke(async () => await OpenArchiveAsync(_startupArchivePath));
            /*if (_rarToolService.ShouldCheckAtStartup && !_rarToolService.IsAvailable)
            {
                MessageBox.Show(this,
                    MessageTipGenerator.GenerateTip("RARTL0001", LanguageManager.Get("RarToolMissing")),
                    LanguageManager.Get("RarUnavailableTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); //RARTL0001
            }*///function:check if rar.exe is available,about: DONT_CHECK_RAR_EXE_AT_START file
        }
        catch (Exception exception)
        {
            ShowException("F00010001", exception); //F00010001
        }
    }

    private void ApplyLocalizedText()
    {
        statusLabel.Text = LanguageManager.Get("readytext");
        OpenToolStripMenuItem.Text = LanguageManager.Get("openText");
        extractToolStripMenuItem.Text = LanguageManager.Get("extractText");
        compressToolStripMenuItem.Text = LanguageManager.Get("compressText");
        SettingsToolStripMenuItem.Text = LanguageManager.Get("settingsText");
        uninstallFileToolStripMenuItem.Text = LanguageManager.Get("uninstallFileText");
        mainTab.Text = LanguageManager.Get("mainTabText");
        extractDirectlyALLToolStripMenuItem.Text = LanguageManager.Get("extractDirectlyALLText");
        extractToFolderALLToolStripMenuItem.Text = LanguageManager.Get("extractToFolderALLText");
        extractDirectlySELECTEDToolStripMenuItem.Text = LanguageManager.Get("extractDirectlySELECTEDText");
        extractToAFolderSELECTEDToolStripMenuItem.Text = LanguageManager.Get("extractToAFolderSELECTEDText");
        compressSelectFileToolStripMenuItem.Text = LanguageManager.Get("SelectFilesToCompress");
        _compressionSourceDialog.Title = LanguageManager.Get("SelectFilesToCompress");
        _compressionSourceDialog.Filter = LanguageManager.Get("AllFilesFilter");
        _extractNestedTarMenuItem.Text = LanguageManager.Get("ExtractNestedTar");
        _stopWorkMenuItem.Text = LanguageManager.Get("StopWork");
        _contextMenuToolStripMenuItem.Text = LanguageManager.Get("ContextMenu");
        _createContextMenuToolStripMenuItem.Text = LanguageManager.Get("CreateContextMenu");
        _deleteContextMenuToolStripMenuItem.Text = LanguageManager.Get("DeleteContextMenu");
        mainOpenFileDialog.Title = LanguageManager.Get("SelectArchive");
        mainOpenFileDialog.Filter = LanguageManager.Get("ArchiveDialogFilter");
    }

#if ENABLE_LIGHT
    private void ConfigureLightEffect()
    {
        _lightOverlay = new TangerineLightOverlay(this)
        {
            TargetFps = 60, Radius = 120f, LightStrength = 0.02f, EdgeStrength = 1.9f,
            EdgeWidth = 3f, disableWhenMouseSpeedGetTooFast = 100000
        };
        _normalEdgeStrength = _lightOverlay.EdgeStrength;
        _fileBoxScrollTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _fileBoxScrollTimer.Tick += FileBoxScrollTimer_Tick;
        fileBox.ViewChanged += FileBox_ViewChanged;
        _lightOverlay.Show(this);
    }
#endif

    private void ConfigureEntryColors()
    {
        fileBox.SetItemColorProvider(index =>
        {
            string displayName = fileBox.Items[index]?.ToString() ?? string.Empty;
            if (displayName == LanguageManager.Get("goToParentDirectoryText")) return Color.White;
            return FindEntry(displayName)?.IsDirectory == true ? Color.LightGoldenrodYellow : Color.WhiteSmoke;
        });
    }

    private async void OpenToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        if (mainOpenFileDialog.ShowDialog(this) != DialogResult.OK) return;
        await OpenArchiveAsync(mainOpenFileDialog.FileName);
    }

    /// <summary>
    /// Opens an archive selected either from the application's file dialog or from Explorer's context menu.
    /// </summary>
    private async Task OpenArchiveAsync(string archivePath)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        try
        {
            await RunOperationAsync(LanguageManager.Get("OpeningFile"), async (progress, token) =>
            {
                FileDetector.FileType type = FileDetector.DetectFileType(archivePath);
                if (!ArchiveCapabilities.CanOpen(type))
                    throw new StageException("F00010002", LanguageManager.Get("NotACompressedFile")); //F00010002
                NestedTarInfo nestedTarInfo = await _archiveService.AnalyzeNestedTarAsync(archivePath, token);
                IReadOnlyList<ArchiveEntryInfo> entries = nestedTarInfo.FlattenAutomatically
                    ? await _archiveService.ListNestedTarAsync(archivePath, nestedTarInfo.TarEntryKeys[0], token)
                    : await _archiveService.ListAsync(archivePath, token);
                _archivePath = archivePath;
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
        }
        catch (OperationCanceledException) { statusLabel.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception)
        {
            UnloadArchive();
            ShowException("F00010003", exception); //F00010003
        }
    }

    private void CreateContextMenuToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            if (MessageBox.Show(this, LanguageManager.Get("ContextMenuCreateWarning"), LanguageManager.Get("ContextMenu"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string executablePath = Environment.ProcessPath ?? throw new StageException("F00010009", LanguageManager.Get("ContextExecutableMissing")); //F00010009
            ContextMenuRegistrationService.Create(executablePath);
            MessageBox.Show(this, LanguageManager.Get("ContextMenuCreated"), LanguageManager.Get("ContextMenu"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { ShowException("F00010009", exception); } //F00010009
    }

    private void DeleteContextMenuToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            ContextMenuRegistrationService.Delete();
            MessageBox.Show(this, LanguageManager.Get("ContextMenuDeleted"), LanguageManager.Get("ContextMenu"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { ShowException("F00010010", exception); } //F00010010
    }

    private async Task RunOperationAsync(string initialStatus, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation)
    {
        _isBusy = true;
        _operationCancellation = new CancellationTokenSource();
        CancellationTokenSource operationIdentity = _operationCancellation;
        _stopWorkMenuItem.Visible = true;
        _stopWorkMenuItem.Enabled = true;
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
            _stopWorkMenuItem.Visible = false;
            SetMenuEnabled(true);
        }
    }

    private void StopWorkMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            CancellationTokenSource? current = _operationCancellation;
            if (current is null || current.IsCancellationRequested) return;
            DialogResult answer = MessageBox.Show(this, LanguageManager.Get("StopWorkRisk"), LanguageManager.Get("StopWork"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            // The modal confirmation pumps messages; the job may finish while the user reads it.
            if (answer != DialogResult.Yes || !ReferenceEquals(current, _operationCancellation)) return;
            current.Cancel();
            _stopWorkMenuItem.Enabled = false;
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
        mainFolderBrowserDialog.Description = LanguageManager.Get("SelectExtractFolderText");
        if (mainFolderBrowserDialog.ShowDialog(this) != DialogResult.OK) return;
        OverwritePolicy policy = AskOverwritePolicy();
        if (policy == OverwritePolicy.Cancel) return;
        string destination = mainFolderBrowserDialog.SelectedPath;
        if (createArchiveFolder) destination = Path.Combine(destination, Path.GetFileNameWithoutExtension(_archivePath));
        try
        {
            await RunOperationAsync(LanguageManager.Get("ExtractingText"), (progress, token) =>
                _nestedTarInfo.FlattenAutomatically
                    ? _archiveService.ExtractNestedTarsAsync(_archivePath, _nestedTarInfo.TarEntryKeys, destination, selectedEntries, false, policy, progress, token)
                    : _archiveService.ExtractAsync(_archivePath, destination, selectedEntries, policy, progress, token));
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
        if (_compressionSourceDialog.ShowDialog(this) != DialogResult.OK) return;
        string[] sourcePaths = _compressionSourceDialog.FileNames;
        if (sourcePaths.Length == 0) return;
        mainSaveFileDialog.Title = LanguageManager.Get("SelectOutputArchive");
        mainSaveFileDialog.Filter = LanguageManager.Get("CreateArchiveFilter");
        mainSaveFileDialog.AddExtension = true;
        mainSaveFileDialog.OverwritePrompt = true;
        if (mainSaveFileDialog.ShowDialog(this) != DialogResult.OK) return;
        FileDetector.FileType type = FileDetector.GetTypeFromCreateFilterIndex(mainSaveFileDialog.FilterIndex);
        try
        {
            await RunOperationAsync(LanguageManager.Get("CompressingText"), (progress, token) =>
                _archiveService.CreateAsync(sourcePaths, mainSaveFileDialog.FileName, type, progress, token));
            statusProgressBar.Value = 100;
            statusLabel.Text = LanguageManager.Get("CompressionCompleted");
        }
        catch (OperationCanceledException) { statusLabel.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("F00010005", exception); } //F00010005
    }

    private OverwritePolicy AskOverwritePolicy()
    {
        DialogResult result = MessageBox.Show(LanguageManager.Get("OverwritePolicyPrompt"), LanguageManager.Get("OverWriteOrNot"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result switch { DialogResult.Yes => OverwritePolicy.OverwriteAll, DialogResult.No => OverwritePolicy.SkipAll, _ => OverwritePolicy.Cancel };
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

        mainFolderBrowserDialog.Description = LanguageManager.Get("SelectExtractFolderText");
        if (mainFolderBrowserDialog.ShowDialog(this) != DialogResult.OK) return;
        OverwritePolicy policy = AskOverwritePolicy();
        if (policy == OverwritePolicy.Cancel) return;

        string destination = Path.Combine(mainFolderBrowserDialog.SelectedPath, Path.GetFileNameWithoutExtension(_archivePath));
        try
        {
            await RunOperationAsync(LanguageManager.Get("ExtractingNestedTar"), (progress, token) =>
                _archiveService.ExtractNestedTarsAsync(_archivePath, tarEntries, destination, null, true, policy, progress, token));
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
        fileBox.BeginUpdate();
        try
        {
            fileBox.Items.Clear();
            if (!string.IsNullOrEmpty(_archiveCurrentDirectory)) fileBox.Items.Add(LanguageManager.Get("goToParentDirectoryText"));
            foreach ((string name, _) in children) fileBox.Items.Add(name);
        }
        finally { fileBox.EndUpdate(); }
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
    private void SetArchiveControls(bool loaded) { uninstallFileToolStripMenuItem.Visible = loaded; extractToolStripMenuItem.Visible = loaded; _extractNestedTarMenuItem.Visible = loaded && _nestedTarInfo.HasNestedTar; }
    private void SetMenuEnabled(bool enabled) { OpenToolStripMenuItem.Enabled = enabled; extractToolStripMenuItem.Enabled = enabled; compressToolStripMenuItem.Enabled = enabled; uninstallFileToolStripMenuItem.Enabled = enabled; _extractNestedTarMenuItem.Enabled = enabled; _contextMenuToolStripMenuItem.Enabled = enabled; }

    private void UnloadArchive()
    {
        _archivePath = string.Empty; _archiveCurrentDirectory = string.Empty; _nestedTarInfo = NestedTarInfo.None; _archiveEntries.Clear(); fileBox.Items.Clear();
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
    private void TZIPToolStripMenuItem_DoubleClick(object sender, EventArgs e) { using TZIPForm aboutForm = new(); aboutForm.ShowDialog(this); }
    private void ShowInformation(string resourceKey) => MessageBox.Show(this, LanguageManager.Get(resourceKey), LanguageManager.Get("ApplicationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ShowException(string fallbackStageCode, Exception exception)
    {
        string stageCode = exception is StageException stageException ? stageException.StageCode : fallbackStageCode;
        MessageBox.Show(this, MessageTipGenerator.GenerateTip(stageCode, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //F00010006
    }

    private void TZIPToolStripMenuItem_Click(object sender, EventArgs e)
    {
        TZIPForm tzip=new TZIPForm();
        tzip.ShowDialog();
        tzip.Dispose();
    }

#if ENABLE_LIGHT
    private void FileBox_ViewChanged(object? sender, EventArgs e)
    {
        if (_lightOverlay is null || _fileBoxScrollTimer is null) return;
        _lightOverlay.EdgeStrength = 0f; _fileBoxScrollTimer.Stop(); _fileBoxScrollTimer.Start(); _lightOverlay.InvalidateCapture();
    }

    private void FileBoxScrollTimer_Tick(object? sender, EventArgs e)
    {
        _fileBoxScrollTimer?.Stop();
        if (_lightOverlay is not null) { _lightOverlay.EdgeStrength = _normalEdgeStrength; _lightOverlay.InvalidateCapture(); }
    }
#endif
}
