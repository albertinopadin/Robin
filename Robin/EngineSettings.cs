using System;
using System.IO;

namespace Robin
{
    internal static class EngineSettings
    {
        public const string YtDlp = "ytdlp";
        public const string Explode = "explode";

        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Robin", "download_engine.txt");

        // Default is yt-dlp.
        public static string Get()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string v = File.ReadAllText(FilePath).Trim();
                    if (v == YtDlp || v == Explode) return v;
                }
            }
            catch { /* fall through to default */ }
            return YtDlp;
        }

        public static void Set(string engine)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, engine == Explode ? Explode : YtDlp);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "[EngineSettings] write failed (non-fatal).");
            }
        }

        // User-visible strings say "yt-dlp" (with the dash). The persisted token
        // above stays "ytdlp" and is never rendered in the UI.
        public static string DisplayName(string engine)
            => engine == Explode ? "YouTube Explode" : "yt-dlp";
    }
}
