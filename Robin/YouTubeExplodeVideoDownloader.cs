using AngleSharp.Media;
using NiL.JS.BaseLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VideoLibrary;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace Robin
{
    internal class YouTubeExplodeVideoDownloader : YouTubeVideoDownloader
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        YoutubeClient youtube;
        string baseFilePath;
        string ffmpegPath;

        public YouTubeExplodeVideoDownloader(string baseFilePath)
        {
            this.baseFilePath = baseFilePath;
            this.ffmpegPath = RobinUtils.GetPathToFFMPEG();
            youtube = new YoutubeClient();
        }

        public async void DownloadVideo(RobinForm form, string url, DownloadState state)
        {
            form.SetCursorLoading();
            await DownloadBestVideo(form, url, true, state);
            form.SetCursorNormal();
        }

        public async ValueTask<string> GetVideoTitle(string url)
        {
            var videoInfo = await GetVideoInfo(url);
            return videoInfo.Title;
        }

        public async ValueTask<YoutubeExplode.Videos.Video> GetVideoInfo(string videoUrl)
        {
            return await youtube.Videos.GetAsync(videoUrl);
        }

        private async Task DownloadBestVideo(RobinForm form, 
                                             string videoUrl, 
                                             bool getManifest, 
                                             DownloadState state)
        {
            var videoInfo = await GetVideoInfo(videoUrl);

            // TODO: Can we still download video if the line below fails?
            if (getManifest)
            {
                try
                {
                    lock (state)
                    {
                        state.VideoId = videoInfo.Id;
                        state.VideoUrl = videoUrl;
                        state.VideoTitle = MakeValidVideoTitle(videoInfo.Title);
                    }

                    var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);
                    await DownloadBestVideoWithManifest(form, videoUrl, streamManifest, state);
                }
                catch (Exception ex)
                {
                    logger.Error("Error getting stream manifest...");
                    logger.Error(ex);
                    // Try without getting manifest
                    logger.Info("Trying to download video without first getting stream manifest...");
                    await DownloadBestVideo(form, videoUrl, false, state);
                }
            } else
            {
                lock (state)
                {
                    state.VideoId = videoInfo.Id;
                    state.VideoUrl = videoUrl;
                    state.VideoTitle = "UNKNOWN_TITLE";
                    state.VideoResolution = "UNKNOWN_RESOLUTION";
                    state.Bitrate = "UNKNOWN_BITRATE";
                    state.SizeInMegabytes = 9.999;
                    state.FileExtension = "mp4";
                }

                DownloadVideo_Explode(form, youtube, state);
            }
        }

        private async Task DownloadBestVideoWithManifest(RobinForm form, 
                                                         string videoUrl, 
                                                         StreamManifest streamManifest, 
                                                         DownloadState state)
        {
            try
            {
                var videoStreams = streamManifest.GetVideoStreams();
                logger.Info($"streamManifest video streams count: {videoStreams.Count()}");

                if (videoStreams.Count() > 0)
                {
                    var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
                    logger.Info($"maxVideoQualityStreamInfo: {maxVideoQualityStreamInfo}");

                    lock (state)
                    {
                        state.VideoResolution = maxVideoQualityStreamInfo.VideoResolution.ToString();
                        state.Bitrate = maxVideoQualityStreamInfo.Bitrate.ToString();
                        state.SizeInMegabytes = maxVideoQualityStreamInfo.Size.MegaBytes;
                        state.FileExtension = maxVideoQualityStreamInfo.Container.Name;
                    }

                    _ = Task.Run(() =>
                    {
                        DownloadVideo_Explode(form, youtube, state);
                    });
                }
                else
                {
                    MessageBox.Show($"No video streams found for URL {videoUrl}.");
                }
            }
            catch (Exception ex)
            {
                RobinUtils.DisplayAndLogException(ex);
            }
        }

        private string MakeValidVideoTitle(string rawVideoTitle)
        {
            logger.Info($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        }

        private async void DownloadVideo_Explode(RobinForm form, YoutubeClient youtube, DownloadState state)
        {
            form.SetVideoInfo(new RobinVideoInfo(state.VideoTitle,
                                                 state.FileExtension,
                                                 state.VideoResolution,
                                                 state.Bitrate,
                                                 state.SizeInMegabytes.ToString("n2")));

            _ = Task.Run(async () =>
            {
                string validVideoTitle = MakeValidVideoTitle(state.VideoTitle);
                ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle, (int)state.SizeInMegabytes);
                string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{state.FileExtension}");

                lock (state)
                {
                    state.FilePath = videoPath;
                    state.ListViewItem = listItem;
                }

                // Register this download with the form for cancellation tracking
                if (state.CancellationToken != CancellationToken.None)
                {
                    form.RegisterActiveDownload(validVideoTitle, state);
                }

                try
                {
                    await DownloadVideoAsync_Explode(form, listItem, youtube, state);
                }
                catch (OperationCanceledException)
                {
                    logger.Info($"Download cancelled for video: {validVideoTitle}");
                    UpdateDownloadStatus("Cancelled", validVideoTitle, listItem, form);
                }
                catch (Exception e)
                {
                    logger.Error($"Download failed for video: {validVideoTitle}");
                    RobinUtils.DisplayAndLogException(e);
                    UpdateDownloadStatus("Failed", validVideoTitle, listItem, form);
                }
            });
        }

        private void UpdateDownloadStatus(string status, string videoTitle, ListViewItem listItem, RobinForm form)
        {
            // Update UI to show error state
            form.UpdateDownloadStatus(listItem, status);

            // Clean up from active downloads
            form.CleanupDownload(videoTitle);
        }

        private void CleanupPartialFile(string videoPath)
        {
            // Clean up partial file if it exists
            if (File.Exists(videoPath))
            {
                try
                {
                    File.Delete(videoPath);
                    logger.Info($"Deleted partial file: {videoPath}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to delete partial file {videoPath}: {ex.Message}");
                }
            }
        }

        private async Task DownloadVideoAsync_Explode(RobinForm form,
                                                      ListViewItem listItem,
                                                      YoutubeClient youtube,
                                                      DownloadState state)
        {
            form.ClearVideoUrlTextbox();

            var progress = new Progress<double>(progress =>
            {
                int progressBarValue = (int)(progress * state.SizeInMegabytes);
                if (progressBarValue % 2 == 0)
                {
                    form.SetProgressBarValue(listItem, progressBarValue);
                }
            });

            try
            {
                await youtube.Videos.DownloadAsync(state.VideoId, 
                                                   state.FilePath, 
                                                   converter => converter.SetFFmpegPath(this.ffmpegPath), 
                                                   progress,
                                                   state.CancellationToken);

                form.NotifyDownloadFinished(listItem, state.FilePath, (int)state.SizeInMegabytes);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"Download cancelled for video: {state.VideoId}");
                CleanupPartialFile(state.FilePath);

                // Re-throw to notify caller
                throw;
            }
            catch (Exception e)
            {
                logger.Error($"Download failed for video: {state.VideoId}");
                RobinUtils.DisplayAndLogException(e);
                
                // Update UI to show error state
                form.UpdateDownloadStatus(listItem, "Failed");
                string videoTitle = form.GetVideoTitleFromListItem(listItem);
                form.CancelProgressBarForVideo(videoTitle);
                form.DisableCancelButton(videoTitle);
                CleanupPartialFile(state.FilePath);

                // Re-throw to notify caller
                //throw;
            }
        }
    }
}
