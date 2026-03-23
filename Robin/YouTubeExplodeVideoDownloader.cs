using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace Robin
{
    internal class YouTubeExplodeVideoDownloader : YouTubeVideoDownloader
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly YoutubeClient youtube;
        private readonly string baseFilePath;
        private readonly string ffmpegPath;

        public YouTubeExplodeVideoDownloader(string baseFilePath)
        {
            this.baseFilePath = baseFilePath;
            this.ffmpegPath = RobinUtils.GetPathToFFMPEG();
            youtube = new YoutubeClient();
        }

        public async ValueTask<string> GetVideoTitle(string url)
        {
            var videoInfo = await GetVideoInfo(url);
            return videoInfo.Title;
        }

        public async Task DownloadVideo(RobinForm form, string url, DownloadState state)
        {
            form.SetCursorLoading();
            try
            {
                await DownloadBestVideo(form, url, true, state);
            }
            finally
            {
                form.SetCursorNormal();
            }
        }

        private async ValueTask<YoutubeExplode.Videos.Video> GetVideoInfo(string videoUrl)
        {
            return await youtube.Videos.GetAsync(videoUrl);
        }

        private async Task DownloadBestVideo(RobinForm form,
                                             string videoUrl,
                                             bool getManifest,
                                             DownloadState state)
        {
            var videoInfo = await GetVideoInfo(videoUrl);

            state.VideoId = videoInfo.Id;
            state.VideoUrl = videoUrl;
            state.VideoTitle = MakeValidVideoTitle(videoInfo.Title);

            if (getManifest)
            {
                try
                {
                    var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);
                    await DownloadBestVideoWithManifest(form, videoUrl, streamManifest, state);
                }
                catch (HttpRequestException ex)
                {
                    logger.Error("Error getting stream manifest...");
                    logger.Error(ex);
                    logger.Info("Trying to download video without first getting stream manifest...");
                    await DownloadBestVideo(form, videoUrl, false, state);
                }
            }
            else
            {
                state.VideoResolution = "UNKNOWN_RESOLUTION";
                state.Bitrate = "UNKNOWN_BITRATE";
                state.SizeInMegabytes = 9.999;
                state.FileExtension = "mp4";

                await DownloadVideo_Explode(form, youtube, state);
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

                if (videoStreams.Any())
                {
                    var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
                    logger.Info($"maxVideoQualityStreamInfo: {maxVideoQualityStreamInfo}");

                    state.VideoResolution = maxVideoQualityStreamInfo.VideoResolution.ToString();
                    state.Bitrate = maxVideoQualityStreamInfo.Bitrate.ToString();
                    state.SizeInMegabytes = maxVideoQualityStreamInfo.Size.MegaBytes;
                    state.FileExtension = maxVideoQualityStreamInfo.Container.Name;

                    await DownloadVideo_Explode(form, youtube, state);
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
            return string.Concat(rawVideoTitle.Split(Path.GetInvalidFileNameChars())).Trim();
        }

        private async Task DownloadVideo_Explode(RobinForm form, YoutubeClient youtube, DownloadState state)
        {
            form.SetVideoInfo(new RobinVideoInfo(state.VideoTitle,
                                                 state.FileExtension,
                                                 state.VideoResolution,
                                                 state.Bitrate,
                                                 state.SizeInMegabytes.ToString("n2")));

            string validVideoTitle = state.VideoTitle;
            ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle, (int)state.SizeInMegabytes);
            string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{state.FileExtension}");

            state.FilePath = videoPath;
            state.ListViewItem = listItem;

            form.RegisterActiveDownload(validVideoTitle, state);

            try
            {
                await DownloadVideoAsync_Explode(form, listItem, youtube, state);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"Download cancelled for video: {validVideoTitle}");
                UpdateDownloadStatus(RobinVideoStatus.Cancelled, validVideoTitle, listItem, form);
            }
            catch (Exception e)
            {
                logger.Error($"Download failed for video: {validVideoTitle}");
                RobinUtils.DisplayAndLogException(e);
                UpdateDownloadStatus(RobinVideoStatus.Failed, validVideoTitle, listItem, form);
            }
        }

        private void UpdateDownloadStatus(string status, string videoTitle, ListViewItem listItem, RobinForm form)
        {
            form.UpdateDownloadStatus(listItem, status);
            form.CleanupDownload(videoTitle);
        }

        private void CleanupPartialFile(string videoPath)
        {
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

            var progress = new Progress<double>(p =>
            {
                int progressBarValue = (int)(p * state.SizeInMegabytes);
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
                throw;
            }
            catch (Exception e)
            {
                logger.Error($"Download failed for video: {state.VideoId}");
                RobinUtils.DisplayAndLogException(e);

                form.UpdateDownloadStatus(listItem, RobinVideoStatus.Failed);
                string videoTitle = form.GetVideoTitleFromListItem(listItem);
                form.CancelProgressBarForVideo(videoTitle);
                form.DisableCancelButton(videoTitle);
                CleanupPartialFile(state.FilePath);

                throw;
            }
        }
    }
}
