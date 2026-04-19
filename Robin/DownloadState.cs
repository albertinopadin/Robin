using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YoutubeExplode.Videos.Streams;

namespace Robin
{
    public class DownloadState : IDisposable
    {
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public CancellationToken CancellationToken => CancellationTokenSource.Token;
        public Task DownloadTask { get; set; }
        public string VideoId { get; set; }
        public string VideoUrl { get; set; }
        public string VideoTitle { get; set; }
        public string VideoResolution { get; set; }
        public string Bitrate { get; set; }
        public double SizeInMegabytes { get; set; }
        public string FileExtension { get; set; }
        public string FilePath { get; set; }
        public ListViewItem ListViewItem { get; set; }
        public ProgressBar ProgressBar { get; set; }
        public IReadOnlyList<IStreamInfo> SelectedStreams { get; set; }
        public int LastProgressTickMs;
        public DateTime StartTime { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsCancelled { get; set; }

        public DownloadState()
        {
            CancellationTokenSource = new CancellationTokenSource();
            StartTime = DateTime.Now;
            IsCompleted = false;
            IsCancelled = false;
        }

        public void Dispose()
        {
            CancellationTokenSource?.Dispose();
        }
    }
}