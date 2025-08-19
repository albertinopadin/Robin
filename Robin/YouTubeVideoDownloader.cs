using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Robin
{
    internal interface YouTubeVideoDownloader
    {
        ValueTask<string> GetVideoTitle(string url);
        void DownloadVideo(RobinForm form, string url);
        void DownloadVideo(RobinForm form, string url, CancellationToken cancellationToken);
    }
}
