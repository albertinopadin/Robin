using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace Robin
{
    /// <summary>
    /// Thin seam over the YoutubeClient operations the downloader actually uses,
    /// so tests can inject canned videos, manifests, and download behavior.
    /// YoutubeExplode domain types intentionally leak through — the goal is
    /// unit-testable orchestration, not library swap-out.
    /// </summary>
    internal interface IYoutubeClientAdapter
    {
        ValueTask<YoutubeExplode.Videos.Video> GetVideoAsync(string url, CancellationToken cancellationToken = default);
        ValueTask<StreamManifest> GetManifestAsync(string url, CancellationToken cancellationToken = default);
        Task DownloadMuxedAsync(
            IReadOnlyList<IStreamInfo> streams,
            ConversionRequest request,
            IProgress<double> progress,
            CancellationToken cancellationToken);
        Task DownloadDirectAsync(
            string url,
            string filePath,
            Action<ConversionRequestBuilder> configure,
            IProgress<double> progress,
            CancellationToken cancellationToken);
    }
}
