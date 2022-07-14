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


namespace Robin
{
    public partial class RobinForm : Form
    {
        string baseFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        public RobinForm()
        {
            InitializeComponent();
        }

        private async void btn_download_Click(object sender, EventArgs e)
        {
            //await downloadBestVideo_libVideo(textBox_videoURL.Text);

            await downloadBestVideo_Explode(textBox_videoURL.Text);
        }

        private async Task downloadBestVideo_Explode(string videoUrl)
        {
            var youtube = new YoutubeClient();
            
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

            var maxVideoQualityMuxedStreamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();

            Console.WriteLine($"maxVideoQualityMuxedManifest: {maxVideoQualityMuxedStreamInfo.ToString()}");

            var videoInfo = await youtube.Videos.GetAsync(videoUrl);

            label_videoTitle.Text = videoInfo.Title;
            label_videoExtension.Text = maxVideoQualityMuxedStreamInfo.Container.Name;
            label_videoResolution.Text = maxVideoQualityMuxedStreamInfo.VideoResolution.ToString();
            label_maxBitrate.Text = maxVideoQualityMuxedStreamInfo.Bitrate.ToString();

            await downloadVideo_Explode(youtube, videoUrl, videoInfo, maxVideoQualityMuxedStreamInfo.Container.Name);
        }

        private async Task downloadVideo_Explode(YoutubeClient youtube, 
                                                 string videoUrl, 
                                                 YoutubeExplode.Videos.Video videoInfo,
                                                 string extension)
        {
            Console.WriteLine("[Explode] Download Started");
            string videoPath = Path.Combine(baseFilePath, $"{videoInfo.Title}.{extension}");
            await youtube.Videos.DownloadAsync(videoUrl, videoPath);
            Console.WriteLine("[Explode] Download Complete");
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
    }
}
