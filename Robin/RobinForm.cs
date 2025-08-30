using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Threading;
using System.Deployment.Application;
using System.Drawing;
using System.Linq;
using FFMpegCore.Enums;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Net.Mime.MediaTypeNames;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Robin
{
    public partial class RobinForm : Form
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public static string baseFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        private static int videoListViewItemTitle = 0;
        private static int videoListViewItemDownloadStatus = 1;
        private static int videoListViewItemDownloadedLocation = 2;

        private static string videoStatusDowloading = "Downloading";
        private static string videoStatusDone = "Done";
        private static string videoStatusCancelled = "Cancelled";
        private static string videoStatusFailed = "Failed";

        YouTubeVideoDownloader videoDownloader = new YouTubeExplodeVideoDownloader(baseFilePath);
        private Dictionary<string, DownloadState> activeDownloads = new Dictionary<string, DownloadState>();

        public RobinForm()
        {
            InitializeComponent();
            DisplayAppVersion();
            SetDownloadListClickHandler();
            this.FormClosing += RobinForm_FormClosing;
        }

        private void RobinForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Cancel all active downloads and clean up resources
            foreach (var kvp in activeDownloads.ToList())
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
                string videoTitle = await videoDownloader.GetVideoTitle(videoUrl);
                logger.Info("Video title is: {0}", videoTitle);
                RobinDownloadVideoWithChecks(videoTitle, videoUrl);
            });
        }

        private void RobinDownloadVideoWithChecks(string videoTitle, string videoUrl)
        {
            // TODO: If video is currently downloading, ask user if should cancel already downloading instance
            //       and start the download over.
            //       If video is present in DL but has already finished downloading, ask user if should overwrite
            //       existing dowload and download again.

            ListViewItem videoListViewItem = listView_downloads.FindItemWithText(videoTitle);
            if (videoListViewItem != null)
            {
                logger.Info("Found video list item with same video URL: {0}", videoUrl);

                if (videoListViewItem.SubItems[videoListViewItemDownloadStatus].Text == videoStatusDone)
                {
                    logger.Info("Found existing, already downloaded video with same URL {0} in downloads list", videoUrl);
                    DialogResult result = MessageBox.Show("The video with URL: " + videoUrl + "  has already been downloaded, " +
                                                          "would you like to download it again? This will overwrite the existing file.",
                                                          "Video Already Downloaded",
                                                          MessageBoxButtons.YesNo,
                                                          MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        // Re-download file after removing file from Downloads List:
                        RemoveVideoItemFromDownloadsList(videoListViewItem);
                        DownloadVideo(videoUrl);
                    }
                    else
                    {
                        // Do nothing
                        ClearVideoUrlTextbox();
                    }
                } else if (videoListViewItem.SubItems[videoListViewItemDownloadStatus].Text == videoStatusDowloading)
                {
                    logger.Info("Found existing currently downloading video with same URL {0} in downloads list", videoUrl);
                    DialogResult result = MessageBox.Show("The video with URL: " + videoUrl + "  already has a download " +
                                                          "in progress, would you like to cancel the download and restart it?",
                                                          "Video Already Downloaded",
                                                          MessageBoxButtons.YesNo,
                                                          MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        // Cancel the existing download
                        CancelDownload(videoTitle);
                        
                        // Remove the item from the list
                        RemoveVideoItemFromDownloadsList(videoListViewItem);
                        
                        // Start a new download
                        DownloadVideo(videoUrl);
                    }
                    else
                    {
                        // Do nothing
                        ClearVideoUrlTextbox();
                    }
                }
            } else
            {
                DownloadVideo(videoUrl);
            }
        }

        private void DownloadVideo(string videoUrl)
        {
            // Create a new download state for tracking
            DownloadState downloadState = new DownloadState();
            downloadState.VideoUrl = videoUrl;
            
            // We'll set the video title after we get it from the downloader
            // For now, start the download with cancellation support
            videoDownloader.DownloadVideo(this, videoUrl, downloadState.CancellationTokenSource.Token);
        }

        public void RegisterActiveDownload(string videoTitle, DownloadState downloadState)
        {
            if (!activeDownloads.ContainsKey(videoTitle))
            {
                downloadState.VideoTitle = videoTitle;
                activeDownloads[videoTitle] = downloadState;
                logger.Info($"Registered active download for: {videoTitle}");
            }
        }

        // Method moved to line 346 (CancelDownload is now implemented above)

        public void ClearVideoUrlTextbox()
        {
            if (textBox_videoURL.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { ClearVideoUrlTextbox(); };
                this.Invoke(threadsafeCall);
            } else
            {
                textBox_videoURL.Clear();
            }
        }

        public void SetCursorLoading()
        {
            if (this.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { SetCursorLoading(); };
                this.Invoke(threadsafeCall);
            } else
            {
                Cursor = Cursors.WaitCursor;
            }
        }

        public void SetCursorNormal()
        {
            if (this.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
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
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { SetVideoInfo(info); };
                label_videoTitle.Invoke(threadsafeCall);
            } else
            {
                label_videoTitle.Text = info.Title;
                label_videoExtension.Text = info.Extension;
                label_videoResolution.Text = info.Resolution;
                label_maxBitrate.Text = info.Bitrate;
                label_size.Text = info.Size;
            }
        }

        private System.Windows.Forms.ProgressBar GetProgressBarForVideo(String videoName)
        {
            return listView_downloads.Controls.OfType<System.Windows.Forms.ProgressBar>().FirstOrDefault(i => i.Name == videoName);
        }

        public void SetProgressBarValue(ListViewItem item, int value)
        {
            String videoName = item.SubItems[0].Text;
            System.Windows.Forms.ProgressBar progressBar = GetProgressBarForVideo(videoName);

            if (progressBar != null)
            {
                SafeSetProgressBarValue(progressBar, value);
            }
        }

        private void SafeSetProgressBarValue(System.Windows.Forms.ProgressBar progressBar, int value)
        {
            if (progressBar.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { SafeSetProgressBarValue(progressBar, value); };
                progressBar.Invoke(threadsafeCall);
            } else
            {
                progressBar.Value = value;
            }
        }

        public ListViewItem AddVideoToDownloadsList(string videoTitle, int videoSize)
        {
            logger.Info($"Valid video title: {videoTitle}");
            ListViewItem videoItem = new ListViewItem(videoTitle);
            videoItem.SubItems.Add(videoStatusDowloading);
            videoItem.SubItems.Add("Download path will appear here");
            videoItem.SubItems.Add("");
            videoItem.SubItems.Add(""); // Column for cancel button

            if (listView_downloads.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { AddVideoItemToDownloadsList(videoItem, videoTitle, videoSize); };
                listView_downloads.Invoke(threadsafeCall);
            } else
            {
                AddVideoItemToDownloadsList(videoItem, videoTitle, videoSize);
            }

            return videoItem;
        }

        private void AddVideoItemToDownloadsList(ListViewItem videoItem, string videoTitle, int videoSize)
        {
            listView_downloads.BeginUpdate();

            listView_downloads.Items.Add(videoItem);

            Rectangle progressBarBounds = videoItem.SubItems[3].Bounds;
            AddProgressBar(progressBarBounds, videoTitle, videoSize);

            Rectangle cancelButtonBounds = videoItem.SubItems[4].Bounds;
            AddCancelButton(cancelButtonBounds, videoTitle);
            
            listView_downloads.EndUpdate();
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

        private void AddCancelButton(Rectangle bounds, string videoTitle)
        {
            System.Windows.Forms.Button cancelButton = new System.Windows.Forms.Button();
            cancelButton.Text = "Cancel";
            cancelButton.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            cancelButton.Name = "cancel_" + videoTitle;
            cancelButton.Visible = true;
            cancelButton.ForeColor = System.Drawing.Color.White;
            cancelButton.BackColor = System.Drawing.Color.Red;
            cancelButton.Font = new System.Drawing.Font(cancelButton.Font, System.Drawing.FontStyle.Bold);
            cancelButton.Click += (s, e) => 
            {
                // Confirm cancellation for user safety
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

        private System.Windows.Forms.Button GetCancelButton(string videoTitle)
        {
            return listView_downloads.Controls.OfType<System.Windows.Forms.Button>()
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
                
                // Remove associated controls (progress bar and cancel button)
                string videoTitle = videoListViewItem.SubItems[videoListViewItemTitle].Text;
                
                // Remove progress bar
                var progressBar = GetProgressBarForVideo(videoTitle);
                if (progressBar != null)
                {
                    listView_downloads.Controls.Remove(progressBar);
                    progressBar.Dispose();
                }
                
                // Remove cancel button
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

        public void NotifyDownloadFinished(ListViewItem listItem, string videoPath, int videoSize)
        {
            if (listView_downloads.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { NotifyDownloadFinished(listItem, videoPath, videoSize); };
                listView_downloads.Invoke(threadsafeCall);
            } else
            {
                listView_downloads.BeginUpdate();
                listItem.SubItems[videoListViewItemTitle].BackColor = SystemColors.Highlight;
                listItem.SubItems[videoListViewItemTitle].ForeColor = SystemColors.HighlightText;
                listItem.SubItems[videoListViewItemDownloadStatus].Text = videoStatusDone;
                listItem.SubItems[videoListViewItemDownloadedLocation].Text = videoPath;

                SetProgressBarValue(listItem, videoSize);
                
                // Hide cancel button when download completes
                string videoTitle = listItem.SubItems[videoListViewItemTitle].Text;
                HideCancelButton(videoTitle);

                // Remove from active downloads if exists
                if (activeDownloads.TryGetValue(videoTitle, out DownloadState state))
                {
                    state.IsCompleted = true;
                    activeDownloads.Remove(videoTitle);
                    state.Dispose();
                }
                
                listView_downloads.EndUpdate();
            }
        }

        private void CancelDownload(string videoTitle)
        {
            if (activeDownloads.TryGetValue(videoTitle, out DownloadState state))
            {
                try
                {
                    logger.Info($"Cancelling download for: {videoTitle}");
                    state.CancellationTokenSource.Cancel();
                    state.IsCancelled = true;
                    
                    // Update UI to show cancelled status
                    ListViewItem videoItem = listView_downloads.FindItemWithText(videoTitle);
                    if (videoItem != null)
                    {
                        UpdateDownloadStatus(videoItem, videoStatusCancelled);
                        DisableCancelButton(videoTitle);
                        CancelProgressBarForVideo(videoTitle);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error cancelling download for {videoTitle}: {ex.Message}");
                }
                finally
                {
                    // Always clean up from active downloads
                    activeDownloads.Remove(videoTitle);
                    state?.Dispose();
                }
            }
            else
            {
                logger.Warn($"Attempted to cancel download for '{videoTitle}' but it was not found in active downloads");
            }
        }

        public void DisableCancelButton(string videoTitle)
        {
            // Disable cancel button
            System.Windows.Forms.Button cancelButton = GetCancelButton(videoTitle);
            if (cancelButton != null)
            {
                SafeDisableCancelButton(cancelButton);
            }
        }

        private void SafeDisableCancelButton(System.Windows.Forms.Button cancelButton)
        {
            if (cancelButton.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { SafeDisableCancelButton(cancelButton); };
                cancelButton.Invoke(threadsafeCall);
            }
            else
            {
                cancelButton.Enabled = false;
                cancelButton.BackColor = System.Drawing.Color.LightGray;
            }
        }

        public void CancelProgressBarForVideo(String videoName)
        {
            logger.Info($"Cancelling progress bar for video: {videoName}");
            System.Windows.Forms.ProgressBar progressBar = GetProgressBarForVideo(videoName);
            if (progressBar != null)
            {
                logger.Info($"Found progress bar for video: {videoName}, cancelling it.");
                SafeCancelProgressBar(progressBar);
            }
        }

        private void SafeCancelProgressBar(System.Windows.Forms.ProgressBar progressBar)
        {
            if (progressBar.InvokeRequired)
            {
                // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-make-thread-safe-calls?view=netdesktop-9.0
                Action threadsafeCall = delegate { SafeCancelProgressBar(progressBar); };
                progressBar.Invoke(threadsafeCall);
            }
            else
            {
                progressBar.ForeColor = System.Drawing.Color.LightGray;
                progressBar.BackColor = System.Drawing.Color.LightGray;
                progressBar.SetState(ColorBar.Color.Red, progressBar.Value);
            }
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
                if (status == videoStatusCancelled)
                {
                    item.SubItems[videoListViewItemTitle].BackColor = System.Drawing.Color.LightGray;
                    item.SubItems[videoListViewItemTitle].ForeColor = System.Drawing.Color.DarkGray;
                }
                else if (status == videoStatusFailed)
                {
                    item.SubItems[videoListViewItemTitle].BackColor = System.Drawing.Color.LightPink;
                    item.SubItems[videoListViewItemTitle].ForeColor = System.Drawing.Color.DarkRed;
                }
            }
        }
        
        public void CleanupDownload(string videoTitle)
        {
            if (activeDownloads.TryGetValue(videoTitle, out DownloadState state))
            {
                activeDownloads.Remove(videoTitle);
                state?.Dispose();
                
                // Hide cancel button
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
            System.Windows.Forms.Button cancelButton = GetCancelButton(videoTitle);
            if (cancelButton != null)
            {
                cancelButton.Enabled = false;
                cancelButton.Visible = false;
            }
        }

        private void OpenVideo(string videoPath)
        {
            System.Diagnostics.Process.Start(videoPath);
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            logger.Info("[checkForUpdatesToolStripMenuItem_Click] Application Product Version: {0}", 
                System.Windows.Forms.Application.ProductVersion);
            RobinUpdater.InstallUpdateSyncWithInfo();
        }
    }
}
