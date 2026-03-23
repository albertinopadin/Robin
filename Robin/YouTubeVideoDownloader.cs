using System.Threading.Tasks;

namespace Robin
{
    internal interface YouTubeVideoDownloader
    {
        ValueTask<string> GetVideoTitle(string url);
        Task DownloadVideo(RobinForm form, string url, DownloadState state);
    }
}
