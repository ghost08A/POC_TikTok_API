using Microsoft.AspNetCore.Mvc;
using TikTokShop.Domain.Interfaces;

namespace TikTokShop.WebAPI.Controllers;

// ================================================================
// ShopController.cs — Shop Management Endpoints
//
// Route Prefix: /api/shops
//
// Endpoints:
//   GET /api/shops/{tenantCode} → Token Health Check + Authorized Shops List
//
// วิธีอ่านผลลัพธ์:
//   token_active = true  → ✅ Token ยัง Active ใช้ได้ปกติ
//   token_active = false → ❌ Token หมดอายุ ต้อง Refresh หรือ Re-authorize
//   cipher (ใน shops[])  → ค่าที่ต้องใช้เป็น shop_cipher ใน API Request ทุกครั้ง
// ================================================================
[ApiController]
[Route("api/shops")]
[Tags("🏪 Shops")]
public class ShopController : ControllerBase
{
    private readonly IShopService              _shopService;
    private readonly ILogger<ShopController>   _logger;

    public ShopController(IShopService shopService, ILogger<ShopController> logger)
    {
        _shopService = shopService;
        _logger      = logger;
    }

    // ────────────────────────────────────────────────────────────
    // GET /api/shops/{tenantCode}
    // Token Health Check — ตรวจสอบว่า Access Token ยัง Active หรือไม่
    // เรียก TikTok: GET /authorization/202309/shops
    // ────────────────────────────────────────────────────────────
    /// <summary>
    /// [Token Health Check] ดึงรายการร้านค้าที่ Authorize ไว้ และตรวจสอบสถานะ Access Token
    /// </summary>
    /// <remarks>
    /// - <b>code = 0</b> → Token ยัง Active ✅ พร้อมใช้งาน<br/>
    /// - <b>code ≠ 0</b> → Token หมดอายุหรือ Invalid ❌ ต้อง Refresh<br/>
    /// - ค่า <c>cipher</c> ในผลลัพธ์ = ค่าที่ต้องใช้เป็น <c>shop_cipher</c> ใน API Request ทุกครั้ง
    /// </remarks>
    /// <param name="tenantCode">รหัส Tenant เช่น "PoC_MobileShop_01"</param>
    [HttpGet("{tenantCode}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAuthorizedShops(string tenantCode)
    {
        _logger.LogInformation("🏪 [Shops] ตรวจสอบ Token และร้านค้าของ Tenant: {TenantCode}", tenantCode);

        try
        {
            var tikTokResponse = await _shopService.GetAuthorizedShopsAsync(tenantCode);

            // code=0 → Token ยัง Active
            bool isTokenActive = tikTokResponse.Code == 0;
            int  shopCount     = tikTokResponse.Data?.Shops.Count ?? 0;

            _logger.LogInformation(
                "🏪 [Shops] TenantCode={TenantCode} | TokenActive={Active} | Shops={Count}",
                tenantCode, isTokenActive, shopCount);

            return Ok(new
            {
                success           = true,
                tenantCode        = tenantCode,
                token_active      = isTokenActive,
                tiktok_code       = tikTokResponse.Code,
                tiktok_message    = tikTokResponse.Message,
                tiktok_request_id = tikTokResponse.RequestId,
                shop_count        = shopCount,
                shops             = tikTokResponse.Data?.Shops
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("⚠️ [Shops] ไม่พบ Tenant: {TenantCode}", tenantCode);
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ [Shops] TikTok API Error | Tenant={TenantCode}", tenantCode);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [Shops] Unexpected Error | Tenant={TenantCode}", tenantCode);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { title = "Internal Server Error", detail = ex.Message });
        }
    }
}
