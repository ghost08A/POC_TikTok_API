using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.Models;
using TikTokShop.Service.Config;
using TikTokShop.Service.Helpers;
using TikTokShop.Service.Stores;

namespace TikTokShop.Service.ImplementServices;

// ================================================================
// AuthService.cs — OAuth 2.0 Authentication Service
//
// รับผิดชอบ:
//   1. ExchangeCodeForTokenAsync — แลก Auth Code → Token (Initial Flow)
//   2. RefreshAccessTokenAsync   — ต่ออายุ AccessToken ด้วย RefreshToken
//
// TikTok Auth Endpoints (Base URL: https://auth.tiktok-shops.com):
//   GET /api/v2/token/get     — Initial Exchange
//   GET /api/v2/token/refresh — Refresh
// ================================================================
public class AuthService : IAuthService
{
    private readonly IHttpClientFactory   _httpClientFactory;
    private readonly IConfiguration       _config;
    private readonly ILogger<AuthService> _logger;
    private readonly TenantStore          _tenantStore;

    // TikTok Auth Base URL แยกต่างหากจาก Open API
    private const string AuthBaseUrl = "https://auth.tiktok-shops.com";

    public AuthService(
        IHttpClientFactory   httpClientFactory,
        IConfiguration       config,
        ILogger<AuthService> logger,
        TenantStore          tenantStore)
    {
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _logger            = logger;
        _tenantStore       = tenantStore;
    }

    // ════════════════════════════════════════════════════════════
    // ExchangeCodeForTokenAsync — แลก Code → Token (Initial)
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<TikTokTokenData> ExchangeCodeForTokenAsync(string authCode, string? state = null)
    {
        var cfg = TikTokAppConfig.FromConfig(_config);

        var queryParams = new Dictionary<string, string>
        {
            { "app_key",    cfg.AppKey    },
            { "app_secret", cfg.AppSecret },
            { "auth_code",  authCode      },
            // grant_type ต้องเป็น "authorized_code" เสมอสำหรับ Initial Exchange
            { "grant_type", "authorized_code" }
        };

        string requestUrl = BuildAuthUrl("/api/v2/token/get", queryParams);

        _logger.LogInformation("[Auth] กำลังแลก Auth Code เป็น Access Token | Code={Code}", authCode);

        var tokenData = await CallAuthApiAsync(requestUrl, "ExchangeCode");

        LogTokenInfo(tokenData, state);

        return tokenData;
    }

    // ════════════════════════════════════════════════════════════
    // RefreshAccessTokenAsync — ต่ออายุด้วย RefreshToken
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<TikTokTokenData> RefreshAccessTokenAsync(string tenantCode)
    {
        if (!_tenantStore.TryGetByCode(tenantCode, out var tenant) || tenant == null)
            throw new KeyNotFoundException($"ไม่พบ Tenant '{tenantCode}' ในระบบ");

        if (string.IsNullOrWhiteSpace(tenant.RefreshToken))
            throw new InvalidOperationException($"Tenant '{tenantCode}' ไม่มี RefreshToken — ต้อง Re-authorize ใหม่");

        if (tenant.IsRefreshTokenExpired)
            throw new InvalidOperationException(
                $"RefreshToken ของ '{tenantCode}' หมดอายุแล้ว ({tenant.RefreshTokenExpireAt:u}) — Seller ต้อง Re-authorize ใหม่");

        var cfg = TikTokAppConfig.FromConfig(_config);

        var queryParams = new Dictionary<string, string>
        {
            { "app_key",       cfg.AppKey           },
            { "app_secret",    cfg.AppSecret        },
            { "refresh_token", tenant.RefreshToken  },
            { "grant_type",    "refresh_token"      }
        };

        string requestUrl = BuildAuthUrl("/api/v2/token/refresh", queryParams);

        _logger.LogInformation(
            "[Auth] กำลัง Refresh Token | Tenant={TenantCode} | AccessTokenExpireAt={ExpireAt:u}",
            tenantCode, tenant.AccessTokenExpireAt);

        var tokenData = await CallAuthApiAsync(requestUrl, "Refresh");

        var accessTokenExpireAt  = TikTokHelper.FromTikTokUnixSeconds(tokenData.AccessTokenExpireIn);
        var refreshTokenExpireAt = TikTokHelper.FromTikTokUnixSeconds(tokenData.RefreshTokenExpireIn);

        _tenantStore.UpdateTokens(
            tenantCode,
            tokenData.AccessToken,
            tokenData.RefreshToken,
            accessTokenExpireAt,
            refreshTokenExpireAt);

        _logger.LogInformation(
            "[Auth] ✅ Refresh Token สำเร็จ | Tenant={TenantCode} | AccessExpire={AccessAt:u} | RefreshExpire={RefreshAt:u}",
            tenantCode, accessTokenExpireAt, refreshTokenExpireAt);

        return tokenData;
    }

    // ── Private Helpers ───────────────────────────────────────────

    /// <summary>สร้าง Full Auth URL พร้อม Query String</summary>
    private static string BuildAuthUrl(string path, Dictionary<string, string> queryParams)
    {
        string qs = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{AuthBaseUrl}{path}?{qs}";
    }

    /// <summary>ยิง HTTP GET ไป TikTok Auth API และ Deserialize ผลลัพธ์</summary>
    private async Task<TikTokTokenData> CallAuthApiAsync(string requestUrl, string operation)
    {
        var client   = _httpClientFactory.CreateClient("TikTokAuthClient");
        var response = await client.GetAsync(requestUrl);
        var rawJson  = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("[Auth:{Op}] Response: {Json}", operation, rawJson);

        var tokenResult = JsonSerializer.Deserialize<TikTokTokenResponse>(rawJson);

        if (tokenResult == null || tokenResult.Code != 0)
        {
            _logger.LogError("[Auth:{Op}] ล้มเหลว! Code={Code}, Msg={Msg}",
                operation, tokenResult?.Code, tokenResult?.Message);
            throw new Exception($"TikTok Auth Error [{tokenResult?.Code}]: {tokenResult?.Message}");
        }

        if (tokenResult.Data == null)
            throw new Exception($"TikTok Auth Error [{operation}]: Response.Data เป็น null");

        return tokenResult.Data;
    }

    /// <summary>Log ข้อมูล Token หลังจาก Exchange สำเร็จ</summary>
    private void LogTokenInfo(TikTokTokenData data, string? state)
    {
        var now                  = DateTime.UtcNow;
        var accessTokenExpireAt  = now.AddSeconds(data.AccessTokenExpireIn);
        var refreshTokenExpireAt = now.AddSeconds(data.RefreshTokenExpireIn);

        _logger.LogInformation("[Auth] ━━━ Token ที่ได้รับ ━━━");
        _logger.LogInformation("[Auth]   SellerName           : {SellerName}",  data.SellerName);
        _logger.LogInformation("[Auth]   ShopCipher (OpenId)  : {OpenId}",      data.OpenId);
        _logger.LogInformation("[Auth]   AccessToken          : {Token}",        data.AccessToken);
        _logger.LogInformation("[Auth]   RefreshToken         : {Token}",        data.RefreshToken);
        _logger.LogInformation("[Auth]   AccessTokenExpireAt  : {ExpireAt:u}",   accessTokenExpireAt);
        _logger.LogInformation("[Auth]   RefreshTokenExpireAt : {ExpireAt:u}",   refreshTokenExpireAt);
        _logger.LogInformation("[Auth]   State                : {State}",        state ?? "(none)");
        _logger.LogInformation("[Auth] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }
}
