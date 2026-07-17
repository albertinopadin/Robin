using System;
using System.Windows.Forms;

namespace Robin
{
    internal class RobinUtils
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string RobinErrorCaption = "Robin Error";

        public static string GetPathToFFMPEG()
        {
            try
            {
                return FFmpegPathResolver.Default().Resolve();
            }
            catch (Exception e)
            {
                DisplayAndLogException(e);
                throw;
            }
        }

        internal static string GetDirectoryThatBeginsWith(string startsWithStr, string baseDir)
            => FFmpegPathResolver.GetDirectoryNameThatBeginsWith(new RealFileSystem(), startsWithStr, baseDir);

        public static void DisplayAndLogException(Exception e)
        {
            string msg = $"Exception: {e.Message}\n\n{e.ToString()}";
            MessageBox.Show(msg, RobinErrorCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            logger.Error("Exception: {0}\n:\n{1}", e.Message, e.ToString());
        }
    }
}
