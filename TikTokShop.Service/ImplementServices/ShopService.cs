using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.ResponseModels;
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
    // GetAuthorizedShopsAsync — Token Health Check
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<TikTokAuthorizedShopsResponse> GetAuthorizedShopsAsync(string tenantCode)
    {
        // ── Step 1: Resolve Tenant ────────────────────────────────
        if (!_tenantStore.TryGetByCode(tenantCode, out var tenant) || tenant == null)
        {
            _logger.LogWarning("[Shops] ไม่พบ Tenant: {TenantCode}", tenantCode);
            throw new KeyNotFoundException($"ไม่พบร้านค้ารหัส '{tenantCode}' ในระบบ");
        }

        // ── Step 2: ดึง Config ────────────────────────────────────
        string appKey    = _config["TikTok:AppKey"]    ?? throw new InvalidOperationException("ไม่พบ TikTok:AppKey");
        string appSecret = _config["TikTok:AppSecret"] ?? throw new InvalidOperationException("ไม่พบ TikTok:AppSecret");
        string baseUrl   = _config["TikTok:BaseUrl"]   ?? "https://open-api-sandbox.tiktokglobalshop.com";

        // ── Step 3: เตรียม Query Params ──────────────────────────
        // ⚠️ ห้ามใส่ sign และ access_token ตอนนี้ — ใส่หลัง GenerateSign เท่านั้น
        string endpointPath = "/authorization/202309/shops";
        var queryParams = new Dictionary<string, string>
        {
            { "app_key",   appKey },
            { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
        };

        // ── Step 4: สร้าง Signature ───────────────────────────────
        queryParams["sign"] = TikTokSignHelper.GenerateSign(appSecret, endpointPath, queryParams);

        string queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        string requestUrl  = $"{baseUrl}{endpointPath}?{queryString}";

        _logger.LogDebug("[Shops] URL: {Url}", requestUrl);
        _logger.LogDebug("[Shops] Tenant={TenantCode} | Sign={Sign}", tenantCode, queryParams["sign"]);

        // ── Step 5: ยิง HTTP GET พร้อม Access Token ───────────────
        var client = _httpClientFactory.CreateClient("TikTokClient");
        client.DefaultRequestHeaders.Remove("x-tts-access-token");
        client.DefaultRequestHeaders.Add("x-tts-access-token", tenant.AccessToken);

        string rawJson;
        try
        {
            var response = await client.GetAsync(requestUrl);
            rawJson = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[Shops] HTTP {StatusCode} | Tenant={TenantCode}",
                (int)response.StatusCode, tenantCode);
            _logger.LogDebug("[Shops] Raw: {Json}", rawJson);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Shops] HTTP Request ล้มเหลว: {TenantCode}", tenantCode);
            throw;
        }

        // ── Step 6: Deserialize → TikTokAuthorizedShopsResponse ───
        var result = JsonSerializer.Deserialize<TikTokAuthorizedShopsResponse>(rawJson);

        if (result == null)
        {
            _logger.LogError("[Shops] Deserialize ได้ null สำหรับ {TenantCode}", tenantCode);
            throw new HttpRequestException("ไม่สามารถ Parse JSON Response จาก TikTok ได้");
        }

        int shopCount = result.Data?.Shops.Count ?? 0;
        _logger.LogInformation(
            "[Shops] ✅ TenantCode={TenantCode} | TikTok Code={Code} | Shops={Count}",
            tenantCode, result.Code, shopCount);

        // Return raw response (รวม code/message) เพื่อให้ Controller ตัดสินใจต่อ
        return result;
    }
}
