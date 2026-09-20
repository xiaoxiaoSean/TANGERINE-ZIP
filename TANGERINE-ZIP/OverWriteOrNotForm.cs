using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using TANGERINE_ZIP.Tools.LightTool;
using TANGERINE_ZIP.Tools;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ScrollBar;

namespace TANGERINE_ZIP
{
    public partial class OverWriteOrNotForm : Form
    {
        public OverWriteOrNotForm()
        {
            InitializeComponent();
        }
        bool aow = false;
        bool askip = false;
        int output = -1;
#if ENABLE_LIGHT
        private TangerineLightOverlay? _lightOverlay;

        private System.Windows.Forms.Timer? _fileBoxScrollTimer;

        private float _normalEdgeStrength;
#endif
        private void OverWriteOrNotForm_Load(object sender, EventArgs e)
        {
            DarkTheme.Apply(this);
#if ENABLE_LIGHT
            #region set light effect
            _lightOverlay = new TangerineLightOverlay(this);
            _lightOverlay.TargetFps = 60;
            _lightOverlay.Radius = 120f;
            _lightOverlay.LightStrength = 0.02f;
            _lightOverlay.EdgeStrength = 3.9f;
            _lightOverlay.EdgeWidth = 3f;
            _lightOverlay.disableWhenMouseSpeedGetTooFast = 100000;
            _normalEdgeStrength = _lightOverlay.EdgeStrength;
            _fileBoxScrollTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 120
                };
            _fileBoxScrollTimer.Tick += FileBoxScrollTimer_Tick;
            listBox1.ViewChanged += FileBox_ViewChanged;
            _lightOverlay.Show(this);
            #endregion
#endif
            this.Text = LanguageManager.Get("OverWriteOrNot");
            label1.Text = LanguageManager.Get("OverWriteText");
            button1.Text = LanguageManager.Get("Execute");
            listBox1.BeginUpdate();

            try
            {
                listBox1.Items.Clear();
                listBox1.Items.Add(LanguageManager.Get("OverWrite1"));
                listBox1.Items.Add(LanguageManager.Get("OverWrite2"));
                listBox1.Items.Add(LanguageManager.Get("OverWrite3"));
                listBox1.Items.Add(LanguageManager.Get("OverWrite4"));
            }
            finally
            {
                listBox1.EndUpdate();
            }
        }
#if ENABLE_LIGHT
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
#endif
        public void SyncBool(ref bool allOverWrite)
        {
            allOverWrite = aow;
        }
        public void SyncBool2(ref bool allSkip)
        {
            allSkip = askip;
        }
        public void GetResult(ref int input)
        {
            input = output;
        }
        private void button1_Click(object sender, EventArgs e)
        {
            switch (listBox1.SelectedIndex)
            {
                default:
                    MessageBox.Show(LanguageManager.Get("Suggestion1") + LanguageManager.Get("OverWriteSuggestion1") + LanguageManager.Get("ErrorCode1") + "OWFSIES" + listBox1.SelectedIndex);
                    return;
                case 0://yes
                    output = 1;
                    aow = false;
                    askip = false;
                    break;
                case 1://no
                    output = 2;
                    aow = false;
                    askip = false;
                    break;
                case 2://yes to all
                    output = -99;
                    aow = true;
                    askip = false;
                    break;
                case 3://no to all
                    output = -99;
                    aow = false;
                    askip = true;
                    break;
            }
            this.Close();
        }
    }
}
