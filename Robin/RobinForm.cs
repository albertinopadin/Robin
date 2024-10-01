using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Threading;
using System.Deployment.Application;

namespace Robin
{
    public partial class RobinForm : Form
    {
        public static string baseFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);

        public RobinForm()
        {
            InitializeComponent();

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                label_appVersion.Text = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            } else
            {
                label_appVersion.Text = "NON-NETWORK DEPLOYED";
            }
        }

        private async void btn_download_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            await videoDownloader.DownloadVideo(this, textBox_videoURL.Text);
            Cursor = Cursors.Arrow;
        }

        public void SetVideoInfo(RobinVideoInfo info)
        {
            label_videoTitle.Text = info.Title;
            label_videoExtension.Text = info.Extension;
            label_videoResolution.Text = info.Resolution;
            label_maxBitrate.Text = info.Bitrate;
            label_size.Text = info.Size;
        }

        public void InitProgressBar(int size)
        {
            progressBarDownload.Maximum = size;
            progressBarDownload.Step = 1;
        }

        public void SetProgressBarValue(int value)
        {
            progressBarDownload.Value = value;
        }

        public ListViewItem AddVideoToDownloadsList(string videoTitle)
        {
            listView_downloads.BeginUpdate();
            Console.WriteLine($"Valid video title: {videoTitle}");
            ListViewItem item1 = new ListViewItem(videoTitle);
            item1.SubItems.Add("Downloading");
            listView_downloads.Items.Add(item1);
            listView_downloads.EndUpdate();
            return item1;
        }

        public void NotifyDownloadFinished(ListViewItem listItem, string videoPath)
        {
            listView_downloads.BeginUpdate();
            listItem.SubItems[1].Text = "Done";
            listItem.SubItems.Add(videoPath);
            listView_downloads.EndUpdate();
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            while (progressBarDownload.Value < progressBarDownload.Maximum * 0.95)
            {
                Thread.Sleep(1000);
                backgroundWorker1.ReportProgress(0, null);
            }
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBarDownload.PerformStep();
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Console.WriteLine("[checkForUpdatesToolStripMenuItem_Click] Application Product Version: " + 
                System.Windows.Forms.Application.ProductVersion);
            RobinUpdater.InstallUpdateSyncWithInfo();
        }
    }
}
