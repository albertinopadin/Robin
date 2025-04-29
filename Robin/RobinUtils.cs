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

        public static string GetPathToFFMPEG()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                logger.Info("[GetPathToFFMPEG] app data path: {0}", appDataPath);
                string fullWinGetPackagesPath = Path.Combine(appDataPath, wingetPackagesPath);
                string ffmpegBaseWinGetFolderName = GetDirectoryThatBeginsWith(GyanFFmpegWingetDirName, fullWinGetPackagesPath);
                string ffmpegWinGetPkgPath = Path.Combine(fullWinGetPackagesPath, ffmpegBaseWinGetFolderName);
                string ffmpegVersionFolderName = GetDirectoryThatBeginsWith("ffmpeg", ffmpegWinGetPkgPath);
                string ffmpegExePath = Path.Combine(ffmpegWinGetPkgPath, ffmpegVersionFolderName, "bin", ffmpegExeFilename);
                return ffmpegExePath;
            }
            catch (Exception e)
            {
                DisplayAndLogException(e);
                throw e;
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
            MessageBox.Show($"Exception: {e.Message}\n\n{e.ToString()}");
            logger.Error("Exception: {0}\n:\n{1}", e.Message, e.ToString());
        }
    }
}
