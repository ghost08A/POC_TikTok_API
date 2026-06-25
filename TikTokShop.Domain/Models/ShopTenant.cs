namespace TikTokShop.Domain.Models;

/// <summary>
/// Entity แทนข้อมูล Credentials ของร้านค้าแต่ละร้านในระบบ (Multi-Tenant)
/// ใน Production ควรเก็บใน Database พร้อม Encrypt AccessToken
/// </summary>
public class ShopTenant
{
    /// <summary>รหัส Tenant ภายในระบบเรา (Primary Key ของ In-Memory DB)</summary>
    public string TenantCode { get; set; } = string.Empty;

    /// <summary>ชื่อร้านค้าที่อ่านง่าย</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>
    /// OAuth Access Token ของร้านนั้นๆ — ได้มาจากขั้นตอน OAuth Authorization
    /// ส่งผ่าน HTTP Header "x-tts-access-token" เสมอ (กฎ TikTok API V2)
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Shop Cipher — ตัวเข้ารหัส Shop ID ของ TikTok V2
    /// ได้มาจาก /authorization/shop/list หลัง OAuth
    /// </summary>
    public string ShopCipher { get; set; } = string.Empty;
    public string ShopId { get; set; } = string.Empty;

}
