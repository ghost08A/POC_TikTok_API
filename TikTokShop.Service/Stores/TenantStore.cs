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
//       "ShopName": "...",
//       "AccessToken": "ROW_xxx",
//       "ShopCipher": "ROW_xxx",
//       "ShopId": "749xxx"
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
//   - เพิ่ม RefreshToken Logic เพื่อต่ออายุ Token อัตโนมัติ
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
            var tenant = new ShopTenant
            {
                TenantCode  = tenantSection.Key,
                ShopName    = tenantSection["ShopName"]    ?? tenantSection.Key,
                AccessToken = tenantSection["AccessToken"] ?? string.Empty,
                ShopCipher  = tenantSection["ShopCipher"]  ?? string.Empty,
                ShopId      = tenantSection["ShopId"]      ?? string.Empty,
            };

            _store[tenant.TenantCode] = tenant;
        }
    }

    // ── Public Methods ─────────────────────────────────────────────

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

    /// <summary>
    /// จำนวน Tenant ที่โหลดได้จาก Config
    /// </summary>
    public int Count => _store.Count;
}
