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
        const string baseFilePath = @"C:\Users\albertinopadin\Videos\";

        public RobinForm()
        {
            InitializeComponent();
        }

        private async void btn_download_Click(object sender, EventArgs e)
        {
            var yt = new FastYouTube();
            var videos = yt.GetAllVideosAsync(textBox_videoURL.Text).GetAwaiter().GetResult();

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

            await downloadVideo(yt,
                                maxResWithAudio,
                                baseFilePath,
                                new Progress<Tuple<long, long>>((Tuple<long, long> v) =>
                                {
                                    var percent = (int)((v.Item1 * 100) / v.Item2);
                                    progressBarDownload.Value = percent;
                                    progressBarDownload.Update();
                                }));
        }

        private async Task downloadVideo(FastYouTube youTube, 
                                         YouTubeVideo video, 
                                         string downloadFolder,
                                         IProgress<Tuple<long, long>> progress)
        {
            Console.WriteLine("Download Started");
            await youTube.CreateDownloadAsync(
                new Uri(video.Uri),
                Path.Combine(downloadFolder, video.FullName),
                progress);
            Console.WriteLine("Download Complete");
        }
    }
}
