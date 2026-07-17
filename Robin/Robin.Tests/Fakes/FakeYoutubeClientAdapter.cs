using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Robin.Tests.Fakes
{
    /// <summary>
    /// Canned IYoutubeClientAdapter: returns a configurable video and manifest,
    /// records download calls, and can throw or report progress on demand.
    /// </summary>
    internal sealed class FakeYoutubeClientAdapter : IYoutubeClientAdapter
    {
        public Video Video { get; set; } = YoutubeTestData.CreateVideo();
        public StreamManifest Manifest { get; set; } = YoutubeTestData.Manifest();
        public Exception ManifestException { get; set; }
        public Exception MuxedDownloadException { get; set; }
        public Exception DirectDownloadException { get; set; }
        public double[] ProgressSequence { get; set; } = new double[0];

        public List<string> GetVideoCalls { get; } = new List<string>();
        public List<string> GetManifestCalls { get; } = new List<string>();
        public List<IReadOnlyList<IStreamInfo>> MuxedDownloadCalls { get; } = new List<IReadOnlyList<IStreamInfo>>();
        public List<(string Url, string FilePath)> DirectDownloadCalls { get; } = new List<(string, string)>();

        public ValueTask<Video> GetVideoAsync(string url, CancellationToken cancellationToken = default)
        {
            GetVideoCalls.Add(url);
            return new ValueTask<Video>(Video);
        }

        public ValueTask<StreamManifest> GetManifestAsync(string url, CancellationToken cancellationToken = default)
        {
            GetManifestCalls.Add(url);
            if (ManifestException != null) throw ManifestException;
            return new ValueTask<StreamManifest>(Manifest);
        }

        public Task DownloadMuxedAsync(
            IReadOnlyList<IStreamInfo> streams,
            ConversionRequest request,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            MuxedDownloadCalls.Add(streams);
            if (MuxedDownloadException != null) return Task.FromException(MuxedDownloadException);
            foreach (double p in ProgressSequence)
            {
                progress?.Report(p);
            }
            return Task.CompletedTask;
        }

        public Task DownloadDirectAsync(
            string url,
            string filePath,
            Action<ConversionRequestBuilder> configure,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            DirectDownloadCalls.Add((url, filePath));
            if (DirectDownloadException != null) return Task.FromException(DirectDownloadException);
            foreach (double p in ProgressSequence)
            {
                progress?.Report(p);
            }
            return Task.CompletedTask;
        }
    }
}
