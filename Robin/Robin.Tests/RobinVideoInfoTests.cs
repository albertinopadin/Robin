using FluentAssertions;
using Xunit;

namespace Robin.Tests
{
    public class RobinVideoInfoTests
    {
        [Fact]
        public void Constructor_AllFields_SetsProperties()
        {
            var info = new RobinVideoInfo(
                Title: "Sample Video",
                Extension: "mp4",
                Resolution: "1080p",
                Bitrate: "128kbps",
                Size: "50MB");

            info.Title.Should().Be("Sample Video");
            info.Extension.Should().Be("mp4");
            info.Resolution.Should().Be("1080p");
            info.Bitrate.Should().Be("128kbps");
            info.Size.Should().Be("50MB");
        }

        [Fact]
        public void Equality_SameValues_AreEqual()
        {
            var a = new RobinVideoInfo("T", "mp4", "720p", "96kbps", "10MB");
            var b = new RobinVideoInfo("T", "mp4", "720p", "96kbps", "10MB");

            a.Should().Be(b);
            (a == b).Should().BeTrue();
            a.GetHashCode().Should().Be(b.GetHashCode());
        }

        [Fact]
        public void Equality_DifferentTitle_AreNotEqual()
        {
            var a = new RobinVideoInfo("One", "mp4", "720p", "96kbps", "10MB");
            var b = new RobinVideoInfo("Two", "mp4", "720p", "96kbps", "10MB");

            a.Should().NotBe(b);
        }

        [Fact]
        public void WithExpression_OverridesField_DoesNotMutateOriginal()
        {
            var original = new RobinVideoInfo("T", "mp4", "720p", "96kbps", "10MB");

            var modified = original with { Resolution = "1080p" };

            modified.Resolution.Should().Be("1080p");
            original.Resolution.Should().Be("720p");
            modified.Should().NotBe(original);
        }
    }
}
