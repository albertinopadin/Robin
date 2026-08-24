using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Robin.Tests.Fakes;
using Xunit;
using YoutubeDLSharp.Metadata;

namespace Robin.Tests
{
    public class YtDlpVideoDownloaderTests : IDisposable
    {
        private const string Url = "https://youtu.be/dQw4w9WgXcQ";

        private readonly string tempDir;

        public YtDlpVideoDownloaderTests()
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

        private YtDlpVideoDownloader CreateSut(FakeYtDlpClientAdapter client)
            => new YtDlpVideoDownloader(tempDir, client);   // internal test ctor (eager fake)

        private static FormatData Fmt(string ext, string vcodec, string acodec,
                                      int? height = null, double? bitrate = null,
                                      long? sizeBytes = null, string resolution = null)
            => new FormatData
            {
                Extension = ext,
                VideoCodec = vcodec,
                AudioCodec = acodec,
                Height = height,
                Bitrate = bitrate,
                FileSize = sizeBytes,
                Resolution = resolution,
            };

        private static FormatData[] NinetyPlusTenMbFormats() => new[]
        {
            Fmt("mp4", "avc1.640028", "none", height: 1080, sizeBytes: 90L * 1024 * 1024, resolution: "1920x1080"),
            Fmt("mp4", "avc1.4d401e", "none", height: 720, sizeBytes: 60L * 1024 * 1024, resolution: "1280x720"),
            Fmt("m4a", "none", "mp4a.40.2", bitrate: 128, sizeBytes: 10L * 1024 * 1024),
            Fmt("webm", "vp9", "none", height: 2160, sizeBytes: 500L * 1024 * 1024),   // wrong container, ignored
        };

        [Fact]
        public async Task DownloadVideo_HappyPath_DrivesSameNotifierContractAsExplodeEngine()
        {
            string outputPath = Path.Combine(tempDir, "Test Video.mp4");
            File.WriteAllText(outputPath, "finished video bytes");
            var client = new FakeYtDlpClientAdapter { OutputPath = outputPath };
            client.Video.Formats = NinetyPlusTenMbFormats();
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();

            await CreateSut(client).DownloadVideo(notifier, Url, state);

            client.DownloadCalls.Should().ContainSingle().Which.Should().Be(Url);
            state.VideoTitle.Should().Be("Test Video");
            state.FileExtension.Should().Be("mp4");
            state.VideoResolution.Should().Be("1920x1080");
            state.SizeInMegabytes.Should().BeApproximately(100.0, 0.01);
            state.FilePath.Should().Be(outputPath, "yt-dlp's reported output path is trusted");

            notifier.SetCursorLoadingCalls.Should().Be(1);
            notifier.SetCursorNormalCalls.Should().Be(1);
            notifier.ClearVideoUrlTextboxCalls.Should().Be(1);
            notifier.VideoInfos.Should().ContainSingle().Which.Title.Should().Be("Test Video");
            notifier.AddedToDownloadsList.Should().ContainSingle().Which.VideoSizeMb.Should().Be(100);
            notifier.RegisteredDownloads.Should().ContainSingle().Which.VideoTitle.Should().Be("Test Video");
            notifier.FinishedDownloads.Should().ContainSingle().Which.Should().BeSameAs(state);
            notifier.StatusUpdates.Should().BeEmpty();
        }

        [Fact]
        public async Task DownloadVideo_MetadataFailure_ThrowsToCallerAndResetsCursor()
        {
            var client = new FakeYtDlpClientAdapter { MetadataSucceeds = false };
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();

            Func<Task> act = () => CreateSut(client).DownloadVideo(notifier, Url, state);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*metadata fetch failed*");
            notifier.SetCursorNormalCalls.Should().Be(1, "the wait cursor must be reset even on failure");
            notifier.AddedToDownloadsList.Should().BeEmpty();
            notifier.FinishedDownloads.Should().BeEmpty();
        }

        [Fact]
        public async Task DownloadVideo_Cancellation_MarksCancelledAndCleansUp()
        {
            var client = new FakeYtDlpClientAdapter
            {
                DownloadException = new OperationCanceledException(),
            };
            client.Video.Formats = NinetyPlusTenMbFormats();
            var notifier = new FakeDownloadUiNotifier();
            using var state = new DownloadState();

            await CreateSut(client).DownloadVideo(notifier, Url, state);

            notifier.StatusUpdates.Should().ContainSingle().Which.Status.Should().Be(RobinVideoStatus.Cancelled);
            notifier.CleanedUpDownloads.Should().ContainSingle().Which.Should().Be("Test Video");
            notifier.FinishedDownloads.Should().BeEmpty();
            notifier.SetCursorNormalCalls.Should().Be(1);
        }

        [Fact]
        public async Task GetVideoTitle_ReturnsMetadataTitle()
        {
            var client = new FakeYtDlpClientAdapter();

            string title = await CreateSut(client).GetVideoTitle(Url);

            title.Should().Be("Test Video");
            client.MetadataCalls.Should().Be(1);
        }

        [Fact]
        public async Task GetVideoTitle_MetadataFailure_Throws()
        {
            var client = new FakeYtDlpClientAdapter { MetadataSucceeds = false };

            Func<Task> act = () => CreateSut(client).GetVideoTitle(Url).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*could not read video info*");
        }

        [Fact]
        public void EstimateSizeMb_SumsBestVideoAndBestAudioFileSizes()
        {
            var info = new VideoData { Formats = NinetyPlusTenMbFormats() };

            YtDlpVideoDownloader.EstimateSizeMb(info).Should().BeApproximately(100.0, 0.01);
        }

        [Fact]
        public void EstimateSizeMb_NoSizesInMetadata_ReturnsZero()
        {
            var info = new VideoData
            {
                Formats = new[]
                {
                    Fmt("mp4", "avc1", "none", height: 1080),
                    Fmt("m4a", "none", "mp4a", bitrate: 128),
                },
            };

            YtDlpVideoDownloader.EstimateSizeMb(info).Should().Be(0);
        }

        [Fact]
        public void EstimateSizeMb_UsesApproximateSizeWhenExactMissing()
        {
            var video = Fmt("mp4", "avc1", "none", height: 1080);
            video.ApproximateFileSize = 50L * 1024 * 1024;
            var info = new VideoData { Formats = new[] { video } };

            YtDlpVideoDownloader.EstimateSizeMb(info).Should().BeApproximately(50.0, 0.01);
        }

        [Fact]
        public void DescribeResolution_PrefersResolutionString_FallsBackToHeight()
        {
            YtDlpVideoDownloader.DescribeResolution(
                Fmt("mp4", "avc1", "none", height: 1080, resolution: "1920x1080"))
                .Should().Be("1920x1080");
            YtDlpVideoDownloader.DescribeResolution(
                Fmt("mp4", "avc1", "none", height: 1080))
                .Should().Be("1080p");
            YtDlpVideoDownloader.DescribeResolution(null)
                .Should().Be("UNKNOWN_RESOLUTION");
        }
    }
}
