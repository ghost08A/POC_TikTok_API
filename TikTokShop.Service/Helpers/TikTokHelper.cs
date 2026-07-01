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

        public static bool IsPossibleFinalRefundStatus(string returnStatus)
        {
            return returnStatus is
                "RETURN_OR_REFUND_REQUEST_SUCCESS" or
                "RETURN_OR_REFUND_REQUEST_COMPLETE" or
                "REFUND_COMPLETE" or
                "REFUND_COMPLETED";
        }
    }
}
