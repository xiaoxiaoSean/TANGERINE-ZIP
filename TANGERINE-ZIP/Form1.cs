using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;
using TANGERINE_ZIP.Tools.LightTool;

namespace TANGERINE_ZIP;

// Stage head: F0001
public partial class Form1 : Form
{
    private readonly ArchiveService _archiveService = new();
    private readonly List<ArchiveEntryInfo> _archiveEntries = [];
    private TangerineLightOverlay? _lightOverlay;
    private System.Windows.Forms.Timer? _fileBoxScrollTimer;
    private CancellationTokenSource? _operationCancellation;
    private float _normalEdgeStrength;
    private string _archivePath = string.Empty;
    private string _archiveCurrentDirectory = string.Empty;
    private bool _isBusy;

    public Form1() => InitializeComponent();

    private void Form1_Load(object sender, EventArgs e)
    {
        try
        {
            ApplyLocalizedText();
            DarkTheme.Apply(this);
            ConfigureLightEffect();
            ConfigureEntryColors();
            SetArchiveControls(false);
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
        mainOpenFileDialog.Title = LanguageManager.Get("SelectArchive");
        mainOpenFileDialog.Filter = LanguageManager.Get("ArchiveDialogFilter");
    }

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
        try
        {
            await RunOperationAsync(LanguageManager.Get("OpeningFile"), async (progress, token) =>
            {
                FileDetector.FileType type = FileDetector.DetectFileType(mainOpenFileDialog.FileName);
                if (!ArchiveCapabilities.CanOpen(type))
                    throw new StageException("F00010002", LanguageManager.Get("NotACompressedFile")); //F00010002
                IReadOnlyList<ArchiveEntryInfo> entries = await _archiveService.ListAsync(mainOpenFileDialog.FileName, token);
                _archivePath = mainOpenFileDialog.FileName;
                _archiveCurrentDirectory = string.Empty;
                _archiveEntries.Clear();
                _archiveEntries.AddRange(entries);
                mainTab.Text = LanguageManager.Get($"Format_{type}") + " " + LanguageManager.Get(type is FileDetector.FileType.Iso or FileDetector.FileType.Wim ? "ImageFile" : "CompressFile");
                RefreshFileBox();
                SetArchiveControls(true);
                progress.Report(new ArchiveProgress(100, string.Empty));
            });
        }
        catch (OperationCanceledException) { statusLabel.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception)
        {
            UnloadArchive();
            ShowException("F00010003", exception); //F00010003
        }
    }

    private async Task RunOperationAsync(string initialStatus, Func<IProgress<ArchiveProgress>, CancellationToken, Task> operation)
    {
        _isBusy = true;
        _operationCancellation = new CancellationTokenSource();
        SetMenuEnabled(false);
        statusLabel.Text = initialStatus;
        statusProgressBar.Value = 0;
        Progress<ArchiveProgress> progress = new(item =>
        {
            statusProgressBar.Value = Math.Clamp(item.Percentage, 0, 100);
            statusLabel.Text = string.IsNullOrWhiteSpace(item.EntryKey) ? initialStatus : string.Format(LanguageManager.Get("ProgressStatus"), initialStatus, item.EntryKey, item.Percentage);
        });
        try { await operation(progress, _operationCancellation.Token); }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _isBusy = false;
            SetMenuEnabled(true);
        }
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
            await RunOperationAsync(LanguageManager.Get("ExtractingText"), (progress, token) => _archiveService.ExtractAsync(_archivePath, destination, selectedEntries, policy, progress, token));
            statusProgressBar.Value = 100;
            statusLabel.Text = LanguageManager.Get("ExtractingCompleted");
        }
        catch (OperationCanceledException) { statusLabel.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception exception) { ShowException("F00010004", exception); } //F00010004
    }

    private async void compressSelectFileToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (_isBusy) { ShowInformation("AlreadyDoingJob"); return; }
        using OpenFileDialog sourceDialog = new() { Multiselect = true, Title = LanguageManager.Get("SelectFilesToCompress"), Filter = LanguageManager.Get("AllFilesFilter") };
        if (sourceDialog.ShowDialog(this) != DialogResult.OK) return;
        mainSaveFileDialog.Title = LanguageManager.Get("SelectOutputArchive");
        mainSaveFileDialog.Filter = LanguageManager.Get("CreateArchiveFilter");
        mainSaveFileDialog.AddExtension = true;
        mainSaveFileDialog.OverwritePrompt = true;
        if (mainSaveFileDialog.ShowDialog(this) != DialogResult.OK) return;
        FileDetector.FileType type = FileDetector.DetectFileTypeFromExtension(mainSaveFileDialog.FileName);
        try
        {
            await RunOperationAsync(LanguageManager.Get("CompressingText"), (progress, token) => _archiveService.CreateAsync(sourceDialog.FileNames, mainSaveFileDialog.FileName, type, progress, token));
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
    private void SetArchiveControls(bool loaded) { uninstallFileToolStripMenuItem.Visible = loaded; extractToolStripMenuItem.Visible = loaded; }
    private void SetMenuEnabled(bool enabled) { OpenToolStripMenuItem.Enabled = enabled; extractToolStripMenuItem.Enabled = enabled; compressToolStripMenuItem.Enabled = enabled; uninstallFileToolStripMenuItem.Enabled = enabled; }

    private void UnloadArchive()
    {
        _archivePath = string.Empty; _archiveCurrentDirectory = string.Empty; _archiveEntries.Clear(); fileBox.Items.Clear();
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
}
