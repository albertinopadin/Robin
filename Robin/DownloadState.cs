using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Robin
{
    public class DownloadState
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