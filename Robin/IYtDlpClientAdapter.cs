using System;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace Robin
{
    /// <summary>
    /// Thin seam over the yt-dlp operations the downloader uses, so tests can
    /// inject canned metadata, progress, and download results without spawning
    /// a real yt-dlp process. Mirrors IYoutubeClientAdapter.
    /// </summary>
    internal interface IYtDlpClientAdapter
    {
        Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct);

        Task<RunResult<string>> DownloadAsync(
            string url,
            string format,
            IProgress<DownloadProgress> progress,
            CancellationToken ct);

        // Wraps `yt-dlp -U`. Returns yt-dlp's status line. Fake no-ops this.
        Task<string> UpdateAsync();
    }
}
