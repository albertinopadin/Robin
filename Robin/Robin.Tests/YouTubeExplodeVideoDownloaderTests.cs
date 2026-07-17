using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Robin.Tests.Fakes;
using Xunit;
using YoutubeExplode.Videos.Streams;

namespace Robin.Tests
{
    public class YouTubeExplodeVideoDownloaderTests : IDisposable
    {
        private const string FakeFFmpegPath = @"C:\fake\ffmpeg.exe";

        private readonly string tempDir;

        public YouTubeExplodeVideoDownloaderTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "Robin.Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        private YouTubeExplodeVideoDownloader CreateSut(FakeYoutubeClientAdapter client)
            => new YouTubeExplodeVideoDownloader(tempDir, client, FakeFFmpegPath);

        [Fact]
        public async Task DownloadVideo_PrefersMp4StreamsOverHigherQualityWebm()
        {
            var mp4Video = YoutubeTestData.VideoStream(Container.Mp4, height: 720);
            var webmVideo = YoutubeTestData.VideoStream(Container.WebM, height: 1080);
            var mp4Audio = YoutubeTestData.AudioStream(Container.Mp4, bitrateBps: 128_000);
            var webmAudio = YoutubeTestData.AudioStream(Container.WebM, bitrateBps: 160_000);
            var client = new FakeYoutubeClientAdapter
            {
                Manifest = YoutubeTestData.Manifest(mp4Video, webmVideo, mp4Audio, webmAudio),
            };
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();

            await CreateSut(client).DownloadVideo(notifier, "https://youtu.be/dQw4w9WgXcQ", state);

            var downloaded = client.MuxedDownloadCalls.Should().ContainSingle().Subject;
            downloaded.Should().Contain(mp4Video).And.Contain(mp4Audio);
            downloaded.Should().NotContain(webmVideo).And.NotContain(webmAudio);
            client.DirectDownloadCalls.Should().BeEmpty();

            state.FileExtension.Should().Be("mp4");
            state.VideoTitle.Should().Be("Test Video");
            state.FilePath.Should().Be(Path.Combine(tempDir, "Test Video.mp4"));
            state.VideoResolution.Should().Be(mp4Video.VideoResolution.ToString());
            state.SizeInMegabytes.Should().BeApproximately(100.0, 0.01);

            notifier.SetCursorLoadingCalls.Should().Be(1);
            notifier.SetCursorNormalCalls.Should().Be(1);
            notifier.RegisteredDownloads.Should().ContainSingle().Which.VideoTitle.Should().Be("Test Video");
            notifier.FinishedDownloads.Should().ContainSingle().Which.Should().BeSameAs(state);
        }

        [Fact]
        public async Task DownloadVideo_ManifestFailure_FallsBackToDirectDownload()
        {
            var client = new FakeYoutubeClientAdapter
            {
                ManifestException = new HttpRequestException("manifest unavailable"),
            };
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();

            await CreateSut(client).DownloadVideo(notifier, "https://youtu.be/dQw4w9WgXcQ", state);

            client.DirectDownloadCalls.Should().ContainSingle();
            client.MuxedDownloadCalls.Should().BeEmpty();
            state.SelectedStreams.Should().BeNull();
            state.VideoResolution.Should().Be("UNKNOWN_RESOLUTION");
            state.SizeInMegabytes.Should().Be(0);
            notifier.FinishedDownloads.Should().ContainSingle();
        }

        [Fact]
        public async Task DownloadVideo_CliWrapTeardownBugWithCompleteFile_TreatedAsSuccess()
        {
            var client = new FakeYoutubeClientAdapter
            {
                Video = YoutubeTestData.CreateVideo("CliWrap Video"),
                Manifest = YoutubeTestData.Manifest(
                    YoutubeTestData.VideoStream(Container.Mp4, height: 720),
                    YoutubeTestData.AudioStream(Container.Mp4)),
                MuxedDownloadException = new IOException(
                    "FFmpeg stderr stream teardown",
                    new NotSupportedException("polyshim")),
            };
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();
            string expectedFilePath = Path.Combine(tempDir, "CliWrap Video.mp4");
            File.WriteAllText(expectedFilePath, "finished video bytes");

            await CreateSut(client).DownloadVideo(notifier, "https://youtu.be/dQw4w9WgXcQ", state);

            File.Exists(expectedFilePath).Should().BeTrue("the completed output must not be deleted");
            notifier.FinishedDownloads.Should().ContainSingle();
            notifier.StatusUpdates.Should().BeEmpty("the download must not be marked failed or cancelled");
        }

        [Fact]
        public async Task DownloadVideo_Cancellation_MarksCancelledAndCleansUp()
        {
            var client = new FakeYoutubeClientAdapter
            {
                Manifest = YoutubeTestData.Manifest(
                    YoutubeTestData.VideoStream(Container.Mp4, height: 720),
                    YoutubeTestData.AudioStream(Container.Mp4)),
                MuxedDownloadException = new OperationCanceledException(),
            };
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();

            await CreateSut(client).DownloadVideo(notifier, "https://youtu.be/dQw4w9WgXcQ", state);

            notifier.StatusUpdates.Should().ContainSingle().Which.Status.Should().Be(RobinVideoStatus.Cancelled);
            notifier.CleanedUpDownloads.Should().ContainSingle().Which.Should().Be("Test Video");
            notifier.FinishedDownloads.Should().BeEmpty();
        }

        [Fact]
        public async Task DownloadVideo_ProgressBurst_IsThrottledButFinalValueAlwaysReported()
        {
            // 50 rapid intermediate reports plus the final 1.0. Without the 100 ms
            // throttle every report would reach the notifier; with it only a handful may.
            double[] burst = new double[51];
            for (int i = 0; i < 50; i++) burst[i] = (i + 1) / 100.0;
            burst[50] = 1.0;

            var client = new FakeYoutubeClientAdapter
            {
                Manifest = YoutubeTestData.Manifest(
                    YoutubeTestData.VideoStream(Container.Mp4, height: 720),   // 90 MB
                    YoutubeTestData.AudioStream(Container.Mp4)),               // 10 MB
                ProgressSequence = burst,
            };
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();
            state.LastProgressTickMs = unchecked(Environment.TickCount - 1000);

            await CreateSut(client).DownloadVideo(notifier, "https://youtu.be/dQw4w9WgXcQ", state);

            // Progress<double> dispatches callbacks asynchronously; wait for the final value.
            int finalValue = (int)state.SizeInMegabytes;
            await WaitUntilAsync(() => Array.IndexOf(notifier.ProgressReportsSnapshot(), finalValue) >= 0);

            int[] reports = notifier.ProgressReportsSnapshot();
            reports.Should().Contain(finalValue, "a report of >= 1.0 bypasses the throttle");
            reports.Length.Should().BeLessThan(burst.Length, "intermediate reports inside the 100 ms window are dropped");
            reports.Length.Should().BeLessThanOrEqualTo(10);
        }

        [Fact]
        public async Task GetVideoTitle_CachesVideoInfo_SoDownloadDoesNotRefetch()
        {
            var client = new FakeYoutubeClientAdapter
            {
                Manifest = YoutubeTestData.Manifest(
                    YoutubeTestData.VideoStream(Container.Mp4, height: 720),
                    YoutubeTestData.AudioStream(Container.Mp4)),
            };
            var notifier = new FakeDownloadUiNotifier();
            var sut = CreateSut(client);
            using var state = new DownloadState();

            string title = await sut.GetVideoTitle("https://youtu.be/dQw4w9WgXcQ");
            await sut.DownloadVideo(notifier, "https://youtu.be/dQw4w9WgXcQ", state);

            title.Should().Be("Test Video");
            client.GetVideoCalls.Should().ContainSingle("DownloadVideo must consume the cached video info");
        }

        [Theory]
        [InlineData("Valid | Title?", "Valid  Title")]
        [InlineData("a/b\\c:d*e", "abcde")]
        [InlineData("  padded title  ", "padded title")]
        [InlineData("plain", "plain")]
        public void MakeValidVideoTitle_StripsInvalidFilenameCharsAndTrims(string raw, string expected)
        {
            YouTubeExplodeVideoDownloader.MakeValidVideoTitle(raw).Should().Be(expected);
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(10);
            }
        }
    }
}
