using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TikTokShop.Domain.Models
{
    public class TikTokOrderDetailApiResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public OrderDetailDataSection? Data { get; set; }
    }

    public class OrderDetailDataSection
    {
        // 💡 TikTok คายมาเป็น Array เสมอ
        [JsonPropertyName("orders")]
        public List<TikTokOrderInfo>? Orders { get; set; }
    }

    public class TikTokOrderInfo
    {
        [JsonPropertyName("id")] 
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("payment")]
        public TikTokPaymentInfo? Payment { get; set; }
    }

    public class TikTokPaymentInfo
    {
        [JsonPropertyName("total_amount")]
        public string TotalAmount { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;
    }
}
