using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Net.Http;
using FFMpegCore;
using VideoLibrary;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Converter;
using System.Threading;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Reflection;
using System.Deployment.Application;


namespace Robin
{
    public partial class RobinForm : Form
    {
        string baseFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        YoutubeClient youtube = new YoutubeClient();

        public RobinForm()
        {
            InitializeComponent();

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                label_appVersion.Text = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            }
        }

        private async void btn_download_Click(object sender, EventArgs e)
        {
            //await downloadBestVideo_libVideo(textBox_videoURL.Text);

            await downloadBestVideo_Explode(textBox_videoURL.Text);
        }

        private async Task downloadBestVideo_Explode(string videoUrl)
        {
            Cursor = Cursors.WaitCursor;
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

            var videoStreams = streamManifest.GetVideoStreams();
            Console.WriteLine($"streamManifest video streams count: {videoStreams.Count()}");

            if (videoStreams.Count() > 0)
            {
                var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
                Console.WriteLine($"maxVideoQualityStreamInfo: {maxVideoQualityStreamInfo}");

                var videoInfo = await youtube.Videos.GetAsync(videoUrl);

                label_videoTitle.Text = videoInfo.Title;
                label_videoExtension.Text = maxVideoQualityStreamInfo.Container.Name;
                label_videoResolution.Text = maxVideoQualityStreamInfo.VideoResolution.ToString();
                label_maxBitrate.Text = maxVideoQualityStreamInfo.Bitrate.ToString();
                label_size.Text = maxVideoQualityStreamInfo.Size.MegaBytes.ToString();

                progressBarDownload.Maximum = (int)maxVideoQualityStreamInfo.Size.MegaBytes;
                progressBarDownload.Step = 1;

                // TODO: figure out how to make this work:
                //if (!backgroundWorker1.IsBusy)
                //{
                //    backgroundWorker1.RunWorkerAsync();
                //}

                await downloadVideo_Explode(youtube, videoUrl, videoInfo, maxVideoQualityStreamInfo.Container.Name);

                progressBarDownload.Value = (int)maxVideoQualityStreamInfo.Size.MegaBytes;
                Cursor = Cursors.Arrow;
            } else
            {
                MessageBox.Show($"No video streams found for URL {videoUrl}.");
            }
        }

        private async Task downloadVideo_Explode(YoutubeClient youtube, 
                                                 string videoUrl, 
                                                 YoutubeExplode.Videos.Video videoInfo,
                                                 string extension)
        {
            Console.WriteLine("[Explode] Download Started");
            listView_downloads.BeginUpdate();
            string validVideoTitle = makeValidVideoTitle(videoInfo.Title);
            Console.WriteLine($"Valid video title: {validVideoTitle}");
            ListViewItem item1 = new ListViewItem(validVideoTitle);
            item1.SubItems.Add("Downloading");
            listView_downloads.Items.Add(item1);
            listView_downloads.EndUpdate();

            string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{extension}");
            await youtube.Videos.DownloadAsync(videoUrl, videoPath);
            Console.WriteLine("[Explode] Download Complete");

            listView_downloads.BeginUpdate();
            item1.SubItems[1].Text = "Done";
            item1.SubItems.Add(videoPath);
            listView_downloads.EndUpdate();
        }

        private string makeValidVideoTitle(string rawVideoTitle)
        {
            Console.WriteLine($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        }

        private async Task downloadBestVideo_libVideo(string videoUrl)
        {
            var yt = new FastYouTube();
            var videos = yt.GetAllVideosAsync(videoUrl).GetAwaiter().GetResult();

            var videosWithAudio = videos.Where(v => v.Resolution > 0 && v.AudioBitrate > 0)
                                        .OrderByDescending(t => t.Resolution)
                                        .ToList();

            var maxResWithAudio = videosWithAudio.First();

            label_videoTitle.Text = maxResWithAudio.Title;
            label_videoExtension.Text = maxResWithAudio.FileExtension;
            label_videoResolution.Text = maxResWithAudio.Resolution.ToString();
            label_maxBitrate.Text = maxResWithAudio.AudioBitrate.ToString();

            foreach (var vi in videosWithAudio)
            {
                Console.WriteLine("Video Info: ");
                Console.WriteLine("Title: " + vi.Title);
                Console.WriteLine("Extension: " + vi.FileExtension);
                Console.WriteLine("Resolution: " + vi.Resolution);
                Console.WriteLine("Bitrate: " + vi.AudioBitrate);
                Console.WriteLine("\n");
            }

            await downloadVideo_libVideo(yt,
                                maxResWithAudio,
                                baseFilePath,
                                new Progress<Tuple<long, long>>((Tuple<long, long> v) =>
                                {
                                    var percent = (int)((v.Item1 * 100) / v.Item2);
                                    progressBarDownload.Value = percent;
                                    progressBarDownload.Update();
                                }));
        }

        private async Task downloadVideo_libVideo(FastYouTube youTube, 
                                         YouTubeVideo video, 
                                         string downloadFolder,
                                         IProgress<Tuple<long, long>> progress)
        {
            Console.WriteLine("[libVideo] Download Started");
            await youTube.CreateDownloadAsync(
                new Uri(video.Uri),
                Path.Combine(downloadFolder, video.FullName),
                progress);
            Console.WriteLine("[libVideo] Download Complete");
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
            InstallUpdateSyncWithInfo();
        }

        private void InstallUpdateSyncWithInfo()
        {
            UpdateCheckInfo info = null;

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                Console.WriteLine("[InstallUpdateSyncWithInfo] App is Network Deployed");

                ApplicationDeployment ad = ApplicationDeployment.CurrentDeployment;

                Console.WriteLine("[InstallUpdateSyncWithInfo] Current version: " + ad.CurrentVersion);

                MessageBox.Show("Application Deployment Current Version: " + ad.CurrentVersion);

                try
                {
                    info = ad.CheckForDetailedUpdate();
                }
                catch (DeploymentDownloadException dde)
                {
                    MessageBox.Show("The new version of the application cannot be downloaded at this time. " +
                        "\n\nPlease check your network connection, or try again later. Error: " + dde.Message);
                    return;
                }
                catch (InvalidDeploymentException ide)
                {
                    MessageBox.Show("Cannot check for a new version of the application. The ClickOnce deployment is corrupt. " +
                        "Please redeploy the application and try again. Error: " + ide.Message);
                    return;
                }
                catch (InvalidOperationException ioe)
                {
                    MessageBox.Show("This application cannot be updated. It is likely not a ClickOnce application. " +
                        "Error: " + ioe.Message);
                    return;
                }

                if (info.UpdateAvailable)
                {
                    Boolean doUpdate = true;

                    if (!info.IsUpdateRequired)
                    {
                        DialogResult dr = MessageBox.Show("An update is available. Would you like to update the application now?",
                            "Update Available", MessageBoxButtons.OKCancel);
                        if (!(DialogResult.OK == dr))
                        {
                            doUpdate = false;
                        }
                    }
                    else
                    {
                        // Display a message that the app MUST reboot. Display the minimum required version.
                        MessageBox.Show("This application has detected a mandatory update from your current " +
                            "version to version " + info.MinimumRequiredVersion.ToString() +
                            ". The application will now install the update and restart.",
                            "Update Available", MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }

                    if (doUpdate)
                    {
                        try
                        {
                            ad.Update();
                            MessageBox.Show("The application has been upgraded, and will now restart.");
                            //Application.Restart();
                            System.Windows.Forms.Application.Restart();
                        }
                        catch (DeploymentDownloadException dde)
                        {
                            MessageBox.Show("Cannot install the latest version of the application. " +
                                "\n\nPlease check your network connection, or try again later. Error: " + dde);
                            return;
                        }
                    } 
                    else
                    {
                        MessageBox.Show("doUpdate is false");
                    }
                }
                else
                {
                    MessageBox.Show("No updates available");
                }
            } 
            else
            {
                Console.WriteLine("[InstallUpdateSyncWithInfo] App is NOT Network Deployed");
                MessageBox.Show("App is NOT Network Deployed");
            }
        }
    }
}
