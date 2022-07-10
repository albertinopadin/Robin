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

using VideoLibrary;

namespace Robin
{
    public partial class RobinForm : Form
    {
        public RobinForm()
        {
            InitializeComponent();
        }

        private void btn_download_Click(object sender, EventArgs e)
        {
            var yt = YouTube.Default;
            //var videos = yt.GetAllVideos(textBox_videoURL.Text);
            var videos = yt.GetAllVideosAsync(textBox_videoURL.Text).GetAwaiter().GetResult();

            var maxResVideo = videos.First(v => v.Resolution == videos.Max(j => j.Resolution));
            label_videoTitle.Text = maxResVideo.Title;
            label_videoExtension.Text = maxResVideo.FileExtension;
            label_videoResolution.Text = maxResVideo.Resolution.ToString();
            label_maxBitrate.Text = maxResVideo.AudioBitrate.ToString();

            foreach (var vi in videos)
            {
                Console.WriteLine("Video Info: ");
                Console.WriteLine("Title: " + vi.Title);
                Console.WriteLine("Extension: " + vi.FileExtension);
                Console.WriteLine("Resolution: " + vi.Resolution);
                Console.WriteLine("Bitrate: " + vi.AudioBitrate);
                Console.WriteLine("\n");
            }

            byte[] contents = maxResVideo.GetBytes();
            File.WriteAllBytes(@"C:\Users\albertinopadin\Videos\" + maxResVideo.FullName, contents);
        }
    }
}
