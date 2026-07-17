using System;
using System.IO;
using System.Linq;

namespace Robin
{
    /// <summary>
    /// Resolves the FFmpeg executable path: first from the cache file under
    /// %LOCALAPPDATA%\Robin, then by scanning the WinGet packages directory.
    /// Extracted from RobinUtils.GetPathToFFMPEG; cache file location and
    /// format are unchanged.
    /// </summary>
    internal sealed class FFmpegPathResolver
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string wingetPackagesPath = Path.Combine("Microsoft", "WinGet", "Packages");
        private const string GyanFFmpegWingetDirName = "Gyan.FFmpeg_Microsoft.Winget";
        private const string ffmpegExeFilename = "ffmpeg.exe";

        private readonly IFileSystem fileSystem;
        private readonly string localAppDataPath;
        private readonly string cachePath;

        public FFmpegPathResolver(IFileSystem fileSystem, string localAppDataPath)
        {
            this.fileSystem = fileSystem;
            this.localAppDataPath = localAppDataPath;
            cachePath = Path.Combine(localAppDataPath, "Robin", "ffmpeg_path.txt");
        }

        public static FFmpegPathResolver Default()
            => new FFmpegPathResolver(
                new RealFileSystem(),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        public string Resolve()
        {
            if (TryReadCache(out string cached))
            {
                return cached;
            }

            string resolved = ResolveFromWinGet();
            TryWriteCache(resolved);
            return resolved;
        }

        private bool TryReadCache(out string path)
        {
            path = null;
            try
            {
                if (fileSystem.FileExists(cachePath))
                {
                    string cached = fileSystem.ReadAllText(cachePath).Trim();
                    if (fileSystem.FileExists(cached))
                    {
                        logger.Info("[FFmpegPathResolver] using cached path: {0}", cached);
                        path = cached;
                        return true;
                    }
                    logger.Info("[FFmpegPathResolver] cached path {0} is stale; re-resolving.", cached);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[FFmpegPathResolver] cache read failed; re-resolving.");
            }
            return false;
        }

        private string ResolveFromWinGet()
        {
            logger.Info("[FFmpegPathResolver] app data path: {0}", localAppDataPath);
            string fullWinGetPackagesPath = Path.Combine(localAppDataPath, wingetPackagesPath);
            string ffmpegBaseWinGetFolderName = GetDirectoryNameThatBeginsWith(fileSystem, GyanFFmpegWingetDirName, fullWinGetPackagesPath);
            string ffmpegWinGetPkgPath = Path.Combine(fullWinGetPackagesPath, ffmpegBaseWinGetFolderName);
            string ffmpegVersionFolderName = GetDirectoryNameThatBeginsWith(fileSystem, "ffmpeg", ffmpegWinGetPkgPath);
            return Path.Combine(ffmpegWinGetPkgPath, ffmpegVersionFolderName, "bin", ffmpegExeFilename);
        }

        private void TryWriteCache(string resolvedPath)
        {
            try
            {
                fileSystem.CreateDirectory(Path.GetDirectoryName(cachePath));
                fileSystem.WriteAllText(cachePath, resolvedPath);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[FFmpegPathResolver] cache write failed (non-fatal).");
            }
        }

        internal static string GetDirectoryNameThatBeginsWith(IFileSystem fileSystem, string startsWithStr, string baseDir)
        {
            logger.Info("[GetDirectoryThatBeginsWith] Base Dir: " + baseDir);
            string[] matchingDirs = fileSystem.GetDirectories(baseDir, startsWithStr + "*");

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
    }
}
