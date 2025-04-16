namespace Robin
{
    partial class RobinForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RobinForm));
            this.textBox_videoURL = new System.Windows.Forms.TextBox();
            this.serviceController1 = new System.ServiceProcess.ServiceController();
            this.label1 = new System.Windows.Forms.Label();
            this.btn_download = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.label_videoTitle = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label_videoResolution = new System.Windows.Forms.Label();
            this.label_videoExtension = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label_maxBitrate = new System.Windows.Forms.Label();
            this.listView_downloads = new System.Windows.Forms.ListView();
            this.videoNameHeader = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.statusHeader = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.locationHeader = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.label_size = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.checkForUpdatesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.label7 = new System.Windows.Forms.Label();
            this.label_appVersion = new System.Windows.Forms.Label();
            this.progressHeader = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // textBox_videoURL
            // 
            this.textBox_videoURL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_videoURL.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.textBox_videoURL.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.textBox_videoURL.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBox_videoURL.Location = new System.Drawing.Point(232, 48);
            this.textBox_videoURL.Name = "textBox_videoURL";
            this.textBox_videoURL.Size = new System.Drawing.Size(2136, 44);
            this.textBox_videoURL.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(12, 60);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(194, 31);
            this.label1.TabIndex = 1;
            this.label1.Text = "YouTube URL:";
            // 
            // btn_download
            // 
            this.btn_download.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btn_download.Location = new System.Drawing.Point(2374, 48);
            this.btn_download.Name = "btn_download";
            this.btn_download.Size = new System.Drawing.Size(149, 44);
            this.btn_download.TabIndex = 2;
            this.btn_download.Text = "Download";
            this.btn_download.UseVisualStyleBackColor = true;
            this.btn_download.Click += new System.EventHandler(this.btn_download_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(15, 152);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(150, 31);
            this.label2.TabIndex = 4;
            this.label2.Text = "Video Title:";
            // 
            // label_videoTitle
            // 
            this.label_videoTitle.AutoSize = true;
            this.label_videoTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_videoTitle.Location = new System.Drawing.Point(229, 152);
            this.label_videoTitle.Name = "label_videoTitle";
            this.label_videoTitle.Size = new System.Drawing.Size(29, 31);
            this.label_videoTitle.TabIndex = 5;
            this.label_videoTitle.Text = "?";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(15, 242);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(208, 31);
            this.label3.TabIndex = 6;
            this.label3.Text = "Max Resolution:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(15, 194);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(141, 31);
            this.label4.TabIndex = 7;
            this.label4.Text = "Extension:";
            // 
            // label_videoResolution
            // 
            this.label_videoResolution.AutoSize = true;
            this.label_videoResolution.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_videoResolution.Location = new System.Drawing.Point(229, 242);
            this.label_videoResolution.Name = "label_videoResolution";
            this.label_videoResolution.Size = new System.Drawing.Size(29, 31);
            this.label_videoResolution.TabIndex = 8;
            this.label_videoResolution.Text = "?";
            // 
            // label_videoExtension
            // 
            this.label_videoExtension.AutoSize = true;
            this.label_videoExtension.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_videoExtension.Location = new System.Drawing.Point(229, 194);
            this.label_videoExtension.Name = "label_videoExtension";
            this.label_videoExtension.Size = new System.Drawing.Size(29, 31);
            this.label_videoExtension.TabIndex = 9;
            this.label_videoExtension.Text = "?";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label5.Location = new System.Drawing.Point(15, 288);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(158, 31);
            this.label5.TabIndex = 10;
            this.label5.Text = "Max Bitrate:";
            // 
            // label_maxBitrate
            // 
            this.label_maxBitrate.AutoSize = true;
            this.label_maxBitrate.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_maxBitrate.Location = new System.Drawing.Point(229, 288);
            this.label_maxBitrate.Name = "label_maxBitrate";
            this.label_maxBitrate.Size = new System.Drawing.Size(29, 31);
            this.label_maxBitrate.TabIndex = 11;
            this.label_maxBitrate.Text = "?";
            // 
            // listView_downloads
            // 
            this.listView_downloads.Alignment = System.Windows.Forms.ListViewAlignment.Left;
            this.listView_downloads.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.listView_downloads.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.videoNameHeader,
            this.statusHeader,
            this.locationHeader,
            this.progressHeader});
            this.listView_downloads.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.listView_downloads.GridLines = true;
            this.listView_downloads.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.listView_downloads.HideSelection = false;
            this.listView_downloads.Location = new System.Drawing.Point(29, 384);
            this.listView_downloads.MultiSelect = false;
            this.listView_downloads.Name = "listView_downloads";
            this.listView_downloads.Size = new System.Drawing.Size(2483, 733);
            this.listView_downloads.TabIndex = 13;
            this.listView_downloads.TileSize = new System.Drawing.Size(800, 78);
            this.listView_downloads.UseCompatibleStateImageBehavior = false;
            this.listView_downloads.View = System.Windows.Forms.View.Details;
            // 
            // videoNameHeader
            // 
            this.videoNameHeader.Text = "Video Name";
            this.videoNameHeader.Width = 758;
            // 
            // statusHeader
            // 
            this.statusHeader.Text = "Status";
            this.statusHeader.Width = 300;
            // 
            // locationHeader
            // 
            this.locationHeader.Text = "Location";
            this.locationHeader.Width = 960;
            // 
            // label_size
            // 
            this.label_size.AutoSize = true;
            this.label_size.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_size.Location = new System.Drawing.Point(229, 332);
            this.label_size.Name = "label_size";
            this.label_size.Size = new System.Drawing.Size(29, 31);
            this.label_size.TabIndex = 15;
            this.label_size.Text = "?";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label8.Location = new System.Drawing.Point(15, 332);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(150, 31);
            this.label8.TabIndex = 14;
            this.label8.Text = "Size in MB:";
            //
            // 
            // menuStrip1
            // 
            this.menuStrip1.GripMargin = new System.Windows.Forms.Padding(2, 2, 0, 2);
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(2535, 45);
            this.menuStrip1.TabIndex = 16;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.checkForUpdatesToolStripMenuItem});
            this.fileToolStripMenuItem.Font = new System.Drawing.Font("Segoe UI", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(78, 41);
            this.fileToolStripMenuItem.Text = "File";
            // 
            // checkForUpdatesToolStripMenuItem
            // 
            this.checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            this.checkForUpdatesToolStripMenuItem.Size = new System.Drawing.Size(370, 46);
            this.checkForUpdatesToolStripMenuItem.Text = "Check for Updates";
            this.checkForUpdatesToolStripMenuItem.Click += new System.EventHandler(this.checkForUpdatesToolStripMenuItem_Click);
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(1163, 9);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(132, 25);
            this.label7.TabIndex = 17;
            this.label7.Text = "App version:";
            // 
            // label_appVersion
            // 
            this.label_appVersion.AutoSize = true;
            this.label_appVersion.Location = new System.Drawing.Point(1302, 9);
            this.label_appVersion.Name = "label_appVersion";
            this.label_appVersion.Size = new System.Drawing.Size(60, 25);
            this.label_appVersion.TabIndex = 18;
            this.label_appVersion.Text = "0.0.0";
            // 
            // progressHeader
            // 
            this.progressHeader.Text = "Progress";
            this.progressHeader.Width = 460;
            // 
            // RobinForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(192F, 192F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(2535, 1129);
            this.Controls.Add(this.label_appVersion);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label_size);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.listView_downloads);
            this.Controls.Add(this.label_maxBitrate);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label_videoExtension);
            this.Controls.Add(this.label_videoResolution);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label_videoTitle);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.btn_download);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBox_videoURL);
            this.Controls.Add(this.menuStrip1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "RobinForm";
            this.Text = "Robin";
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBox_videoURL;
        private System.ServiceProcess.ServiceController serviceController1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btn_download;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label_videoTitle;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label_videoResolution;
        private System.Windows.Forms.Label label_videoExtension;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label_maxBitrate;
        private System.Windows.Forms.ListView listView_downloads;
        private System.Windows.Forms.ColumnHeader videoNameHeader;
        private System.Windows.Forms.ColumnHeader statusHeader;
        private System.Windows.Forms.Label label_size;
        private System.Windows.Forms.Label label8;
        private System.ComponentModel.BackgroundWorker backgroundWorker1;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label_appVersion;
        private System.Windows.Forms.ColumnHeader locationHeader;
        private System.Windows.Forms.ColumnHeader progressHeader;
    }
}

