using System;
using System.IO;
using System.Threading.Tasks;

namespace Robin
{
    /// <summary>
    /// Shared behavior for the concrete download engines
    /// (YouTubeExplodeVideoDownloader, YtDlpVideoDownloader): filename
    /// sanitization, partial-file cleanup, and the progress-report throttle.
    /// </summary>
    internal abstract class VideoDownloaderBase : YouTubeVideoDownloader
    {
        // Own category so the shared helpers log under "VideoDownloaderBase";
        // named distinctly so derived classes keep their own `logger` with no CS0108 hiding.
        private static readonly NLog.Logger baseLogger = NLog.LogManager.GetCurrentClassLogger();

        protected const int ProgressThrottleMs = 100;
        protected readonly string baseFilePath;

        protected VideoDownloaderBase(string baseFilePath)
        {
            this.baseFilePath = baseFilePath;
        }

        public abstract ValueTask<string> GetVideoTitle(string url);
        public abstract Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state);

        internal static string MakeValidVideoTitle(string rawVideoTitle)
        {
            baseLogger.Info($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(Path.GetInvalidFileNameChars())).Trim();
        }

        /// <summary>
        /// Rate-limits progress reports to one per <see cref="ProgressThrottleMs"/>,
        /// but always lets the final tick through. Mutates state.LastProgressTickMs.
        /// </summary>
        protected static bool ShouldReportProgress(DownloadState state, bool isFinal)
        {
            int now = Environment.TickCount;
            if (!isFinal && now - state.LastProgressTickMs < ProgressThrottleMs) return false;
            state.LastProgressTickMs = now;
            return true;
        }

        protected void CleanupPartialFile(string videoPath)
        {
            if (File.Exists(videoPath))
            {
                try
                {
                    File.Delete(videoPath);
                    baseLogger.Info($"Deleted partial file: {videoPath}");
                }
                catch (Exception ex)
                {
                    baseLogger.Error($"Failed to delete partial file {videoPath}: {ex.Message}");
                }
            }
        }
    }
}
