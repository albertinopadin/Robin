using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
// YoutubeDLSharp.DownloadState (the enum) collides with Robin.DownloadState.
using YtDlpState = YoutubeDLSharp.DownloadState;

namespace Robin
{
    internal class YtDlpVideoDownloader : VideoDownloaderBase
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // Prefer H.264 mp4 + m4a (two-pass), fall back to muxed mp4, then anything.
        private const string Format =
            "bv*[ext=mp4][vcodec^=avc1]+ba[ext=m4a]/bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b";

        private readonly Lazy<IYtDlpClientAdapter> client;

        // Default ctor: lazy so no download happens on the UI thread at construction.
        public YtDlpVideoDownloader(string baseFilePath)
            : base(baseFilePath)
        {
            client = new Lazy<IYtDlpClientAdapter>(() =>
            {
                var resolver = YtDlpPathResolver.Default();
                string ytDlp = resolver.Resolve();    // cached after YtDlpBootstrap warms it
                resolver.ResolveDeno();               // JS runtime alongside yt-dlp.exe
                return new YtDlpClientAdapter(ytDlp, RobinUtils.GetPathToFFMPEG(), baseFilePath);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        // Test-visible ctor: inject a fake (eager).
        internal YtDlpVideoDownloader(string baseFilePath, IYtDlpClientAdapter client)
            : base(baseFilePath)
        {
            this.client = new Lazy<IYtDlpClientAdapter>(() => client);
        }

        public override async ValueTask<string> GetVideoTitle(string url)
        {
            var res = await client.Value.FetchMetadataAsync(url, default).ConfigureAwait(false);
            if (!res.Success || res.Data == null)
                throw new InvalidOperationException(
                    $"yt-dlp could not read video info for {url}: {string.Join("; ", res.ErrorOutput)}");
            return res.Data.Title;
        }

        public override async Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state)
        {
            notifier.SetCursorLoading();
            try
            {
                // Failures up to here (metadata fetch, before the item exists in the
                // downloads list) propagate to the caller, which owns the error dialog —
                // same contract as the YoutubeExplode engine's manifest/stream failures.
                var meta = await client.Value.FetchMetadataAsync(url, state.CancellationToken).ConfigureAwait(false);
                if (!meta.Success || meta.Data == null)
                    throw new InvalidOperationException(
                        $"yt-dlp metadata fetch failed: {string.Join("; ", meta.ErrorOutput)}");

                VideoData info = meta.Data;
                FormatData bestVideo = SelectBestVideo(info.Formats);

                state.VideoId = info.ID;
                state.VideoUrl = url;
                state.VideoTitle = MakeValidVideoTitle(info.Title);     // inherited from base
                state.FileExtension = "mp4";
                state.VideoResolution = DescribeResolution(bestVideo);
                state.Bitrate = "UNKNOWN_BITRATE";
                state.SizeInMegabytes = EstimateSizeMb(info);
                state.FilePath = Path.Combine(baseFilePath, $"{state.VideoTitle}.mp4");

                notifier.SetVideoInfo(new RobinVideoInfo(
                    state.VideoTitle, state.FileExtension, state.VideoResolution,
                    state.Bitrate, state.SizeInMegabytes.ToString("n2")));
                notifier.AddVideoToDownloadsList(state, (int)state.SizeInMegabytes);
                notifier.RegisterActiveDownload(state.VideoTitle, state);
                notifier.ClearVideoUrlTextbox();

                try
                {
                    await RunDownload(notifier, url, state).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger.Info($"Download cancelled for: {state.VideoTitle}");
                    notifier.UpdateDownloadStatus(state.ListViewItem, RobinVideoStatus.Cancelled);
                    notifier.CleanupDownload(state.VideoTitle);
                    CleanupPartialFile(state.FilePath);                 // inherited
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Download failed for: {url}");
                    RobinUtils.DisplayAndLogException(e);
                    notifier.UpdateDownloadStatus(state.ListViewItem, RobinVideoStatus.Failed);
                    notifier.CancelProgressBarForVideo(state);
                    notifier.DisableCancelButton(state.VideoTitle);
                    CleanupPartialFile(state.FilePath);
                }
            }
            finally
            {
                notifier.SetCursorNormal();
            }
        }

        private async Task RunDownload(IDownloadUiNotifier notifier, string url, DownloadState state)
        {
            var mapper = new YtDlpProgressMapper();
            var progress = new Progress<DownloadProgress>(p =>
            {
                double overall = mapper.ToOverallFraction(p);          // 0..1 across both passes
                bool isFinal = p.State == YtDlpState.Success || overall >= 1.0;
                if (!ShouldReportProgress(state, isFinal)) return;     // inherited throttle
                notifier.ReportDownloadProgress(state, (int)(overall * state.SizeInMegabytes));
            });

            RunResult<string> res =
                await client.Value.DownloadAsync(url, Format, progress, state.CancellationToken)
                                  .ConfigureAwait(false);

            if (!res.Success)
                throw new InvalidOperationException(
                    $"yt-dlp download failed: {string.Join("; ", res.ErrorOutput)}");

            if (!string.IsNullOrEmpty(res.Data) && File.Exists(res.Data))
                state.FilePath = res.Data;   // trust yt-dlp's real output path

            notifier.NotifyDownloadFinished(state);
        }

        // Download size from the up-front metadata: best mp4 video-only + best m4a
        // audio-only filesize/filesize_approx, matching the Format selection string.
        internal static double EstimateSizeMb(VideoData info)
        {
            long totalBytes = Bytes(SelectBestVideo(info?.Formats)) + Bytes(SelectBestAudio(info?.Formats));
            return totalBytes / (1024.0 * 1024.0);   // 0 when sizes are unknown
        }

        internal static FormatData SelectBestVideo(FormatData[] formats)
            => (formats ?? Array.Empty<FormatData>())
                .Where(f => f != null
                         && string.Equals(f.Extension, "mp4", StringComparison.OrdinalIgnoreCase)
                         && !NoCodec(f.VideoCodec) && NoCodec(f.AudioCodec))
                .OrderByDescending(f => f.Height ?? 0)
                .ThenByDescending(Bytes)
                .FirstOrDefault();

        internal static FormatData SelectBestAudio(FormatData[] formats)
            => (formats ?? Array.Empty<FormatData>())
                .Where(f => f != null
                         && string.Equals(f.Extension, "m4a", StringComparison.OrdinalIgnoreCase)
                         && NoCodec(f.VideoCodec) && !NoCodec(f.AudioCodec))
                .OrderByDescending(f => f.Bitrate ?? 0)
                .ThenByDescending(Bytes)
                .FirstOrDefault();

        // VideoData has no Resolution property; take it from the selected video format.
        internal static string DescribeResolution(FormatData bestVideo)
        {
            if (!string.IsNullOrEmpty(bestVideo?.Resolution)) return bestVideo.Resolution;
            if (bestVideo?.Height != null) return $"{bestVideo.Height}p";
            return "UNKNOWN_RESOLUTION";
        }

        private static long Bytes(FormatData f) => f?.FileSize ?? f?.ApproximateFileSize ?? 0L;

        private static bool NoCodec(string codec) => string.IsNullOrEmpty(codec) || codec == "none";
    }
}
