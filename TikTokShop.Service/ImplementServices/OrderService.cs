using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.Models;
using TikTokShop.Domain.RequestModels;
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
    public async Task<string?> FetchAndPrintOrderDetailAsync(string shopId, string orderId)
    {
        var tenant = _tenantStore.FindByShopId(shopId);

        if (tenant == null)
        {
            _logger.LogError("[OrderDetail] ShopId not found: {ShopId}", shopId);
            return null;
        }

        await EnsureValidAccessTokenAsync(tenant);

        string appKey    = _config["TikTok:AppKey"]    ?? "";
        string appSecret = _config["TikTok:AppSecret"] ?? "";
        string baseUrl   = _config["TikTok:BaseUrl"]   ?? "https://open-api-sandbox.tiktokglobalshop.com";
        _logger.LogInformation("tastURL{url}-----------------------------------------------", baseUrl);


        string endpointPath = "/order/202507/orders";
        var queryParams = new Dictionary<string, string>
        {
            { "app_key",     appKey             },
            { "ids",         orderId            },
            { "shop_cipher", tenant.ShopCipher  },
            { "timestamp",   DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
        };

        queryParams["sign"] = TikTokSignHelper.GenerateSign(appSecret, endpointPath, queryParams);

        string queryString = string.Join("&", queryParams
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        string requestUrl = $"{baseUrl}{endpointPath}?{queryString}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Add("x-tts-access-token", tenant.AccessToken);
        request.Headers.Add("Accept", "application/json");

        var client   = _httpClientFactory.CreateClient();
        var response = await client.SendAsync(request);
        string rawJson = await response.Content.ReadAsStringAsync();

        _logger.LogWarning("[OrderDetail] HTTP {StatusCode}: {Body}", (int)response.StatusCode, rawJson);

        return rawJson;
    }
    public async Task<string?> SearchOrderListAsync(
    string shopId,
    SearchOrderListRequestModel request)
    {
        var tenant = _tenantStore.FindByShopId(shopId);
        if (tenant == null)
        {
            _logger.LogError("[OrderList] ShopId not found: {ShopId}", shopId);
            return null;
        }

        await EnsureValidAccessTokenAsync(tenant);

        string appKey = _config["TikTok:AppKey"] ?? "";
        string appSecret = _config["TikTok:AppSecret"] ?? "";
        string baseUrl = _config["TikTok:BaseUrl"]
            ?? "https://open-api-sandbox.tiktokglobalshop.com";

       

       string endpointPath = $"/order/202309/orders/search";

        // ── Step 3: เตรียม timezone สำหรับแปลง DateTime → Unix ─
        var inputTimeZone = GetInputTimeZone();
        // ── Step 4: query params ─────────────────────────────────
        var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        pageSize = Math.Min(pageSize, 100);

        var queryParams = new Dictionary<string, string>
        {
            { "app_key",     appKey },
            { "shop_cipher", tenant.ShopCipher },
            { "timestamp",   DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
            { "page_size",   pageSize.ToString() }
        };

        if (!string.IsNullOrWhiteSpace(request.PageToken))
            queryParams["page_token"] = request.PageToken;

        // ── Step 5: body filter ──────────────────────────────────
        var bodyObj = new Dictionary<string, object>();

        if (request.CreateTimeFrom.HasValue)
            bodyObj["create_time_ge"] = ToUnixSeconds(request.CreateTimeFrom.Value, inputTimeZone);

        if (request.CreateTimeTo.HasValue)
            bodyObj["create_time_lt"] = ToUnixSeconds(request.CreateTimeTo.Value, inputTimeZone);

        if (request.UpdateTimeFrom.HasValue)
            bodyObj["update_time_ge"] = ToUnixSeconds(request.UpdateTimeFrom.Value, inputTimeZone);

        if (request.UpdateTimeTo.HasValue)
            bodyObj["update_time_lt"] = ToUnixSeconds(request.UpdateTimeTo.Value, inputTimeZone);

        // sort ต้องอยู่ใน body (TikTok 202309 spec)
        bodyObj["sort_field"] = string.IsNullOrWhiteSpace(request.SortField) ? "create_time" : request.SortField;
        bodyObj["sort_order"] = string.IsNullOrWhiteSpace(request.SortOrder) ? "DESC"        : request.SortOrder;

        // ── TikTok API 202309 ต้องมี time range เสมอ ────────────────
        // fallback: create_time ย้อนหลัง 1 ปี — ครอบคลุม sandbox orders ทุกชุด
        bool hasTimeFilter =
            request.CreateTimeFrom.HasValue || request.CreateTimeTo.HasValue ||
            request.UpdateTimeFrom.HasValue || request.UpdateTimeTo.HasValue;

        if (!hasTimeFilter)
        {
            var now  = DateTimeOffset.UtcNow;
            var from = now.AddDays(-365);
            bodyObj["create_time_ge"] = from.ToUnixTimeSeconds();
            bodyObj["create_time_lt"] = now.ToUnixTimeSeconds();

            _logger.LogInformation(
                "[OrderList] No time filter, fallback create_time 1 year | from={From}({GeVal}) to={To}({LtVal})",
                from.ToString("yyyy-MM-dd HH:mm:ss"),
                from.ToUnixTimeSeconds(),
                now.ToString("yyyy-MM-dd HH:mm:ss"),
                now.ToUnixTimeSeconds());
        }

        if (!string.IsNullOrWhiteSpace(request.OrderStatus))
            bodyObj["order_status"] = request.OrderStatus;

        if (!string.IsNullOrWhiteSpace(request.BuyerUserId))
            bodyObj["buyer_user_id"] = request.BuyerUserId;

        if (!string.IsNullOrWhiteSpace(request.ShippingType))
            bodyObj["shipping_type"] = request.ShippingType;

        string requestBody = JsonSerializer.Serialize(bodyObj);

        // ── Step 6: sign ต้องใช้ body จริงที่ส่ง ───────────────
        queryParams["sign"] = TikTokSignHelper.GenerateSign(
            appSecret,
            endpointPath,
            queryParams,
            requestBody);

        string queryString = string.Join("&", queryParams
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        string requestUrl = $"{baseUrl}{endpointPath}?{queryString}";

        // ── Step 7: ยิง request ─────────────────────────────────
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        httpRequest.Headers.Add("x-tts-access-token", tenant.AccessToken);
        httpRequest.Headers.Add("Accept", "application/json");

        httpRequest.Content = new StringContent(
      requestBody,
      Encoding.UTF8,
      "application/json");

        var client = _httpClientFactory.CreateClient();

        _logger.LogInformation(
            "[OrderList] Request | ShopId={ShopId} | Url={Url} | Body={Body}",
            shopId,
            requestUrl,
            requestBody);

        var response = await client.SendAsync(httpRequest);
        string rawJson = await response.Content.ReadAsStringAsync();

        _logger.LogWarning("[OrderList] HTTP {StatusCode}: {Body}", (int)response.StatusCode, rawJson);

        return rawJson;
    }


    public async Task ProcessCancellationWebhookAsync(
   string shopId,
   string orderId,
   string cancelStatus,
   long webhookTimestamp,
   string rawWebhookJson)
    {
        _logger.LogWarning(
            "[CancellationService] เริ่มประมวลผล Cancellation | ShopId={ShopId} | OrderId={OrderId} | CancelStatus={CancelStatus}",
            shopId,
            orderId,
            cancelStatus);

        // Step 1: ดึง Order Detail 
        await FetchAndPrintOrderDetailAsync(shopId, orderId);

        // Step 2: ดึงรายละเอียดการยกเลิกจาก TikTok Search Cancellations API
        var cancellationRawJson = await SearchCancellationByOrderIdAsync(shopId, orderId);

        if (string.IsNullOrWhiteSpace(cancellationRawJson))
        {
            _logger.LogWarning(
                "[CancellationService] ไม่พบรายละเอียด cancellation จาก TikTok | OrderId={OrderId}",
                orderId);
        }
        else
        {
            _logger.LogWarning(
                "[CancellationService] ได้รายละเอียด cancellation แล้ว | OrderId={OrderId}",
                orderId);

            _logger.LogWarning(
                "[CancellationService] Cancellation RawJson: {RawJson}",
                cancellationRawJson);
        }

        // Step 3: ถ้าสถานะ cancel จบแล้ว ค่อยเตรียมหักแต้ม
        if (cancelStatus is
            "CANCELLATION_REQUEST_SUCCESS" or
            "CANCELLATION_REQUEST_COMPLETE")
        {
            _logger.LogWarning(
                "[CancellationService] Cancellation สำเร็จแล้ว OrderId={OrderId} ต่อไปต้องเตรียม REVERSE แต้ม",
                orderId);

            // TODO ขั้นต่อไป:
            // 1. Parse cancellationRawJson
            // 2. เก็บ cancellation detail ลง DB / InMemory
            // 3. เช็คว่า order นี้เคยได้แต้มแล้วหรือยัง
            // 4. ถ้าเคยได้แต้มแล้ว และยังไม่เคย REVERSE → สร้าง PointTransaction แบบ REVERSE
        }
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

    public async Task<string?> SearchCancellationByOrderIdAsync(string shopId, string orderId)
    {
        // ── Step 1: หา tenant จาก shopId ─────────────────────────
        var tenant = _tenantStore.FindByShopId(shopId);

        if (tenant == null)
        {
            _logger.LogError("[CancellationSearch] ShopId not found: {ShopId}", shopId);
            return null;
        }

        await EnsureValidAccessTokenAsync(tenant);

        // ── Step 2: ดึง config ───────────────────────────────────
        string appKey = _config["TikTok:AppKey"] ?? "";
        string appSecret = _config["TikTok:AppSecret"] ?? "";
        string baseUrl = _config["TikTok:BaseUrl"]
            ?? "https://open-api-sandbox.tiktokglobalshop.com";

        // ใช้ endpoint Search Cancellations 
        string endpointPath = "/return_refund/202602/cancellations/search";

        // ── Step 3: query params ─────────────────────────────────
        var queryParams = new Dictionary<string, string>
    {
        { "app_key", appKey },
        { "shop_cipher", tenant.ShopCipher },
        { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
    };

        // ── Step 4: body สำหรับ POST ─────────────────────────────
        var bodyObj = new
        {
            order_ids = new[] { orderId },
            page_size = 10
        };

        string requestBody = JsonSerializer.Serialize(bodyObj);

        // ── Step 5: sign ต้องเอา requestBody เข้าไปด้วย ─────────
        queryParams["sign"] = TikTokSignHelper.GenerateSign(
            appSecret,
            endpointPath,
            queryParams,
            requestBody);

        string queryString = string.Join("&", queryParams
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        string requestUrl = $"{baseUrl}{endpointPath}?{queryString}";

        // ── Step 6: ยิง request ─────────────────────────────────
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        request.Headers.Add("x-tts-access-token", tenant.AccessToken);
        request.Headers.Add("Accept", "application/json");

        request.Content = new StringContent(
            requestBody,
            Encoding.UTF8,
            "application/json");

        var client = _httpClientFactory.CreateClient();

        var response = await client.SendAsync(request);
        string rawJson = await response.Content.ReadAsStringAsync();

        _logger.LogWarning("[CancellationSearch] HTTP {StatusCode}: {Body}", (int)response.StatusCode, rawJson);

        return rawJson;
    }

    public async Task<string?> SearchReturnByOrderIdAsync(
        string        shopId,
        string        orderId,
        List<string>? returnStatus = null,
        List<string>? returnType   = null,
        DateTime?     createFrom   = null,
        DateTime?     createTo     = null,
        string?       pageToken    = null,
        int           pageSize     = 10)
    {
        // ── Step 1: หา tenant จาก shopId ─────────────────────────
        var tenant = _tenantStore.FindByShopId(shopId);

        if (tenant == null)
        {
            _logger.LogError("[ReturnSearch] ShopId not found: {ShopId}", shopId);
            return null;
        }

        await EnsureValidAccessTokenAsync(tenant);

        // ── Step 2: ดึง config ───────────────────────────────────
        string appKey    = _config["TikTok:AppKey"]    ?? "";
        string appSecret = _config["TikTok:AppSecret"] ?? "";
        string baseUrl   = _config["TikTok:BaseUrl"]
            ?? "https://open-api-sandbox.tiktokglobalshop.com";

        // Search Returns 202602
        string endpointPath = "/return_refund/202602/returns/search";

        // ── Step 3: query params ─────────────────────────────────
        var queryParams = new Dictionary<string, string>
        {
            { "app_key",     appKey },
            { "shop_cipher", tenant.ShopCipher },
            { "timestamp",   DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
        };

        // ── Step 4: body ตาม TikTok API spec ─────────────────────
        // order_ids: required  (array of string)
        // return_status: optional array — RETURN_OR_REFUND_REQUEST_PENDING ฯลฯ
        // return_type: optional array  — REFUND | RETURN_AND_REFUND | REPLACEMENT
        // create_time_ge / le: optional Unix timestamp (second)
        // page_token: optional string สำหรับ pagination
        // page_size: optional int (default 10, max 100)
        var bodyDict = new Dictionary<string, object>
        {
            { "order_ids", new[] { orderId } },
            { "page_size", pageSize }
        };

        if (returnStatus is { Count: > 0 })
            bodyDict["return_status"] = returnStatus;

        if (returnType is { Count: > 0 })
            bodyDict["return_type"] = returnType;

        if (createFrom.HasValue)
        {
            long createTimeGe = new DateTimeOffset(createFrom.Value, TimeSpan.FromHours(7)).ToUnixTimeSeconds();
            bodyDict["create_time_ge"] = createTimeGe;
        }

        if (createTo.HasValue)
        {
            long createTimeLe = new DateTimeOffset(createTo.Value, TimeSpan.FromHours(7)).ToUnixTimeSeconds();
            bodyDict["create_time_le"] = createTimeLe;
        }

        if (!string.IsNullOrWhiteSpace(pageToken))
            bodyDict["page_token"] = pageToken;

        string requestBody = JsonSerializer.Serialize(bodyDict);

        // ── Step 5: sign ต้องใช้ requestBody ด้วย เพราะเป็น POST ──
        queryParams["sign"] = TikTokSignHelper.GenerateSign(
            appSecret,
            endpointPath,
            queryParams,
            requestBody);

        string queryString = string.Join("&", queryParams
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        string requestUrl = $"{baseUrl}{endpointPath}?{queryString}";

        // ── Step 6: ยิง request ─────────────────────────────────
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        request.Headers.Add("x-tts-access-token", tenant.AccessToken);
        request.Headers.Add("Accept", "application/json");

        request.Content = new StringContent(
            requestBody,
            Encoding.UTF8,
            "application/json");

        var client = _httpClientFactory.CreateClient();

        var response = await client.SendAsync(request);
        string rawJson = await response.Content.ReadAsStringAsync();

        _logger.LogWarning("[ReturnSearch] HTTP {StatusCode}: {Body}", (int)response.StatusCode, rawJson);

        return rawJson;
    }

    public async Task ProcessReturnWebhookAsync(
    string shopId,
    string orderId,
    string returnStatus,
    long webhookTimestamp,
    string rawWebhookJson)
    {
        _logger.LogWarning(
            "[ReturnService] เริ่มประมวลผล Return/Refund | ShopId={ShopId} | OrderId={OrderId} | ReturnStatus={ReturnStatus}",
            shopId,
            orderId,
            returnStatus);

        // Step 1: ดึง Order Detail เพื่อดู order หลัก
        await FetchAndPrintOrderDetailAsync(shopId, orderId);

        // Step 2: ดึงรายละเอียด Return/Refund จาก TikTok
        var returnRawJson = await SearchReturnByOrderIdAsync(shopId, orderId);

        if (string.IsNullOrWhiteSpace(returnRawJson))
        {
            _logger.LogWarning(
                "[ReturnService] ไม่พบรายละเอียด return/refund จาก TikTok | OrderId={OrderId}",
                orderId);
        }
        else
        {
            _logger.LogWarning(
                "[ReturnService] ได้รายละเอียด return/refund แล้ว | OrderId={OrderId}",
                orderId);

            _logger.LogWarning(
                "[ReturnService] Return RawJson: {RawJson}",
                returnRawJson);
        }

        // Step 3: รอบนี้ยังไม่หักแต้มจริง แค่ log ก่อน
        // ขั้นถัดไปค่อย parse returnRawJson แล้วดู refund amount / return_status
        if (TikTokHelper.IsPossibleFinalRefundStatus(returnStatus))
        {
            _logger.LogWarning(
                "[ReturnService] Return/Refund อาจสำเร็จแล้ว OrderId={OrderId} ต่อไปต้องเตรียม REVERSE แต้ม",
                orderId);
        }
    }

    private TimeZoneInfo GetInputTimeZone()
    {
        var timeZoneId = _config["TikTok:InputTimeZone"];

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZoneId = "Asia/Bangkok";
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            // กันกรณีรันบน Windows แล้วไม่รู้จัก Asia/Bangkok
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    private static long ToUnixSeconds(DateTime value, TimeZoneInfo inputTimeZone)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(value).ToUnixTimeSeconds();
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return new DateTimeOffset(value).ToUnixTimeSeconds();
        }

        // ถ้า frontend ส่ง "2026-06-29T00:00:00" แบบไม่มี timezone
        // เราจะถือว่าเป็นเวลาตาม inputTimeZone เช่น Asia/Bangkok
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(unspecified, inputTimeZone);

        return new DateTimeOffset(utcDateTime).ToUnixTimeSeconds();
    }
}
