using System.Text.Json.Serialization;

namespace TikTokShop.Domain.ResponseModels;

// ================================================================
// TikTokAuthorizedShopsResponse.cs
// ใช้รับ JSON Response จาก GET /authorization/202309/shops
// ทุก Property บังคับมี [JsonPropertyName] เพื่อป้องกัน Case-sensitive bug
// ================================================================

/// <summary>
/// Root response wrapper จาก TikTok API
/// {"code":0,"message":"success","data":{...},"request_id":"..."}
/// </summary>
public class TikTokAuthorizedShopsResponse
{
    /// <summary>0 = สำเร็จ, อื่นๆ = มี Error</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>"success" หรือ error message</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Unique Request ID จาก TikTok สำหรับ Debugging</summary>
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Payload หลักที่มีรายการร้านค้า</summary>
    [JsonPropertyName("data")]
    public TikTokAuthorizedShopsData? Data { get; set; }
}

/// <summary>
/// Data object ที่ครอบ Array ของร้านค้า
/// {"shops":[...]}
/// </summary>
public class TikTokAuthorizedShopsData
{
    [JsonPropertyName("shops")]
    public List<TikTokAuthorizedShop> Shops { get; set; } = new();
}

/// <summary>
/// ข้อมูลของแต่ละร้านค้าที่ Seller ได้ Authorize ให้ App ของเรา
/// </summary>
public class TikTokAuthorizedShop
{
    /// <summary>
    /// Shop Cipher — ต้องส่งไปใน Query Param `shop_cipher` ทุกครั้งที่ยิง API อื่นๆ ของร้านนี้
    /// </summary>
    [JsonPropertyName("cipher")]
    public string Cipher { get; set; } = string.Empty;

    /// <summary>Shop Code เช่น "TH11223344"</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Shop ID (numeric string)</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>ชื่อร้านค้าที่ตั้งไว้ใน TikTok Seller Center</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>ประเทศ/ภูมิภาคของร้านค้า เช่น "TH", "SG", "MY"</summary>
    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    /// <summary>ประเภท Seller เช่น "LOCAL", "CROSS_BORDER"</summary>
    [JsonPropertyName("seller_type")]
    public string SellerType { get; set; } = string.Empty;
}
