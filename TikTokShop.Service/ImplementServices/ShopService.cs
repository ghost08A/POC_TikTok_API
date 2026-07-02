using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.ResponseModels;
using TikTokShop.Service.Config;
using TikTokShop.Service.Helpers;
using TikTokShop.Service.Stores;

namespace TikTokShop.Service.ImplementServices;

// ================================================================
// ShopService.cs — Shop & Token Health Check Service
//
// รับผิดชอบ:
//   1. GetAuthorizedShopsAsync — ดึงรายการร้านค้าที่ Authorize แล้ว
//      ใช้ตรวจสอบว่า Access Token ยัง Active หรือหมดอายุ
//
// TikTok Endpoint: GET /authorization/202309/shops
//
// ผลลัพธ์ที่สำคัญ:
//   - code=0 → Token ยัง Active ✅
//   - code≠0 → Token หมดอายุ/Invalid ❌ ต้อง Refresh หรือ Re-authorize
//   - cipher  → ค่า shop_cipher ที่ต้องส่งใน API Request ทุกครั้ง
// ================================================================
public class ShopService : IShopService
{
    private readonly IHttpClientFactory      _httpClientFactory;
    private readonly IConfiguration          _config;
    private readonly ILogger<ShopService>    _logger;
    private readonly TenantStore             _tenantStore;

    private const string ShopsEndpoint = "/authorization/202309/shops";

    public ShopService(
        IHttpClientFactory   httpClientFactory,
        IConfiguration       config,
        ILogger<ShopService> logger,
        TenantStore          tenantStore)
    {
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _logger            = logger;
        _tenantStore       = tenantStore;
    }

    // ════════════════════════════════════════════════════════════
    // GetAuthorizedShopsAsync — Token Health Check (by TenantCode)
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<TikTokAuthorizedShopsResponse> GetAuthorizedShopsAsync(string tenantCode)
    {
        if (!_tenantStore.TryGetByCode(tenantCode, out var tenant) || tenant == null)
        {
            _logger.LogWarning("[Shops] ไม่พบ Tenant: {TenantCode}", tenantCode);
            throw new KeyNotFoundException($"ไม่พบร้านค้ารหัส '{tenantCode}' ในระบบ");
        }

        return await GetAuthorizedShopsByAccessTokenAsync(tenant.AccessToken);
    }

    // ════════════════════════════════════════════════════════════
    // GetAuthorizedShopsByAccessTokenAsync — Token Health Check (by Token)
    // ════════════════════════════════════════════════════════════
    public async Task<TikTokAuthorizedShopsResponse> GetAuthorizedShopsByAccessTokenAsync(string accessToken)
    {
        var cfg = TikTokAppConfig.FromConfig(_config);

        var queryParams = TikTokRequestBuilder.CreateBaseParams(cfg.AppKey);
        string requestUrl = TikTokRequestBuilder.BuildSignedGetUrl(cfg.BaseUrl, ShopsEndpoint, cfg.AppSecret, queryParams);

        // Named client "TikTokClient" มี BaseAddress + default headers ตั้งไว้แล้ว
        var client = _httpClientFactory.CreateClient("TikTokClient");
        client.DefaultRequestHeaders.Remove("x-tts-access-token");
        client.DefaultRequestHeaders.Add("x-tts-access-token", accessToken);

        string rawJson;
        try
        {
            var response = await client.GetAsync(requestUrl);
            rawJson = await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Shops] HTTP Request ล้มเหลว");
            throw;
        }

        var result = JsonSerializer.Deserialize<TikTokAuthorizedShopsResponse>(rawJson);

        if (result == null)
        {
            _logger.LogError("[Shops] Deserialize ได้ null");
            throw new HttpRequestException("ไม่สามารถ Parse JSON Response จาก TikTok ได้");
        }

        return result;
    }
}
