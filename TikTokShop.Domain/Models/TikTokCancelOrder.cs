using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TikTokShop.Domain.Models
{
    public class TikTokCancellationRecord
    {
        public string ShopId { get; set; } = string.Empty;

        public string OrderId { get; set; } = string.Empty;

        public string? CancelId { get; set; }

        public string? BuyerUserId { get; set; }

        public string CancelStatus { get; set; } = string.Empty;

        public string? CancelType { get; set; }

        public string? CancelRole { get; set; }

        public string? CancelReason { get; set; }

        public long? CreateTime { get; set; }

        public long? UpdateTime { get; set; }

        public string Source { get; set; } = "WEBHOOK";

        public string? RawWebhookJson { get; set; }

        public string? RawCancellationJson { get; set; }

        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    }
}
