using TANGERINE_ZIP.Tools.TControls1;

namespace TANGERINE_ZIP
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            mainMenu = new MenuStrip();
            TZIPToolStripMenuItem = new ToolStripMenuItem();
            OpenToolStripMenuItem = new ToolStripMenuItem();
            extractToolStripMenuItem = new ToolStripMenuItem();
            extractDirectlyALLToolStripMenuItem = new ToolStripMenuItem();
            extractToFolderALLToolStripMenuItem = new ToolStripMenuItem();
            extractDirectlySELECTEDToolStripMenuItem = new ToolStripMenuItem();
            extractToAFolderSELECTEDToolStripMenuItem = new ToolStripMenuItem();
            compressToolStripMenuItem = new ToolStripMenuItem();
            compressSelectFileToolStripMenuItem = new ToolStripMenuItem();
            SettingsToolStripMenuItem = new ToolStripMenuItem();
            uninstallFileToolStripMenuItem = new ToolStripMenuItem();
            mainTabControl = new DarkTabControl();
            mainTab = new TabPage();
            fileBox = new FlickerFreeListBox();
            statusStrip1 = new StatusStrip();
            statusProgressBar = new ToolStripProgressBar();
            statusLabel = new ToolStripStatusLabel();
            mainOpenFileDialog = new OpenFileDialog();
            mainSaveFileDialog = new SaveFileDialog();
            mainFolderBrowserDialog = new FolderBrowserDialog();
            mainMenu.SuspendLayout();
            mainTabControl.SuspendLayout();
            mainTab.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // mainMenu
            // 
            mainMenu.BackColor = Color.Black;
            mainMenu.ImageScalingSize = new Size(24, 24);
            mainMenu.Items.AddRange(new ToolStripItem[] { TZIPToolStripMenuItem, OpenToolStripMenuItem, extractToolStripMenuItem, compressToolStripMenuItem, SettingsToolStripMenuItem, uninstallFileToolStripMenuItem });
            mainMenu.Location = new Point(0, 0);
            mainMenu.Name = "mainMenu";
            mainMenu.Size = new Size(1199, 32);
            mainMenu.TabIndex = 1;
            // 
            // TZIPToolStripMenuItem
            // 
            TZIPToolStripMenuItem.ForeColor = Color.White;
            TZIPToolStripMenuItem.Name = "TZIPToolStripMenuItem";
            TZIPToolStripMenuItem.Size = new Size(63, 28);
            TZIPToolStripMenuItem.Text = "TZIP";
            TZIPToolStripMenuItem.Click += TZIPToolStripMenuItem_Click;
            TZIPToolStripMenuItem.DoubleClick += TZIPToolStripMenuItem_DoubleClick;
            // 
            // OpenToolStripMenuItem
            // 
            OpenToolStripMenuItem.ForeColor = Color.White;
            OpenToolStripMenuItem.Name = "OpenToolStripMenuItem";
            OpenToolStripMenuItem.Size = new Size(74, 28);
            OpenToolStripMenuItem.Text = "Open";
            OpenToolStripMenuItem.Click += OpenToolStripMenuItem_Click;
            // 
            // extractToolStripMenuItem
            // 
            extractToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { extractDirectlyALLToolStripMenuItem, extractToFolderALLToolStripMenuItem, extractDirectlySELECTEDToolStripMenuItem, extractToAFolderSELECTEDToolStripMenuItem });
            extractToolStripMenuItem.ForeColor = Color.White;
            extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            extractToolStripMenuItem.Size = new Size(85, 28);
            extractToolStripMenuItem.Text = "extract";
            extractToolStripMenuItem.Click += extractToolStripMenuItem_Click;
            // 
            // extractDirectlyALLToolStripMenuItem
            // 
            extractDirectlyALLToolStripMenuItem.Name = "extractDirectlyALLToolStripMenuItem";
            extractDirectlyALLToolStripMenuItem.Size = new Size(346, 34);
            extractDirectlyALLToolStripMenuItem.Text = "extract directly(all)";
            extractDirectlyALLToolStripMenuItem.Click += extractDirectlyALLToolStripMenuItem_Click;
            // 
            // extractToFolderALLToolStripMenuItem
            // 
            extractToFolderALLToolStripMenuItem.Name = "extractToFolderALLToolStripMenuItem";
            extractToFolderALLToolStripMenuItem.Size = new Size(346, 34);
            extractToFolderALLToolStripMenuItem.Text = "extract to a folder(all)";
            extractToFolderALLToolStripMenuItem.Click += extractToFolderALLToolStripMenuItem_Click;
            // 
            // extractDirectlySELECTEDToolStripMenuItem
            // 
            extractDirectlySELECTEDToolStripMenuItem.Name = "extractDirectlySELECTEDToolStripMenuItem";
            extractDirectlySELECTEDToolStripMenuItem.Size = new Size(346, 34);
            extractDirectlySELECTEDToolStripMenuItem.Text = "extract directly(selected)";
            extractDirectlySELECTEDToolStripMenuItem.Click += extractDirectlySELECTEDToolStripMenuItem_Click;
            // 
            // extractToAFolderSELECTEDToolStripMenuItem
            // 
            extractToAFolderSELECTEDToolStripMenuItem.Name = "extractToAFolderSELECTEDToolStripMenuItem";
            extractToAFolderSELECTEDToolStripMenuItem.Size = new Size(346, 34);
            extractToAFolderSELECTEDToolStripMenuItem.Text = "extract to a folder(selected)";
            extractToAFolderSELECTEDToolStripMenuItem.Click += extractToAFolderSELECTEDToolStripMenuItem_Click;
            // 
            // compressToolStripMenuItem
            // 
            compressToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { compressSelectFileToolStripMenuItem });
            compressToolStripMenuItem.ForeColor = Color.White;
            compressToolStripMenuItem.Name = "compressToolStripMenuItem";
            compressToolStripMenuItem.Size = new Size(108, 28);
            compressToolStripMenuItem.Text = "compress";
            compressToolStripMenuItem.Click += compressToolStripMenuItem_Click;
            // 
            // compressSelectFileToolStripMenuItem
            // 
            compressSelectFileToolStripMenuItem.Name = "compressSelectFileToolStripMenuItem";
            compressSelectFileToolStripMenuItem.Size = new Size(244, 34);
            compressSelectFileToolStripMenuItem.Text = "Select (a) file (s)";
            compressSelectFileToolStripMenuItem.Click += compressSelectFileToolStripMenuItem_Click;
            // 
            // SettingsToolStripMenuItem
            // 
            SettingsToolStripMenuItem.ForeColor = Color.White;
            SettingsToolStripMenuItem.Name = "SettingsToolStripMenuItem";
            SettingsToolStripMenuItem.Size = new Size(94, 28);
            SettingsToolStripMenuItem.Text = "settings";
            SettingsToolStripMenuItem.Click += SettingsToolStripMenuItem_Click;
            // 
            // uninstallFileToolStripMenuItem
            // 
            uninstallFileToolStripMenuItem.ForeColor = Color.White;
            uninstallFileToolStripMenuItem.Name = "uninstallFileToolStripMenuItem";
            uninstallFileToolStripMenuItem.Size = new Size(263, 28);
            uninstallFileToolStripMenuItem.Text = "uninstall the file(not delete)";
            uninstallFileToolStripMenuItem.Click += uninstallFileToolStripMenuItem_Click;
            // 
            // mainTabControl
            // 
            mainTabControl.Controls.Add(mainTab);
            mainTabControl.Dock = DockStyle.Fill;
            mainTabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            mainTabControl.ForeColor = Color.White;
            mainTabControl.Location = new Point(0, 32);
            mainTabControl.Name = "mainTabControl";
            mainTabControl.SelectedIndex = 0;
            mainTabControl.Size = new Size(1199, 689);
            mainTabControl.TabIndex = 0;
            // 
            // mainTab
            // 
            mainTab.BackColor = Color.Black;
            mainTab.Controls.Add(fileBox);
            mainTab.ForeColor = Color.White;
            mainTab.Location = new Point(4, 33);
            mainTab.Name = "mainTab";
            mainTab.Size = new Size(1191, 652);
            mainTab.TabIndex = 0;
            // 
            // fileBox
            // 
            fileBox.BackColor = Color.Black;
            fileBox.Dock = DockStyle.Fill;
            fileBox.DrawMode = DrawMode.OwnerDrawFixed;
            fileBox.ForeColor = Color.White;
            fileBox.FormattingEnabled = true;
            fileBox.Location = new Point(0, 0);
            fileBox.Name = "fileBox";
            fileBox.Size = new Size(1191, 652);
            fileBox.TabIndex = 1;
            fileBox.DoubleClick += fileBox_DoubleClick;
            // 
            // statusStrip1
            // 
            statusStrip1.BackColor = Color.Black;
            statusStrip1.ImageScalingSize = new Size(24, 24);
            statusStrip1.Items.AddRange(new ToolStripItem[] { statusProgressBar, statusLabel });
            statusStrip1.Location = new Point(0, 690);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1199, 31);
            statusStrip1.TabIndex = 0;
            // 
            // statusProgressBar
            // 
            statusProgressBar.Name = "statusProgressBar";
            statusProgressBar.Size = new Size(100, 23);
            // 
            // statusLabel
            // 
            statusLabel.ForeColor = Color.White;
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(137, 24);
            statusLabel.Text = "TZIP is starting";
            // 
            // mainOpenFileDialog
            // 
            mainOpenFileDialog.FileName = "openFileDialog1";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1199, 721);
            Controls.Add(statusStrip1);
            Controls.Add(mainTabControl);
            Controls.Add(mainMenu);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "TZIP";
            Load += Form1_Load;
            mainMenu.ResumeLayout(false);
            mainMenu.PerformLayout();
            mainTabControl.ResumeLayout(false);
            mainTab.ResumeLayout(false);
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip mainMenu;
        private ToolStripMenuItem TZIPToolStripMenuItem;
        private ToolStripMenuItem extractToolStripMenuItem;
        private ToolStripMenuItem compressToolStripMenuItem;
        private ToolStripMenuItem SettingsToolStripMenuItem;
        private DarkTabControl mainTabControl;
        private TabPage mainTab;
        private FlickerFreeListBox fileBox;
        private ToolStripMenuItem OpenToolStripMenuItem;
        private OpenFileDialog mainOpenFileDialog;
        private StatusStrip statusStrip1;
        private ToolStripProgressBar statusProgressBar;
        private ToolStripStatusLabel statusLabel;
        private ToolStripMenuItem uninstallFileToolStripMenuItem;
        private ToolStripMenuItem extractDirectlyALLToolStripMenuItem;
        private ToolStripMenuItem extractToFolderALLToolStripMenuItem;
        private ToolStripMenuItem extractDirectlySELECTEDToolStripMenuItem;
        private ToolStripMenuItem extractToAFolderSELECTEDToolStripMenuItem;
        private ToolStripMenuItem compressSelectFileToolStripMenuItem;
        private SaveFileDialog mainSaveFileDialog;
        private FolderBrowserDialog mainFolderBrowserDialog;
    }
}
