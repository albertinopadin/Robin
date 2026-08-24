namespace Robin
{
    internal static class DownloaderFactory
    {
        public static YouTubeVideoDownloader Create(string baseFilePath, string engine)
            => engine == EngineSettings.Explode
                ? (YouTubeVideoDownloader)new YouTubeExplodeVideoDownloader(baseFilePath)
                : new YtDlpVideoDownloader(baseFilePath);
    }
}
