using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

public sealed partial class FilePickerWindow : Window
{
    private string? _currentPath;
    private string? _initialPath;
    private CancellationTokenSource? _loadCancellation;
    private readonly Dictionary<string, string> _displayPaths = new(StringComparer.CurrentCultureIgnoreCase);

    public IReadOnlyList<string> SelectedFiles { get; private set; } = [];

    public FilePickerWindow()
    {
        InitializeComponent();
        FontSize = SystemParameters.WorkArea.Height * 0.018;
        WpfUi.SizeWindow(this, 0.7, 0.7);
        Title = LanguageManager.Get("FilePickerWindowTitle");
        _confirmButton.Content = LanguageManager.Get("Confirm");
        Closed += (_, _) => { _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); };
    }

    public void ShowTipText(string inputTip) => _tipText.Text = inputTip;
    public void InputPath(string inputPath)
    {
        _initialPath = inputPath;
        if (IsLoaded) _ = LoadPathAsync(inputPath);
    }

    private CancellationTokenSource ReplaceLoadCancellation()
    {
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCancellation, current);
        previous?.Cancel();
        previous?.Dispose();
        return current;
    }

    private async Task LoadPathAsync(string path)
    {
        var current = ReplaceLoadCancellation();
        _filesList.Items.Clear();
        _filesList.Items.Add(LanguageManager.Get("LoadingFiles"));
        _displayPaths.Clear();
        try
        {
            string[] items = await Task.Run(() =>
            {
                current.Token.ThrowIfCancellationRequested();
                return PathSorter.MergeAndSort(Directory.GetFiles(path), Directory.GetDirectories(path));
            }, current.Token);
            if (current.IsCancellationRequested || !IsLoaded) return;
            _filesList.Items.Clear();
            _filesList.Items.Add(LanguageManager.Get("goToParentDirectoryText") + "...");
            foreach (string item in items)
            {
                string name = Path.GetFileName(item);
                _filesList.Items.Add(name);
                _displayPaths[name] = item;
            }
            _currentPath = path;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ShowError("F00020001", exception); }
    }

    private async Task LoadDrivesAsync()
    {
        var current = ReplaceLoadCancellation();
        _filesList.Items.Clear();
        _filesList.Items.Add(LanguageManager.Get("LoadingFiles"));
        _displayPaths.Clear();
        try
        {
            string[] drives = await Task.Run(() => DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToArray(), current.Token);
            if (current.IsCancellationRequested || !IsLoaded) return;
            _filesList.Items.Clear();
            foreach (string drive in drives) { _filesList.Items.Add(drive); _displayPaths[drive] = drive; }
            _currentPath = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ShowError("F00020003", exception); }
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedFiles = _filesList.SelectedItems.Cast<object>().Select(item => item.ToString() ?? string.Empty)
            .Where(_displayPaths.ContainsKey).Select(item => _displayPaths[item]).Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (SelectedFiles.Count == 0)
        {
            MessageBox.Show(this, LanguageManager.Get("SelectAtLeastOneFile"), LanguageManager.Get("ApplicationTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private async void FilesList_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            if (_filesList.SelectedItem is not string item) return;
            if (item == LanguageManager.Get("goToParentDirectoryText") + "...")
            {
                string? parent = _currentPath is null ? null : Directory.GetParent(_currentPath)?.FullName;
                if (parent is null) await LoadDrivesAsync(); else await LoadPathAsync(parent);
                return;
            }
            if (!_displayPaths.TryGetValue(item, out string? path)) return;
            if (Directory.Exists(path)) await LoadPathAsync(path);
            else if (File.Exists(path)) ConfirmButton_Click(sender, new RoutedEventArgs());
        }
        catch (Exception exception) { ShowError("F00020004", exception); }
    }

    private async void FilePickerWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)) return;
        try
        {
            await Task.Yield();
            string initial = !string.IsNullOrWhiteSpace(_initialPath) && Directory.Exists(_initialPath)
                ? _initialPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(initial)) await LoadPathAsync(initial); else await LoadDrivesAsync();
        }
        catch (Exception exception) { ShowError("F00020005", exception); }
    }

    private void ShowError(string code, Exception exception) => MessageBox.Show(this,
        MessageTipGenerator.GenerateTip(code, exception.Message), LanguageManager.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
}
