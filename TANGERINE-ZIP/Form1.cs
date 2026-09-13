using SharpCompress.Archives;
using SharpCompress.Common;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using TANGERINE_ZIP.Tools;
using TANGERINE_ZIP.Tools.LightTool;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
//first 5 letters of stage code:F0001
namespace TANGERINE_ZIP
{
    public partial class Form1 : Form
    {

        public Form1()
        {
            InitializeComponent();
        }
        private TangerineLightOverlay? _lightOverlay;
        private System.Windows.Forms.Timer? _fileBoxScrollTimer;
        private float _normalEdgeStrength;
        string zippath = string.Empty;//file path of the zip file
        bool isDoingJob = false;
        private List<string> _archiveEntries = new();
        private string _archiveCurrentDirectory = "";
        private void Form1_Load(object sender, EventArgs e)
        {
            #region set light effect
            _lightOverlay = new TangerineLightOverlay(this);
            _lightOverlay.TargetFps = 60;
            _lightOverlay.Radius = 120f;
            _lightOverlay.LightStrength = 0.02f;
            _lightOverlay.EdgeStrength = 1.9f;
            _lightOverlay.EdgeWidth = 3f;
            _lightOverlay.disableWhenMouseSpeedGetTooFast = 100000;
            _normalEdgeStrength = _lightOverlay.EdgeStrength;
            _fileBoxScrollTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 120
                };
            _fileBoxScrollTimer.Tick += FileBoxScrollTimer_Tick;
            fileBox.ViewChanged += FileBox_ViewChanged;
            _lightOverlay.Show(this);
            #endregion
            #region set text
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
            #endregion
            #region set visibility
            uninstallFileToolStripMenuItem.Visible = false;
            extractToolStripMenuItem.Visible = false;
            compressToolStripMenuItem.Visible = true;
            #endregion
            fileBox.SetItemColorProvider(
    index =>
    {
        string displayName =
            fileBox.Items[index]?.ToString() ?? "";

        // 空白项
        if (string.IsNullOrEmpty(displayName))
        {
            return Color.White;
        }

        // 返回上一级
        if (displayName ==
            LanguageManager.Get(
                "goToParentDirectoryText"))
        {
            return Color.White;
        }

        string path =
            FindEntryPath(displayName);

        // 文件夹：浅黄色
        if (path.EndsWith("/"))
        {
            return Color.Yellow;
        }

        // 文件：白色
        return Color.White;
    });
        }
        private void FileBox_ViewChanged(
            object? sender,
            EventArgs e)
        {
            if (_lightOverlay == null ||
                _fileBoxScrollTimer == null)
            {
                return;
            }

            _lightOverlay.EdgeStrength =
                0f;

            _fileBoxScrollTimer.Stop();
            _fileBoxScrollTimer.Start();
            _lightOverlay?.InvalidateCapture();
        }

        private void FileBoxScrollTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _fileBoxScrollTimer?.Stop();

            if (_lightOverlay == null)
            {
                return;
            }

            _lightOverlay.EdgeStrength =
                _normalEdgeStrength;
            
            _lightOverlay.InvalidateCapture();
        }
        private void refreshCurrentDirectory()
        {
            if (!string.IsNullOrEmpty(zippath))
            {
                if (string.IsNullOrEmpty(_archiveCurrentDirectory))
                {
                    statusLabel.Text = LanguageManager.Get("YouAreCurrentlyAt") + LanguageManager.Get("Root");
                }
                else
                {
 statusLabel.Text=LanguageManager.Get("YouAreCurrentlyAt")+" "+ _archiveCurrentDirectory;
                }               
            }
            else
            {
                //F00010100
                statusLabel.Text = "F00010100";
            }
        }
        private async void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            isDoingJob = true;
            if (mainOpenFileDialog.ShowDialog() == DialogResult.OK)
            {
                zippath = mainOpenFileDialog.FileName;
                if (FileDetector.IsCompressedFile(zippath))
                {
                    uninstallFileToolStripMenuItem.Visible = true;
                    statusLabel.Text = LanguageManager.Get("OpeningFile");
                    statusProgressBar.Value = 50;
                    await LoadArchiveAsync(zippath);
                    extractToolStripMenuItem.Visible = true;
                    compressToolStripMenuItem.Visible = true;
                    switch (FileDetector.DetectFileType(zippath))
                    {
                        case FileDetector.FileType.Unknown:
                            break;
                        case FileDetector.FileType.Zip:
                            mainTab.Text = "ZIP" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.Rar:
                            mainTab.Text = "RAR" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.SevenZip:
                            mainTab.Text = "7Z" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.Tar:
                            mainTab.Text = "TAR" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.GZip:
                            mainTab.Text = "GZ" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.BZip2:
                            mainTab.Text = "BZ2" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.Xz:
                            mainTab.Text = "XZ" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.Lz4:
                            mainTab.Text = "LZ4" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.Zstd:
                            mainTab.Text = "ZSTD" + LanguageManager.Get("CompressFile");
                            break;
                        case FileDetector.FileType.Iso:
                            mainTab.Text = "ISO" + LanguageManager.Get("ImageFile");
                            break;
                        case FileDetector.FileType.Wim:
                            mainTab.Text = "WIM" + LanguageManager.Get("ImageFile");
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    MessageBox.Show(LanguageManager.Get("NotACompressedFile"));
                    uninstallFile();
                    return;
                }
            }
            else
            {
                return;
            }
            isDoingJob = false;
        }
        private async Task LoadArchiveAsync(string zippath)
        {
            isDoingJob = true;
            try
            {
                List<string> items =
                    await Task.Run(() =>
                    {
                        List<string> result = new();

                        using var archive = ArchiveFactory.OpenArchive(zippath);

                        foreach (var entry in archive.Entries)
                        {
                            string entryKey = entry.Key.Replace('\\', '/');

                            if (entry.IsDirectory)
                            {
                                if (!entryKey.EndsWith("/"))
                                {
                                    entryKey += "/";
                                }
                            }

                            result.Add(entryKey);
                        }

                        return result;
                    });

                _archiveEntries = items;
                _archiveCurrentDirectory = "";
                fileBox.Items.Clear();

                fileBox.Items.AddRange(
                    items.ConvertAll(
                        item => (object)item)
                    .ToArray());
                RefreshFileBox();

                _fileBoxScrollTimer?.Stop();

                if (_lightOverlay != null)
                {
                    _lightOverlay.EdgeStrength =
                        _normalEdgeStrength;

                    _lightOverlay.InvalidateCapture();
                }

                statusLabel.Text =
                    LanguageManager.Get("readytext");

                statusProgressBar.Value =
                    100;
            }
            catch (Exception ex)
            {
                MessageBox.Show("TZIP");
                MessageBox.Show(
                    MessageTipGenerator.GenerateTip(
                        "F00010052",
                        ex.Message)); //F00010052
            }
            finally
            {
                isDoingJob = false;
            }
            refreshCurrentDirectory();
        }
        private void RefreshFileBox()
        {
            List<object> displayItems = new();

            if (string.IsNullOrEmpty(_archiveCurrentDirectory))
            {
                // 根目录第一项为空
                displayItems.Add("");
            }
            else
            {
                // 非根目录第一项为返回上一级
                displayItems.Add(
                    LanguageManager.Get(
                        "goToParentDirectoryText"));
            }

            HashSet<string> addedDirectories = new();

            foreach (string entry in _archiveEntries)
            {
                string normalizedEntry =
                    entry.Replace('\\', '/');

                string relativePath;

                if (string.IsNullOrEmpty(_archiveCurrentDirectory))
                {
                    relativePath = normalizedEntry;
                }
                else
                {
                    if (!normalizedEntry.StartsWith(
                        _archiveCurrentDirectory,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    relativePath =
                        normalizedEntry.Substring(
                            _archiveCurrentDirectory.Length);
                }

                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                int slashIndex =
                    relativePath.IndexOf('/');

                if (slashIndex == -1)
                {
                    /*
                     * 文件
                     *
                     * 实际：
                     *     folder/test.txt
                     *
                     * 显示：
                     *     test.txt
                     */
                    displayItems.Add(
                        relativePath);
                }
                else
                {
                    /*
                     * 文件夹
                     *
                     * relativePath：
                     *     folder2/test.txt
                     *
                     * directoryName：
                     *     folder2/
                     */
                    string directoryName =
                        relativePath.Substring(
                            0,
                            slashIndex + 1);

                    string fullDirectoryPath =
                        _archiveCurrentDirectory +
                        directoryName;

                    if (addedDirectories.Add(
                        fullDirectoryPath))
                    {
                        /*
                         * 只显示文件夹名称，不显示路径。
                         *
                         * folder2/
                         *     ↓
                         * folder2
                         */
                        displayItems.Add(
                            directoryName.TrimEnd('/'));
                    }
                }
            }

            fileBox.SuspendLayout();
            fileBox.BeginUpdate();

            try
            {
                fileBox.Items.Clear();

                foreach (object item in displayItems)
                {
                    fileBox.Items.Add(item);
                }
            }
            finally
            {
                fileBox.EndUpdate();
                fileBox.ResumeLayout(true);
            }

            fileBox.Invalidate(true);
            fileBox.Update();

            if (_lightOverlay != null)
            {
                _lightOverlay.EdgeStrength =
                    _normalEdgeStrength;

                _lightOverlay.InvalidateCapture();
            }
        }
        private async Task ExtractSelectedEntriesDIRECTLYAsync(
    string zippath,
    List<string> selectedEntries,
    string destinationPath)
        {
            var fileType = FileDetector.DetectFileType(zippath);
            HashSet<string> selectedSet = new(selectedEntries);

            try
            {
                isDoingJob = true;

                await Task.Run(() =>
                {
                    ZipArchive? archiveZIP = null;
                    IArchive? archiveRAR = null;

                    bool allOverWrite = false;
                    bool allSkip = false;
                    bool isThisFileExtracted = false;
                    bool stopJob = false;

                    DateTime lastProgressUpdate = DateTime.MinValue;
                    int lastProgress = -1;

                    int totalSelectedEntries = selectedEntries.Count;
                    int currentSelectedEntry = 0;

                    void UpdateProgress()
                    {
                        if (totalSelectedEntries <= 0)
                        {
                            return;
                        }

                        int progress =
                            currentSelectedEntry * 100 / totalSelectedEntries;

                        if (progress > 100)
                        {
                            progress = 100;
                        }

                        if (progress == lastProgress)
                        {
                            return;
                        }

                        if ((DateTime.Now - lastProgressUpdate).TotalSeconds < 1)
                        {
                            return;
                        }

                        lastProgressUpdate = DateTime.Now;
                        lastProgress = progress;

                        BeginInvoke(() =>
                        {
                            if (!IsDisposed)
                            {
                                statusProgressBar.Value = progress;
                            }
                        });
                    }

                    switch (fileType)
                    {
                        case FileDetector.FileType.Unknown:
                            MessageBox.Show("TZIP");
                            MessageBox.Show(
                                MessageTipGenerator.GenerateTip(
                                    "F00010030",
                                    LanguageManager.Get("UnknownFileType"))); //F00010030
                            return;

                        case FileDetector.FileType.Zip:
                            try
                            {
                                archiveZIP = ZipFile.OpenRead(zippath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("TZIP");
                                MessageBox.Show(
                                    MessageTipGenerator.GenerateTip(
                                        "F00010031",
                                        ex.Message)); //F00010031
                                return;
                            }
                            break;

                        case FileDetector.FileType.Rar:
                            try
                            {
                                archiveRAR = ArchiveFactory.OpenArchive(zippath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("TZIP");
                                MessageBox.Show(
                                    MessageTipGenerator.GenerateTip(
                                        "F00010032",
                                        ex.Message)); //F00010032
                                return;
                            }
                            break;

                        case FileDetector.FileType.SevenZip:
                            try
                            {
                                archiveRAR = ArchiveFactory.OpenArchive(zippath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("TZIP");
                                MessageBox.Show(
                                    MessageTipGenerator.GenerateTip(
                                        "F00010033",
                                        ex.Message)); //F00010033
                                return;
                            }
                            break;

                        case FileDetector.FileType.Tar:
                            break;

                        case FileDetector.FileType.GZip:
                            break;

                        case FileDetector.FileType.BZip2:
                            break;

                        case FileDetector.FileType.Xz:
                            break;

                        case FileDetector.FileType.Lz4:
                            break;

                        case FileDetector.FileType.Zstd:
                            break;

                        case FileDetector.FileType.Iso:
                            break;

                        case FileDetector.FileType.Wim:
                            break;

                        default:
                            return;
                    }

                    switch (fileType)
                    {
                        case FileDetector.FileType.Unknown:
                            break;

                        case FileDetector.FileType.Zip:
                            {
                                foreach (var entry in archiveZIP!.Entries)
                                {
                                    if (string.IsNullOrEmpty(entry.Name))
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        if (!selectedSet.Contains(entry.Name))
                                        {
                                            continue;
                                        }

                                        Invoke(() =>
                                        {
                                            statusLabel.Text =
                                                LanguageManager.Get("ExtractingText") +
                                                $" {entry.FullName}";
                                        });

                                        if (string.IsNullOrEmpty(entry.Name))
                                        {
                                            try
                                            {
                                                Directory.CreateDirectory(
                                                    Path.Combine(
                                                        destinationPath,
                                                        entry.FullName));
                                            }
                                            catch (Exception ex)
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010034",
                                                        ex.Message)); //F00010034
                                                return;
                                            }
                                        }

                                        if (File.Exists(
                                            Path.Combine(
                                                destinationPath,
                                                entry.FullName)))
                                        {
                                            if (allOverWrite)
                                            {
                                                goto cExtract;
                                            }
                                            else
                                            {
                                                if ((!allOverWrite) && (!allSkip))
                                                {
                                                reShowOWF:

                                                    OverWriteOrNotForm owonf =
                                                        new OverWriteOrNotForm();

                                                    owonf.ShowDialog();

                                                    owonf.SyncBool(
                                                        ref allOverWrite);

                                                    owonf.SyncBool2(
                                                        ref allSkip);

                                                    int OWFresult = -1;

                                                    owonf.GetResult(
                                                        ref OWFresult);

                                                    if ((!allOverWrite) && (!allSkip))
                                                    {
                                                        switch (OWFresult)
                                                        {
                                                            default:
                                                                break;

                                                            case -100:
                                                                goto reShowOWF;

                                                            case 1:
                                                                goto cExtract;

                                                            case 2:
                                                                continue;

                                                            case 1000:
                                                                stopJob = true;
                                                                break;
                                                        }
                                                    }
                                                }

                                                if (allOverWrite)
                                                {
                                                    goto cExtract;
                                                }

                                                if (allSkip)
                                                {
                                                    currentSelectedEntry++;
                                                    UpdateProgress();
                                                    continue;
                                                }

                                                if (stopJob)
                                                {
                                                    return;
                                                }
                                            }
                                        }

                                    cExtract:

                                        string targetPath =
                                            Path.Combine(
                                                destinationPath,
                                                entry.FullName);

                                        string? parent =
                                            Path.GetDirectoryName(targetPath);

                                        if (!Directory.Exists(parent))
                                        {
                                            if (!string.IsNullOrEmpty(parent))
                                            {
                                                try
                                                {
                                                    Directory.CreateDirectory(parent);
                                                }
                                                catch (Exception ex)
                                                {
                                                    MessageBox.Show("TZIP");
                                                    MessageBox.Show(
                                                        MessageTipGenerator.GenerateTip(
                                                            "F00010035",
                                                            ex.Message)); //F00010035
                                                    return;
                                                }
                                            }
                                            else
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010036",
                                                        "Parent directory information is empty")); //F00010036
                                                return;
                                            }
                                        }

                                        try
                                        {
                                            File.Delete(targetPath);

                                            entry.ExtractToFile(
                                                targetPath,
                                                true);
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show("TZIP");
                                            MessageBox.Show(
                                                MessageTipGenerator.GenerateTip(
                                                    "F00010037",
                                                    ex.Message)); //F00010037
                                            return;
                                        }

                                        currentSelectedEntry++;
                                        UpdateProgress();
                                    }
                                }

                                break;
                            }

                        case FileDetector.FileType.Rar:
                            {
                                foreach (var entry in archiveRAR!.Entries)
                                {
                                    if (entry.IsDirectory)
                                    {
                                        continue;
                                    }

                                    if (!selectedSet.Contains(entry.Key))
                                    {
                                        continue;
                                    }

                                    Invoke(() =>
                                    {
                                        statusLabel.Text =
                                            LanguageManager.Get("ExtractingText") +
                                            $" {entry.Key}";
                                    });

                                    string filePath =
                                        Path.Combine(
                                            destinationPath,
                                            entry.Key);

                                    if (File.Exists(filePath))
                                    {
                                        if (allOverWrite)
                                        {
                                            goto cExtract;
                                        }
                                        else
                                        {
                                            if ((!allOverWrite) && (!allSkip))
                                            {
                                            reShowOWF:

                                                OverWriteOrNotForm owonf =
                                                    new OverWriteOrNotForm();

                                                owonf.ShowDialog();

                                                owonf.SyncBool(
                                                    ref allOverWrite);

                                                owonf.SyncBool2(
                                                    ref allSkip);

                                                int OWFresult = -1;

                                                owonf.GetResult(
                                                    ref OWFresult);

                                                if ((!allOverWrite) && (!allSkip))
                                                {
                                                    switch (OWFresult)
                                                    {
                                                        default:
                                                            break;

                                                        case -100:
                                                            goto reShowOWF;

                                                        case 1:
                                                            goto cExtract;

                                                        case 2:
                                                            currentSelectedEntry++;
                                                            UpdateProgress();
                                                            continue;

                                                        case 1000:
                                                            stopJob = true;
                                                            break;
                                                    }
                                                }
                                            }

                                            if (allOverWrite)
                                            {
                                                goto cExtract;
                                            }

                                            if (allSkip)
                                            {
                                                currentSelectedEntry++;
                                                UpdateProgress();
                                                continue;
                                            }

                                            if (stopJob)
                                            {
                                                return;
                                            }
                                        }
                                    }

                                cExtract:

                                    string? parent =
                                        Path.GetDirectoryName(filePath);

                                    if (!Directory.Exists(parent))
                                    {
                                        if (!string.IsNullOrEmpty(parent))
                                        {
                                            try
                                            {
                                                Directory.CreateDirectory(parent);
                                            }
                                            catch (Exception ex)
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010038",
                                                        ex.Message)); //F00010038
                                                return;
                                            }
                                        }
                                        else
                                        {
                                            MessageBox.Show("TZIP");
                                            MessageBox.Show(
                                                MessageTipGenerator.GenerateTip(
                                                    "F00010039",
                                                    "Parent directory information is empty")); //F00010039
                                            return;
                                        }
                                    }

                                    try
                                    {
                                        entry.WriteToDirectory(
                                            destinationPath,
                                            new ExtractionOptions
                                            {
                                                ExtractFullPath = true,
                                                Overwrite = true
                                            });
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show("TZIP");
                                        MessageBox.Show(
                                            MessageTipGenerator.GenerateTip(
                                                "F00010040",
                                                ex.Message)); //F00010040
                                        return;
                                    }

                                    currentSelectedEntry++;
                                    UpdateProgress();
                                }

                                break;
                            }

                        case FileDetector.FileType.SevenZip:
                            goto case FileDetector.FileType.Rar;

                        case FileDetector.FileType.Tar:
                            break;

                        case FileDetector.FileType.GZip:
                            break;

                        case FileDetector.FileType.BZip2:
                            break;

                        case FileDetector.FileType.Xz:
                            break;

                        case FileDetector.FileType.Lz4:
                            break;

                        case FileDetector.FileType.Zstd:
                            break;

                        case FileDetector.FileType.Iso:
                            break;

                        case FileDetector.FileType.Wim:
                            break;

                        default:
                            break;
                    }
                });

                Invoke(() =>
                {
                    statusLabel.Text =
                        LanguageManager.Get("ExtractingCompleted");

                    statusProgressBar.Value = 100;
                });

                isDoingJob = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("TZIP");
                MessageBox.Show(
                    MessageTipGenerator.GenerateTip(
                        "F00010041",
                        ex.Message)); //F00010041
            }
            finally
            {
                isDoingJob = false;
            }
        }
        private async Task ExtractAllEntriesDIRECTLYAsync(string zippath, string destinationPath)
        {
            var fileType = FileDetector.DetectFileType(zippath);

            try
            {
                isDoingJob = true;

                await Task.Run(() =>
                {
                    ZipArchive? archiveZIP = null;
                    IArchive? archiveRAR = null;

                    bool allOverWrite = false;
                    bool allSkip = false;
                    bool isThisFileExtracted = false;
                    bool stopJob = false;

                    DateTime lastProgressUpdate = DateTime.MinValue;
                    int lastProgress = -1;

                    void UpdateProgress(int current, int total)
                    {
                        if (total <= 0)
                        {
                            return;
                        }

                        int progress = current * 100 / total;

                        if (progress > 100)
                        {
                            progress = 100;
                        }

                        if (progress == lastProgress)
                        {
                            return;
                        }

                        if ((DateTime.Now - lastProgressUpdate).TotalSeconds < 1)
                        {
                            return;
                        }

                        lastProgressUpdate = DateTime.Now;
                        lastProgress = progress;

                        BeginInvoke(() =>
                        {
                            if (!IsDisposed)
                            {
                                statusProgressBar.Value = progress;
                            }
                        });
                    }

                    switch (fileType)
                    {
                        case FileDetector.FileType.Unknown:
                            return;

                        case FileDetector.FileType.Zip:
                            try
                            {
                                archiveZIP = ZipFile.OpenRead(zippath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("TZIP");
                                MessageBox.Show(
                                    MessageTipGenerator.GenerateTip(
                                        "F00010020",
                                        ex.Message)); //F00010020
                                return;
                            }
                            break;

                        case FileDetector.FileType.Rar:
                            try
                            {
                                archiveRAR = ArchiveFactory.OpenArchive(zippath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("TZIP");
                                MessageBox.Show(
                                    MessageTipGenerator.GenerateTip(
                                        "F00010021",
                                        ex.Message)); //F00010021
                                return;
                            }
                            break;

                        case FileDetector.FileType.SevenZip:
                            goto case FileDetector.FileType.Rar;

                        case FileDetector.FileType.Tar:
                            break;

                        case FileDetector.FileType.GZip:
                            break;

                        case FileDetector.FileType.BZip2:
                            break;

                        case FileDetector.FileType.Xz:
                            break;

                        case FileDetector.FileType.Lz4:
                            break;

                        case FileDetector.FileType.Zstd:
                            break;

                        case FileDetector.FileType.Iso:
                            break;

                        case FileDetector.FileType.Wim:
                            break;

                        default:
                            return;
                    }

                    switch (fileType)
                    {
                        case FileDetector.FileType.Unknown:
                            break;

                        case FileDetector.FileType.Zip:
                            {
                                int totalEntries = archiveZIP!.Entries.Count;
                                int currentEntry = 0;

                                foreach (var entry in archiveZIP.Entries)
                                {
                                    currentEntry++;

                                    if (string.IsNullOrEmpty(entry.Name))
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        BeginInvoke(() =>
                                        {
                                            if (!IsDisposed)
                                            {
                                                statusLabel.Text =
                                                    LanguageManager.Get("ExtractingText") +
                                                    $" {entry.FullName}";
                                            }
                                        });

                                        if (string.IsNullOrEmpty(entry.Name))
                                        {
                                            try
                                            {
                                                Directory.CreateDirectory(
                                                    Path.Combine(
                                                        destinationPath,
                                                        entry.FullName));
                                            }
                                            catch (Exception ex)
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010022",
                                                        ex.Message)); //F00010022
                                                return;
                                            }
                                        }

                                        if (File.Exists(
                                            Path.Combine(
                                                destinationPath,
                                                entry.FullName)))
                                        {
                                            if (allOverWrite)
                                            {
                                                goto cExtract;
                                            }
                                            else
                                            {
                                                if ((!allOverWrite) && (!allSkip))
                                                {
                                                reShowOWF:

                                                    OverWriteOrNotForm owonf =
                                                        new OverWriteOrNotForm();

                                                    owonf.ShowDialog();

                                                    owonf.SyncBool(
                                                        ref allOverWrite);

                                                    owonf.SyncBool2(
                                                        ref allSkip);

                                                    int OWFresult = -1;

                                                    owonf.GetResult(
                                                        ref OWFresult);

                                                    if ((!allOverWrite) &&
                                                        (!allSkip))
                                                    {
                                                        switch (OWFresult)
                                                        {
                                                            default:
                                                                break;

                                                            case -100:
                                                                goto reShowOWF;

                                                            case 1:
                                                                goto cExtract;

                                                            case 2:
                                                                continue;

                                                            case 1000:
                                                                stopJob = true;
                                                                break;
                                                        }
                                                    }
                                                }

                                                if (allOverWrite)
                                                {
                                                    goto cExtract;
                                                }

                                                if (allSkip)
                                                {
                                                    UpdateProgress(
                                                        currentEntry,
                                                        totalEntries);

                                                    continue;
                                                }

                                                if (stopJob)
                                                {
                                                    return;
                                                }
                                            }
                                        }

                                    cExtract:

                                        string? parent =
                                            Path.GetDirectoryName(
                                                Path.Combine(
                                                    destinationPath,
                                                    entry.FullName));

                                        if (!Directory.Exists(parent))
                                        {
                                            if (!string.IsNullOrEmpty(parent))
                                            {
                                                try
                                                {
                                                    Directory.CreateDirectory(parent);
                                                }
                                                catch (Exception ex)
                                                {
                                                    MessageBox.Show("TZIP");
                                                    MessageBox.Show(
                                                        MessageTipGenerator.GenerateTip(
                                                            "F00010023",
                                                            ex.Message)); //F00010023
                                                    return;
                                                }
                                            }
                                            else
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010024",
                                                        "we cannot create the parent directory,because the directory information we got is empty")); //F00010024
                                                return;
                                            }
                                        }

                                        try
                                        {
                                            entry.ExtractToFile(
                                                Path.Combine(
                                                    destinationPath,
                                                    entry.FullName),
                                                true);
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show("TZIP");
                                            MessageBox.Show(
                                                MessageTipGenerator.GenerateTip(
                                                    "F00010025",
                                                    ex.Message)); //F00010025
                                            return;
                                        }

                                        UpdateProgress(
                                            currentEntry,
                                            totalEntries);
                                    }
                                }

                                break;
                            }

                        case FileDetector.FileType.Rar:
                            {
                                int totalEntries = archiveRAR!.Entries.Count();
                                int currentEntry = 0;

                                foreach (var entry in archiveRAR.Entries)
                                {
                                    currentEntry++;

                                    // Skip directories
                                    if (entry.IsDirectory)
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        BeginInvoke(() =>
                                        {
                                            if (!IsDisposed)
                                            {
                                                statusLabel.Text =
                                                    LanguageManager.Get("ExtractingText") +
                                                    $" {entry.Key}";
                                            }
                                        });

                                        // Get the destination path of the current file
                                        string filePath =
                                            Path.Combine(
                                                destinationPath,
                                                entry.Key);

                                        // Check whether the target file already exists
                                        if (File.Exists(filePath))
                                        {
                                            if (allOverWrite)
                                            {
                                                goto cExtract;
                                            }
                                            else
                                            {
                                                if ((!allOverWrite) && (!allSkip))
                                                {
                                                reShowOWF:

                                                    OverWriteOrNotForm owonf =
                                                        new OverWriteOrNotForm();

                                                    owonf.ShowDialog();

                                                    owonf.SyncBool(
                                                        ref allOverWrite);

                                                    owonf.SyncBool2(
                                                        ref allSkip);

                                                    int OWFresult = -1;

                                                    owonf.GetResult(
                                                        ref OWFresult);

                                                    if ((!allOverWrite) &&
                                                        (!allSkip))
                                                    {
                                                        switch (OWFresult)
                                                        {
                                                            default:
                                                                break;

                                                            // Show the overwrite dialog again
                                                            case -100:
                                                                goto reShowOWF;

                                                            // Overwrite the current file
                                                            case 1:
                                                                goto cExtract;

                                                            // Skip the current file
                                                            case 2:
                                                                continue;

                                                            // Stop the entire extraction task
                                                            case 1000:
                                                                stopJob = true;
                                                                break;
                                                        }
                                                    }

                                                    // Overwrite all remaining files
                                                    if (allOverWrite)
                                                    {
                                                        goto cExtract;
                                                    }

                                                    // Skip all remaining files
                                                    if (allSkip)
                                                    {
                                                        UpdateProgress(
                                                            currentEntry,
                                                            totalEntries);

                                                        continue;
                                                    }

                                                    // Stop the entire extraction task
                                                    if (stopJob)
                                                    {
                                                        return;
                                                    }
                                                }
                                            }
                                        }

                                    cExtract:

                                        // Get the parent directory of the destination file
                                        string? parent =
                                            Path.GetDirectoryName(filePath);

                                        // Create the parent directory if it does not exist
                                        if (!Directory.Exists(parent))
                                        {
                                            if (!string.IsNullOrEmpty(parent))
                                            {
                                                try
                                                {
                                                    Directory.CreateDirectory(parent);
                                                }
                                                catch (Exception ex)
                                                {
                                                    MessageBox.Show("TZIP");
                                                    MessageBox.Show(
                                                        MessageTipGenerator.GenerateTip(
                                                            "F00010026",
                                                            ex.Message)); //F00010026
                                                    return;
                                                }
                                            }
                                            else
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010027",
                                                        "we cannot create the parent directory,because the directory information we got is empty")); //F00010027
                                                return;
                                            }
                                        }

                                        // Extract the file while preserving its full directory structure
                                        try
                                        {
                                            entry.WriteToDirectory(
                                                destinationPath,
                                                new ExtractionOptions
                                                {
                                                    ExtractFullPath = true,
                                                    Overwrite = true
                                                });
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show("TZIP");
                                            MessageBox.Show(
                                                MessageTipGenerator.GenerateTip(
                                                    "F00010028",
                                                    ex.Message)); //F00010028
                                            return;
                                        }

                                        UpdateProgress(
                                            currentEntry,
                                            totalEntries);
                                    }
                                }

                                break;
                            }

                        case FileDetector.FileType.SevenZip:
                            goto case FileDetector.FileType.Rar;

                        case FileDetector.FileType.Tar:
                            break;

                        case FileDetector.FileType.GZip:
                            break;

                        case FileDetector.FileType.BZip2:
                            break;

                        case FileDetector.FileType.Xz:
                            break;

                        case FileDetector.FileType.Lz4:
                            break;

                        case FileDetector.FileType.Zstd:
                            break;

                        case FileDetector.FileType.Iso:
                            break;

                        case FileDetector.FileType.Wim:
                            break;

                        default:
                            break;
                    }
                });

                Invoke(() =>
                {
                    statusProgressBar.Value = 100;
                    statusLabel.Text = LanguageManager.Get("readytext");
                });

                isDoingJob = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("TZIP");
                MessageBox.Show(
                    MessageTipGenerator.GenerateTip(
                        "F00010029",
                        ex.Message)); //F00010029
            }
            finally
            {
                isDoingJob = false;
            }
        }
        private async Task ExtractSelectedEntriesAsync(string zippath, List<string> selectedEntries, string destinationPath)
        {
            var fileType = FileDetector.DetectFileType(zippath);
            HashSet<string> selectedSet = new(selectedEntries);
            try
            {
                isDoingJob = true;
                destinationPath = Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(zippath));
                await Task.Run(() =>
                {
                    using var archiveZIP = ZipFile.OpenRead(zippath);
                    using var archiveRAR = ArchiveFactory.OpenArchive(zippath);
                    bool allOverWrite = false;
                    bool allSkip = false;
                    bool isThisFileExtracted = false;
                    bool stopJob = false;
                    switch (fileType)
                    {
                        case FileDetector.FileType.Unknown:
                            MessageBox.Show(LanguageManager.Get("UnknownFileType"));
                            break;

                        case FileDetector.FileType.Zip:
                            foreach (var entry in archiveZIP.Entries)
                            {
                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    continue;
                                }
                                else
                                {
                                    if (!selectedSet.Contains(entry.Name))
                                    {
                                        continue;
                                    }
                                    if (string.IsNullOrEmpty(entry.Name))
                                    {
                                        Directory.CreateDirectory(Path.Combine(destinationPath, entry.FullName));
                                    }
                                    if (File.Exists(Path.Combine(destinationPath, entry.FullName)))
                                    {
                                        if (allOverWrite)
                                        {
                                            goto cExtract;
                                        }
                                        else
                                        {
                                            if ((!allOverWrite) && (!allSkip))
                                            {
                                            reShowOWF:
                                                OverWriteOrNotForm owonf = new OverWriteOrNotForm();
                                                owonf.ShowDialog();
                                                owonf.SyncBool(ref allOverWrite);
                                                owonf.SyncBool2(ref allSkip);
                                                int OWFresult = -1;
                                                owonf.GetResult(ref OWFresult);
                                                if ((!allOverWrite) && (!allSkip))
                                                {
                                                    switch (OWFresult)
                                                    {
                                                        default:
                                                            break;
                                                        case -100:
                                                            goto reShowOWF;
                                                        case 1:
                                                            goto cExtract;
                                                        case 2:
                                                            continue;
                                                        case 1000:
                                                            stopJob = true;
                                                            break;
                                                    }
                                                }
                                            }
                                            if (allOverWrite)
                                            {
                                                goto cExtract;
                                            }
                                            if (allSkip)
                                            {
                                                continue;
                                            }
                                            if (stopJob)
                                            {
                                                isDoingJob = false;
                                                return;
                                            }
                                        }
                                    }
                                cExtract:
                                    string? parent = Path.GetDirectoryName(Path.Combine(destinationPath, entry.FullName));
                                    if (!Directory.Exists(parent))
                                    {
                                        if (!string.IsNullOrEmpty(parent))
                                        {
                                            Directory.CreateDirectory(parent);
                                        }
                                        else
                                        {
                                            MessageBox.Show(LanguageManager.Get("ErrorTitle") + "\n an error occurred\nwe cannot create the parent directory,because the directory information we got is empty");
                                        }
                                    }
                                    File.Delete(Path.Combine(destinationPath, entry.FullName));
                                    entry.ExtractToFile(Path.Combine(destinationPath, entry.FullName), true);
                                }
                            }
                            break;
                        case FileDetector.FileType.Rar:
                            foreach (var entry in archiveRAR.Entries)
                            {
                                if (entry.IsDirectory)
                                {
                                    continue;
                                }
                                if (selectedSet.Contains(entry.Key))
                                {
                                    Invoke(() =>
                                    {
                                        statusLabel.Text = LanguageManager.Get("ExtractingText") + $" {entry.Key}";
                                    });
                                    entry.WriteToDirectory(destinationPath, new ExtractionOptions
                                    {
                                        ExtractFullPath = true,
                                        Overwrite = true
                                    });
                                }
                            }
                            break;
                        case FileDetector.FileType.SevenZip:
                            goto case FileDetector.FileType.Rar;
                            break;
                        case FileDetector.FileType.Tar:
                            break;
                        case FileDetector.FileType.GZip:
                            break;
                        case FileDetector.FileType.BZip2:
                            break;
                        case FileDetector.FileType.Xz:
                            break;
                        case FileDetector.FileType.Lz4:
                            break;
                        case FileDetector.FileType.Zstd:
                            break;
                        case FileDetector.FileType.Iso:
                            break;
                        case FileDetector.FileType.Wim:
                            break;
                        default:
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, LanguageManager.Get("ExtractFailedText"));
            }
            finally
            {
                isDoingJob = false;
            }
            Invoke(() =>
            {
                statusLabel.Text = LanguageManager.Get("ExtractingCompleted");
                statusProgressBar.Value = 100;
            });
        }
        private async Task ExtractAllEntriesAsync(string zippath, string destinationPath)
        {
            var fileType = FileDetector.DetectFileType(zippath);

            try
            {
                isDoingJob = true;

                destinationPath = Path.Combine(
                    destinationPath,
                    Path.GetFileNameWithoutExtension(zippath));

                await Task.Run(() =>
                {
                    ZipArchive? archiveZIP = null;
                    IArchive? archiveRAR = null;

                    bool allOverWrite = false;
                    bool allSkip = false;
                    bool isThisFileExtracted = false;
                    bool stopJob = false;

                    DateTime lastProgressUpdate = DateTime.MinValue;
                    int lastProgress = -1;

                    void UpdateProgress(int current, int total)
                    {
                        if (total <= 0)
                        {
                            return;
                        }

                        int progress = current * 100 / total;

                        if (progress > 100)
                        {
                            progress = 100;
                        }

                        if (progress == lastProgress)
                        {
                            return;
                        }

                        if ((DateTime.Now - lastProgressUpdate).TotalSeconds < 1)
                        {
                            return;
                        }

                        lastProgressUpdate = DateTime.Now;
                        lastProgress = progress;

                        BeginInvoke(() =>
                        {
                            if (!IsDisposed)
                            {
                                statusProgressBar.Value = progress;
                            }
                        });
                    }

                    switch (fileType)
                    {
                        case FileDetector.FileType.Unknown:
                            return;

                        case FileDetector.FileType.Zip:
                            try
                            {
                                archiveZIP = ZipFile.OpenRead(zippath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("TZIP");
                                MessageBox.Show(
                                    MessageTipGenerator.GenerateTip(
                                        "F00010042",
                                        ex.Message)); //F00010042
                                return;
                            }
                            break;

                        case FileDetector.FileType.Rar:
                            try
                            {
                                archiveRAR = ArchiveFactory.OpenArchive(zippath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("TZIP");
                                MessageBox.Show(
                                    MessageTipGenerator.GenerateTip(
                                        "F00010043",
                                        ex.Message)); //F00010043
                                return;
                            }
                            break;

                        case FileDetector.FileType.SevenZip:
                            goto case FileDetector.FileType.Rar;
                            break;

                        case FileDetector.FileType.Tar:
                            break;

                        case FileDetector.FileType.GZip:
                            break;

                        case FileDetector.FileType.BZip2:
                            break;

                        case FileDetector.FileType.Xz:
                            break;

                        case FileDetector.FileType.Lz4:
                            break;

                        case FileDetector.FileType.Zstd:
                            break;

                        case FileDetector.FileType.Iso:
                            break;

                        case FileDetector.FileType.Wim:
                            break;

                        default:
                            return;
                    }

                    switch (fileType)
                    {
                        case FileDetector.FileType.Unknown:
                            break;

                        case FileDetector.FileType.Zip:
                            {
                                int totalEntries = archiveZIP!.Entries.Count;
                                int currentEntry = 0;

                                foreach (var entry in archiveZIP.Entries)
                                {
                                    currentEntry++;

                                    if (string.IsNullOrEmpty(entry.Name))
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        BeginInvoke(() =>
                                        {
                                            if (!IsDisposed)
                                            {
                                                statusLabel.Text =
                                                    LanguageManager.Get("ExtractingText") +
                                                    $" {entry.FullName}";
                                            }
                                        });

                                        if (string.IsNullOrEmpty(entry.Name))
                                        {
                                            try
                                            {
                                                Directory.CreateDirectory(
                                                    Path.Combine(
                                                        destinationPath,
                                                        entry.FullName));
                                            }
                                            catch (Exception ex)
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010044",
                                                        ex.Message)); //F00010044
                                                return;
                                            }
                                        }

                                        if (File.Exists(
                                            Path.Combine(
                                                destinationPath,
                                                entry.FullName)))
                                        {
                                            if (allOverWrite)
                                            {
                                                goto cExtract;
                                            }
                                            else
                                            {
                                                if ((!allOverWrite) && (!allSkip))
                                                {
                                                reShowOWF:

                                                    OverWriteOrNotForm owonf =
                                                        new OverWriteOrNotForm();

                                                    owonf.ShowDialog();

                                                    owonf.SyncBool(
                                                        ref allOverWrite);

                                                    owonf.SyncBool2(
                                                        ref allSkip);

                                                    int OWFresult = -1;

                                                    owonf.GetResult(
                                                        ref OWFresult);

                                                    if ((!allOverWrite) &&
                                                        (!allSkip))
                                                    {
                                                        switch (OWFresult)
                                                        {
                                                            default:
                                                                break;

                                                            case -100:
                                                                goto reShowOWF;

                                                            case 1:
                                                                goto cExtract;

                                                            case 2:
                                                                continue;

                                                            case 1000:
                                                                stopJob = true;
                                                                break;
                                                        }
                                                    }
                                                }

                                                if (allOverWrite)
                                                {
                                                    goto cExtract;
                                                }

                                                if (allSkip)
                                                {
                                                    UpdateProgress(
                                                        currentEntry,
                                                        totalEntries);

                                                    continue;
                                                }

                                                if (stopJob)
                                                {
                                                    return;
                                                }
                                            }
                                        }

                                    cExtract:

                                        string? parent =
                                            Path.GetDirectoryName(
                                                Path.Combine(
                                                    destinationPath,
                                                    entry.FullName));

                                        if (!Directory.Exists(parent))
                                        {
                                            if (!string.IsNullOrEmpty(parent))
                                            {
                                                try
                                                {
                                                    Directory.CreateDirectory(parent);
                                                }
                                                catch (Exception ex)
                                                {
                                                    MessageBox.Show("TZIP");
                                                    MessageBox.Show(
                                                        MessageTipGenerator.GenerateTip(
                                                            "F00010045",
                                                            ex.Message)); //F00010045
                                                    return;
                                                }
                                            }
                                            else
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010046",
                                                        "we cannot create the parent directory,because the directory information we got is empty")); //F00010046
                                                return;
                                            }
                                        }

                                        try
                                        {
                                            entry.ExtractToFile(
                                                Path.Combine(
                                                    destinationPath,
                                                    entry.FullName),
                                                true);
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show("TZIP");
                                            MessageBox.Show(
                                                MessageTipGenerator.GenerateTip(
                                                    "F00010047",
                                                    ex.Message)); //F00010047
                                            return;
                                        }

                                        UpdateProgress(
                                            currentEntry,
                                            totalEntries);
                                    }
                                }

                                break;
                            }

                        case FileDetector.FileType.Rar:
                            {
                                int totalEntries = archiveRAR!.Entries.Count();
                                int currentEntry = 0;

                                foreach (var entry in archiveRAR.Entries)
                                {
                                    currentEntry++;

                                    // Skip directories
                                    if (entry.IsDirectory)
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        BeginInvoke(() =>
                                        {
                                            if (!IsDisposed)
                                            {
                                                statusLabel.Text =
                                                    LanguageManager.Get("ExtractingText") +
                                                    $" {entry.Key}";
                                            }
                                        });

                                        // Get the destination path of the current file
                                        string filePath =
                                            Path.Combine(
                                                destinationPath,
                                                entry.Key);

                                        // Check whether the target file already exists
                                        if (File.Exists(filePath))
                                        {
                                            if (allOverWrite)
                                            {
                                                goto cExtract;
                                            }
                                            else
                                            {
                                                if ((!allOverWrite) && (!allSkip))
                                                {
                                                reShowOWF:

                                                    OverWriteOrNotForm owonf =
                                                        new OverWriteOrNotForm();

                                                    owonf.ShowDialog();

                                                    owonf.SyncBool(
                                                        ref allOverWrite);

                                                    owonf.SyncBool2(
                                                        ref allSkip);

                                                    int OWFresult = -1;

                                                    owonf.GetResult(
                                                        ref OWFresult);

                                                    if ((!allOverWrite) &&
                                                        (!allSkip))
                                                    {
                                                        switch (OWFresult)
                                                        {
                                                            default:
                                                                break;

                                                            // Show the overwrite dialog again
                                                            case -100:
                                                                goto reShowOWF;

                                                            // Overwrite the current file
                                                            case 1:
                                                                goto cExtract;

                                                            // Skip the current file
                                                            case 2:
                                                                continue;

                                                            // Stop the entire extraction task
                                                            case 1000:
                                                                stopJob = true;
                                                                break;
                                                        }
                                                    }

                                                    // Overwrite all remaining files
                                                    if (allOverWrite)
                                                    {
                                                        goto cExtract;
                                                    }

                                                    // Skip all remaining files
                                                    if (allSkip)
                                                    {
                                                        UpdateProgress(
                                                            currentEntry,
                                                            totalEntries);

                                                        continue;
                                                    }

                                                    // Stop the entire extraction task
                                                    if (stopJob)
                                                    {
                                                        return;
                                                    }
                                                }
                                            }
                                        }

                                    cExtract:

                                        // Get the parent directory of the destination file
                                        string? parent =
                                            Path.GetDirectoryName(filePath);

                                        // Create the parent directory if it does not exist
                                        if (!Directory.Exists(parent))
                                        {
                                            if ((!string.IsNullOrEmpty(parent)) &&
                                                (!Directory.Exists(parent)))
                                            {
                                                try
                                                {
                                                    Directory.CreateDirectory(parent);
                                                }
                                                catch (Exception ex)
                                                {
                                                    MessageBox.Show("TZIP");
                                                    MessageBox.Show(
                                                        MessageTipGenerator.GenerateTip(
                                                            "F00010048",
                                                            ex.Message)); //F00010048
                                                    return;
                                                }
                                            }
                                            else
                                            {
                                                MessageBox.Show("TZIP");
                                                MessageBox.Show(
                                                    MessageTipGenerator.GenerateTip(
                                                        "F00010049",
                                                        "we cannot create the parent directory,because the directory information we got is empty")); //F00010049
                                                return;
                                            }
                                        }

                                        // Extract the file while preserving its full directory structure
                                        try
                                        {
                                            entry.WriteToDirectory(
                                                destinationPath,
                                                new ExtractionOptions
                                                {
                                                    ExtractFullPath = true,
                                                    Overwrite = true
                                                });
                                        }
                                        catch (Exception ex)
                                        {
                                            MessageBox.Show("TZIP");
                                            MessageBox.Show(
                                                MessageTipGenerator.GenerateTip(
                                                    "F00010050",
                                                    ex.Message)); //F00010050
                                            return;
                                        }

                                        UpdateProgress(
                                            currentEntry,
                                            totalEntries);
                                    }
                                }

                                break;
                            }

                        case FileDetector.FileType.SevenZip:
                            break;

                        case FileDetector.FileType.Tar:
                            break;

                        case FileDetector.FileType.GZip:
                            break;

                        case FileDetector.FileType.BZip2:
                            break;

                        case FileDetector.FileType.Xz:
                            break;

                        case FileDetector.FileType.Lz4:
                            break;

                        case FileDetector.FileType.Zstd:
                            break;

                        case FileDetector.FileType.Iso:
                            break;

                        case FileDetector.FileType.Wim:
                            break;

                        default:
                            break;
                    }
                });

                Invoke(() =>
                {
                    statusProgressBar.Value = 100;
                    statusLabel.Text = LanguageManager.Get("readytext");
                });

                isDoingJob = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("TZIP");
                MessageBox.Show(
                    MessageTipGenerator.GenerateTip(
                        "F00010051",
                        ex.Message)); //F00010051
            }
            finally
            {
                isDoingJob = false;
            }
        }
        private void uninstallFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                uninstallFile();
            }
            catch (Exception ex)
            {
                ShowErrorMessage(MessageTipGenerator.GenerateTip("F0010201",ex.ToString()));//F00010201
            }
        }
        void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        void uninstallFile()
        {
            statusProgressBar.Value = 50;
            statusLabel.Text = LanguageManager.Get("UninstallingText");
            zippath = string.Empty;
            statusLabel.Text = LanguageManager.Get("readytext");
            statusProgressBar.Value = 0;
            fileBox.SuspendLayout();
            fileBox.BeginUpdate();

            try
            {
                fileBox.Items.Clear();
            }
            finally
            {
                fileBox.EndUpdate();
                fileBox.ResumeLayout(true);
            }
            extractToolStripMenuItem.Visible = false;
            compressToolStripMenuItem.Visible = false;
            uninstallFileToolStripMenuItem.Visible = false;
            // 清除压缩包数据
            _archiveEntries.Clear();

            // 回到根目录
            _archiveCurrentDirectory = "";

            // 清除 fileBox 显示内容
            fileBox.Items.Clear();

            // 清除选择状态
            fileBox.ClearSelected();

            // 强制 ListBox 重新绘制
            fileBox.Invalidate(true);
            fileBox.Update();

            // 停止 fileBox 相关计时器
            _fileBoxScrollTimer?.Stop();

            // 清除光效捕获缓存
            if (_lightOverlay != null)
            {
                _lightOverlay.InvalidateCapture();
            }

            // 重置状态栏
            statusProgressBar.Value = 0;
            statusLabel.Text =
                LanguageManager.Get("readytext");

            // 当前没有正在进行的任务
            isDoingJob = false;
            mainTab.Text = LanguageManager.Get("mainTabText");
        }
        private void extractToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileBox.Items.Count == 0 || zippath == string.Empty)
            {
                MessageBox.Show(LanguageManager.Get("NoOpenedFile"));
                extractToolStripMenuItem.DropDown.Close();
                return;
            }
        }
        private async void extractDirectlyALLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            mainFolderBrowserDialog.Description = LanguageManager.Get("SelectExtractFolderText");
            if (mainFolderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                if (isDoingJob)
                {
                    MessageBox.Show(LanguageManager.Get("AlreadyDoingJob"));
                    return;
                }
                isDoingJob = true;
                if (File.Exists(zippath))
                {
                    statusProgressBar.Value = 50;
                    statusLabel.Text = LanguageManager.Get("ExtractingText");
                    await ExtractAllEntriesDIRECTLYAsync(zippath, mainFolderBrowserDialog.SelectedPath);
                }
            }
        }
        private async void extractToFolderALLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            mainFolderBrowserDialog.Description = LanguageManager.Get("SelectExtractFolderText");
            if (mainFolderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                if (isDoingJob)
                {
                    MessageBox.Show(LanguageManager.Get("AlreadyDoingJob"));
                    return;
                }
                isDoingJob = true;
                if (File.Exists(zippath))
                {
                    statusProgressBar.Value = 50;
                    statusLabel.Text = LanguageManager.Get("ExtractingText");
                    LoadArchiveAsync(zippath);
                    await ExtractAllEntriesAsync(zippath, mainFolderBrowserDialog.SelectedPath);
                }
            }
        }
        private async void extractDirectlySELECTEDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            mainFolderBrowserDialog.Description = LanguageManager.Get("SelectExtractFolderText");
            if (mainFolderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                if (isDoingJob)
                {
                    MessageBox.Show(LanguageManager.Get("AlreadyDoingJob"));
                    return;
                }
                isDoingJob = true;
                if (File.Exists(zippath))
                {
                    statusProgressBar.Value = 50;
                    statusLabel.Text = LanguageManager.Get("ExtractingText");
                    await ExtractSelectedEntriesDIRECTLYAsync(zippath, fileBox.SelectedItems.Cast<string>().ToList(), mainFolderBrowserDialog.SelectedPath);
                }
            }
        }
        private async void extractToAFolderSELECTEDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            mainFolderBrowserDialog.Description = LanguageManager.Get("SelectExtractFolderText");
            if (mainFolderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                if (isDoingJob)
                {
                    MessageBox.Show(LanguageManager.Get("AlreadyDoingJob"));
                    return;
                }
                isDoingJob = true;
                if (File.Exists(zippath))
                {
                    statusProgressBar.Value = 50;
                    statusLabel.Text = LanguageManager.Get("ExtractingText");
                    await ExtractSelectedEntriesAsync(zippath, fileBox.SelectedItems.Cast<string>().ToList(), mainFolderBrowserDialog.SelectedPath);
                }
            }
        }

        private void SettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(LanguageManager.Get("Unavailble1"), "TZIP");
        }
        private void compressSelectFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FreeFilePickerForm ffpf = new FreeFilePickerForm();
            ffpf.ShowDialog();
        }

        private void TZIPToolStripMenuItem_DoubleClick(object sender, EventArgs e)
        {
            TZIPForm tZIPForm = new TZIPForm();
            tZIPForm.ShowDialog();
            tZIPForm.Dispose();
        }

        private void compressToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void fileBox_DoubleClick(
       object? sender,
       EventArgs e)
        {
            if (fileBox.SelectedItem == null)
            {
                return;
            }

            string selectedItem =
                fileBox.SelectedItem.ToString() ?? "";

            // 根目录第一项为空
            if (string.IsNullOrEmpty(
                    _archiveCurrentDirectory) &&
                string.IsNullOrEmpty(selectedItem))
            {
                return;
            }

            // 返回上一级
            if (selectedItem ==
                LanguageManager.Get(
                    "goToParentDirectoryText"))
            {
                GoToParentDirectory();
                return;
            }

            // 查找当前显示项目对应的真实路径
            string selectedPath =
                    FindEntryPath(selectedItem);

            if (string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            // 只有目录可以进入
            if (!selectedPath.EndsWith("/"))
            {
                return;
            }

            _archiveCurrentDirectory =
                selectedPath;

            RefreshFileBox();

            fileBox.ClearSelected();
            refreshCurrentDirectory();
        }
        private string FindEntryPath(
    string displayName)
        {
            string currentDirectory =
                _archiveCurrentDirectory.Replace('\\', '/');

            if (!string.IsNullOrEmpty(currentDirectory) &&
                !currentDirectory.EndsWith("/"))
            {
                currentDirectory += "/";
            }

            foreach (string entry in _archiveEntries)
            {
                string normalizedEntry =
                    entry.Replace('\\', '/');

                // 必须位于当前目录
                if (!normalizedEntry.StartsWith(
                    currentDirectory,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath =
                    normalizedEntry.Substring(
                        currentDirectory.Length);

                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                /*
                 * 只处理当前目录下的第一层。
                 *
                 * 例如：
                 *
                 * 当前目录：
                 *     folder1/
                 *
                 * entry：
                 *     folder1/folder2/test.txt
                 *
                 * relativePath：
                 *     folder2/test.txt
                 *
                 * 这不是当前显示的直接项目。
                 */
                int slashIndex =
                    relativePath.IndexOf('/');

                if (slashIndex >= 0)
                {
                    string directoryName =
                        relativePath.Substring(
                            0,
                            slashIndex);

                    if (string.Equals(
                        directoryName,
                        displayName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return currentDirectory +
                               directoryName +
                               "/";
                    }
                }
                else
                {
                    /*
                     * 当前目录下的普通文件
                     */
                    if (string.Equals(
                        relativePath,
                        displayName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return currentDirectory +
                               relativePath;
                    }
                }
            }

            return "";
        }
        private string GetEntryPath(
    string displayName)
        {
            string targetPath =
                _archiveCurrentDirectory +
                displayName;

            targetPath =
                targetPath.Replace('\\', '/');

            foreach (string entry in _archiveEntries)
            {
                string normalizedEntry =
                    entry.Replace('\\', '/');

                if (string.Equals(
                    normalizedEntry,
                    targetPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedEntry;
                }

                // 当前显示的是文件夹名称
                if (normalizedEntry.EndsWith("/") &&
                    string.Equals(
                        normalizedEntry.TrimEnd('/'),
                        targetPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedEntry;
                }
            }

            return "";
        }
        private List<string> GetSelectedArchiveEntries()
        {
            List<string> result = new();

            foreach (object selectedItem in fileBox.SelectedItems)
            {
                string displayName =
                    selectedItem.ToString() ?? "";

                /*
                 * 空白项：
                 * 根目录第一项，不是文件。
                 */
                if (string.IsNullOrEmpty(displayName))
                {
                    continue;
                }

                /*
                 * 返回上一级：
                 * 不是文件。
                 */
                if (displayName ==
                    LanguageManager.Get(
                        "goToParentDirectoryText"))
                {
                    continue;
                }

                string entryPath =
                    GetEntryPath(displayName);

                if (!string.IsNullOrEmpty(entryPath))
                {
                    result.Add(entryPath);
                }
            }

            return result;
        }
        private void GoToParentDirectory()
        {
            if (string.IsNullOrEmpty(_archiveCurrentDirectory))
            {
                return;
            }

            string currentDirectory =
                _archiveCurrentDirectory.TrimEnd('/');

            int slashIndex =
                currentDirectory.LastIndexOf('/');

            if (slashIndex == -1)
            {
                // folder/
                // 返回根目录
                _archiveCurrentDirectory = "";
            }
            else
            {
                // folder/sub/
                // 返回 folder/
                _archiveCurrentDirectory =
                    currentDirectory.Substring(
                        0,
                        slashIndex + 1);
            }

            RefreshFileBox();

            fileBox.ClearSelected();
            refreshCurrentDirectory();
        }
    }
}
