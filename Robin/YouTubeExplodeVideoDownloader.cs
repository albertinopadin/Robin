using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace Robin
{
    internal class YouTubeExplodeVideoDownloader : YouTubeVideoDownloader
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly HttpClient sharedHttpClient = CreateSharedHttpClient();

        private readonly YoutubeClient youtube;
        private readonly string baseFilePath;
        private readonly string ffmpegPath;

        private readonly ConcurrentDictionary<string, YoutubeExplode.Videos.Video> videoInfoCache
            = new ConcurrentDictionary<string, YoutubeExplode.Videos.Video>();

        private const int ProgressThrottleMs = 100;

        private static HttpClient CreateSharedHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false,
            };
            var client = new HttpClient(handler, disposeHandler: true);
            client.Timeout = TimeSpan.FromHours(1);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            return client;
        }

        public YouTubeExplodeVideoDownloader(string baseFilePath)
        {
            this.baseFilePath = baseFilePath;
            this.ffmpegPath = RobinUtils.GetPathToFFMPEG();
            youtube = new YoutubeClient(sharedHttpClient);
        }

        public async ValueTask<string> GetVideoTitle(string url)
        {
            var videoInfo = await youtube.Videos.GetAsync(url).ConfigureAwait(false);
            videoInfoCache[url] = videoInfo;
            return videoInfo.Title;
        }

        public async Task DownloadVideo(IDownloadUiNotifier notifier, string url, DownloadState state)
        {
            notifier.SetCursorLoading();
            try
            {
                await DownloadBestVideo(notifier, url, state).ConfigureAwait(false);
            }
            finally
            {
                notifier.SetCursorNormal();
            }
        }

        private async Task DownloadBestVideo(IDownloadUiNotifier notifier, string videoUrl, DownloadState state)
        {
            if (!videoInfoCache.TryRemove(videoUrl, out var videoInfo))
            {
                videoInfo = await youtube.Videos.GetAsync(videoUrl).ConfigureAwait(false);
            }

            state.VideoId = videoInfo.Id;
            state.VideoUrl = videoUrl;
            state.VideoTitle = MakeValidVideoTitle(videoInfo.Title);

            StreamManifest manifest;
            try
            {
                manifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                logger.Error(ex, "Manifest fetch failed; falling back to direct live-stream download.");
                await DownloadLiveFallback(notifier, state).ConfigureAwait(false);
                return;
            }

            var videoStream = manifest.GetVideoOnlyStreams()
                                      .Where(s => s.Container == Container.Mp4)
                                      .GetWithHighestVideoQuality()
                              ?? manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality();

            var audioStream = manifest.GetAudioOnlyStreams()
                                      .Where(s => s.Container == Container.Mp4)
                                      .GetWithHighestBitrate()
                              ?? manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            if (videoStream == null || audioStream == null)
            {
                throw new InvalidOperationException($"No usable streams found for URL {videoUrl}.");
            }

            logger.Info($"Selected video stream: {videoStream}");
            logger.Info($"Selected audio stream: {audioStream}");

            state.SelectedStreams = new IStreamInfo[] { videoStream, audioStream };
            state.VideoResolution = videoStream.VideoResolution.ToString();
            state.Bitrate = videoStream.Bitrate.ToString();
            state.SizeInMegabytes = videoStream.Size.MegaBytes + audioStream.Size.MegaBytes;
            state.FileExtension = "mp4";

            await DownloadVideo_Explode(notifier, state).ConfigureAwait(false);
        }

        private async Task DownloadLiveFallback(IDownloadUiNotifier notifier, DownloadState state)
        {
            state.SelectedStreams = null;
            state.VideoResolution = "UNKNOWN_RESOLUTION";
            state.Bitrate = "UNKNOWN_BITRATE";
            state.SizeInMegabytes = 0;
            state.FileExtension = "mp4";
            await DownloadVideo_Explode(notifier, state).ConfigureAwait(false);
        }

        private string MakeValidVideoTitle(string rawVideoTitle)
        {
            logger.Info($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(Path.GetInvalidFileNameChars())).Trim();
        }

        private async Task DownloadVideo_Explode(IDownloadUiNotifier notifier, DownloadState state)
        {
            notifier.SetVideoInfo(new RobinVideoInfo(state.VideoTitle,
                                                     state.FileExtension,
                                                     state.VideoResolution,
                                                     state.Bitrate,
                                                     state.SizeInMegabytes.ToString("n2")));

            state.FilePath = Path.Combine(baseFilePath, $"{state.VideoTitle}.{state.FileExtension}");
            notifier.AddVideoToDownloadsList(state, (int)state.SizeInMegabytes);
            notifier.RegisterActiveDownload(state.VideoTitle, state);

            try
            {
                await DownloadVideoAsync_Explode(notifier, state).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"Download cancelled for video: {state.VideoTitle}");
                UpdateDownloadStatus(RobinVideoStatus.Cancelled, state, notifier);
            }
            catch (Exception e)
            {
                // Inner already logged + dialogued + marked the item failed. Don't double-report.
                logger.Error(e, $"Download failed for video: {state.VideoTitle}");
            }
        }

        private void UpdateDownloadStatus(string status, DownloadState state, IDownloadUiNotifier notifier)
        {
            notifier.UpdateDownloadStatus(state.ListViewItem, status);
            notifier.CleanupDownload(state.VideoTitle);
        }

        private void CleanupPartialFile(string videoPath)
        {
            if (File.Exists(videoPath))
            {
                try
                {
                    File.Delete(videoPath);
                    logger.Info($"Deleted partial file: {videoPath}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to delete partial file {videoPath}: {ex.Message}");
                }
            }
        }

        private async Task DownloadVideoAsync_Explode(IDownloadUiNotifier notifier, DownloadState state)
        {
            notifier.ClearVideoUrlTextbox();

            var progress = new Progress<double>(p =>
            {
                int now = Environment.TickCount;
                bool isFinal = p >= 1.0;
                if (!isFinal && now - state.LastProgressTickMs < ProgressThrottleMs) return;
                state.LastProgressTickMs = now;

                int value = (int)(p * state.SizeInMegabytes);
                notifier.ReportDownloadProgress(state, value);
            });

            try
            {
                if (state.SelectedStreams != null)
                {
                    await youtube.Videos.DownloadAsync(
                        state.SelectedStreams,
                        new ConversionRequestBuilder(state.FilePath)
                            .SetFFmpegPath(this.ffmpegPath)
                            .SetContainer(Container.Mp4)
                            .SetPreset(ConversionPreset.UltraFast)
                            .Build(),
                        progress,
                        state.CancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await youtube.Videos.DownloadAsync(
                        state.VideoUrl,
                        state.FilePath,
                        o => o.SetFFmpegPath(this.ffmpegPath)
                              .SetPreset(ConversionPreset.UltraFast),
                        progress,
                        state.CancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                logger.Info($"Download cancelled for video: {state.VideoId}");
                CleanupPartialFile(state.FilePath);
                throw;
            }
            catch (Exception e)
            {
                // YoutubeExplode.Converter + CliWrap + polyshim on .NET Framework 4.8 throws
                // NotSupportedException during FFmpeg stderr stream teardown AFTER the output
                // file is already written. If the exception matches that pattern and the file
                // looks complete, treat as success instead of deleting the user's video.
                if (IsCliWrapTeardownBug(e) && IsOutputFileValid(state.FilePath))
                {
                    logger.Warn(e, $"Ignoring CliWrap/polyshim teardown exception; output looks complete: {state.FilePath}");
                }
                else
                {
                    logger.Error(e, $"Download failed for video: {state.VideoId}");
                    RobinUtils.DisplayAndLogException(e);

                    notifier.UpdateDownloadStatus(state.ListViewItem, RobinVideoStatus.Failed);
                    notifier.CancelProgressBarForVideo(state);
                    notifier.DisableCancelButton(state.VideoTitle);
                    CleanupPartialFile(state.FilePath);

                    throw;
                }
            }

            // Post-download: completing the UI state is a separate concern from the download itself.
            // A failure here must not delete the successfully-saved file.
            try
            {
                notifier.NotifyDownloadFinished(state);
            }
            catch (Exception e)
            {
                logger.Error(e, $"NotifyDownloadFinished threw for {state.VideoTitle}; file at {state.FilePath} is preserved.");
            }
        }

        private static bool IsCliWrapTeardownBug(Exception e)
        {
            for (Exception current = e; current != null; current = current.InnerException)
            {
                if (current is NotSupportedException) return true;
            }
            return false;
        }

        private static bool IsOutputFileValid(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
