using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Robin
{
    internal class RobinUtils
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string wingetPackagesPath = Path.Combine("Microsoft", "WinGet", "Packages");
        private static readonly string GyanFFmpegWingetDirName = "Gyan.FFmpeg_Microsoft.Winget";
        private static readonly string ffmpegExeFilename = "ffmpeg.exe";

        private static readonly string RobinErrorCaption = "Robin Error";

        private static readonly string FFmpegCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Robin", "ffmpeg_path.txt");

        public static string GetPathToFFMPEG()
        {
            try
            {
                if (File.Exists(FFmpegCachePath))
                {
                    string cached = File.ReadAllText(FFmpegCachePath).Trim();
                    if (File.Exists(cached))
                    {
                        logger.Info("[GetPathToFFMPEG] using cached path: {0}", cached);
                        return cached;
                    }
                    logger.Info("[GetPathToFFMPEG] cached path {0} is stale; re-resolving.", cached);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[GetPathToFFMPEG] cache read failed; re-resolving.");
            }

            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                logger.Info("[GetPathToFFMPEG] app data path: {0}", appDataPath);
                string fullWinGetPackagesPath = Path.Combine(appDataPath, wingetPackagesPath);
                string ffmpegBaseWinGetFolderName = GetDirectoryThatBeginsWith(GyanFFmpegWingetDirName, fullWinGetPackagesPath);
                string ffmpegWinGetPkgPath = Path.Combine(fullWinGetPackagesPath, ffmpegBaseWinGetFolderName);
                string ffmpegVersionFolderName = GetDirectoryThatBeginsWith("ffmpeg", ffmpegWinGetPkgPath);
                string ffmpegExePath = Path.Combine(ffmpegWinGetPkgPath, ffmpegVersionFolderName, "bin", ffmpegExeFilename);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FFmpegCachePath));
                    File.WriteAllText(FFmpegCachePath, ffmpegExePath);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "[GetPathToFFMPEG] cache write failed (non-fatal).");
                }

                return ffmpegExePath;
            }
            catch (Exception e)
            {
                DisplayAndLogException(e);
                throw;
            }
        }

        private static string GetDirectoryThatBeginsWith(string startsWithStr, string baseDir)
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

        public static void DisplayAndLogException(Exception e)
        {
            string msg = $"Exception: {e.Message}\n\n{e.ToString()}";
            MessageBox.Show(msg, RobinErrorCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            logger.Error("Exception: {0}\n:\n{1}", e.Message, e.ToString());
        }
    }
}
