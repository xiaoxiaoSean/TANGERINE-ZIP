using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using TANGERINE_ZIP.Resources;
using TANGERINE_ZIP.Tools.LightTool;
using TANGERINE_ZIP.Tools;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ScrollBar;
namespace TANGERINE_ZIP
{
    public partial class TZIPForm : Form
    {
        private TangerineLightControl? tangerineLightControl;
        private System.Windows.Forms.Timer? colorTimer;
        private KnownColor[]? knownColors;
        private int colorIndex;

        public TZIPForm()
        {
            InitializeComponent();
        }
        private TangerineLightOverlay? _lightOverlay;
        private void TZIPForm_Load(object sender, EventArgs e)
        {
            DarkTheme.Apply(this);
            this.WindowState= FormWindowState.Maximized;
            _lightOverlay = new TangerineLightOverlay(this);
            _lightOverlay.TargetFps = 12000;
            _lightOverlay.Radius = 30f;
            _lightOverlay.LightStrength = 2.0f;
            _lightOverlay.EdgeStrength = 30.0f;
            _lightOverlay.EdgeWidth = 30f;
            _lightOverlay.disableWhenMouseSpeedGetTooFast = 100000000;
            _lightOverlay.eDelay = 0;
            _lightOverlay.eAnimationTime = 0;
            tangerineLightControl =
               new TangerineLightControl();
            tangerineLightControl.BackColor = Color.Black;
            tangerineLightControl.Dock =
                DockStyle.Fill;
            tangerineLightControl.LightRadius =
               500f;
           
            tangerineLightControl.GlowStrength =
                7.0f;
            tangerineLightControl.EdgeThickness = 10;
            tangerineLightControl.GlowColor =
                Color.White;

            tangerineLightControl.SetImage(
                TZIPResource.Kiro
            );

            Controls.Add(
                tangerineLightControl
            );

            tangerineLightControl.BringToFront();

            knownColors = Enum.GetValues<KnownColor>();
            colorTimer = new System.Windows.Forms.Timer
            {
                Interval = 50
            };
            colorTimer.Tick += (_, _) =>
            {
                if (tangerineLightControl == null || knownColors == null)
                    return;

                tangerineLightControl.GlowColor =
                    Color.FromKnownColor(knownColors[colorIndex]);
                colorIndex = (colorIndex + 1) % knownColors.Length;
            };
            colorTimer.Start();
        }
    }
}
