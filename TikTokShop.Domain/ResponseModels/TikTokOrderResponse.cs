using System.Text.Json.Serialization;

namespace TikTokShop.Domain.ResponseModels;

// =============================================================
// Raw Response Models สำหรับ Deserialize JSON จาก TikTok API
// ตรงนี้เป็น "โครงกระดูก" ของ JSON ที่ TikTok ส่งกลับมา
// =============================================================

/// <summary>Root response wrapper จาก TikTok API</summary>
public class TikTokApiResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public TikTokOrderData? Data { get; set; }
}

/// <summary>ชั้น data ของ Response</summary>
public class TikTokOrderData
{
    [JsonPropertyName("orders")]
    public List<TikTokOrder>? Orders { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }
}

/// <summary>ข้อมูลออเดอร์แต่ละใบ (ดึงเฉพาะ Fields ที่ต้องการ)</summary>
public class TikTokOrder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("payment_info")]
    public TikTokPaymentInfo? PaymentInfo { get; set; }

    [JsonPropertyName("buyer_uid")]
    public string BuyerUid { get; set; } = string.Empty;

    [JsonPropertyName("buyer_email")]
    public string BuyerEmail { get; set; } = string.Empty;

    [JsonPropertyName("create_time")]
    public long CreateTime { get; set; }
}

/// <summary>ข้อมูลการชำระเงิน</summary>
public class TikTokPaymentInfo
{
    [JsonPropertyName("total_amount")]
    public string TotalAmount { get; set; } = "0";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "THB";

    [JsonPropertyName("original_total_product_price")]
    public string OriginalTotalProductPrice { get; set; } = "0";
}
