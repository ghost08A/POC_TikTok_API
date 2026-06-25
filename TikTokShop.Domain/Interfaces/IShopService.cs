using TikTokShop.Domain.ResponseModels;

namespace TikTokShop.Domain.Interfaces;

// ================================================================
// IShopService.cs — Contract สำหรับ Shop / Token Management
//
// รับผิดชอบ:
//   1. ดึงรายการร้านค้าที่ Authorize แล้ว
//   2. ตรวจสอบสถานะ Access Token (Token Health Check)
// ================================================================
public interface IShopService
{
    /// <summary>
    /// [Token Health Check] ดึงรายการร้านค้าที่ Tenant ได้ Authorize ให้ App ของเรา
    /// code=0 → Token ยัง Active | code≠0 → Token หมดอายุ ต้อง Refresh
    /// เรียก TikTok: GET /authorization/202309/shops
    /// </summary>
    /// <param name="tenantCode">รหัส Tenant ภายในระบบ</param>
    Task<TikTokAuthorizedShopsResponse> GetAuthorizedShopsAsync(string tenantCode);
}
