using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TANGERINE_ZIP.Tools;
using TANGERINE_ZIP.Tools.LightTool;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ScrollBar;
// Stage head: F0002
namespace TANGERINE_ZIP
{
    public partial class FreeFilePickerForm : Form
    {
        private string? currentPath;
        private TangerineLightOverlay? _lightOverlay;

        private System.Windows.Forms.Timer? _fileBoxScrollTimer;

        private float _normalEdgeStrength;
        public FreeFilePickerForm()
        {
            InitializeComponent();
            fileListBox.DoubleClick += FileListBox_DoubleClick;
        }
        public void ShowTipText(string inputTip)
        {
            formTipText.Text = inputTip;
        }
        public void InputPath(string inputPath)
        {
            LoadPath(inputPath);
        }
        void LoadPath(string inputPath)
        {
            fileListBox.Items.Clear();            
            string[] files;
            string[] folders;
            try
            {
                files = Directory.GetFiles(inputPath);
            }
            catch (Exception ex) //F00020001
            {
                MessageBox.Show(MessageTipGenerator.GenerateTip("F00020001", ex.Message)); //F00020001
                return;
            }
            try
            {
                folders = Directory.GetDirectories(inputPath);
            }
            catch (Exception ex) //F00020002
            {
                MessageBox.Show(MessageTipGenerator.GenerateTip("F00020002", ex.Message)); //F00020002
                return;
            }           
            string[] allItems = PathSorter.MergeAndSort(files, folders);

            fileListBox.BeginUpdate();

            try
            {
                fileListBox.Items.Clear();
                fileListBox.Items.Add(LanguageManager.Get("goToParentDirectoryText") + "...");
                foreach (string item in allItems)
                {
                    fileListBox.Items.Add(Path.GetFileName(item));
                }
            }
            finally
            {
                fileListBox.EndUpdate();
            }

            currentPath = inputPath;
        }

        private void LoadDrives()
        {
            fileListBox.Items.Clear();            
DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch (Exception ex) //F00020003
            {
                MessageBox.Show(MessageTipGenerator.GenerateTip("F00020003", ex.Message)); //F00020003
                return;
            }

            fileListBox.BeginUpdate();
            try
            {
                fileListBox.Items.Clear();
                foreach (DriveInfo drive in drives)
                {
                    fileListBox.Items.Add(drive.Name);
                }
            }
            finally
            {
                fileListBox.EndUpdate();
            }

            currentPath = null;
        }

        private void FileListBox_DoubleClick(object? sender, EventArgs e)
        {
            if (fileListBox.SelectedItem is not string selectedItem)
            {
                return;
            }

            if (currentPath == null)
            {
                LoadPath(selectedItem);
                return;
            }

            string parentItem = LanguageManager.Get("goToParentDirectoryText") + "...";
            if (selectedItem == parentItem)
            {
                string? parentPath = Directory.GetParent(currentPath)?.FullName;
                if (parentPath == null)
                {
                    LoadDrives();
                }
                else
                {
                    LoadPath(parentPath);
                }
                return;
            }

            string selectedPath = Path.Combine(currentPath, selectedItem);
            if (Directory.Exists(selectedPath))
            {
                LoadPath(selectedPath);
            }
        }
        private void FreeFilePickerForm_Load(object sender, EventArgs e)
        {
            DarkTheme.Apply(this);
            Text = LanguageManager.Get("FreeFilePickerFormTitle");
            #region set light effect
            _lightOverlay = new TangerineLightOverlay(this);
            _lightOverlay.TargetFps = 60;
            _lightOverlay.Radius = 180f;
            _lightOverlay.LightStrength = 0.02f;
            _lightOverlay.EdgeStrength = 7.9f;
            _lightOverlay.EdgeWidth = 3f;
            _lightOverlay.disableWhenMouseSpeedGetTooFast = 100000;
            _normalEdgeStrength = _lightOverlay.EdgeStrength;
            _fileBoxScrollTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 120
                };
            _fileBoxScrollTimer.Tick += FileListBoxScrollTimer_Tick;
            fileListBox.ViewChanged += FileListBox_ViewChanged;
            _lightOverlay.Show(this);
            #endregion
            confirmButton.Text=LanguageManager.Get("Confirm");
            LoadDrives();
        }
        private void FileListBoxScrollTimer_Tick(
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
        private void FileListBox_ViewChanged(
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
    }
}
