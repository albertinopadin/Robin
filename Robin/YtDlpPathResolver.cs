using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace Robin
{
    /// <summary>
    /// Locates, fetches, and updates the yt-dlp.exe / deno.exe pair under
    /// %LOCALAPPDATA%\Robin. Downloads are done in-house (not via
    /// YoutubeDLSharp.Utils, which reports no progress) so the setup popups can
    /// show real progress bars. Virtual members are the YtDlpBootstrapTests seam.
    /// </summary>
    internal class YtDlpPathResolver
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        // Same endpoints YoutubeDLSharp.Utils 1.2.0 uses (verified against the assembly).
        private const string YtDlpDownloadUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string DenoVersionUrl = "https://dl.deno.land/release-latest.txt";
        private const string DenoDownloadUrlTemplate =
            "https://dl.deno.land/release/{0}/deno-{1}.zip";
        private const string DenoTarget = "x86_64-pc-windows-msvc";

        // Infinite timeout: the default 100 s applies to the whole GetAsync operation
        // and would abort the ~40 MB Deno fetch on slow links. Streamed reads below
        // fail on their own if the connection actually drops.
        private static readonly HttpClient http = new HttpClient
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        private static readonly string CacheDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Robin");

        public static YtDlpPathResolver Default() => new YtDlpPathResolver();

        public string YtDlpPath => Path.Combine(CacheDir, "yt-dlp.exe");
        public string DenoPath => Path.Combine(CacheDir, "deno.exe");

        // "First run" for the consent popup = either binary missing.
        public virtual bool IsInstalled => File.Exists(YtDlpPath) && File.Exists(DenoPath);

        // Synchronous fetch-if-missing fallbacks for the lazy adapter path in
        // YtDlpVideoDownloader (already off the UI thread there). In the normal flow
        // the bootstrap has already downloaded both with consent, so these no-op.
        public string Resolve()
        {
            if (!File.Exists(YtDlpPath))
                DownloadYtDlpAsync(progress: null).GetAwaiter().GetResult();
            return YtDlpPath;
        }

        public string ResolveDeno()
        {
            if (!File.Exists(DenoPath))
                DownloadDenoAsync(progress: null).GetAwaiter().GetResult();
            return DenoPath;
        }

        // Always fetches the latest release (no version check) — used by the
        // first-run download (YtDlpBootstrap) and the force-update menu buttons.
        public virtual async Task DownloadYtDlpAsync(IProgress<double> progress)
        {
            Directory.CreateDirectory(CacheDir);
            await DownloadFileAsync(YtDlpDownloadUrl, YtDlpPath, progress).ConfigureAwait(false);
            MarkUpdated();   // fresh binary — suppress the same-day `yt-dlp -U` check
        }

        public virtual async Task DownloadDenoAsync(IProgress<double> progress)
        {
            Directory.CreateDirectory(CacheDir);
            string version = (await http.GetStringAsync(DenoVersionUrl).ConfigureAwait(false)).Trim();
            string zipPath = Path.Combine(CacheDir, "deno.zip");
            string url = string.Format(DenoDownloadUrlTemplate, version, DenoTarget);
            await DownloadFileAsync(url, zipPath, progress).ConfigureAwait(false);
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry entry = archive.GetEntry("deno.exe")
                    ?? throw new InvalidOperationException("deno.exe not found in the Deno release zip.");
                entry.ExtractToFile(DenoPath, overwrite: true);
            }
            File.Delete(zipPath);
        }

        // Streamed download reporting 0..1 fractions; writes to "<dest>.download" and
        // swaps in at the end, so a failed fetch never leaves a truncated binary.
        private static async Task DownloadFileAsync(string url, string destination, IProgress<double> progress)
        {
            using (HttpResponseMessage response =
                await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                string tempPath = destination + ".download";
                using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream target = File.Create(tempPath))
                {
                    byte[] buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        target.Write(buffer, 0, read);
                        done += read;
                        if (total > 0) progress?.Report((double)done / total);
                    }
                }
                File.Delete(destination);   // no-op when absent
                File.Move(tempPath, destination);
                progress?.Report(1.0);
            }
        }

        // --- Update throttle (used by YtDlpBootstrap's daily `yt-dlp -U`) ---
        private string StampPath => Path.Combine(CacheDir, ".ytdlp-update-stamp");

        public virtual bool ShouldUpdate(TimeSpan? maxAge = null)
        {
            TimeSpan window = maxAge ?? TimeSpan.FromHours(24);
            try
            {
                return !File.Exists(StampPath)
                    || DateTime.UtcNow - File.GetLastWriteTimeUtc(StampPath) > window;
            }
            catch { return true; }
        }

        public virtual void MarkUpdated()
        {
            try { Directory.CreateDirectory(CacheDir); File.WriteAllText(StampPath, ""); }
            catch (Exception ex) { logger.Warn(ex, "[YtDlpPathResolver] update stamp write failed (non-fatal)."); }
        }
    }
}
