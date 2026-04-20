using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Robin.Tests
{
    public class RobinUtilsTests : IDisposable
    {
        private readonly string _root;

        public RobinUtilsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "Robin.Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public void GetDirectoryThatBeginsWith_SingleMatch_ReturnsDirectoryName()
        {
            const string prefix = "Gyan.FFmpeg_Microsoft.Winget";
            const string fullName = prefix + "_1.0.0";
            Directory.CreateDirectory(Path.Combine(_root, fullName));

            var result = RobinUtils.GetDirectoryThatBeginsWith(prefix, _root);

            result.Should().Be(fullName);
        }

        [Fact]
        public void GetDirectoryThatBeginsWith_PrefixIsItselfDirectoryName_ReturnsIt()
        {
            const string prefix = "ffmpeg";
            Directory.CreateDirectory(Path.Combine(_root, prefix));

            var result = RobinUtils.GetDirectoryThatBeginsWith(prefix, _root);

            result.Should().Be(prefix);
        }

        [Fact]
        public void GetDirectoryThatBeginsWith_NoMatches_ThrowsDirectoryNotFoundException()
        {
            Directory.CreateDirectory(Path.Combine(_root, "unrelated"));

            Action act = () => RobinUtils.GetDirectoryThatBeginsWith("Gyan.FFmpeg", _root);

            act.Should().Throw<DirectoryNotFoundException>()
                .WithMessage("*Gyan.FFmpeg*")
                .WithMessage("*" + _root + "*");
        }

        [Fact]
        public void GetDirectoryThatBeginsWith_EmptyBaseDir_ThrowsDirectoryNotFoundException()
        {
            Action act = () => RobinUtils.GetDirectoryThatBeginsWith("anything", _root);

            act.Should().Throw<DirectoryNotFoundException>();
        }

        [Fact]
        public void GetDirectoryThatBeginsWith_MultipleMatches_ReturnsOneOfThem()
        {
            const string prefix = "ffmpeg";
            Directory.CreateDirectory(Path.Combine(_root, prefix + "-7.0.0"));
            Directory.CreateDirectory(Path.Combine(_root, prefix + "-7.1.0"));

            var result = RobinUtils.GetDirectoryThatBeginsWith(prefix, _root);

            result.Should().StartWith(prefix);
            Directory.Exists(Path.Combine(_root, result)).Should().BeTrue();
        }
    }
}
