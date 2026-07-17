using System.Collections.Generic;
using System.Windows.Forms;

namespace Robin.Tests.Fakes
{
    /// <summary>
    /// Records every IDownloadUiNotifier call so tests can assert on the
    /// downloader's UI interactions without any WinForms controls.
    /// </summary>
    internal sealed class FakeDownloadUiNotifier : IDownloadUiNotifier
    {
        private readonly object gate = new object();
        private readonly List<int> progressReports = new List<int>();

        public int SetCursorLoadingCalls { get; private set; }
        public int SetCursorNormalCalls { get; private set; }
        public int ClearVideoUrlTextboxCalls { get; private set; }
        public List<RobinVideoInfo> VideoInfos { get; } = new List<RobinVideoInfo>();
        public List<(DownloadState State, int VideoSizeMb)> AddedToDownloadsList { get; } = new List<(DownloadState, int)>();
        public List<(string VideoTitle, DownloadState State)> RegisteredDownloads { get; } = new List<(string, DownloadState)>();
        public List<(ListViewItem Item, string Status)> StatusUpdates { get; } = new List<(ListViewItem, string)>();
        public List<string> CleanedUpDownloads { get; } = new List<string>();
        public List<DownloadState> CancelledProgressBars { get; } = new List<DownloadState>();
        public List<string> DisabledCancelButtons { get; } = new List<string>();
        public List<DownloadState> FinishedDownloads { get; } = new List<DownloadState>();

        public void SetCursorLoading() => SetCursorLoadingCalls++;
        public void SetCursorNormal() => SetCursorNormalCalls++;
        public void ClearVideoUrlTextbox() => ClearVideoUrlTextboxCalls++;
        public void SetVideoInfo(RobinVideoInfo info) => VideoInfos.Add(info);
        public void AddVideoToDownloadsList(DownloadState state, int videoSizeMb) => AddedToDownloadsList.Add((state, videoSizeMb));
        public void RegisterActiveDownload(string videoTitle, DownloadState state) => RegisteredDownloads.Add((videoTitle, state));
        public void UpdateDownloadStatus(ListViewItem item, string status) => StatusUpdates.Add((item, status));
        public void CleanupDownload(string videoTitle) => CleanedUpDownloads.Add(videoTitle);
        public void CancelProgressBarForVideo(DownloadState state) => CancelledProgressBars.Add(state);
        public void DisableCancelButton(string videoTitle) => DisabledCancelButtons.Add(videoTitle);
        public void NotifyDownloadFinished(DownloadState state) => FinishedDownloads.Add(state);

        // Progress<double> posts callbacks asynchronously, so this one needs to be thread-safe.
        public void ReportDownloadProgress(DownloadState state, int progressValue)
        {
            lock (gate)
            {
                progressReports.Add(progressValue);
            }
        }

        public int[] ProgressReportsSnapshot()
        {
            lock (gate)
            {
                return progressReports.ToArray();
            }
        }
    }
}
