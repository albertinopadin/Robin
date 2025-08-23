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

        public async void DownloadVideo(RobinForm form, string url)
        {
            DownloadVideo(form, url, CancellationToken.None);
        }

        public async void DownloadVideo(RobinForm form, string url, CancellationToken cancellationToken)
        {
            form.SetCursorLoading();
            await DownloadBestVideo(form, url, true, cancellationToken);
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

        private async Task DownloadBestVideo(RobinForm form, string videoUrl, bool getManifest, CancellationToken cancellationToken = default)
        {
            // TODO: Can we still download video if the line below fails?

            if (getManifest)
            {
                try
                {
                    var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);
                    await DownloadBestVideo(form, videoUrl, streamManifest, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.Error("Error getting stream manifest...");
                    logger.Error(ex);
                    // Try without getting manifest
                    logger.Info("Trying to download video without first getting stream manifest...");
                    await DownloadBestVideo(form, videoUrl, false, cancellationToken);
                }
            } else
            {
                DownloadVideo_Explode(form,
                                      youtube,
                                      videoUrl,
                                      "mp4",
                                      "UNKNOWN_RESOLUTION",
                                      "UNKNOWN_BITRATE",
                                      9.999,
                                      cancellationToken);
            }
        }

        private async Task DownloadBestVideo(RobinForm form, string videoUrl, StreamManifest streamManifest, CancellationToken cancellationToken = default)
        {
            try
            {
                var videoStreams = streamManifest.GetVideoStreams();
                logger.Info($"streamManifest video streams count: {videoStreams.Count()}");

                if (videoStreams.Count() > 0)
                {
                    var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
                    logger.Info($"maxVideoQualityStreamInfo: {maxVideoQualityStreamInfo}");

                    //var videoInfo = await youtube.Videos.GetAsync(videoUrl);
                    /*
                    var videoInfo = await GetVideoInfo(videoUrl);

                    form.SetVideoInfo(new RobinVideoInfo(videoInfo.Title,
                                                         maxVideoQualityStreamInfo.Container.Name,
                                                         maxVideoQualityStreamInfo.VideoResolution.ToString(),
                                                         maxVideoQualityStreamInfo.Bitrate.ToString(),
                                                         maxVideoQualityStreamInfo.Size.MegaBytes.ToString("n2")));

                    _ = Task.Run(() =>
                    {
                        DownloadVideo_Explode(form,
                                              youtube,
                                              videoUrl,
                                              videoInfo,
                                              (int)maxVideoQualityStreamInfo.Size.MegaBytes,
                                              maxVideoQualityStreamInfo.Container.Name);
                    });
                    */

                    _ = Task.Run(() =>
                    {
                        DownloadVideo_Explode(form,
                                              youtube,
                                              videoUrl,
                                              maxVideoQualityStreamInfo.Container.Name,
                                              maxVideoQualityStreamInfo.VideoResolution.ToString(),
                                              maxVideoQualityStreamInfo.Bitrate.ToString(),
                                              maxVideoQualityStreamInfo.Size.MegaBytes,
                                              cancellationToken);
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

        private async void DownloadVideo_Explode(RobinForm form,
                                                 YoutubeClient youtube,
                                                 string videoUrl, 
                                                 string fileExtension, 
                                                 string resolution, 
                                                 string bitrate, 
                                                 double fileSize,
                                                 CancellationToken cancellationToken = default)
        {
            var videoInfo = await GetVideoInfo(videoUrl);

            form.SetVideoInfo(new RobinVideoInfo(videoInfo.Title,
                                                 fileExtension,
                                                 resolution,
                                                 bitrate,
                                                 fileSize.ToString("n2")));

            _ = Task.Run(() =>
            {
                DownloadVideo_Explode(form,
                                      youtube,
                                      videoUrl,
                                      videoInfo,
                                      (int)fileSize,
                                      fileExtension,
                                      cancellationToken);
            });
        }

        private string MakeValidVideoTitle(string rawVideoTitle)
        {
            logger.Info($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        }

        private async void DownloadVideo_Explode(RobinForm form,
                                                 YoutubeClient youtube,
                                                 string videoUrl,
                                                 YoutubeExplode.Videos.Video videoInfo,
                                                 int videoSizeInMegabytes,
                                                 string extension,
                                                 CancellationToken cancellationToken = default)
        {
            string validVideoTitle = MakeValidVideoTitle(videoInfo.Title);
            ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle, videoSizeInMegabytes);
            string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{extension}");
            
            // Register this download with the form for cancellation tracking
            if (cancellationToken != CancellationToken.None)
            {
                DownloadState state = new DownloadState();
                state.VideoUrl = videoUrl;
                state.VideoTitle = validVideoTitle;
                state.ListViewItem = listItem;
                state.CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                form.RegisterActiveDownload(validVideoTitle, state);
                cancellationToken = state.CancellationTokenSource.Token;
            }

            try
            {
                await DownloadVideoAsync_Explode(form, 
                                                 listItem, 
                                                 youtube, 
                                                 videoInfo.Id, 
                                                 videoPath, 
                                                 videoSizeInMegabytes,
                                                 cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"Download cancelled for video: {validVideoTitle}");
                form.UpdateDownloadStatus(listItem, "Cancelled");
                
                // Clean up from active downloads
                form.CleanupDownload(validVideoTitle);
            }
            catch (Exception e)
            {
                logger.Error($"Download failed for video: {validVideoTitle}");
                RobinUtils.DisplayAndLogException(e);
                
                // Update UI to show error state
                form.UpdateDownloadStatus(listItem, "Failed");
                
                // Clean up from active downloads
                form.CleanupDownload(validVideoTitle);
            }
        }

        private async Task DownloadVideoAsync_Explode(RobinForm form,
                                                      ListViewItem listItem,
                                                      YoutubeClient youtube,
                                                      string videoId,
                                                      string videoPath,
                                                      int videoSizeInMegabytes,
                                                      CancellationToken cancellationToken = default)
        {
            form.ClearVideoUrlTextbox();

            var progress = new Progress<double>(progress =>
            {
                int progressBarValue = (int)(progress * videoSizeInMegabytes);
                if (progressBarValue % 2 == 0)
                {
                    form.SetProgressBarValue(listItem, progressBarValue);
                }
            });

            try
            {
                await youtube.Videos.DownloadAsync(videoId, 
                                                   videoPath, 
                                                   converter => converter.SetFFmpegPath(this.ffmpegPath), 
                                                   progress,
                                                   cancellationToken);

                form.NotifyDownloadFinished(listItem, videoPath, videoSizeInMegabytes);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"Download cancelled for video: {videoId}");
                
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
                
                // Re-throw to notify caller
                throw;
            }
        }
    }
}
