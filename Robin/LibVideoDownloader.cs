using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoLibrary;

namespace Robin
{
    internal class LibVideoDownloader : YouTubeVideoDownloader
    {
        FastYouTube youtube;
        string baseFilePath;

        public LibVideoDownloader(string baseFilePath) 
        {
            this.baseFilePath = baseFilePath;
            youtube = new FastYouTube();
        }
        
        public async Task DownloadVideo(RobinForm form, string url)
        {
            await DownloadBestVideo(form, url);
        }

        private async Task DownloadBestVideo(RobinForm form, string videoUrl)
        {
            var videos = youtube.GetAllVideosAsync(videoUrl).GetAwaiter().GetResult();

            var videosWithAudio = videos.Where(v => v.Resolution > 0 && v.AudioBitrate > 0)
                                        .OrderByDescending(t => t.Resolution)
                                        .ToList();

            var maxResWithAudio = videosWithAudio.First();

            form.SetVideoInfo(new RobinVideoInfo(maxResWithAudio.Title,
                                                 maxResWithAudio.FileExtension,
                                                 maxResWithAudio.Resolution.ToString(),
                                                 maxResWithAudio.AudioBitrate.ToString(),
                                                 "UKNOWN"));

            foreach (var vi in videosWithAudio)
            {
                Console.WriteLine("Video Info: ");
                Console.WriteLine("Title: " + vi.Title);
                Console.WriteLine("Extension: " + vi.FileExtension);
                Console.WriteLine("Resolution: " + vi.Resolution);
                Console.WriteLine("Bitrate: " + vi.AudioBitrate);
                Console.WriteLine("\n");
            }

            //await DownloadVideo_libVideo(maxResWithAudio,
            //                    baseFilePath,
            //                    new Progress<Tuple<long, long>>((Tuple<long, long> v) =>
            //                    {
            //                        var percent = (int)((v.Item1 * 100) / v.Item2);
            //                        progressBarDownload.Value = percent;
            //                        progressBarDownload.Update();
            //                    }));
        }

        private async Task DownloadVideo_libVideo(YouTubeVideo video,
                                                  string downloadFolder,
                                                  IProgress<Tuple<long, long>> progress)
        {
            Console.WriteLine("[libVideo] Download Started");
            await youtube.CreateDownloadAsync(
                new Uri(video.Uri),
                Path.Combine(downloadFolder, video.FullName),
                progress);
            Console.WriteLine("[libVideo] Download Complete");
        }
    }
}
