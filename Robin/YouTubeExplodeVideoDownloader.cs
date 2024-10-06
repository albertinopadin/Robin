using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
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
                                                     maxVideoQualityStreamInfo.Size.MegaBytes.ToString()));

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
            try
            {
                string validVideoTitle = MakeValidVideoTitle(videoInfo.Title);
                ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle, videoSizeInMegabytes);
                string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{extension}");
                await youtube.Videos.DownloadAsync(videoInfo.Id, videoPath, new Progress<double>(progress =>
                {
                    form.SetProgressBarValue(listItem, (int)(progress * videoSizeInMegabytes));
                }));

                form.NotifyDownloadFinished(listItem, videoPath, videoSizeInMegabytes);
            }
            catch (Exception e)
            {
                MessageBox.Show($"Exception: {e.Message}\n\n{e.ToString()}");
            }
        }
    }
}
