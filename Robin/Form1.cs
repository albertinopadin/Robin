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

namespace Robin
{
    public partial class RobinForm : Form
    {
        public RobinForm()
        {
            InitializeComponent();
        }

        private async void btn_download_Click(object sender, EventArgs e)
        {
            //var yt = YouTube.Default;
            var yt = new FastYouTube();
            //var videos = yt.GetAllVideos(textBox_videoURL.Text);
            var videos = yt.GetAllVideosAsync(textBox_videoURL.Text).GetAwaiter().GetResult();

            //var video = videos.First(v => v.Resolution == videos.Max(j => j.Resolution));
            var video = yt.GetVideo(textBox_videoURL.Text);
            label_videoTitle.Text = video.Title;
            label_videoExtension.Text = video.FileExtension;
            label_videoResolution.Text = video.Resolution.ToString();
            label_maxBitrate.Text = video.AudioBitrate.ToString();

            var maxRes = videos.First(v => v.Resolution == videos.Max(m => m.Resolution));
            var maxBR = videos.First(v => v.AudioBitrate == videos.Max(m => m.AudioBitrate));

            Console.WriteLine($"Max resolution video -> Resolution: {maxRes.Resolution}, " +
                              $"Audio Bit Rate: {maxRes.AudioBitrate}");

            Console.WriteLine($"Max audio bit rate video -> Resolution: {maxBR.Resolution}, " +
                              $"Audio Bit Rate: {maxBR.AudioBitrate}");

            var maxResWithBR = videos.FirstOrDefault(v => v.AudioBitrate > 0 && v.Resolution == videos.Max(m => m.Resolution));
            if (maxResWithBR != null)
            {
                Console.WriteLine($"Max resolution video with Audio BR -> Resolution: {maxResWithBR.Resolution}, " +
                                  $"Audio Bit Rate: {maxResWithBR.AudioBitrate}");
            } else
            {
                Console.WriteLine("There is no video with max resolution AND audio bitrate!");
            }

            var videosWithAudio = videos.Where(v => v.Resolution > 0 && v.AudioBitrate > 0).ToList();

            Console.WriteLine("VIdeos with Audio:");
            foreach (var vi in videosWithAudio)
            {
                Console.WriteLine("Video Info: ");
                Console.WriteLine("Title: " + vi.Title);
                Console.WriteLine("Extension: " + vi.FileExtension);
                Console.WriteLine("Resolution: " + vi.Resolution);
                Console.WriteLine("Bitrate: " + vi.AudioBitrate);
                Console.WriteLine("\n");
            }

            var baseFilePath = @"C:\Users\albertinopadin\Videos\";
            await downloadVideo(baseFilePath, video, yt);
        }

        private async Task downloadVideo(string downloadFolder, YouTubeVideo video, FastYouTube youTube)
        {
            Console.WriteLine("Download Started");
            await youTube.CreateDownloadAsync(
                new Uri(video.Uri),
                Path.Combine(downloadFolder, video.FullName),
                new Progress<Tuple<long, long>>((Tuple<long, long> v) =>
                {
                    var percent = (int)((v.Item1 * 100) / v.Item2);
                    //Console.WriteLine("Progress: " + percent);
                    progressBarDownload.Value = percent;
                    progressBarDownload.Update();
                }));
            Console.WriteLine("Download Complete");

            /*
            long? totalBytes = 0;
            var httpClientHandler = new HttpClientHandler()
            {
                MaxResponseHeadersLength = 64
            };

            var client = new HttpClient(httpClientHandler);
            progressBarDownload.Minimum = 0;
            progressBarDownload.Maximum = 100;

            using (Stream output = File.OpenWrite(downloadFolder + video.FullName))
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, video.Uri))
                {
                    totalBytes =
                        client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result.Content.Headers.ContentLength;
                }

                using (var input = await client.GetStreamAsync(video.Uri))
                {
                    byte[] buffer = new byte[256 * 1024];
                    int read;
                    int totalRead = 0;
                    Console.WriteLine("Download Started");
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        totalRead += read;
                        var progress = (int)(((double)totalRead / (double)totalBytes) * 100);
                        progressBarDownload.Value = progress;
                        progressBarDownload.Update();
                        Console.WriteLine("Read: " + read);
                        Console.WriteLine("Total read: " + totalRead);
                        Console.WriteLine("Progress: " + progress);
                    }
                    Console.WriteLine("Download Complete");
                }
            } */
        }
    }
}
