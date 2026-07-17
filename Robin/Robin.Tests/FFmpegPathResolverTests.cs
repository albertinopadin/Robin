using System;
using System.IO;
using FluentAssertions;
using Robin.Tests.Fakes;
using Xunit;

namespace Robin.Tests
{
    public class FFmpegPathResolverTests
    {
        private const string LocalAppData = @"C:\localapp";
        private const string CachePath = @"C:\localapp\Robin\ffmpeg_path.txt";
        private const string PackagesDir = @"C:\localapp\Microsoft\WinGet\Packages";
        private const string FFmpegPkgDir = PackagesDir + @"\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe";
        private const string FFmpegVersionDir = FFmpegPkgDir + @"\ffmpeg-7.1-full_build";
        private const string ResolvedExePath = FFmpegVersionDir + @"\bin\ffmpeg.exe";

        private static InMemoryFileSystem FileSystemWithWinGetInstall()
        {
            var fs = new InMemoryFileSystem();
            fs.AddDirectory(PackagesDir);
            fs.AddDirectory(FFmpegPkgDir);
            fs.AddDirectory(FFmpegVersionDir);
            return fs;
        }

        [Fact]
        public void Resolve_CacheHit_ReturnsCachedPathWithoutScanningWinGet()
        {
            var fs = new InMemoryFileSystem();
            fs.AddFile(CachePath, @"C:\cached\ffmpeg.exe");
            fs.AddFile(@"C:\cached\ffmpeg.exe", "");
            var sut = new FFmpegPathResolver(fs, LocalAppData);

            sut.Resolve().Should().Be(@"C:\cached\ffmpeg.exe");

            fs.GetDirectoriesCalls.Should().BeEmpty("a cache hit must not scan the WinGet packages directory");
            fs.WrittenFiles.Should().BeEmpty();
        }

        [Fact]
        public void Resolve_CacheHitWithWhitespace_TrimsCachedPath()
        {
            var fs = new InMemoryFileSystem();
            fs.AddFile(CachePath, "  C:\\cached\\ffmpeg.exe \r\n");
            fs.AddFile(@"C:\cached\ffmpeg.exe", "");
            var sut = new FFmpegPathResolver(fs, LocalAppData);

            sut.Resolve().Should().Be(@"C:\cached\ffmpeg.exe");
        }

        [Fact]
        public void Resolve_NoCache_ScansWinGetAndWritesCache()
        {
            var fs = FileSystemWithWinGetInstall();
            var sut = new FFmpegPathResolver(fs, LocalAppData);

            var result = sut.Resolve();

            result.Should().Be(ResolvedExePath);
            fs.CreatedDirectories.Should().Contain(@"C:\localapp\Robin");
            fs.WrittenFiles.Should().ContainKey(CachePath).WhoseValue.Should().Be(ResolvedExePath);
        }

        [Fact]
        public void Resolve_StaleCache_ReScansWinGetAndRewritesCache()
        {
            var fs = FileSystemWithWinGetInstall();
            fs.AddFile(CachePath, @"C:\gone\ffmpeg.exe");   // cached exe no longer exists
            var sut = new FFmpegPathResolver(fs, LocalAppData);

            var result = sut.Resolve();

            result.Should().Be(ResolvedExePath);
            fs.GetDirectoriesCalls.Should().NotBeEmpty();
            fs.WrittenFiles.Should().ContainKey(CachePath).WhoseValue.Should().Be(ResolvedExePath);
        }

        [Fact]
        public void Resolve_CacheReadThrows_FallsBackToWinGetScan()
        {
            var fs = FileSystemWithWinGetInstall();
            fs.AddFile(CachePath, "irrelevant");
            fs.ReadException = new IOException("cache file locked");
            var sut = new FFmpegPathResolver(fs, LocalAppData);

            sut.Resolve().Should().Be(ResolvedExePath);
        }

        [Fact]
        public void Resolve_NoWinGetInstall_ThrowsDirectoryNotFoundException()
        {
            var fs = new InMemoryFileSystem();
            var sut = new FFmpegPathResolver(fs, LocalAppData);

            Action act = () => sut.Resolve();

            act.Should().Throw<DirectoryNotFoundException>();
        }

        [Fact]
        public void Resolve_PackagesDirExistsButNoFFmpegPackage_ThrowsDirectoryNotFoundException()
        {
            var fs = new InMemoryFileSystem();
            fs.AddDirectory(PackagesDir);
            fs.AddDirectory(PackagesDir + @"\SomeOther.Package_abc");
            var sut = new FFmpegPathResolver(fs, LocalAppData);

            Action act = () => sut.Resolve();

            act.Should().Throw<DirectoryNotFoundException>()
                .WithMessage("*Gyan.FFmpeg_Microsoft.Winget*");
        }
    }
}
