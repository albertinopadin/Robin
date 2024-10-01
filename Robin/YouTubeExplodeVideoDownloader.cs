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


                form.InitProgressBar((int)maxVideoQualityStreamInfo.Size.MegaBytes);

                // TODO: figure out how to make this work:
                //if (!backgroundWorker1.IsBusy)
                //{
                //    backgroundWorker1.RunWorkerAsync();
                //}

                await DownloadVideo_Explode(form, 
                                            youtube, 
                                            videoUrl, 
                                            videoInfo, 
                                            maxVideoQualityStreamInfo.Container.Name);

                form.SetProgressBarValue((int)maxVideoQualityStreamInfo.Size.MegaBytes);
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
                                                 string extension)
        {
            try
            {
                Console.WriteLine("[Explode] Download Started");
                
                string validVideoTitle = MakeValidVideoTitle(videoInfo.Title);

                ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle);

                string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{extension}");
                //MessageBox.Show($"video path: {videoPath}");

                await youtube.Videos.DownloadAsync(videoUrl, videoPath);
                Console.WriteLine("[Explode] Download Complete");

                form.NotifyDownloadFinished(listItem, videoPath);
            }
            catch (Exception e)
            {
                MessageBox.Show($"Exception: {e.Message}\n\n{e.ToString()}");
            }
        }
    }
}
