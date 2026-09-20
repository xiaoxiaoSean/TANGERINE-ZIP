using TANGERINE_ZIP.Tools;
using TANGERINE_ZIP.Tools.LightTool;

// Stage head: F0002
namespace TANGERINE_ZIP;

public partial class FreeFilePickerForm : Form
{
    private string? _currentPath;
    private string? _initialPath;
    private CancellationTokenSource? _loadCancellation;
    private readonly Dictionary<string, string> _displayPaths = new(StringComparer.CurrentCultureIgnoreCase);

    public IReadOnlyList<string> SelectedFiles { get; private set; } = [];

#if ENABLE_LIGHT
    private TangerineLightOverlay? _lightOverlay;
    private System.Windows.Forms.Timer? _fileBoxScrollTimer;
    private float _normalEdgeStrength;
#endif

    public FreeFilePickerForm()
    {
        InitializeComponent();
        fileListBox.DoubleClick += FileListBox_DoubleClick;
        confirmButton.Click += ConfirmButton_Click;
        formTipText.ReadOnly = true;
    }

    public void ShowTipText(string inputTip) => formTipText.Text = inputTip;

    public void InputPath(string inputPath)
    {
        _initialPath = inputPath;
        if (IsHandleCreated) _ = LoadPathAsync(inputPath);
    }

    private async Task LoadPathAsync(string inputPath)
    {
        CancellationTokenSource currentLoad = ReplaceLoadCancellation();
        fileListBox.Items.Clear();
        fileListBox.Items.Add(LanguageManager.Get("LoadingFiles"));
        _displayPaths.Clear();
        try
        {
            string[] allItems = await Task.Run(() =>
            {
                currentLoad.Token.ThrowIfCancellationRequested();
                string[] files = Directory.GetFiles(inputPath);
                currentLoad.Token.ThrowIfCancellationRequested();
                string[] folders = Directory.GetDirectories(inputPath);
                currentLoad.Token.ThrowIfCancellationRequested();
                return PathSorter.MergeAndSort(files, folders);
            }, currentLoad.Token);
            if (currentLoad.IsCancellationRequested || IsDisposed) return;
            fileListBox.BeginUpdate();
            try
            {
                string parentItem = LanguageManager.Get("goToParentDirectoryText") + "...";
                fileListBox.Items.Add(parentItem);
                foreach (string item in allItems)
                {
                    string displayName = Path.GetFileName(item);
                    fileListBox.Items.Add(displayName);
                    _displayPaths[displayName] = item;
                }
            }
            finally
            {
                fileListBox.EndUpdate();
            }
            _currentPath = inputPath;
        }
        catch (OperationCanceledException)
        {
            // A newer navigation request owns the list now.
        }
        catch (Exception exception)
        {
            if (currentLoad.IsCancellationRequested || IsDisposed) return;
            MessageBox.Show(this, MessageTipGenerator.GenerateTip("F00020001", exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //F00020001
        }
    }

    private async Task LoadDrivesAsync()
    {
        CancellationTokenSource currentLoad = ReplaceLoadCancellation();
        fileListBox.Items.Clear();
        fileListBox.Items.Add(LanguageManager.Get("LoadingFiles"));
        _displayPaths.Clear();
        try
        {
            string[] drives = await Task.Run(() => DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => drive.Name)
                .ToArray(), currentLoad.Token);
            if (currentLoad.IsCancellationRequested || IsDisposed) return;
            fileListBox.Items.AddRange(drives);
            foreach (string drive in drives) _displayPaths[drive] = drive;
            _currentPath = null;
        }
        catch (OperationCanceledException)
        {
            // A newer navigation request owns the list now.
        }
        catch (Exception exception)
        {
            if (currentLoad.IsCancellationRequested || IsDisposed) return;
            MessageBox.Show(this, MessageTipGenerator.GenerateTip("F00020003", exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //F00020003
        }
    }

    private CancellationTokenSource ReplaceLoadCancellation()
    {
        CancellationTokenSource currentLoad = new();
        CancellationTokenSource? previousLoad = Interlocked.Exchange(ref _loadCancellation, currentLoad);
        previousLoad?.Cancel();
        previousLoad?.Dispose();
        return currentLoad;
    }

    private void ConfirmButton_Click(object? sender, EventArgs e)
    {
        SelectedFiles = fileListBox.SelectedItems.Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(_displayPaths.ContainsKey)
            .Select(item => _displayPaths[item])
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (SelectedFiles.Count == 0)
        {
            MessageBox.Show(this, LanguageManager.Get("SelectAtLeastOneFile"), LanguageManager.Get("ApplicationTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void FileListBox_DoubleClick(object? sender, EventArgs e)
    {
        try
        {
            if (fileListBox.SelectedItem is not string selectedItem) return;
            if (selectedItem == LanguageManager.Get("goToParentDirectoryText") + "...")
            {
                string? parentPath = _currentPath is null ? null : Directory.GetParent(_currentPath)?.FullName;
                if (parentPath is null) await LoadDrivesAsync(); else await LoadPathAsync(parentPath);
                return;
            }
            if (!_displayPaths.TryGetValue(selectedItem, out string? selectedPath)) return;
            if (Directory.Exists(selectedPath)) await LoadPathAsync(selectedPath);
            else if (File.Exists(selectedPath)) ConfirmButton_Click(sender, e);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, MessageTipGenerator.GenerateTip("F00020004", exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //F00020004
        }
    }

    private async void FreeFilePickerForm_Load(object sender, EventArgs e)
    {
        try
        {
            DarkTheme.Apply(this);
            Text = LanguageManager.Get("FreeFilePickerFormTitle");
#if ENABLE_LIGHT
            _lightOverlay = new TangerineLightOverlay(this)
            {
                TargetFps = 60,
                Radius = 180f,
                LightStrength = 0.02f,
                EdgeStrength = 7.9f,
                EdgeWidth = 3f,
                disableWhenMouseSpeedGetTooFast = 100000
            };
            _normalEdgeStrength = _lightOverlay.EdgeStrength;
            _fileBoxScrollTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _fileBoxScrollTimer.Tick += FileListBoxScrollTimer_Tick;
            fileListBox.ViewChanged += FileListBox_ViewChanged;
            _lightOverlay.Show(this);
#endif
            confirmButton.Text = LanguageManager.Get("Confirm");
            await Task.Yield();
            string initialPath = !string.IsNullOrWhiteSpace(_initialPath) && Directory.Exists(_initialPath)
                ? _initialPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(initialPath)) await LoadPathAsync(initialPath); else await LoadDrivesAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, MessageTipGenerator.GenerateTip("F00020005", exception.Message),
                LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); //F00020005
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CancellationTokenSource? currentLoad = Interlocked.Exchange(ref _loadCancellation, null);
        currentLoad?.Cancel();
        currentLoad?.Dispose();
        base.OnFormClosed(e);
    }

#if ENABLE_LIGHT
    private void FileListBoxScrollTimer_Tick(object? sender, EventArgs e)
    {
        _fileBoxScrollTimer?.Stop();
        if (_lightOverlay is null) return;
        _lightOverlay.EdgeStrength = _normalEdgeStrength;
        _lightOverlay.InvalidateCapture();
    }

    private void FileListBox_ViewChanged(object? sender, EventArgs e)
    {
        if (_lightOverlay is null || _fileBoxScrollTimer is null) return;
        _lightOverlay.EdgeStrength = 0f;
        _fileBoxScrollTimer.Stop();
        _fileBoxScrollTimer.Start();
        _lightOverlay.InvalidateCapture();
    }
#endif
}
