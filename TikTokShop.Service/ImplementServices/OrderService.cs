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

    public OrderService(
        IHttpClientFactory    httpClientFactory,
        IConfiguration        config,
        ILogger<OrderService> logger,
        TenantStore           tenantStore)
    {
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _logger            = logger;
        _tenantStore       = tenantStore;
    }

    // ════════════════════════════════════════════════════════════
    // GetOrdersAsync — Pull Engine: ดึงรายการออเดอร์
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<List<CleanOrderDto>> GetOrdersAsync(string tenantCode)
    {
        // ── Step 1: Resolve Tenant จาก TenantStore ───────────────
        if (!_tenantStore.TryGetByCode(tenantCode, out var tenant) || tenant == null)
        {
            _logger.LogWarning("[Orders] ไม่พบ Tenant: {TenantCode}", tenantCode);
            throw new KeyNotFoundException($"ไม่พบร้านค้ารหัส '{tenantCode}' ในระบบ");
        }

        _logger.LogInformation("[Orders] กำลังดึงออเดอร์ของ [{ShopName}] ({TenantCode})",
            tenant.ShopName, tenant.TenantCode);

        // ── Step 2: ดึง Config จาก appsettings ───────────────────
        string appKey    = _config["TikTok:AppKey"]    ?? throw new InvalidOperationException("ไม่พบ TikTok:AppKey");
        string appSecret = _config["TikTok:AppSecret"] ?? throw new InvalidOperationException("ไม่พบ TikTok:AppSecret");
        string baseUrl   = _config["TikTok:BaseUrl"]   ?? "https://open-api-sandbox.tiktokglobalshop.com";

        // ── Step 3: เตรียม Query Params (ยังไม่ใส่ sign) ─────────
        string endpointPath = "/order/202309/orders";
        var queryParams = new Dictionary<string, string>
        {
            { "app_key",   appKey },
            { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
            // ⚠️ PoC: ใช้ ids mock ก่อน — Production ควร Query จริงๆ ด้วย time_range
            { "ids",       "584695018161079306" }
        };

        // ใส่ shop_cipher ถ้ามี (V2 API บังคับต้องมี)
        if (!string.IsNullOrWhiteSpace(tenant.ShopCipher))
            queryParams["shop_cipher"] = tenant.ShopCipher;

        // ── Step 4: สร้าง Signature ด้วย TikTokSignHelper ────────
        // ห้าม Implement HMAC ซ้ำในนี้ — ใช้ Helper เท่านั้น
        queryParams["sign"] = TikTokSignHelper.GenerateSign(appSecret, endpointPath, queryParams);

        string queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        string requestUrl  = $"{baseUrl}{endpointPath}?{queryString}";
        _logger.LogDebug("[Orders] URL: {Url}", requestUrl);

        // ── Step 5: ยิง HTTP GET พร้อม Access Token ───────────────
        // Access Token ส่งผ่าน Header x-tts-access-token (ไม่ใช่ Query Param)
        var client = _httpClientFactory.CreateClient("TikTokClient");
        client.DefaultRequestHeaders.Remove("x-tts-access-token");
        client.DefaultRequestHeaders.Add("x-tts-access-token", tenant.AccessToken);

        string rawJson;
        try
        {
            var response = await client.GetAsync(requestUrl);
            rawJson = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[Orders] HTTP {StatusCode}", (int)response.StatusCode);
            _logger.LogDebug("[Orders] Body: {Json}", rawJson);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[Orders] HTTP Request ล้มเหลว: {TenantCode}", tenantCode);
            throw;
        }

        // ── Step 6: Deserialize และ Transform → CleanOrderDto ─────
        var tikTokResponse = JsonSerializer.Deserialize<TikTokApiResponse>(rawJson);

        if (tikTokResponse == null || tikTokResponse.Code != 0)
        {
            string errMsg = tikTokResponse?.Message ?? "ไม่สามารถ parse response ได้";
            _logger.LogWarning("[Orders] TikTok Error: Code={Code}, Msg={Msg}",
                tikTokResponse?.Code, errMsg);
            throw new HttpRequestException($"TikTok API Error [{tikTokResponse?.Code}]: {errMsg}");
        }

        var orders = tikTokResponse.Data?.Orders ?? new List<TikTokOrder>();

        // Map TikTokOrder → CleanOrderDto (เฉพาะ fields ที่จำเป็น)
        return orders.Select(o => new CleanOrderDto
        {
            OrderId          = o.Id,
            CustomerId       = !string.IsNullOrWhiteSpace(o.BuyerUid) ? o.BuyerUid : o.BuyerEmail,
            TotalAmountTHB   = decimal.TryParse(o.PaymentInfo?.TotalAmount, out var amt) ? amt : 0m,
            OrderStatus      = o.Status,
            CreatedTimestamp = o.CreateTime.ToString(),
            TenantCode       = tenantCode
        }).ToList();
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
        _logger.LogWarning(
            "[OrderDetail] 🔍 ShopId={ShopId} | Tenant={TenantCode} | ShopCipher={ShopCipher} | OrderId={OrderId}",
            shopId, tenant.TenantCode, tenant.ShopCipher, orderId);

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
}
