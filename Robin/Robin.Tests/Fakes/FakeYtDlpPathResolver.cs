using System;
using System.Threading.Tasks;

namespace Robin.Tests.Fakes
{
    /// <summary>
    /// Overrides YtDlpPathResolver's virtual members so YtDlpBootstrap tests
    /// never touch the file system or the network.
    /// </summary>
    internal class FakeYtDlpPathResolver : YtDlpPathResolver
    {
        public bool Installed { get; set; }
        public bool NeedsUpdate { get; set; }
        public bool ThrowOnYtDlpDownload { get; set; }
        public int YtDlpDownloads { get; private set; }
        public int DenoDownloads { get; private set; }
        public int MarkUpdatedCalls { get; private set; }

        public override bool IsInstalled => Installed;
        public override bool ShouldUpdate(TimeSpan? maxAge = null) => NeedsUpdate;
        public override void MarkUpdated() => MarkUpdatedCalls++;   // keep tests off the real stamp file

        public override Task DownloadYtDlpAsync(IProgress<double> progress)
        {
            if (ThrowOnYtDlpDownload) throw new InvalidOperationException("yt-dlp download failed (fake)");
            YtDlpDownloads++;
            MarkUpdated();          // mirror the real method's stamping
            return Task.CompletedTask;
        }

        public override Task DownloadDenoAsync(IProgress<double> progress)
        {
            DenoDownloads++;
            return Task.CompletedTask;
        }
    }
}
