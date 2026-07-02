using Microsoft.Extensions.Configuration;
using TikTokShop.Domain.Models;

namespace TikTokShop.Service.Stores;

// ================================================================
// TenantStore.cs — In-Memory Multi-Tenant Data Store
//
// โหลดข้อมูล Credentials ของร้านค้าจาก IConfiguration
// (appsettings.Development.json → ไม่ Commit ขึ้น Git)
//
// Config Structure (ใน appsettings.Development.json):
// {
//   "TikTokTenants": {
//     "PoC_MobileShop_01": {
//       "ShopName":              "ชื่อร้าน",
//       "AccessToken":           "ROW_xxx",
//       "RefreshToken":          "ROW_xxx",
//       "AccessTokenExpireAt":   "2026-06-26T10:00:00Z",   ← ISO 8601 UTC
//       "RefreshTokenExpireAt":  "2026-12-20T10:00:00Z",   ← ISO 8601 UTC
//       "ShopCipher":            "ROW_xxx",
//       "ShopId":                "749xxx"
//     }
//   }
// }
//
// เพิ่มร้านค้าใหม่: เพิ่ม Object ใหม่ใน TikTokTenants ใน Config
// ไม่ต้องแตะ Code แม้แต่บรรทัดเดียว ✅
//
// ⚠️ Production:
//   - ย้ายไป Database (SQL Server / PostgreSQL)
//   - ใช้ Azure Key Vault หรือ AWS Secrets Manager เก็บ Token
//   - เพิ่ม Background Job ต่ออายุ Token อัตโนมัติ
// ================================================================
public class TenantStore
{
    private readonly Dictionary<string, ShopTenant> _store;

    /// <summary>
    /// โหลด Tenant ทั้งหมดจาก Configuration Section "TikTokTenants"
    /// </summary>
    public TenantStore(IConfiguration config)
    {
        _store = new(StringComparer.OrdinalIgnoreCase);

        // อ่าน TikTokTenants Section — แต่ละ Child Key คือ TenantCode
        var tenantsSection = config.GetSection("TikTokTenants");

        foreach (var tenantSection in tenantsSection.GetChildren())
        {
            // Parse วันเวลาหมดอายุ — ถ้าไม่มีในไฟล์ ให้ถือว่า "หมดอายุแล้ว" (DateTime.MinValue)
            // เพื่อให้ IsAccessTokenExpired = true และบังคับ Refresh ก่อนใช้
            DateTime.TryParse(tenantSection["AccessTokenExpireAt"],  out var accessExpire);
            DateTime.TryParse(tenantSection["RefreshTokenExpireAt"], out var refreshExpire);

            var tenant = new ShopTenant
            {
                TenantCode           = tenantSection.Key,
                ShopName             = tenantSection["ShopName"]    ?? tenantSection.Key,
                AccessToken          = tenantSection["AccessToken"]  ?? string.Empty,
                RefreshToken         = tenantSection["RefreshToken"] ?? string.Empty,
                AccessTokenExpireAt  = accessExpire,   // DateTime.MinValue ถ้า parse ไม่ได้
                RefreshTokenExpireAt = refreshExpire,  // DateTime.MinValue ถ้า parse ไม่ได้
                ShopCipher           = tenantSection["ShopCipher"]  ?? string.Empty,
                ShopId               = tenantSection["ShopId"]      ?? string.Empty,
            };

            _store[tenant.TenantCode] = tenant;
        }
    }

    // ── Read Methods ───────────────────────────────────────────────

    /// <summary>
    /// ค้นหา Tenant จาก TenantCode (Primary Key)
    /// ใช้เรียก API เช่น GET /api/orders/{tenantCode}
    /// </summary>
    public bool TryGetByCode(string tenantCode, out ShopTenant? tenant)
        => _store.TryGetValue(tenantCode, out tenant);

    /// <summary>
    /// ค้นหา Tenant จาก ShopId
    /// ใช้รับ Webhook ที่ TikTok ส่ง shop_id มาใน Body
    /// </summary>
    public ShopTenant? FindByShopId(string shopId)
        => _store.Values.FirstOrDefault(t =>
            t.ShopId.Equals(shopId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ดึงรายการ Tenant ทั้งหมด (ใช้สำหรับ Health Check หรือ Admin)
    /// </summary>
    public IEnumerable<ShopTenant> GetAll()
        => _store.Values;

    /// <summary>จำนวน Tenant ที่โหลดได้จาก Config</summary>
    public int Count => _store.Count;

    public void AddOrUpdate(ShopTenant tenant)
        => _store[tenant.TenantCode] = tenant;

    // ── Write Method ───────────────────────────────────────────────

    /// <summary>
    /// อัปเดต Token ใหม่ใน In-Memory Store หลังจาก Refresh สำเร็จ
    ///
    /// ⚠️ หมายเหตุ: อัปเดตเฉพาะ In-Memory เท่านั้น!
    /// ถ้า App Restart ค่าจะหายไป ต้องอัปเดต appsettings.Development.json ด้วยตัวเอง
    /// (ดูค่าใหม่ได้จาก Response ของ POST /api/auth/refresh/{tenantCode})
    ///
    /// Production → เปลี่ยนมา Update Database แทน
    /// </summary>
    /// <param name="tenantCode">TenantCode ที่ต้องการอัปเดต</param>
    /// <param name="accessToken">Access Token ใหม่จาก TikTok</param>
    /// <param name="refreshToken">Refresh Token ใหม่จาก TikTok</param>
    /// <param name="accessTokenExpireAt">วันเวลาหมดอายุของ AccessToken (UTC)</param>
    /// <param name="refreshTokenExpireAt">วันเวลาหมดอายุของ RefreshToken (UTC)</param>
    /// <returns>true ถ้าพบ Tenant และอัปเดตสำเร็จ</returns>
    public bool UpdateTokens(
        string   tenantCode,
        string   accessToken,
        string   refreshToken,
        DateTime accessTokenExpireAt,
        DateTime refreshTokenExpireAt)
    {
        if (!_store.TryGetValue(tenantCode, out var tenant))
            return false;

        tenant.AccessToken          = accessToken;
        tenant.RefreshToken         = refreshToken;
        tenant.AccessTokenExpireAt  = accessTokenExpireAt;
        tenant.RefreshTokenExpireAt = refreshTokenExpireAt;

        return true;
    }
}
