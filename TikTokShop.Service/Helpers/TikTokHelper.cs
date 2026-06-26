using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TikTokShop.Service.Helpers
{
    internal class TikTokHelper
    {
        private static DateTime FromTikTokExpireUnix(long expireUnix)
        {
            return DateTimeOffset
                .FromUnixTimeSeconds(expireUnix)
                .UtcDateTime;
        }
    }
}
