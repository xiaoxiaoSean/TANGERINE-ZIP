using TANGERINE_ZIP.Tools.TControls1;
using TANGERINE_ZIP.Resources;
namespace TANGERINE_ZIP
{
    partial class OverWriteOrNotForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OverWriteOrNotForm));
            tableLayoutPanel1 = new TableLayoutPanel();
            label1 = new Label();
            listBox1 = new FlickerFreeListBox();
            pictureBox1 = new PictureBox();
            button1 = new Button();
            tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Controls.Add(label1, 0, 0);
            tableLayoutPanel1.Controls.Add(listBox1, 0, 1);
            tableLayoutPanel1.Controls.Add(pictureBox1, 1, 0);
            tableLayoutPanel1.Controls.Add(button1, 1, 1);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 40.514904F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 59.485096F));
            tableLayoutPanel1.Size = new Size(1501, 738);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Dock = DockStyle.Fill;
            label1.Location = new Point(3, 0);
            label1.Name = "label1";
            label1.Size = new Size(744, 299);
            label1.TabIndex = 0;
            label1.Text = "notice";
            // 
            // listBox1
            // 
            listBox1.Dock = DockStyle.Fill;
            listBox1.FormattingEnabled = true;
            listBox1.Location = new Point(3, 302);
            listBox1.Name = "listBox1";
            listBox1.Size = new Size(744, 433);
            listBox1.TabIndex = 1;
#if ENABLE_LIGHT
            listBox1.ViewChanged += FileBox_ViewChanged;
#endif
            // 
            // pictureBox1
            // 
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Image = TZIPResource.TZIP;
            pictureBox1.Location = new Point(753, 3);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(745, 293);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 2;
            pictureBox1.TabStop = false;
            // 
            // button1
            // 
            button1.Dock = DockStyle.Fill;
            button1.Location = new Point(753, 302);
            button1.Name = "button1";
            button1.Size = new Size(745, 433);
            button1.TabIndex = 3;
            button1.Text = "excute";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // OverWriteOrNotForm
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1501, 738);
            Controls.Add(tableLayoutPanel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "OverWriteOrNotForm";
            Text = "OverWrite?";
            Load += OverWriteOrNotForm_Load;
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel tableLayoutPanel1;
        private Label label1;
        private PictureBox pictureBox1;
        private Button button1;
        private FlickerFreeListBox listBox1;
    }
}
