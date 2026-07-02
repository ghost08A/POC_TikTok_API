using System.Net;
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
            var tokenData = await _authService.ExchangeCodeForTokenAsync(code, state);

            if (!string.IsNullOrEmpty(state))
            {
                //ตรวจ state ตรงนี้
                _logger.LogWarning("-----------------------------------------------------/n มีการเรียกใช้ callBack State={State}/n----------------------------------------------------------------", state);

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


            _tenantStore.AddOrUpdate(tenant);
            LogConnectedTenant(tenant);

            return Content(BuildThankYouPage(tenant), "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auth] เกิดข้อผิดพลาดในการแลก Token");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "Token exchange failed.", detail = ex.Message });
        }
    }

    // ── Helper Methods ───────────────────────────────────────────
    private void LogConnectedTenant(ShopTenant tenant)
    {
        _logger.LogInformation(
            """

            ============================================================
            TikTok Shop connected successfully
            ------------------------------------------------------------
            Tenant Code     : {TenantCode}
            Shop Name       : {ShopName}
            Shop ID         : {ShopId}
            Shop Cipher     : {ShopCipher}
            Status          : {Status}
            Connected At    : {ConnectedAt:u}
            Access Expires  : {AccessTokenExpireAt:u}
            Refresh Expires : {RefreshTokenExpireAt:u}
            Access Token    : {AccessToken}
            Refresh Token   : {RefreshToken}
            ============================================================

            """,
            tenant.TenantCode,
            tenant.ShopName,
            tenant.ShopId,
            tenant.ShopCipher,
            tenant.Status,
            tenant.ConnectedAt,
            tenant.AccessTokenExpireAt,
            tenant.RefreshTokenExpireAt,
            MaskSecret(tenant.AccessToken),
            MaskSecret(tenant.RefreshToken));
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";

        if (value.Length <= 12)
            return new string('*', value.Length);

        return $"{value[..6]}...{value[^6..]}";
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



    private static string BuildThankYouPage(ShopTenant tenant)
    {
        var shopName = WebUtility.HtmlEncode(tenant.ShopName);
        var tenantCode = WebUtility.HtmlEncode(tenant.TenantCode);
        var shopId = WebUtility.HtmlEncode(tenant.ShopId);
        var connectedAt = WebUtility.HtmlEncode(tenant.ConnectedAt.ToString("u"));

        return $$"""
        <!doctype html>
        <html lang="th">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>TikTok Shop Connected</title>
            <style>
                :root {
                    color-scheme: light;
                    --ink: #18212f;
                    --muted: #637083;
                    --line: #dfe5ee;
                    --accent: #00a6a6;
                    --accent-strong: #007f7f;
                    --bg: #f7f9fc;
                    --panel: #ffffff;
                }

                * {
                    box-sizing: border-box;
                }

                body {
                    margin: 0;
                    min-height: 100vh;
                    display: grid;
                    place-items: center;
                    padding: 32px 16px;
                    color: var(--ink);
                    background:
                        radial-gradient(circle at 18% 12%, rgba(0, 166, 166, .16), transparent 28%),
                        linear-gradient(135deg, #fbfcff 0%, var(--bg) 52%, #eef6f8 100%);
                    font-family: "Segoe UI", Tahoma, Arial, sans-serif;
                }

                main {
                    width: min(720px, 100%);
                    padding: clamp(28px, 5vw, 48px);
                    border: 1px solid var(--line);
                    border-radius: 8px;
                    background: rgba(255, 255, 255, .92);
                    box-shadow: 0 24px 70px rgba(24, 33, 47, .12);
                }

                .badge {
                    width: 56px;
                    height: 56px;
                    display: grid;
                    place-items: center;
                    border-radius: 50%;
                    color: white;
                    background: var(--accent);
                    font-size: 30px;
                    font-weight: 700;
                    line-height: 1;
                }

                h1 {
                    margin: 22px 0 10px;
                    font-size: clamp(30px, 5vw, 48px);
                    line-height: 1.05;
                    letter-spacing: 0;
                }

                p {
                    margin: 0;
                    color: var(--muted);
                    font-size: 17px;
                    line-height: 1.7;
                }

                dl {
                    display: grid;
                    grid-template-columns: minmax(120px, 180px) 1fr;
                    gap: 14px 18px;
                    margin: 30px 0 0;
                    padding: 22px;
                    border: 1px solid var(--line);
                    border-radius: 8px;
                    background: #fbfdff;
                }

                dt {
                    color: var(--muted);
                    font-size: 14px;
                }

                dd {
                    margin: 0;
                    min-width: 0;
                    overflow-wrap: anywhere;
                    font-weight: 650;
                }

                .status {
                    display: inline-flex;
                    align-items: center;
                    gap: 8px;
                    color: var(--accent-strong);
                }

                .status::before {
                    content: "";
                    width: 9px;
                    height: 9px;
                    border-radius: 50%;
                    background: currentColor;
                }

                footer {
                    margin-top: 26px;
                    color: var(--muted);
                    font-size: 14px;
                    line-height: 1.6;
                }

                @media (max-width: 560px) {
                    main {
                        padding: 24px;
                    }

                    dl {
                        grid-template-columns: 1fr;
                        gap: 6px;
                        padding: 18px;
                    }

                    dd {
                        margin-bottom: 10px;
                    }
                }
            </style>
        </head>
        <body>
            <main>
                <div class="badge" aria-hidden="true">✓</div>
                <h1>เชื่อมต่อ Arm app สำเร็จ</h1>
                <p>กราบขอบคุณพ่อแม่พี่น้องที่ช่วยนะครับ อามได้รับสิทธิ์การเชื่อมต่อร้านค้าเรียบร้อยแล้ว สามารถปิดหน้านี้ได้เลยนะครับ</p>

                <dl>
                    <dt>ร้านค้า</dt>
                    <dd>{{shopName}}</dd>

                    <dt>Tenant Code</dt>
                    <dd>{{tenantCode}}</dd>

                    <dt>Shop ID</dt>
                    <dd>{{shopId}}</dd>

                    <dt>สถานะ</dt>
                    <dd><span class="status">Active</span></dd>

                    <dt>เชื่อมต่อเมื่อ</dt>
                    <dd>{{connectedAt}}</dd>
                </dl>

                <footer>รายละเอียดสำหรับผู้พัฒนาถูกบันทึกไว้ใน server log แล้ว โดย token จะถูกซ่อนไว้บางส่วนเพื่อความปลอดภัย</footer>
            </main>
        </body>
        </html>
        """;
    }
}
