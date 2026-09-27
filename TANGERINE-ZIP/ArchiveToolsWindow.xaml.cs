using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

internal enum ArchiveToolKind { Integrity, Repair, Hash }

internal sealed partial class ArchiveToolsWindow : Window
{
    private readonly ArchiveToolKind _kind;
    private readonly CancellationTokenSource _cancellation = new();
    private string[] _paths = [];

    public ArchiveToolsWindow(ArchiveToolKind kind, string? currentArchive, string? currentPassword = null)
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, 105);
        _kind = kind;
        Title = LanguageManager.Get(kind switch { ArchiveToolKind.Integrity => "IntegrityMenu",
            ArchiveToolKind.Repair => "RepairMenu", _ => "HashMenu" });
        chooseButton.Content = LanguageManager.Get(kind == ArchiveToolKind.Integrity ? "ToolChooseArchives" : "ToolChooseArchive");
        modeLabel.Text = LanguageManager.Get("ToolMode");
        blocksOption.Content = LanguageManager.Get("ToolDataBlocks");
        algorithmLabel.Text = LanguageManager.Get("ToolHashAlgorithm");
        expectedLabel.Text = LanguageManager.Get(kind == ArchiveToolKind.Integrity ? "ToolExpectedHash" : "ToolExpected");
        passwordLabel.Text = LanguageManager.Get("PasswordLabel");
        passwordBox.Password = currentPassword ?? string.Empty;
        passwordBox.Visibility = passwordLabel.Visibility = kind == ArchiveToolKind.Hash ? Visibility.Collapsed : Visibility.Visible;
        runButton.Content = LanguageManager.Get("ToolRun");
        modeCombo.Visibility = modeLabel.Visibility = kind == ArchiveToolKind.Integrity ? Visibility.Visible : Visibility.Collapsed;
        algorithmCombo.Visibility = algorithmLabel.Visibility = kind == ArchiveToolKind.Hash ? Visibility.Visible : Visibility.Collapsed;
        expectedBox.Visibility = expectedLabel.Visibility = kind == ArchiveToolKind.Repair ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(currentArchive) && File.Exists(currentArchive)) SetPaths([currentArchive]);
        Closed += (_, _) => { _cancellation.Cancel(); _cancellation.Dispose(); };
    }

    private void SetPaths(string[] paths)
    {
        _paths = paths;
        pathsBox.Text = string.Join(Environment.NewLine, paths);
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new() { Multiselect = _kind == ArchiveToolKind.Integrity,
            Title = LanguageManager.Get(_kind == ArchiveToolKind.Integrity ? "ToolChooseArchives" : "ToolChooseArchive"),
            Filter = LanguageManager.Get("AllFilesFilter") };
        if (dialog.ShowDialog(this) == true) SetPaths(dialog.FileNames);
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_paths.Length == 0) { ThemedPromptWindow.Inform(this, Title, LanguageManager.Get("NoArchiveSelected")); return; }
        if (_kind == ArchiveToolKind.Repair && ThemedPromptWindow.Ask(this, Title, LanguageManager.Get("RepairWarning"),
                (LanguageManager.Get("PromptContinue"), MessageBoxResult.Yes),
                (LanguageManager.Get("Cancel"), MessageBoxResult.Cancel)) != MessageBoxResult.Yes) return;
        runButton.IsEnabled = chooseButton.IsEnabled = false;
        resultsBox.Clear();
        try
        {
            if (_kind == ArchiveToolKind.Repair)
            {
                resultsBox.Text = await ArchiveDiagnostics.RecoverAsync(_paths[0], passwordBox.Password, _cancellation.Token);
                return;
            }
            if (_kind == ArchiveToolKind.Hash)
            {
                string algorithm = ((ComboBoxItem)algorithmCombo.SelectedItem).Content.ToString()!;
                string actual = await ArchiveDiagnostics.ComputeHashAsync(_paths[0], algorithm, _cancellation.Token);
                bool matches = string.IsNullOrWhiteSpace(expectedBox.Text) ||
                    actual.Equals(expectedBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
                resultsBox.Text = algorithm + ": " + actual + Environment.NewLine +
                    (string.IsNullOrWhiteSpace(expectedBox.Text) ? "" : matches ?
                        LanguageManager.Get("IntegrityHealthy") : string.Format(LanguageManager.Get("IntegrityHashMismatch"), actual));
                return;
            }
            IntegrityMode mode = (IntegrityMode)modeCombo.SelectedIndex;
            string password = passwordBox.Password;
            string expected = expectedBox.Text;
            string[] reports = new string[_paths.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, _paths.Length),
                new ParallelOptions { CancellationToken = _cancellation.Token, MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4) },
                async (index, token) =>
                {
                    try { reports[index] = _paths[index] + Environment.NewLine +
                        await ArchiveDiagnostics.TestAsync(_paths[index], mode, expected, password, token); }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { reports[index] = _paths[index] + Environment.NewLine + error.Message; }
                });
            resultsBox.Text = string.Join(Environment.NewLine + Environment.NewLine, reports);
        }
        catch (OperationCanceledException) { resultsBox.Text = LanguageManager.Get("OperationCancelled"); }
        catch (Exception error) { resultsBox.Text = error.Message; }
        finally { runButton.IsEnabled = chooseButton.IsEnabled = true; }
    }
}
