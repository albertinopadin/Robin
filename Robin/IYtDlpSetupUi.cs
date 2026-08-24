using System;
using System.Threading.Tasks;

namespace Robin
{
    /// <summary>
    /// One named unit of binary work shown as its own progress bar in the popup.
    /// Run receives the bar's sink and reports 0..1 fractions (ignored in marquee mode).
    /// </summary>
    internal sealed class BinaryProgressJob
    {
        public string Name { get; }
        public Func<IProgress<double>, Task> Run { get; }

        public BinaryProgressJob(string name, Func<IProgress<double>, Task> run)
        {
            Name = name;
            Run = run;
        }
    }

    /// <summary>
    /// The popups YtDlpBootstrap needs. RobinForm implements this;
    /// tests use FakeYtDlpSetupUi.
    /// </summary>
    internal interface IYtDlpSetupUi
    {
        // First-run popup: "download yt-dlp + Deno now?" — true = Yes.
        bool ConfirmFirstRunDownload();

        // Show a popup with one progress bar per job, run the jobs sequentially,
        // and return when all are done; propagates the first job failure.
        // marquee = true for work with no measurable percentage (yt-dlp -U).
        Task RunWithProgressAsync(string title, BinaryProgressJob[] jobs, bool marquee);
    }
}
