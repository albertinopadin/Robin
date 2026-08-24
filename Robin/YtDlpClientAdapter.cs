using System;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace Robin
{
    internal sealed class YtDlpClientAdapter : IYtDlpClientAdapter
    {
        private readonly YoutubeDL ytdl;
        private readonly OptionSet baseOptions;

        public YtDlpClientAdapter(string ytDlpPath, string ffmpegPath, string outputFolder,
                                  OptionSet baseOptions = null)
        {
            ytdl = new YoutubeDL
            {
                YoutubeDLPath = ytDlpPath,          // absolute path to yt-dlp.exe (YtDlpPathResolver)
                FFmpegPath = ffmpegPath,            // RobinUtils.GetPathToFFMPEG()
                OutputFolder = outputFolder,        // RobinForm.baseFilePath (MyVideos)
                OutputFileTemplate = "%(title)s.%(ext)s",
                RestrictFilenames = false,          // Robin sanitizes titles itself
                OverwriteFiles = true,
            };
            this.baseOptions = baseOptions;         // reserved for cookies later
        }

        public Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct)
            => ytdl.RunVideoDataFetch(url, ct, overrideOptions: baseOptions);

        public Task<RunResult<string>> DownloadAsync(
            string url, string format, IProgress<DownloadProgress> progress, CancellationToken ct)
            => ytdl.RunVideoDownload(
                   url,
                   format: format,
                   mergeFormat: DownloadMergeFormat.Mp4,
                   ct: ct,
                   progress: progress,
                   overrideOptions: baseOptions);

        // Updates yt-dlp in place (downloads only when a newer build exists on the
        // stable channel). Needs a writable dir; %LOCALAPPDATA%\Robin qualifies.
        public Task<string> UpdateAsync() => ytdl.RunUpdate();
    }
}
