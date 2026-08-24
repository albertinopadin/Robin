using YoutubeDLSharp;
// YoutubeDLSharp.DownloadState (the enum) collides with Robin.DownloadState
// (the per-download state class) inside this namespace, so alias it.
using YtDlpState = YoutubeDLSharp.DownloadState;

namespace Robin
{
    /// <summary>
    /// Collapses yt-dlp's two download passes (video-only, then audio-only) plus
    /// the merge step into a single monotonic 0..1 sweep for Robin's progress bar:
    ///   video download   -> 0.00 .. 0.90
    ///   audio download   -> 0.90 .. 0.98
    ///   merge/postproc   -> 0.98
    ///   success          -> 1.00
    /// yt-dlp reports each file's progress as its own 0..1 sequence, and
    /// DownloadProgress.VideoIndex is playlist-scoped (not per sub-stream), so the
    /// video->audio switch is detected by the progress value resetting downward.
    /// One instance per download (not thread-safe; Progress&lt;T&gt; marshals callbacks).
    /// </summary>
    internal sealed class YtDlpProgressMapper
    {
        private const double VideoBandEnd = 0.90;   // video pass occupies 0..90%
        private const double AudioBandEnd = 0.98;   // audio pass 90..98%, merge holds at 98%
        private const double ResetDrop = 0.30;      // downward jump => new file started

        private int pass = 0;        // 0 = video, 1 = audio
        private double lastRaw = 0;  // last raw per-file fraction seen while Downloading

        public double ToOverallFraction(DownloadProgress p)
        {
            switch (p.State)
            {
                case YtDlpState.Downloading:
                    if (p.Progress + ResetDrop < lastRaw) pass = 1;   // reset => audio pass began
                    lastRaw = p.Progress;
                    return pass == 0
                        ? p.Progress * VideoBandEnd
                        : VideoBandEnd + p.Progress * (AudioBandEnd - VideoBandEnd);

                case YtDlpState.PostProcessing:
                    return AudioBandEnd;                              // merging: hold near the end

                case YtDlpState.Success:
                    return 1.0;

                case YtDlpState.PreProcessing:
                default:
                    return pass == 0 ? 0.0 : VideoBandEnd;
            }
        }
    }
}
