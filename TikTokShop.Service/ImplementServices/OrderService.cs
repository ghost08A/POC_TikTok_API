using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.Models;
using TikTokShop.Domain.ResponseModels;
using TikTokShop.Service.Helpers;
using TikTokShop.Service.Stores;

namespace TikTokShop.Service.ImplementServices;

// ================================================================
// OrderService.cs — Order Management Service
//
// รับผิดชอบ:
//   1. GetOrdersAsync        — Pull Engine: ดึงรายการออเดอร์ทั้งหมด
//   2. FetchAndPrintOrderDetailAsync — ดึงรายละเอียดออเดอร์รายเดี่ยว
//
// TikTok Endpoints ที่ใช้:
//   - GET /order/202309/orders   (รายการออเดอร์)
//   - GET /order/202507/orders   (รายละเอียด รองรับ fields ใหม่กว่า)
// ================================================================
public class OrderService : IOrderService
{
    private readonly IHttpClientFactory       _httpClientFactory;
    private readonly IConfiguration           _config;
    private readonly ILogger<OrderService>    _logger;
    private readonly TenantStore              _tenantStore;
    private readonly IAuthService _authService;   

    public OrderService(
        IHttpClientFactory    httpClientFactory,
        IConfiguration        config,
        ILogger<OrderService> logger,
        TenantStore           tenantStore,
        IAuthService          authService)
    {
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _logger            = logger;
        _tenantStore       = tenantStore;
        _authService        = authService;
    }
    // ════════════════════════════════════════════════════════════
    // FetchAndPrintOrderDetailAsync — ดึงรายละเอียดออเดอร์รายเดี่ยว
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task FetchAndPrintOrderDetailAsync(string shopId, string orderId)
    {
        // ── Step 1: ค้นหา Tenant จาก ShopId (Webhook ส่ง shop_id มา) ─
        var tenant = _tenantStore.FindByShopId(shopId);

        if (tenant == null)
        {
            _logger.LogError("[OrderDetail] ❌ ไม่พบร้านค้า ShopId: {ShopId}", shopId);
            return;
        }
        await EnsureValidAccessTokenAsync(tenant);

        // ── Step 2: ดึง Config ────────────────────────────────────
        string appKey    = _config["TikTok:AppKey"]    ?? "";
        string appSecret = _config["TikTok:AppSecret"] ?? "";
        string baseUrl   = _config["TikTok:BaseUrl"]   ?? "https://open-api-sandbox.tiktokglobalshop.com";

        // ── Step 3: เตรียม Query Params ──────────────────────────
        // ใช้ Endpoint เวอร์ชัน 202507 ที่รองรับ field ใหม่กว่า
        string endpointPath = "/order/202507/orders";
        var queryParams = new Dictionary<string, string>
        {
            { "app_key",     appKey             },
            { "ids",         orderId            }, // TikTok รับ ids เป็น comma-separated ได้
            { "shop_cipher", tenant.ShopCipher  },
            { "timestamp",   DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
        };

        // ── Step 4: สร้าง Signature ───────────────────────────────
        queryParams["sign"] = TikTokSignHelper.GenerateSign(appSecret, endpointPath, queryParams);

        string queryString = string.Join("&", queryParams
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        string requestUrl = $"{baseUrl}{endpointPath}?{queryString}";

        // Debug Log: แสดงข้อมูลครบสำหรับ Troubleshooting
        //_logger.LogWarning(
        //    "[OrderDetail] 🔍 ShopId={ShopId} | Tenant={TenantCode} | ShopCipher={ShopCipher} | OrderId={OrderId}",
        //    shopId, tenant.TenantCode, tenant.ShopCipher, orderId);

        // ── Step 5: ยิง HTTP Request ──────────────────────────────
        // ใช้ HttpRequestMessage เพื่อควบคุม Header ได้แม่นยำกว่า
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Add("x-tts-access-token", tenant.AccessToken);
        request.Headers.Add("Accept", "application/json");

        var client   = _httpClientFactory.CreateClient();
        var response = await client.SendAsync(request);
        string rawJson = await response.Content.ReadAsStringAsync();

        _logger.LogWarning("[OrderDetail] HTTP {StatusCode}: {Body}",
            (int)response.StatusCode, rawJson);

        if (!response.IsSuccessStatusCode)
            return;

        // ── Step 6: Parse และแสดงผล ──────────────────────────────
        var apiResult = JsonSerializer.Deserialize<TikTokOrderDetailApiResponse>(rawJson);
        var orderData = apiResult?.Data?.Orders?.FirstOrDefault();

        if (orderData == null)
        {
            _logger.LogWarning("[OrderDetail] ⚠️ ไม่พบ order ใน response: {Json}", rawJson);
            return;
        }

        // Print สรุปข้อมูลออเดอร์ออก Console (สำหรับ PoC Demo)
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"  📦 Order Detail — {orderData.OrderId}");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"  order_id     : {orderData.OrderId}");
        Console.WriteLine($"  user_id      : {orderData.UserId}");
        Console.WriteLine($"  status       : {orderData.Status}");
        Console.WriteLine($"  total_amount : {orderData.Payment?.TotalAmount} {orderData.Payment?.Currency}");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    private async Task EnsureValidAccessTokenAsync(ShopTenant tenant)
    {
        if (tenant.IsRefreshTokenExpired)
        {
            tenant.Status = "REAUTHORIZE_REQUIRED";

            throw new InvalidOperationException(
                $"RefreshToken ของร้าน {tenant.TenantCode} หมดอายุแล้ว ต้องให้ร้านเชื่อมต่อ TikTok ใหม่");
        }

        // Refresh ล่วงหน้า 30 นาที ไม่รอให้หมดจริง
        if (DateTime.UtcNow.AddMinutes(30) < tenant.AccessTokenExpireAt)
        {
            return;
        }

        _logger.LogWarning(
            "[Token] AccessToken ใกล้หมดอายุ | Tenant={TenantCode}, ExpireAt={ExpireAt:u}, RemainingMinutes={Minutes}",
            tenant.TenantCode,
            tenant.AccessTokenExpireAt,
            tenant.AccessTokenRemainingMinutes
        );

        await _authService.RefreshAccessTokenAsync(tenant.TenantCode);

        // หลัง RefreshAccessTokenAsync ต้อง update token ใน TenantStore แล้ว
        _logger.LogInformation(
            "[Token] Refresh AccessToken สำเร็จ | Tenant={TenantCode}, NewExpireAt={ExpireAt:u}",
            tenant.TenantCode,
            tenant.AccessTokenExpireAt
        );
    }
}
