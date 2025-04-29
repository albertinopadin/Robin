using AngleSharp.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            form.SetCursorLoading();
            await DownloadBestVideo(form, url);
            form.SetCursorNormal();
        }

        private async Task DownloadBestVideo(RobinForm form, string videoUrl)
        {
            try
            {
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

                var videoStreams = streamManifest.GetVideoStreams();
                logger.Info($"streamManifest video streams count: {videoStreams.Count()}");

                if (videoStreams.Count() > 0)
                {
                    var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
                    logger.Info($"maxVideoQualityStreamInfo: {maxVideoQualityStreamInfo}");

                    var videoInfo = await youtube.Videos.GetAsync(videoUrl);

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

        private async void DownloadVideo_Explode(RobinForm form,
                                                 YoutubeClient youtube,
                                                 string videoUrl,
                                                 YoutubeExplode.Videos.Video videoInfo,
                                                 int videoSizeInMegabytes,
                                                 string extension)
        {
            string validVideoTitle = MakeValidVideoTitle(videoInfo.Title);
            ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle, videoSizeInMegabytes);
            string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{extension}");

            try
            {
                await DownloadVideoAsync_Explode(form, 
                                                 listItem, 
                                                 youtube, 
                                                 videoInfo.Id, 
                                                 videoPath, 
                                                 videoSizeInMegabytes);
            }
            catch (Exception e)
            {
                if (videoPath.Split('/').Contains("live"))
                {
                    try
                    {
                        videoPath = videoPath.Replace("/live/", "/watch?v=");
                        logger.Info("[DownloadVideo_Explode] Replaced live video path with watch: {0}", videoPath);
                        await DownloadVideoAsync_Explode(form, 
                                                         listItem, 
                                                         youtube, 
                                                         videoInfo.Id, 
                                                         videoPath, 
                                                         videoSizeInMegabytes);
                    }
                    catch (Exception ex)
                    {
                        RobinUtils.DisplayAndLogException(ex);
                    }
                }
                else
                {
                    RobinUtils.DisplayAndLogException(e);
                }
            }
        }

        private async Task DownloadVideoAsync_Explode(RobinForm form,
                                                      ListViewItem listItem,
                                                      YoutubeClient youtube,
                                                      string videoId,
                                                      string videoPath,
                                                      int videoSizeInMegabytes)
        {
            var progress = new Progress<double>(progress =>
            {
                int progressBarValue = (int)(progress * videoSizeInMegabytes);
                if (progressBarValue % 2 == 0)
                {
                    form.SetProgressBarValue(listItem, progressBarValue);
                }
            });

            await youtube.Videos.DownloadAsync(videoId, 
                                               videoPath, 
                                               converter => converter.SetFFmpegPath(this.ffmpegPath), 
                                               progress);

            form.NotifyDownloadFinished(listItem, videoPath, videoSizeInMegabytes);
        }
    }
}
