using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Robin.Tests.Fakes
{
    /// <summary>
    /// Records YtDlpBootstrap's popup requests and runs the jobs inline so the
    /// consent/update flows can be tested headlessly (no WinForms dialogs).
    /// </summary>
    internal sealed class FakeYtDlpSetupUi : IYtDlpSetupUi
    {
        public bool ConfirmAnswer { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public List<(string Title, string[] JobNames, bool Marquee)> ProgressRuns { get; }
            = new List<(string, string[], bool)>();

        public bool ConfirmFirstRunDownload()
        {
            ConfirmCalls++;
            return ConfirmAnswer;
        }

        public async Task RunWithProgressAsync(string title, BinaryProgressJob[] jobs, bool marquee)
        {
            ProgressRuns.Add((title, jobs.Select(j => j.Name).ToArray(), marquee));
            foreach (BinaryProgressJob job in jobs)
                await job.Run(new Progress<double>());   // run inline, discard fractions
        }
    }
}
