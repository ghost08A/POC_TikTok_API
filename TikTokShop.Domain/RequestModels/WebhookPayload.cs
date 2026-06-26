using System.Text.Json;
using System.Text.Json.Serialization;

namespace TikTokShop.Domain.RequestModels;

/// <summary>
/// โครงสร้าง Webhook Payload ที่ TikTok ส่งมาให้เรา (Push Model)
/// ใช้สำหรับ Deserialize Body ของ POST /api/poc/webhook
/// </summary>
public class WebhookPayload
{
    /// <summary>ประเภทของ Event เช่น "order" หรือ "logistics"</summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    /// <summary>Timestamp Unix ที่ Event เกิดขึ้น</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>Shop ID ของร้านค้าที่เกิด Event</summary>
    [JsonPropertyName("shop_id")]
    public string ShopId { get; set; } = string.Empty;

    /// <summary>ข้อมูลจริงๆ ของ Event (JSON Object ด้านใน)</summary>
    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}

/// <summary>ข้อมูล Order ภายใน Webhook Event</summary>
public class WebhookOrderData
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("order_status")]
    public string OrderStatus { get; set; } = string.Empty;

    [JsonPropertyName("update_time")]
    public long UpdateTime { get; set; }
}


public class WebhookCancellationData
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("cancel_status")]
    public string CancelStatus { get; set; } = string.Empty;

    [JsonPropertyName("update_time")]
    public long UpdateTime { get; set; }
}