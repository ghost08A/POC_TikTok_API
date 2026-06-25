using Microsoft.AspNetCore.Mvc;
using TikTokShop.Domain.Interfaces;

namespace TikTokShop.WebAPI.Controllers;

// ================================================================
// AuthController.cs — OAuth 2.0 Authentication Endpoints
//
// Route Prefix: /api/auth
//
// Endpoints:
//   GET /api/auth/callback  — รับ Authorization Code จาก TikTok OAuth Redirect
//
// Flow:
//   1. Seller คลิก "เชื่อมต่อร้านค้า" → Redirect ไป TikTok Authorization Page
//   2. Seller กดอนุญาต → TikTok Redirect กลับมาที่ /api/auth/callback?code=xxx
//   3. Controller รับ code แล้วส่งให้ AuthService แลกเป็น Access Token
//   4. บันทึก Token ลง DB (TODO) แล้ว Redirect ไป Dashboard
// ================================================================
[ApiController]
[Route("api/auth")]
[Tags("🔐 Authentication")]
public class AuthController : ControllerBase
{
    private readonly IAuthService              _authService;
    private readonly ILogger<AuthController>   _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger      = logger;
    }

    // ────────────────────────────────────────────────────────────
    // GET /api/auth/callback?code={authCode}&state={state}
    // OAuth Redirect URI — TikTok จะ Redirect Seller มาที่นี่หลังอนุญาต
    // ────────────────────────────────────────────────────────────
    /// <summary>
    /// [OAuth Callback] รับ Authorization Code จาก TikTok หลัง Seller กดอนุญาต
    /// นำ Code ไปแลกเป็น Access Token + Refresh Token ทันที
    /// </summary>
    /// <remarks>
    /// URL นี้ต้องถูกลงทะเบียนเป็น "Redirect URI" ใน TikTok Developer Console
    /// <br/>⚠️ Auth Code หมดอายุเร็วมาก ต้องแลกทันทีที่ได้รับ
    /// </remarks>
    /// <param name="code">Authorization Code จาก TikTok (Required)</param>
    /// <param name="state">State parameter สำหรับ CSRF Protection (Optional)</param>
    [HttpGet("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Callback(
        [FromQuery] string  code,
        [FromQuery] string? state = null)
    {
        // ── ตรวจสอบว่า TikTok ส่ง Code กลับมาจริงไหม ─────────────
        // ถ้าไม่มี code แสดงว่า Seller กดยกเลิก หรือ OAuth Error
        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("[Auth] TikTok ไม่ได้ส่ง Auth Code (Seller อาจกดยกเลิก | State={State})", state);
            return BadRequest(new { success = false, message = "Authorization failed or user cancelled." });
        }

        _logger.LogInformation("[Auth] ได้รับ Auth Code (State={State}) กำลังแลก Token...", state);

        try
        {
            // แลก Code → Access Token (ต้องรวดเร็ว เพราะ Code หมดอายุไวมาก)
            var tokenData = await _authService.ExchangeCodeForTokenAsync(code);

            // ════════════════════════════════════════════════════
            // 💾 TODO: บันทึก Token ลง Database
            //
            // Production ควรทำ:
            //   - _dbContext.Shops.Upsert({ OpenId, AccessToken, RefreshToken, ExpireAt })
            //   - OpenId (tokenData.OpenId) = ShopCipher ที่ใช้ใน API Request
            //   - AccessToken หมดอายุทุก ~24h → ต้องมี RefreshToken Flow
            //   - RefreshToken หมดอายุทุก 180 วัน → ต้องแจ้ง Seller Re-authorize
            // ════════════════════════════════════════════════════

            _logger.LogInformation("[Auth] ✅ ได้ Token ของ: {SellerName}", tokenData.SellerName);
            _logger.LogInformation("[Auth] AccessToken หมดอายุใน {Expire} วินาที", tokenData.AccessTokenExpireIn);
            _logger.LogInformation("[Auth] OpenId (ShopCipher): {OpenId}", tokenData.OpenId);

            // PoC: Return Token Data โดยตรง
            // Production: Redirect ไป Dashboard → return Redirect("https://my-saas.com/dashboard?sync=success");
            return Ok(new
            {
                success       = true,
                seller_name   = tokenData.SellerName,
                open_id       = tokenData.OpenId,
                token_expires = tokenData.AccessTokenExpireIn,
                // ⚠️ ไม่ควร Return AccessToken/RefreshToken ตรงๆ ใน Production
                //    ควรเก็บไว้ใน Server-side เท่านั้น
                debug_token   = tokenData.AccessToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auth] เกิดข้อผิดพลาดในการแลก Token");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "Token exchange failed.", detail = ex.Message });
        }
    }
}
