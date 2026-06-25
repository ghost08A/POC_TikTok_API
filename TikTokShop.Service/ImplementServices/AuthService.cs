using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.Models;

namespace TikTokShop.Service.ImplementServices;

// ================================================================
// AuthService.cs — OAuth 2.0 Authentication Service
//
// รับผิดชอบ: แลก Authorization Code → Access Token
// TikTok Auth Endpoint: https://auth.tiktok-shops.com/api/v2/token/get
//
// Flow:
//   Seller คลิก "เชื่อมต่อร้านค้า"
//     → Redirect ไปหน้า TikTok Authorization
//     → TikTok Redirect กลับมา /api/auth/callback?code=xxx
//     → AuthController เรียก ExchangeCodeForTokenAsync(code)
//     → ได้ AccessToken + RefreshToken กลับมา
// ================================================================
public class AuthService : IAuthService
{
    private readonly IHttpClientFactory        _httpClientFactory;
    private readonly IConfiguration            _config;
    private readonly ILogger<AuthService>      _logger;

    public AuthService(
        IHttpClientFactory    httpClientFactory,
        IConfiguration        config,
        ILogger<AuthService>  logger)
    {
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _logger            = logger;
    }

    // ════════════════════════════════════════════════════════════
    // ExchangeCodeForTokenAsync — แลก Code → Token
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<TikTokTokenData> ExchangeCodeForTokenAsync(string authCode)
    {
        // ── Step 1: ดึง App Credentials จาก Config ────────────────
        string appKey    = _config["TikTok:AppKey"]    ?? throw new InvalidOperationException("Missing TikTok:AppKey");
        string appSecret = _config["TikTok:AppSecret"] ?? throw new InvalidOperationException("Missing TikTok:AppSecret");

        // ── Step 2: เตรียม Query Params ──────────────────────────
        // ⚠️ ใช้ Auth Base URL คนละตัวกับ Open API Base URL
        string authBaseUrl = "https://auth.tiktok-shops.com";

        var queryParams = new Dictionary<string, string>
        {
            { "app_key",    appKey     },
            { "app_secret", appSecret  },
            { "auth_code",  authCode   },
            { "grant_type", "authorized_code" }  // grant_type ต้องเป็น "authorized_code" เสมอสำหรับ Initial Token

        };

        string queryString = string.Join("&", queryParams
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        string requestUrl  = $"{authBaseUrl}/api/v2/token/get?{queryString}";

        _logger.LogInformation("[Auth] กำลังแลก Auth Code เป็น Access Token...");

        // ── Step 3: ยิง HTTP GET ──────────────────────────────────
        // ใช้ TikTokAuthClient (Base URL ต่างจาก TikTokClient ปกติ)
        var client  = _httpClientFactory.CreateClient("TikTokAuthClient");
        var response = await client.GetAsync(requestUrl);
        var rawJson  = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("[Auth] Response: {Json}", rawJson);

        // ── Step 4: Deserialize → TikTokTokenResponse ─────────────
        var tokenResult = JsonSerializer.Deserialize<TikTokTokenResponse>(rawJson);

        if (tokenResult == null || tokenResult.Code != 0)
        {
            _logger.LogError("[Auth] แลก Token ล้มเหลว! Code={Code}, Msg={Msg}",
                tokenResult?.Code, tokenResult?.Message);
            throw new Exception($"TikTok Auth Error [{tokenResult?.Code}]: {tokenResult?.Message}");
        }

        _logger.LogInformation("[Auth] ✅ ได้ Token ของร้าน: {SellerName}", tokenResult.Data.SellerName);
        _logger.LogInformation("[Auth] Token หมดอายุใน: {Expire} วินาที", tokenResult.Data.AccessTokenExpireIn);

        return tokenResult.Data;
    }
}
