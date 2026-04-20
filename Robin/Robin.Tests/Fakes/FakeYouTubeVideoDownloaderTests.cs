using FluentAssertions;
using Xunit;

namespace Robin.Tests.Fakes
{
    public class FakeYouTubeVideoDownloaderTests
    {
        [Fact]
        public async System.Threading.Tasks.Task GetVideoTitle_ReturnsConfiguredTitle_AndRecordsCall()
        {
            var fake = new FakeYouTubeVideoDownloader { NextTitle = "Hello" };

            var result = await fake.GetVideoTitle("https://youtu.be/abc");

            result.Should().Be("Hello");
            fake.GetVideoTitleCalls.Should().ContainSingle().Which.Should().Be("https://youtu.be/abc");
        }

        [Fact]
        public async System.Threading.Tasks.Task DownloadVideo_CompletesState_AndRecordsCall()
        {
            var fake = new FakeYouTubeVideoDownloader();
            using var state = new DownloadState();

            await fake.DownloadVideo(form: null, url: "https://youtu.be/abc", state: state);

            state.IsCompleted.Should().BeTrue();
            fake.DownloadVideoCalls.Should().ContainSingle().Which.Should().Be("https://youtu.be/abc");
        }

        [Fact]
        public void SeamIsInjectable_ViaInterface()
        {
            YouTubeVideoDownloader downloader = new FakeYouTubeVideoDownloader();

            downloader.Should().NotBeNull();
            downloader.Should().BeAssignableTo<YouTubeVideoDownloader>();
        }
    }
}
