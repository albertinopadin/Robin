using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Robin
{
    internal interface YouTubeVideoDownloader
    {
        Task DownloadVideo(RobinForm form, string url);
    }
}
