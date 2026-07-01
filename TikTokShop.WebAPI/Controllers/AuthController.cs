using Microsoft.AspNetCore.Mvc;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.Models;
using TikTokShop.Service.Stores;

namespace TikTokShop.WebAPI.Controllers;

// ================================================================
// AuthController.cs — OAuth 2.0 Authentication Endpoints
//
// Route Prefix: /api/auth
//
// Endpoints:
//   GET  /api/auth/callback          — รับ OAuth Code แลก Token (Initial)
//   POST /api/auth/refresh/{tenantCode} — ต่ออายุ AccessToken ด้วย RefreshToken
//   GET  /api/auth/status/{tenantCode}  — ดูสถานะ Token ของ Tenant
// ================================================================
[ApiController]
[Route("api/auth")]
[Tags("🔐 Authentication")]
public class AuthController : ControllerBase
{
    private readonly IAuthService            _authService;
    private readonly TenantStore             _tenantStore;
    private readonly ILogger<AuthController> _logger;
    private readonly IShopService _shopService;

    public AuthController(
        IAuthService            authService,
        TenantStore             tenantStore,
        ILogger<AuthController> logger,
        IShopService shopService)
    {
        _authService = authService;
        _tenantStore = tenantStore;
        _logger      = logger;
        _shopService = shopService;
    }


    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string  code,
        [FromQuery] string? state = null)
    {
        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("[Auth] TikTok ไม่ได้ส่ง Auth Code | State={State}", state);
            return BadRequest(new
            {
                success = false,
                message = "Authorization failed or user cancelled."
            });
        }

        try
        {
            DateTime timenow = DateTime.UtcNow;
            // 1. แลก code เป็น token
            var tokenData = await _authService.ExchangeCodeForTokenAsync(code);

            if (!string.IsNullOrEmpty(state))
            {
                //ตรวจ state ตรงนี้
            }

            // คำนวณวันเวลาหมดอายุ (UTC)
            var accessTokenExpireAt = DateTimeOffset
             .FromUnixTimeSeconds(tokenData.AccessTokenExpireIn)
             .UtcDateTime;

            var refreshTokenExpireAt = DateTimeOffset
                .FromUnixTimeSeconds(tokenData.RefreshTokenExpireIn)
                .UtcDateTime;


            var authorizedShops = await _shopService.GetAuthorizedShopsByAccessTokenAsync(
               tokenData.AccessToken
            );

            var shop = authorizedShops.Data?.Shops?.FirstOrDefault();

            if (shop == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "ไม่พบร้านค้าที่ authorize กับ TikTok"
                });
            }

            var tenant = new ShopTenant
            {
                TenantCode = $"TikTok_{shop.Id}",
                ShopName = shop.Name,

                ShopId = shop.Id,
                ShopCipher = shop.Cipher,

                AccessToken = tokenData.AccessToken,
                RefreshToken = tokenData.RefreshToken,

                AccessTokenExpireAt = accessTokenExpireAt,
                RefreshTokenExpireAt = refreshTokenExpireAt,

                ConnectedAt = timenow,
                Status = "active"
            };


            return Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auth] เกิดข้อผิดพลาดในการแลก Token");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "Token exchange failed.", detail = ex.Message });
        }
    }

    // ────────────────────────────────────────────────────────────
    // POST /api/auth/refresh/{tenantCode}
    // ต่ออายุ AccessToken ที่หมดหรือใกล้หมด ด้วย RefreshToken
    [HttpPost("refresh/{tenantCode}")]
    public async Task<IActionResult> RefreshToken(string tenantCode)
    {
        _logger.LogInformation("🔄 [Auth] Refresh Token ของ Tenant: {TenantCode}", tenantCode);

        try
        {
            var tokenData = await _authService.RefreshAccessTokenAsync(tenantCode);

            var now = DateTime.UtcNow;

            var accessTokenExpireAt = DateTimeOffset
                .FromUnixTimeSeconds(tokenData.AccessTokenExpireIn)
                .UtcDateTime;

            var refreshTokenExpireAt = DateTimeOffset
                .FromUnixTimeSeconds(tokenData.RefreshTokenExpireIn)
                .UtcDateTime;

            return Ok(new
            {
                success = true,
                tenantCode,
                message = "✅ Refresh Token สำเร็จ — In-Memory Store อัปเดตแล้ว",
                warning = "⚠️ กรุณาคัดลอก config_to_save ไปอัปเดตใน appsettings.Development.json เพื่อให้ค่าคงอยู่หลัง Restart",
                config_to_save = new
                {
                    AccessToken = tokenData.AccessToken,
                    RefreshToken = tokenData.RefreshToken,
                    AccessTokenExpireAt = accessTokenExpireAt.ToString("u"),
                    RefreshTokenExpireAt = refreshTokenExpireAt.ToString("u"),
                },
                token_expires_in_minutes = (int)(accessTokenExpireAt - now).TotalMinutes
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auth] Refresh ล้มเหลว | Tenant={TenantCode}", tenantCode);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = ex.Message });
        }
    }

    // ────────────────────────────────────────────────────────────
    // GET /api/auth/status/{tenantCode}
    // ดูสถานะ Token ของ Tenant โดยไม่ต้องเรียก TikTok API
    // ────────────────────────────────────────────────────────────
    /// <summary>
    /// [Token Status] ดูสถานะและเวลาหมดอายุของ Token จาก In-Memory Store
    /// </summary>
    /// <param name="tenantCode">รหัส Tenant เช่น "PoC_MobileShop_01"</param>
    [HttpGet("status/{tenantCode}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetTokenStatus(string tenantCode)
    {
        if (!_tenantStore.TryGetByCode(tenantCode, out var tenant) || tenant == null)
            return NotFound(new { success = false, message = $"ไม่พบ Tenant '{tenantCode}'" });

        return Ok(new
        {
            success    = true,
            tenantCode = tenant.TenantCode,
            shopName   = tenant.ShopName,
            access_token = new
            {
                is_expired       = tenant.IsAccessTokenExpired,
                expire_at        = tenant.AccessTokenExpireAt.ToString("u"),
                remaining_minutes = Math.Round(tenant.AccessTokenRemainingMinutes, 1)
            },
            refresh_token = new
            {
                is_expired = tenant.IsRefreshTokenExpired,
                expire_at  = tenant.RefreshTokenExpireAt.ToString("u"),
            },
            // แนะนำการดำเนินการถัดไป
            recommendation = tenant.IsRefreshTokenExpired
                ? "🔴 RefreshToken หมดอายุ — Seller ต้อง Re-authorize ใหม่ทั้งกระบวนการ"
                : tenant.IsAccessTokenExpired
                    ? "🟡 AccessToken หมดอายุ — เรียก POST /api/auth/refresh/{tenantCode} เพื่อต่ออายุ"
                    : "🟢 Token ยังใช้งานได้ปกติ"
        });
    }
}
