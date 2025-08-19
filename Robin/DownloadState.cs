using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Robin
{
    public class DownloadState
    {
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public Task DownloadTask { get; set; }
        public string VideoUrl { get; set; }
        public string VideoTitle { get; set; }
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