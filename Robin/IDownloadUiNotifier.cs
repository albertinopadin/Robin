using System.Windows.Forms;

namespace Robin
{
    /// <summary>
    /// UI operations the download pipeline needs from the main form.
    /// Extracted from RobinForm so the downloader can be unit-tested
    /// without instantiating any WinForms controls.
    /// </summary>
    internal interface IDownloadUiNotifier
    {
        void SetCursorLoading();
        void SetCursorNormal();
        void ClearVideoUrlTextbox();
        void SetVideoInfo(RobinVideoInfo info);
        void AddVideoToDownloadsList(DownloadState state, int videoSizeMb);
        void RegisterActiveDownload(string videoTitle, DownloadState state);
        void UpdateDownloadStatus(ListViewItem item, string status);
        void CleanupDownload(string videoTitle);
        void CancelProgressBarForVideo(DownloadState state);
        void DisableCancelButton(string videoTitle);
        void NotifyDownloadFinished(DownloadState state);
        void ReportDownloadProgress(DownloadState state, int progressValue);
    }
}
