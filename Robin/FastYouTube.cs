using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;

using VideoLibrary;

namespace Robin
{
    class CustomHandler
    {
        public HttpMessageHandler GetHandler()
        {
            CookieContainer cookieContainer = new CookieContainer();
            cookieContainer.Add(new Cookie("CONSENT", "YES+cb", "/", "youtube.com"));
            return new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = cookieContainer
            };
        }
    }

    internal class FastYouTube: YouTube
    {
        private long chunkSize = 10_485_760;
        private long _fileSize = 0L;
        private HttpClient _client = new HttpClient();

        protected override HttpClient MakeClient(HttpMessageHandler handler)
        {
            return base.MakeClient(handler);
        }

        protected override HttpMessageHandler MakeHandler()
        {
            return new CustomHandler().GetHandler();
        }

        public async Task CreateDownloadAsync(Uri uri, string filepath, IProgress<Tuple<long, long>> progress)
        {
            Console.WriteLine($"In CreateDownloadAsync; URI: {uri}, download folder: {filepath}");
            var totalBytesCopied = 0L;
            _fileSize = await GetContentLengthAsync(uri.AbsoluteUri) ?? 0;
            if (_fileSize == 0)
            {
                throw new Exception("File has no content!");
            }

            using (Stream output = File.OpenWrite(filepath))
            {
                var segmentCount = (int)Math.Ceiling(1.0 * _fileSize / chunkSize);
                for (var i = 0; i < segmentCount; i++)
                {
                    var from = i * chunkSize;
                    var to = (i + 1) * chunkSize - 1;
                    var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Range = new RangeHeaderValue(from, to);
                    using (request)
                    {
                        // Download Stream
                        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                        if (response.IsSuccessStatusCode)
                        {
                            response.EnsureSuccessStatusCode();
                        }

                        var stream = await response.Content.ReadAsStreamAsync();

                        // File stream
                        var buffer = new byte[81920];
                        int bytesCopied;
                        do
                        {
                            bytesCopied = await stream.ReadAsync(buffer, 0, buffer.Length);
                            output.Write(buffer, 0, bytesCopied);
                            totalBytesCopied += bytesCopied;
                            progress.Report(new Tuple<long, long>(totalBytesCopied, _fileSize));
                        } while (bytesCopied > 0);
                    }
                }
            }
        }

        private async Task<long?> GetContentLengthAsync(string requestUri, bool ensureSuccess = true)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Head, requestUri))
            {
                var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (ensureSuccess)
                {
                    response.EnsureSuccessStatusCode();
                }

                return response.Content.Headers.ContentLength;
            }
        }
    }
}
