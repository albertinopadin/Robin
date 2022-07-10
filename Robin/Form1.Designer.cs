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
            this.textBox_videoURL = new System.Windows.Forms.TextBox();
            this.serviceController1 = new System.ServiceProcess.ServiceController();
            this.label1 = new System.Windows.Forms.Label();
            this.btn_download = new System.Windows.Forms.Button();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.label2 = new System.Windows.Forms.Label();
            this.label_videoTitle = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label_videoResolution = new System.Windows.Forms.Label();
            this.label_videoExtension = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label_maxBitrate = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // textBox_videoURL
            // 
            this.textBox_videoURL.Font = new System.Drawing.Font("Microsoft Sans Serif", 13.875F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBox_videoURL.Location = new System.Drawing.Point(212, 17);
            this.textBox_videoURL.Name = "textBox_videoURL";
            this.textBox_videoURL.Size = new System.Drawing.Size(1083, 49);
            this.textBox_videoURL.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(12, 29);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(194, 31);
            this.label1.TabIndex = 1;
            this.label1.Text = "YouTube URL:";
            // 
            // btn_download
            // 
            this.btn_download.Location = new System.Drawing.Point(1301, 17);
            this.btn_download.Name = "btn_download";
            this.btn_download.Size = new System.Drawing.Size(149, 49);
            this.btn_download.TabIndex = 2;
            this.btn_download.Text = "Download";
            this.btn_download.UseVisualStyleBackColor = true;
            this.btn_download.Click += new System.EventHandler(this.btn_download_Click);
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(212, 92);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(1083, 23);
            this.progressBar1.TabIndex = 3;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(12, 166);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(150, 31);
            this.label2.TabIndex = 4;
            this.label2.Text = "Video Title:";
            // 
            // label_videoTitle
            // 
            this.label_videoTitle.AutoSize = true;
            this.label_videoTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_videoTitle.Location = new System.Drawing.Point(226, 166);
            this.label_videoTitle.Name = "label_videoTitle";
            this.label_videoTitle.Size = new System.Drawing.Size(29, 31);
            this.label_videoTitle.TabIndex = 5;
            this.label_videoTitle.Text = "?";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(12, 270);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(208, 31);
            this.label3.TabIndex = 6;
            this.label3.Text = "Max Resolution:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(12, 222);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(141, 31);
            this.label4.TabIndex = 7;
            this.label4.Text = "Extension:";
            // 
            // label_videoResolution
            // 
            this.label_videoResolution.AutoSize = true;
            this.label_videoResolution.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_videoResolution.Location = new System.Drawing.Point(226, 270);
            this.label_videoResolution.Name = "label_videoResolution";
            this.label_videoResolution.Size = new System.Drawing.Size(29, 31);
            this.label_videoResolution.TabIndex = 8;
            this.label_videoResolution.Text = "?";
            // 
            // label_videoExtension
            // 
            this.label_videoExtension.AutoSize = true;
            this.label_videoExtension.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_videoExtension.Location = new System.Drawing.Point(226, 222);
            this.label_videoExtension.Name = "label_videoExtension";
            this.label_videoExtension.Size = new System.Drawing.Size(29, 31);
            this.label_videoExtension.TabIndex = 9;
            this.label_videoExtension.Text = "?";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label5.Location = new System.Drawing.Point(12, 316);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(158, 31);
            this.label5.TabIndex = 10;
            this.label5.Text = "Max Bitrate:";
            // 
            // label_maxBitrate
            // 
            this.label_maxBitrate.AutoSize = true;
            this.label_maxBitrate.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.125F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_maxBitrate.Location = new System.Drawing.Point(226, 316);
            this.label_maxBitrate.Name = "label_maxBitrate";
            this.label_maxBitrate.Size = new System.Drawing.Size(29, 31);
            this.label_maxBitrate.TabIndex = 11;
            this.label_maxBitrate.Text = "?";
            // 
            // RobinForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1462, 866);
            this.Controls.Add(this.label_maxBitrate);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label_videoExtension);
            this.Controls.Add(this.label_videoResolution);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label_videoTitle);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.btn_download);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBox_videoURL);
            this.Name = "RobinForm";
            this.Text = "Robin";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBox_videoURL;
        private System.ServiceProcess.ServiceController serviceController1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btn_download;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label_videoTitle;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label_videoResolution;
        private System.Windows.Forms.Label label_videoExtension;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label_maxBitrate;
    }
}

