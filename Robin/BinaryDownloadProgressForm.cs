using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Robin
{
    /// <summary>
    /// Small modal popup with one labeled progress bar per binary job (first-run
    /// downloads, self-update, force-update). The work starts on Shown, runs
    /// sequentially off the UI thread, and the dialog closes itself when every
    /// job finishes; the first failure lands in Error. Determinate 0-100 bars
    /// normally; marquee for work with no measurable percentage (yt-dlp -U).
    /// </summary>
    internal sealed class BinaryDownloadProgressForm : Form
    {
        public Exception Error { get; private set; }

        private readonly Dictionary<string, ProgressBar> bars = new Dictionary<string, ProgressBar>();
        private readonly Dictionary<string, IProgress<double>> sinks = new Dictionary<string, IProgress<double>>();

        public BinaryDownloadProgressForm(string title, BinaryProgressJob[] jobs, bool marquee)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;               // not closable mid-download
            ClientSize = new Size(430, 16 + jobs.Length * 56);

            int y = 12;
            foreach (BinaryProgressJob job in jobs)
            {
                Controls.Add(new Label { Text = job.Name, Location = new Point(12, y), AutoSize = true });
                var bar = new ProgressBar
                {
                    Location = new Point(12, y + 22),
                    Size = new Size(406, 18),
                    Minimum = 0,
                    Maximum = 100,
                    Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous,
                };
                Controls.Add(bar);
                bars[job.Name] = bar;
                // Progress<T> posts to the SynchronizationContext captured HERE (the
                // UI thread constructing the dialog), so jobs may report from any thread.
                sinks[job.Name] = new Progress<double>(f => SetBarValue(bar, f));
                y += 56;
            }

            Shown += async (s, e) =>
            {
                try
                {
                    foreach (BinaryProgressJob job in jobs)
                    {
                        IProgress<double> sink = sinks[job.Name];
                        await Task.Run(() => job.Run(sink));
                        SetBarValue(bars[job.Name], 1.0);   // fill even if size was unknown
                    }
                }
                catch (Exception ex)
                {
                    Error = ex;
                }
                finally
                {
                    Close();
                }
            };
        }

        private static void SetBarValue(ProgressBar bar, double fraction)
        {
            if (bar.Style == ProgressBarStyle.Marquee) return;   // indeterminate stays animated
            bar.Value = Math.Max(0, Math.Min(100, (int)(fraction * 100)));
        }
    }
}
