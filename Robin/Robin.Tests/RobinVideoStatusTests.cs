using FluentAssertions;
using Xunit;

namespace Robin.Tests
{
    public class RobinVideoStatusTests
    {
        [Fact]
        public void Downloading_IsExpectedString()
        {
            RobinVideoStatus.Dowloading.Should().Be("Downloading");
        }

        [Fact]
        public void Done_IsExpectedString()
        {
            RobinVideoStatus.Done.Should().Be("Done");
        }

        [Fact]
        public void Cancelled_IsExpectedString()
        {
            RobinVideoStatus.Cancelled.Should().Be("Cancelled");
        }

        [Fact]
        public void Failed_IsExpectedString()
        {
            RobinVideoStatus.Failed.Should().Be("Failed");
        }

        [Fact]
        public void AllStatuses_AreDistinct()
        {
            var all = new[]
            {
                RobinVideoStatus.Dowloading,
                RobinVideoStatus.Done,
                RobinVideoStatus.Cancelled,
                RobinVideoStatus.Failed,
            };

            all.Should().OnlyHaveUniqueItems();
        }
    }
}
