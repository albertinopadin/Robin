using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VideoLibrary;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

namespace Robin
{
    internal class YouTubeExplodeVideoDownloader : YouTubeVideoDownloader
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        YoutubeClient youtube;
        string baseFilePath;
        string ffmpegPath;

        public YouTubeExplodeVideoDownloader(string baseFilePath)
        {
            this.baseFilePath = baseFilePath;
            this.ffmpegPath = GetPathToFFMPEG();
            youtube = new YoutubeClient();
        }

        private string GetPathToFFMPEG()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                logger.Info("[GetPathToFFMPEG] app data path: {0}", appDataPath);
                string wingetPackagesPath = Path.Combine("Microsoft", "WinGet", "Packages");
                string fullWinGetPackagesPath = Path.Combine(appDataPath, wingetPackagesPath);
                string ffmpegBaseWinGetFolderName = GetDirectoryThatBeginsWith("Gyan.FFmpeg_Microsoft.Winget", fullWinGetPackagesPath);
                string ffmpegWinGetPkgPath = Path.Combine(fullWinGetPackagesPath, ffmpegBaseWinGetFolderName);
                string ffmpegVersionFolderName = GetDirectoryThatBeginsWith("ffmpeg", ffmpegWinGetPkgPath);
                string ffmpegExePath = Path.Combine(ffmpegWinGetPkgPath, ffmpegVersionFolderName, "bin", "ffmpeg.exe");
                return ffmpegExePath;
            }
            catch (Exception e)
            {
                DisplayAndLogException(e);
                throw e;
            }
        }

        private string GetDirectoryThatBeginsWith(string startsWithStr, string baseDir)
        {
            logger.Info("[GetDirectoryThatBeginsWith] Base Dir: " + baseDir);
            string[] matchingDirs = Directory.GetDirectories(baseDir, startsWithStr + "*", SearchOption.TopDirectoryOnly);

            if (matchingDirs.Length > 0)
            {
                if (matchingDirs.Length > 1)
                {
                    logger.Warn("[GetDirectoryThatBeginsWith] More than one matching directory starts with {0}", startsWithStr);
                }

                DirectoryInfo directoryInfo = new DirectoryInfo(matchingDirs.First());
                return directoryInfo.Name;
            }

            throw new DirectoryNotFoundException("Directory starting with " + startsWithStr + " in base dir " + baseDir + " not found.");
        }

        public async Task DownloadVideo(RobinForm form, string url)
        {
            await DownloadBestVideo(form, url);
        }

        private async Task DownloadBestVideo(RobinForm form, string videoUrl)
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

            var videoStreams = streamManifest.GetVideoStreams();
            logger.Info($"streamManifest video streams count: {videoStreams.Count()}");

            if (videoStreams.Count() > 0)
            {
                var maxVideoQualityStreamInfo = videoStreams.GetWithHighestVideoQuality();
                logger.Info($"maxVideoQualityStreamInfo: {maxVideoQualityStreamInfo}");

                var videoInfo = await youtube.Videos.GetAsync(videoUrl);

                form.SetVideoInfo(new RobinVideoInfo(videoInfo.Title,
                                                     maxVideoQualityStreamInfo.Container.Name,
                                                     maxVideoQualityStreamInfo.VideoResolution.ToString(),
                                                     maxVideoQualityStreamInfo.Bitrate.ToString(),
                                                     maxVideoQualityStreamInfo.Size.MegaBytes.ToString("n2")));

                // TODO: figure out how to make this work:
                //if (!backgroundWorker1.IsBusy)
                //{
                //    backgroundWorker1.RunWorkerAsync();
                //}

                await DownloadVideo_Explode(form,
                                            youtube,
                                            videoUrl,
                                            videoInfo,
                                            (int)maxVideoQualityStreamInfo.Size.MegaBytes,
                                            maxVideoQualityStreamInfo.Container.Name);
            }
            else
            {
                MessageBox.Show($"No video streams found for URL {videoUrl}.");
            }
        }
        private string MakeValidVideoTitle(string rawVideoTitle)
        {
            logger.Info($"Raw video title: {rawVideoTitle}");
            return string.Concat(rawVideoTitle.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        }

        private async Task DownloadVideo_Explode(RobinForm form,
                                                 YoutubeClient youtube,
                                                 string videoUrl,
                                                 YoutubeExplode.Videos.Video videoInfo,
                                                 int videoSizeInMegabytes,
                                                 string extension)
        {
            string validVideoTitle = MakeValidVideoTitle(videoInfo.Title);
            ListViewItem listItem = form.AddVideoToDownloadsList(validVideoTitle, videoSizeInMegabytes);
            string videoPath = Path.Combine(baseFilePath, $"{validVideoTitle}.{extension}");

            try
            {
                await DownloadVideoAsync_Explode(form, listItem, youtube, videoInfo.Id, videoPath, videoSizeInMegabytes);
            }
            catch (Exception e)
            {
                if (videoPath.Split('/').Contains("live"))
                {
                    try
                    {
                        videoPath = videoPath.Replace("/live/", "/watch?v=");
                        logger.Info("[DownloadVideo_Explode] Replaced live video path with watch: {0}", videoPath);
                        await DownloadVideoAsync_Explode(form, listItem, youtube, videoInfo.Id, videoPath, videoSizeInMegabytes);
                    }
                    catch (Exception ex)
                    {
                        DisplayAndLogException(ex);
                    }
                }
                else
                {
                    DisplayAndLogException(e);
                }
            }
        }

        private async Task DownloadVideoAsync_Explode(RobinForm form,
                                                      ListViewItem listItem,
                                                      YoutubeClient youtube,
                                                      string videoId,
                                                      string videoPath,
                                                      int videoSizeInMegabytes)
        {
            await youtube.Videos.DownloadAsync(videoId, 
                                               videoPath, 
                                               converter => converter.SetFFmpegPath(this.ffmpegPath), 
                                               new Progress<double>(progress =>
            {
                form.SetProgressBarValue(listItem, (int)(progress * videoSizeInMegabytes));
            }));

            form.NotifyDownloadFinished(listItem, videoPath, videoSizeInMegabytes);
        }

        private void DisplayAndLogException(Exception e)
        {
            MessageBox.Show($"Exception: {e.Message}\n\n{e.ToString()}");
            logger.Error("Exception: {0}\n:\n{1}", e.Message, e.ToString());
        }
    }
}
