using System;
using System.Threading.Tasks;

namespace Robin
{
    internal enum YtDlpSetupResult
    {
        Ready,      // binaries present (just downloaded or already cached); engine usable
        Declined,   // first run and the user said No — caller switches to YouTube Explode
        Failed,     // first-run download errored — caller informs the user and switches
    }

    /// <summary>
    /// Startup/engine-switch orchestration for the yt-dlp binaries: first-run
    /// consent popup, consented downloads with progress, and the daily yt-dlp -U
    /// self-update behind a marquee popup. All UI goes through IYtDlpSetupUi.
    /// Runs on the UI thread; the heavy work runs inside the progress popup.
    /// </summary>
    internal static class YtDlpBootstrap
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // resolver/selfUpdate are test seams; production callers pass only `ui`.
        public static async Task<YtDlpSetupResult> EnsureReadyAsync(
            IYtDlpSetupUi ui, YtDlpPathResolver resolver = null, Func<Task> selfUpdate = null)
        {
            resolver = resolver ?? YtDlpPathResolver.Default();
            YtDlpPathResolver capturedResolver = resolver;
            selfUpdate = selfUpdate ?? (() => SelfUpdateAsync(capturedResolver));

            if (!resolver.IsInstalled)
            {
                // Ask before pulling ~57 MB on first run.
                if (!ui.ConfirmFirstRunDownload())
                    return YtDlpSetupResult.Declined;

                try
                {
                    await ui.RunWithProgressAsync(
                        "Robin — setting up yt-dlp",
                        new[]
                        {
                            new BinaryProgressJob("yt-dlp", p => resolver.DownloadYtDlpAsync(p)),
                            new BinaryProgressJob("Deno (JS runtime)", p => resolver.DownloadDenoAsync(p)),
                        },
                        marquee: false);
                }
                catch (Exception e)
                {
                    logger.Error(e, "yt-dlp first-run download failed.");
                    return YtDlpSetupResult.Failed;
                }
                return YtDlpSetupResult.Ready;   // DownloadYtDlpAsync stamped the throttle
            }

            if (resolver.ShouldUpdate())
            {
                // The daily self-update is visible in a marquee popup
                // (`yt-dlp -U` emits no usable percentage).
                try
                {
                    await ui.RunWithProgressAsync(
                        "Robin — updating yt-dlp",
                        new[] { new BinaryProgressJob("yt-dlp", _ => selfUpdate()) },
                        marquee: true);
                }
                catch (Exception e)
                {
                    logger.Warn(e, "yt-dlp self-update failed; continuing on the cached binary");
                }
                finally
                {
                    resolver.MarkUpdated();
                }
            }
            return YtDlpSetupResult.Ready;
        }

        private static async Task SelfUpdateAsync(YtDlpPathResolver resolver)
        {
            var updater = new YtDlpClientAdapter(resolver.YtDlpPath, ffmpegPath: "", outputFolder: ".");
            string status = await updater.UpdateAsync().ConfigureAwait(false);   // yt-dlp -U
            logger.Info($"yt-dlp self-update: {status}");
        }
    }
}
