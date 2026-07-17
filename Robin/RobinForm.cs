using System;
using System.Windows.Forms;
using System.Deployment.Application;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Robin
{
    public partial class RobinForm : Form, IDownloadUiNotifier
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static string baseFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        private static int videoListViewItemTitle = 0;
        private static int videoListViewItemDownloadStatus = 1;
        private static int videoListViewItemDownloadedLocation = 2;

        YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);
        private ConcurrentDictionary<string, DownloadState> activeDownloads = new ConcurrentDictionary<string, DownloadState>();

        public RobinForm()
        {
            InitializeComponent();
            DisplayAppVersion();
            SetDownloadListClickHandler();
            this.FormClosing += RobinForm_FormClosing;
        }

        private void RobinForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var kvp in activeDownloads.ToArray())
            {
                try
                {
                    kvp.Value.CancellationTokenSource.Cancel();
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Error($"Error cancelling download on form close: {ex.Message}");
                }
            }
            activeDownloads.Clear();
        }

        private void DisplayAppVersion()
        {
            bool isNetworkDeployed = false;

            try
            {
                isNetworkDeployed = ApplicationDeployment.IsNetworkDeployed;
            }
            catch (Exception ex)
            {
                logger.Warn("ApplicationDeployment threw exception; application may not be network deployed. {0}", ex.Message);
            }

            if (isNetworkDeployed)
            {
                string robinVersion = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
                label_appVersion.Text = robinVersion;
                logger.Info("Robin version: {0}", robinVersion);
            }
            else
            {
                label_appVersion.Text = "NON-NETWORK DEPLOYED";
                logger.Info("This instance of Robin is not network deployed.");
            }
        }

        private void SetDownloadListClickHandler()
        {
            listView_downloads.ItemActivate += (s, e) =>
            {
                var selectedItems = listView_downloads.SelectedItems;
                if (selectedItems.Count == 1)
                {
                    ListViewItem selected = selectedItems[0];
                    if (selected.SubItems.Count > 2)
                    {
                        string videoLocation = selected.SubItems[videoListViewItemDownloadedLocation].Text;
                        OpenVideo(videoLocation);
                    }
                }
            };
        }

        private void btn_download_Click(object sender, EventArgs e)
        {
            RobinDownloadVideoWithChecks(textBox_videoURL.Text);
        }

        private void textBox_videoURL_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                RobinDownloadVideoWithChecks(textBox_videoURL.Text);
            }
        }

        private void RobinDownloadVideoWithChecks(string videoUrl)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string videoTitle = await videoDownloader.GetVideoTitle(videoUrl);
                    logger.Info("Video title is: {0}", videoTitle);
                    this.Invoke((Action)(() => RobinDownloadVideoWithChecks_OnUIThread(videoTitle, videoUrl)));
                }
                catch (Exception ex)
                {
                    RobinUtils.DisplayAndLogException(ex);
                }
            });
        }

        private void RobinDownloadVideoWithChecks_OnUIThread(string videoTitle, string videoUrl)
        {
            ListViewItem videoListViewItem = listView_downloads.FindItemWithText(videoTitle);
            if (videoListViewItem != null)
            {
                logger.Info("Found video list item with same video URL: {0}", videoUrl);

                if (videoListViewItem.SubItems[videoListViewItemDownloadStatus].Text == RobinVideoStatus.Done)
                {
                    logger.Info("Found existing, already downloaded video with same URL {0} in downloads list", videoUrl);
                    DialogResult result = MessageBox.Show("The video with URL: " + videoUrl + "  has already been downloaded, " +
                                                          "would you like to download it again? This will overwrite the existing file.",
                                                          "Video Already Downloaded",
                                                          MessageBoxButtons.YesNo,
                                                          MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        RemoveVideoItemFromDownloadsList(videoListViewItem);
                        DownloadVideo(videoUrl);
                    }
                    else
                    {
                        ClearVideoUrlTextbox();
                    }
                }
                else if (videoListViewItem.SubItems[videoListViewItemDownloadStatus].Text == RobinVideoStatus.Dowloading)
                {
                    logger.Info("Found existing currently downloading video with same URL {0} in downloads list", videoUrl);
                    DialogResult result = MessageBox.Show("The video with URL: " + videoUrl + "  already has a download " +
                                                          "in progress, would you like to cancel the download and restart it?",
                                                          "Video Already Downloaded",
                                                          MessageBoxButtons.YesNo,
                                                          MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        CancelDownload(videoTitle);
                        RemoveVideoItemFromDownloadsList(videoListViewItem);
                        DownloadVideo(videoUrl);
                    }
                    else
                    {
                        ClearVideoUrlTextbox();
                    }
                }
            }
            else
            {
                DownloadVideo(videoUrl);
            }
        }

        private void DownloadVideo(string videoUrl)
        {
            DownloadState downloadState = new DownloadState();
            downloadState.VideoUrl = videoUrl;

            _ = Task.Run(async () =>
            {
                try
                {
                    await videoDownloader.DownloadVideo(this, videoUrl, downloadState);
                }
                catch (OperationCanceledException)
                {
                    logger.Info($"Download was cancelled for URL: {videoUrl}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Unhandled error during download of {videoUrl}: {ex.Message}");
                    RobinUtils.DisplayAndLogException(ex);
                }
            });
        }

        public void RegisterActiveDownload(string videoTitle, DownloadState downloadState)
        {
            if (activeDownloads.TryAdd(videoTitle, downloadState))
            {
                downloadState.VideoTitle = videoTitle;
                logger.Info($"Registered active download for: {videoTitle}");
            }
        }

        public void ClearVideoUrlTextbox()
        {
            if (textBox_videoURL.InvokeRequired)
            {
                Action threadsafeCall = delegate { ClearVideoUrlTextbox(); };
                this.Invoke(threadsafeCall);
            }
            else
            {
                textBox_videoURL.Clear();
            }
        }

        public void SetCursorLoading()
        {
            if (this.InvokeRequired)
            {
                Action threadsafeCall = delegate { SetCursorLoading(); };
                this.Invoke(threadsafeCall);
            }
            else
            {
                Cursor = Cursors.WaitCursor;
            }
        }

        public void SetCursorNormal()
        {
            if (this.InvokeRequired)
            {
                Action threadsafeCall = delegate { SetCursorNormal(); };
                this.Invoke(threadsafeCall);
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }

        public void SetVideoInfo(RobinVideoInfo info)
        {
            if (label_videoTitle.InvokeRequired)
            {
                Action threadsafeCall = delegate { SetVideoInfo(info); };
                label_videoTitle.Invoke(threadsafeCall);
            }
            else
            {
                label_videoTitle.Text = info.Title;
                label_videoExtension.Text = info.Extension;
                label_videoResolution.Text = info.Resolution;
                label_maxBitrate.Text = info.Bitrate;
                label_size.Text = info.Size;
            }
        }

        private ProgressBar GetProgressBarForVideo(string videoName)
        {
            return listView_downloads.Controls.OfType<ProgressBar>().FirstOrDefault(i => i.Name == videoName);
        }

        public void AddVideoToDownloadsList(DownloadState state, int videoSize)
        {
            logger.Info($"Valid video title: {state.VideoTitle}");

            if (listView_downloads.InvokeRequired)
            {
                Action threadsafeCall = delegate { AddVideoItemToDownloadsList(state, videoSize); };
                listView_downloads.Invoke(threadsafeCall);
            }
            else
            {
                AddVideoItemToDownloadsList(state, videoSize);
            }
        }

        private void AddVideoItemToDownloadsList(DownloadState state, int videoSize)
        {
            listView_downloads.BeginUpdate();

            ListViewItem videoItem = new ListViewItem(state.VideoTitle);
            videoItem.SubItems.Add(RobinVideoStatus.Dowloading);
            videoItem.SubItems.Add("Download path will appear here");
            videoItem.SubItems.Add("");
            videoItem.SubItems.Add("");
            listView_downloads.Items.Add(videoItem);
            state.ListViewItem = videoItem;

            state.ProgressBar = CreateProgressBar(videoItem.SubItems[3].Bounds, state.VideoTitle, videoSize);
            listView_downloads.Controls.Add(state.ProgressBar);

            AddCancelButton(videoItem.SubItems[4].Bounds, state.VideoTitle);

            listView_downloads.EndUpdate();
        }

        private ProgressBar CreateProgressBar(Rectangle bounds, string videoTitle, int videoSize)
        {
            ProgressBar progressBar = new ProgressBar();
            progressBar.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            progressBar.Minimum = 0;
            progressBar.Maximum = Math.Max(videoSize, 1);
            progressBar.Value = 1;
            progressBar.Step = 1;
            progressBar.Name = videoTitle;
            progressBar.Visible = true;
            return progressBar;
        }

        private void AddCancelButton(Rectangle bounds, string videoTitle)
        {
            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            cancelButton.Name = "cancel_" + videoTitle;
            cancelButton.Visible = true;
            cancelButton.ForeColor = Color.White;
            cancelButton.BackColor = Color.Red;
            cancelButton.Font = new Font(cancelButton.Font, FontStyle.Bold);
            cancelButton.Click += (s, e) =>
            {
                DialogResult result = MessageBox.Show(
                    $"Are you sure you want to cancel the download of '{videoTitle}'?",
                    "Confirm Cancellation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    CancelDownload(videoTitle);
                }
            };

            listView_downloads.Controls.Add(cancelButton);
        }

        private Button GetCancelButton(string videoTitle)
        {
            return listView_downloads.Controls.OfType<Button>()
                .FirstOrDefault(b => b.Name == "cancel_" + videoTitle);
        }

        private void RemoveVideoItemFromDownloadsList(ListViewItem videoListViewItem)
        {
            if (listView_downloads.InvokeRequired)
            {
                Action threadsafeCall = delegate { RemoveVideoItemFromDownloadsList(videoListViewItem); };
                listView_downloads.Invoke(threadsafeCall);
            }
            else
            {
                listView_downloads.BeginUpdate();

                string videoTitle = videoListViewItem.SubItems[videoListViewItemTitle].Text;

                var progressBar = GetProgressBarForVideo(videoTitle);
                if (progressBar != null)
                {
                    listView_downloads.Controls.Remove(progressBar);
                    progressBar.Dispose();
                }

                var cancelButton = GetCancelButton(videoTitle);
                if (cancelButton != null)
                {
                    listView_downloads.Controls.Remove(cancelButton);
                    cancelButton.Dispose();
                }

                listView_downloads.Items.Remove(videoListViewItem);
                listView_downloads.EndUpdate();
            }
        }

        public void NotifyDownloadFinished(DownloadState state)
        {
            if (listView_downloads.InvokeRequired)
            {
                Action threadsafeCall = delegate { NotifyDownloadFinished(state); };
                listView_downloads.Invoke(threadsafeCall);
            }
            else
            {
                listView_downloads.BeginUpdate();

                ListViewItem listItem = state.ListViewItem;
                listItem.SubItems[videoListViewItemTitle].BackColor = SystemColors.Highlight;
                listItem.SubItems[videoListViewItemTitle].ForeColor = SystemColors.HighlightText;
                listItem.SubItems[videoListViewItemDownloadStatus].Text = RobinVideoStatus.Done;
                listItem.SubItems[videoListViewItemDownloadedLocation].Text = state.FilePath;

                ProgressBar bar = state.ProgressBar;
                if (bar != null && !bar.IsDisposed)
                {
                    bar.Value = bar.Maximum;
                }

                HideCancelButton(state.VideoTitle);

                if (activeDownloads.TryRemove(state.VideoTitle, out DownloadState removed))
                {
                    removed.IsCompleted = true;
                    removed.Dispose();
                }

                listView_downloads.EndUpdate();
            }
        }

        private void CancelDownload(string videoTitle)
        {
            if (activeDownloads.TryRemove(videoTitle, out DownloadState state))
            {
                try
                {
                    logger.Info($"Cancelling download for: {videoTitle}");
                    state.CancellationTokenSource.Cancel();
                    state.IsCancelled = true;

                    ListViewItem videoItem = listView_downloads.FindItemWithText(videoTitle);
                    if (videoItem != null)
                    {
                        UpdateDownloadStatus(videoItem, RobinVideoStatus.Cancelled);
                        DisableCancelButton(videoTitle);
                        CancelProgressBarForVideo(state);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error cancelling download for {videoTitle}: {ex.Message}");
                }
                finally
                {
                    state.Dispose();
                }
            }
            else
            {
                logger.Warn($"Attempted to cancel download for '{videoTitle}' but it was not found in active downloads");
            }
        }

        public void DisableCancelButton(string videoTitle)
        {
            Button cancelButton = GetCancelButton(videoTitle);
            if (cancelButton != null)
            {
                SafeDisableCancelButton(cancelButton);
            }
        }

        private void SafeDisableCancelButton(Button cancelButton)
        {
            if (cancelButton.InvokeRequired)
            {
                Action threadsafeCall = delegate { SafeDisableCancelButton(cancelButton); };
                cancelButton.Invoke(threadsafeCall);
            }
            else
            {
                cancelButton.Enabled = false;
                cancelButton.BackColor = Color.LightGray;
            }
        }

        public void CancelProgressBarForVideo(DownloadState state)
        {
            logger.Info($"Cancelling progress bar for video: {state.VideoTitle}");
            ProgressBar progressBar = state.ProgressBar;
            if (progressBar != null && !progressBar.IsDisposed)
            {
                SafeCancelProgressBar(progressBar);
            }
        }

        private void SafeCancelProgressBar(ProgressBar progressBar)
        {
            if (progressBar.InvokeRequired)
            {
                Action threadsafeCall = delegate { SafeCancelProgressBar(progressBar); };
                progressBar.Invoke(threadsafeCall);
            }
            else
            {
                progressBar.ForeColor = Color.LightGray;
                progressBar.BackColor = Color.LightGray;
                progressBar.SetState(ColorBar.Color.Red, progressBar.Value);
            }
        }

        public void ReportDownloadProgress(DownloadState state, int progressValue)
        {
            ProgressBar bar = state.ProgressBar;
            if (bar == null || bar.IsDisposed || !bar.IsHandleCreated) return;

            bar.BeginInvoke((Action)(() =>
            {
                if (!bar.IsDisposed) bar.Value = Math.Min(Math.Max(progressValue, bar.Minimum), bar.Maximum);
            }));
        }

        public string GetVideoTitleFromListItem(ListViewItem item)
        {
            return item.SubItems[videoListViewItemTitle].Text;
        }

        public void UpdateDownloadStatus(ListViewItem item, string status)
        {
            if (listView_downloads.InvokeRequired)
            {
                Action threadsafeCall = delegate { UpdateDownloadStatus(item, status); };
                listView_downloads.Invoke(threadsafeCall);
            }
            else
            {
                item.SubItems[videoListViewItemDownloadStatus].Text = status;
                if (status == RobinVideoStatus.Cancelled)
                {
                    item.SubItems[videoListViewItemTitle].BackColor = Color.LightGray;
                    item.SubItems[videoListViewItemTitle].ForeColor = Color.DarkGray;
                }
                else if (status == RobinVideoStatus.Failed)
                {
                    item.SubItems[videoListViewItemTitle].BackColor = Color.LightPink;
                    item.SubItems[videoListViewItemTitle].ForeColor = Color.DarkRed;
                }
            }
        }

        public void CleanupDownload(string videoTitle)
        {
            if (activeDownloads.TryRemove(videoTitle, out DownloadState state))
            {
                state.Dispose();

                if (listView_downloads.InvokeRequired)
                {
                    Action threadsafeCall = delegate { HideCancelButton(videoTitle); };
                    listView_downloads.Invoke(threadsafeCall);
                }
                else
                {
                    HideCancelButton(videoTitle);
                }
            }
        }

        private void HideCancelButton(string videoTitle)
        {
            Button cancelButton = GetCancelButton(videoTitle);
            if (cancelButton != null)
            {
                cancelButton.Enabled = false;
                cancelButton.Visible = false;
            }
        }

        private void OpenVideo(string videoPath)
        {
            logger.Info("Opening video at path: {0}", videoPath);
            System.Diagnostics.Process.Start(videoPath);
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            logger.Info("[checkForUpdatesToolStripMenuItem_Click] Application Product Version: {0}",
                Application.ProductVersion);
            RobinUpdater.InstallUpdateSyncWithInfo();
        }
    }
}
