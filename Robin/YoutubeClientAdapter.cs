using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace Robin
{
    internal sealed class YoutubeClientAdapter : IYoutubeClientAdapter
    {
        private readonly YoutubeClient youtube;

        public YoutubeClientAdapter(HttpClient httpClient)
        {
            youtube = new YoutubeClient(httpClient);
        }

        public ValueTask<YoutubeExplode.Videos.Video> GetVideoAsync(string url, CancellationToken cancellationToken = default)
            => youtube.Videos.GetAsync(url, cancellationToken);

        public ValueTask<StreamManifest> GetManifestAsync(string url, CancellationToken cancellationToken = default)
            => youtube.Videos.Streams.GetManifestAsync(url, cancellationToken);

        public Task DownloadMuxedAsync(
            IReadOnlyList<IStreamInfo> streams,
            ConversionRequest request,
            IProgress<double> progress,
            CancellationToken cancellationToken)
            => youtube.Videos.DownloadAsync(streams, request, progress, cancellationToken).AsTask();

        public Task DownloadDirectAsync(
            string url,
            string filePath,
            Action<ConversionRequestBuilder> configure,
            IProgress<double> progress,
            CancellationToken cancellationToken)
            => youtube.Videos.DownloadAsync(url, filePath, configure, progress, cancellationToken).AsTask();
    }
}
