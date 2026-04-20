using System.Collections.Generic;
using System.Threading.Tasks;

namespace Robin.Tests.Fakes
{
    internal sealed class FakeYouTubeVideoDownloader : YouTubeVideoDownloader
    {
        public string NextTitle { get; set; } = "Fake Title";
        public List<string> GetVideoTitleCalls { get; } = new List<string>();
        public List<string> DownloadVideoCalls { get; } = new List<string>();

        public ValueTask<string> GetVideoTitle(string url)
        {
            GetVideoTitleCalls.Add(url);
            return new ValueTask<string>(NextTitle);
        }

        public Task DownloadVideo(RobinForm form, string url, DownloadState state)
        {
            DownloadVideoCalls.Add(url);
            state.IsCompleted = true;
            return Task.CompletedTask;
        }
    }
}
