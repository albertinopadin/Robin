using System.Threading.Tasks;

namespace Robin
{
    internal interface YouTubeVideoDownloader
    {
        ValueTask<string> GetVideoTitle(string url);
        Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state);
    }
}
