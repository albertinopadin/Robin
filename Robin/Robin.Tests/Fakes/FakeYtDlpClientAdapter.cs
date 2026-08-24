using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;

namespace Robin.Tests.Fakes
{
    /// <summary>
    /// Canned yt-dlp metadata/progress/download results so YtDlpVideoDownloader
    /// can be tested without spawning a real yt-dlp process.
    /// </summary>
    internal sealed class FakeYtDlpClientAdapter : IYtDlpClientAdapter
    {
        public VideoData Video { get; set; } = new VideoData { Title = "Test Video", ID = "abc123" };
        public bool MetadataSucceeds { get; set; } = true;
        public bool DownloadSucceeds { get; set; } = true;
        public Exception DownloadException { get; set; }
        public string OutputPath { get; set; }
        public List<DownloadProgress> ProgressSequence { get; set; } = new List<DownloadProgress>();
        public List<string> DownloadCalls { get; } = new List<string>();
        public int MetadataCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<RunResult<VideoData>> FetchMetadataAsync(string url, CancellationToken ct)
        {
            MetadataCalls++;
            return Task.FromResult(MetadataSucceeds
                ? new RunResult<VideoData>(true, new string[0], Video)
                : new RunResult<VideoData>(false, new[] { "metadata error (fake)" }, null));
        }

        public Task<RunResult<string>> DownloadAsync(
            string url, string format, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            DownloadCalls.Add(url);
            if (DownloadException != null) throw DownloadException;
            foreach (var p in ProgressSequence) progress?.Report(p);
            return Task.FromResult(new RunResult<string>(
                DownloadSucceeds,
                DownloadSucceeds ? new string[0] : new[] { "download error (fake)" },
                OutputPath));
        }

        public Task<string> UpdateAsync()
        {
            UpdateCalls++;
            return Task.FromResult("yt-dlp is up to date (fake)");
        }
    }
}
