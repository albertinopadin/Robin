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
        YoutubeClient youtube;
        string baseFilePath;

        public YouTubeExplodeVideoDownloader(string baseFilePath)
        {
            this.baseFilePath = baseFilePath;
            youtube = new YoutubeClient();
        }

        public async Task DownloadVideo(RobinForm form, string url)
        {
            await DownloadBestVideo(form, url);
        }

        private async Task DownloadBestVideo(RobinForm form, string videoUrl)
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

            var videoStreams = streamManifest.GetVideoStreams();
            Console.WriteLine($"streamManifest video streams count: {videoStreams.Count()}");

            if (videoStreams.Count() > 0)
            {
                var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
                Console.WriteLine($"maxVideoQualityStreamInfo: {maxVideoQualityStreamInfo}");

                var videoInfo = await youtube.Videos.GetAsync(videoUrl);

                form.SetVideoInfo(new RobinVideoInfo(videoInfo.Title,
                                                     maxVideoQualityStreamInfo.Container.Name,
                                                     maxVideoQualityStreamInfo.VideoResolution.ToString(),
                                                     maxVideoQualityStreamInfo.Bitrate.ToString(),
                                                     maxVideoQualityStreamInfo.Size.MegaBytes.ToString("n2")));

                // TODO: figure out how to make this work:
                //if (!backgroundWorker1.IsBusy)
                //{
                //    backgroundWorker1.RunWorkerAsync();
                //}

                await DownloadVideo_Explode(form,
                                            youtube,
                                            videoUrl,
                                            videoInfo,
                                            (int)maxVideoQualityStreamInfo.Size.MegaBytes,
                                            maxVideoQualityStreamInfo.Container.Name);
            }
            else
            {
                MessageBox.Show($"No video streams found for URL {videoUrl}.");
            }
        }
        private string MakeValidVideoTitle(string rawVideoTitle)
        {
            Console.WriteLine($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        }

        private async Task DownloadVideo_Explode(RobinForm form,
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
                await DownloadVideoAsync_Explode(form, listItem, youtube, videoInfo.Id, videoPath, videoSizeInMegabytes);
            }
            catch (Exception e)
            {
                if (videoPath.Split('/').Contains("live"))
                {
                    try
                    {
                        videoPath = videoPath.Replace("/live/", "/watch?v=");
                        Console.WriteLine("[DownloadVideo_Explode] Replaced live video path with watch: " + videoPath);
                        await DownloadVideoAsync_Explode(form, listItem, youtube, videoInfo.Id, videoPath, videoSizeInMegabytes);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Exception: {ex.Message}\n\n{ex.ToString()}");
                    }
                }
                else
                {
                    MessageBox.Show($"Exception: {e.Message}\n\n{e.ToString()}");
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
            await youtube.Videos.DownloadAsync(videoId, videoPath, new Progress<double>(progress =>
            {
                form.SetProgressBarValue(listItem, (int)(progress * videoSizeInMegabytes));
            }));

            form.NotifyDownloadFinished(listItem, videoPath, videoSizeInMegabytes);
        }
    }
}
