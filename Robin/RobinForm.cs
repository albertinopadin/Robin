using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Threading;
using System.Deployment.Application;
using System.Drawing;
using System.Linq;
using FFMpegCore.Enums;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Robin
{
    public partial class RobinForm : Form
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static string baseFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);

        public RobinForm()
        {
            InitializeComponent();

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                string robinVersion = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
                label_appVersion.Text = robinVersion;
                logger.Info("Robin version: {0}", robinVersion);
            } else
            {
                label_appVersion.Text = "NON-NETWORK DEPLOYED";
                logger.Info("This instance of Robin is not network deployed.");
            }

            listView_downloads.ItemActivate += (s, e) =>
            {
                System.Windows.Forms.ListView.SelectedListViewItemCollection selectedItems = listView_downloads.SelectedItems;
                if (selectedItems.Count > 0)
                {
                    if (selectedItems.Count == 1)
                    {
                        ListViewItem selected = selectedItems[0];
                        if (selected.SubItems.Count > 2)
                        {
                            string videoLocation = selected.SubItems[2].Text;
                            OpenVideo(videoLocation);
                        }
                    }
                }
            };
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

        public void SetProgressBarValue(ListViewItem item, int value)
        {
            String videoName = item.SubItems[0].Text;
            System.Windows.Forms.ProgressBar progressBar =
                listView_downloads.Controls.OfType<System.Windows.Forms.ProgressBar>().FirstOrDefault(i => i.Name == videoName);
            if (progressBar != null)
            {
                progressBar.Value = value;
            }
        }

        public ListViewItem AddVideoToDownloadsList(string videoTitle, int videoSize)
        {
            listView_downloads.BeginUpdate();
            logger.Info($"Valid video title: {videoTitle}");
            ListViewItem videoItem = new ListViewItem(videoTitle);
            videoItem.SubItems.Add("Downloading");
            videoItem.SubItems.Add("Download path will appear here");
            videoItem.SubItems.Add("");
            listView_downloads.Items.Add(videoItem);

            Rectangle progressBarBounds = videoItem.SubItems[3].Bounds;
            AddProgressBar(progressBarBounds, videoTitle, videoSize);

            // TODO: Add cancel button

            listView_downloads.EndUpdate();
            return videoItem;
        }

        private void AddProgressBar(Rectangle bounds, string videoTitle, int videoSize)
        {
            System.Windows.Forms.ProgressBar progressBar = new System.Windows.Forms.ProgressBar();
            progressBar.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            logger.Info($"[AddProgressBar] Progress bar bounds: {bounds.X}, {bounds.Y}, {bounds.Width}, {bounds.Height}");
            progressBar.Minimum = 0;
            progressBar.Maximum = videoSize;
            progressBar.Value = 1;
            progressBar.Step = 1;
            progressBar.Name = videoTitle;
            progressBar.Visible = true;

            listView_downloads.Controls.Add(progressBar);
        }

        public void NotifyDownloadFinished(ListViewItem listItem, string videoPath, int videoSize)
        {
            listView_downloads.BeginUpdate();
            listItem.SubItems[1].Text = "Done";
            listItem.SubItems[2].Text = videoPath;

            SetProgressBarValue(listItem, videoSize);

            listView_downloads.EndUpdate();
        }

        private void OpenVideo(string videoPath)
        {
            System.Diagnostics.Process.Start(videoPath);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            //while (progressBarDownload.Value < progressBarDownload.Maximum * 0.95)
            //{
            //    Thread.Sleep(1000);
            //    backgroundWorker1.ReportProgress(0, null);
            //}
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //progressBarDownload.PerformStep();
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            logger.Info("[checkForUpdatesToolStripMenuItem_Click] Application Product Version: {0}", 
                System.Windows.Forms.Application.ProductVersion);
            RobinUpdater.InstallUpdateSyncWithInfo();
        }
    }
}
