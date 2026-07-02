using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.Models;
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
    public async Task<TikTokTokenData> ExchangeCodeForTokenAsync(string authCode ,string state = null)
    {
        // ── Step 1: ดึง App Credentials ──────────────────────────
        string appKey    = _config["TikTok:AppKey"]    ?? throw new InvalidOperationException("Missing TikTok:AppKey");
        string appSecret = _config["TikTok:AppSecret"] ?? throw new InvalidOperationException("Missing TikTok:AppSecret");
        string baseUrl = "/api/v2/token/get";

        // ── Step 2: เตรียม Request ────────────────────────────────
        var queryParams = new Dictionary<string, string>
        {
            { "app_key",    appKey    },
            { "app_secret", appSecret },
            { "auth_code",  authCode  },
            // grant_type ต้องเป็น "authorized_code" เสมอสำหรับ Initial Exchange
            { "grant_type", "authorized_code" }
        };

        string requestUrl = BuildUrl(baseUrl, queryParams);
        _logger.LogInformation("Code = {code}", authCode);

        _logger.LogInformation("[Auth] กำลังแลก Auth Code เป็น Access Token...");

        // ── Step 3: ยิง Request และรับ Token ─────────────────────
        var tokenData = await CallAuthApiAsync(requestUrl, "ExchangeCode");

        // Log ข้อมูลที่จำเป็นต้องเก็บลง Config (สำหรับ PoC)
        LogTokenInfo(tokenData,state);

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

        string appKey = _config["TikTok:AppKey"]
            ?? throw new InvalidOperationException("Missing TikTok:AppKey");

        string appSecret = _config["TikTok:AppSecret"]
            ?? throw new InvalidOperationException("Missing TikTok:AppSecret");

        var queryParams = new Dictionary<string, string>
    {
        { "app_key", appKey },
        { "app_secret", appSecret },
        { "refresh_token", tenant.RefreshToken },
        { "grant_type", "refresh_token" }
    };

        string requestUrl = BuildUrl("/api/v2/token/refresh", queryParams);

        _logger.LogInformation(
            "[Auth] กำลัง Refresh Token ของ {TenantCode} | AccessTokenExpireAt={ExpireAt:u}",
            tenantCode,
            tenant.AccessTokenExpireAt);

        var tokenData = await CallAuthApiAsync(requestUrl, "Refresh");

        var accessTokenExpireAt = DateTimeOffset
            .FromUnixTimeSeconds(tokenData.AccessTokenExpireIn)
            .UtcDateTime;

        var refreshTokenExpireAt = DateTimeOffset
            .FromUnixTimeSeconds(tokenData.RefreshTokenExpireIn)
            .UtcDateTime;

        _tenantStore.UpdateTokens(
            tenantCode,
            tokenData.AccessToken,
            tokenData.RefreshToken,
            accessTokenExpireAt,
            refreshTokenExpireAt);

        _logger.LogInformation(
            "[Auth] ✅ Refresh Token สำเร็จ | Tenant={TenantCode} | AccessTokenExpireAt={ExpireAt:u} | RefreshTokenExpireAt={RefreshExpireAt:u}",
            tenantCode,
            accessTokenExpireAt,
            refreshTokenExpireAt);

        return tokenData;
    }

    // ── Private Helpers ───────────────────────────────────────────

    /// <summary>สร้าง Full URL พร้อม Query String</summary>
    private static string BuildUrl(string path, Dictionary<string, string> queryParams)
    {
        string queryString = string.Join("&", queryParams
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{AuthBaseUrl}{path}?{queryString}";
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

        return tokenResult.Data;
    }

    /// <summary>
    /// Log ข้อมูล Token ที่ต้องนำไปอัปเดต appsettings.Development.json
    /// </summary>
    private void LogTokenInfo(TikTokTokenData data,string state)
    {
        var now                  = DateTime.UtcNow;
        var accessTokenExpireAt  = now.AddSeconds(data.AccessTokenExpireIn);
        var refreshTokenExpireAt = now.AddSeconds(data.RefreshTokenExpireIn);

        _logger.LogInformation("[Auth] ━━━ ค่าที่ต้องอัปเดตใน appsettings.Development.json ━━━");
        _logger.LogInformation("[Auth]   SellerName           : {SellerName}",  data.SellerName);
        _logger.LogInformation("[Auth]   ShopCipher (OpenId)  : {OpenId}",      data.OpenId);
        _logger.LogInformation("[Auth]   AccessToken          : {Token}",        data.AccessToken);
        _logger.LogInformation("[Auth]   RefreshToken         : {Token}",        data.RefreshToken);
        _logger.LogInformation("[Auth]   AccessTokenExpireAt  : {ExpireAt:u}",   accessTokenExpireAt);
        _logger.LogInformation("[Auth]   RefreshTokenExpireAt : {ExpireAt:u}",   refreshTokenExpireAt);
        _logger.LogInformation("[Auth]   State                : {state}",        state);
        _logger.LogInformation("[Auth] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }
}
