using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.Models;
using TikTokShop.Domain.RequestModels;
using TikTokShop.Service.Config;
using TikTokShop.Service.Helpers;
using TikTokShop.Service.Stores;

namespace TikTokShop.Service.ImplementServices;

// ================================================================
// OrderService.cs — Order Management Service
//
// รับผิดชอบ:
//   1. FetchAndPrintOrderDetailAsync  — ดึงรายละเอียดออเดอร์รายเดี่ยว
//   2. SearchOrderListAsync           — ค้นหารายการออเดอร์
//   3. SearchCancellationByOrderIdAsync — ดึงรายละเอียดการยกเลิก
//   4. SearchReturnByOrderIdAsync     — ดึงรายละเอียด Return/Refund
//   5. ProcessCancellationWebhookAsync / ProcessReturnWebhookAsync — ประมวลผล Webhook
//
// TikTok Endpoints ที่ใช้:
//   GET  /order/202507/orders                        — รายละเอียดออเดอร์
//   POST /order/202309/orders/search                 — ค้นหาออเดอร์
//   POST /return_refund/202602/cancellations/search  — ค้นหาการยกเลิก
//   POST /return_refund/202602/returns/search        — ค้นหา Return/Refund
// ================================================================
public class OrderService : IOrderService
{
    private readonly IHttpClientFactory    _httpClientFactory;
    private readonly IConfiguration       _config;
    private readonly ILogger<OrderService> _logger;
    private readonly TenantStore          _tenantStore;
    private readonly IAuthService         _authService;

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
        _authService       = authService;
    }

    // ════════════════════════════════════════════════════════════
    // FetchAndPrintOrderDetailAsync — ดึงรายละเอียดออเดอร์รายเดี่ยว
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<string?> FetchAndPrintOrderDetailAsync(string shopId, string orderId)
    {
        var tenant = await ResolveTenantAsync(shopId, "[OrderDetail]");
        if (tenant == null) return null;

        var cfg = TikTokAppConfig.FromConfig(_config);
        const string endpoint = "/order/202507/orders";

        var queryParams = TikTokRequestBuilder.CreateShopParams(cfg.AppKey, tenant.ShopCipher);
        queryParams["ids"] = orderId;

        string url = TikTokRequestBuilder.BuildSignedGetUrl(cfg.BaseUrl, endpoint, cfg.AppSecret, queryParams);

        var apiClient = new TikTokApiClient(_httpClientFactory, _logger);
        var rawJson = await apiClient.GetAsync(url, tenant.AccessToken, "[OrderDetail]");
        LogTikTokOrderPayload("[OrderDetail]", "GET /order/202507/orders", rawJson);
        return rawJson;
    }

    // ════════════════════════════════════════════════════════════
    // SearchOrderListAsync — ค้นหารายการออเดอร์
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public async Task<string?> SearchOrderListAsync(string shopId, SearchOrderListRequestModel request)
    {
        var tenant = await ResolveTenantAsync(shopId, "[OrderList]");
        if (tenant == null) return null;

        var cfg = TikTokAppConfig.FromConfig(_config);
        const string endpoint = "/order/202309/orders/search";

        var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 100);

        var queryParams = TikTokRequestBuilder.CreateShopParams(cfg.AppKey, tenant.ShopCipher);
        queryParams["page_size"] = pageSize.ToString();

        if (!string.IsNullOrWhiteSpace(request.PageToken))
            queryParams["page_token"] = request.PageToken;

        string requestBody = JsonSerializer.Serialize(BuildOrderSearchBody(request));

        string url = TikTokRequestBuilder.BuildSignedPostUrl(cfg.BaseUrl, endpoint, cfg.AppSecret, queryParams, requestBody);

        _logger.LogInformation(
            "[OrderList] ShopId={ShopId} | Url={Url} | Body={Body}",
            shopId, url, requestBody);

        var apiClient = new TikTokApiClient(_httpClientFactory, _logger);
        var rawJson = await apiClient.PostJsonAsync(url, tenant.AccessToken, requestBody, "[OrderList]");
        LogTikTokOrderPayload("[OrderList]", "POST /order/202309/orders/search", rawJson);
        return rawJson;
    }

    // ════════════════════════════════════════════════════════════
    // SearchCancellationByOrderIdAsync — ดึงรายละเอียดการยกเลิก
    // ════════════════════════════════════════════════════════════
    public async Task<string?> SearchCancellationByOrderIdAsync(string shopId, string orderId)
    {
        var tenant = await ResolveTenantAsync(shopId, "[CancellationSearch]");
        if (tenant == null) return null;

        var cfg = TikTokAppConfig.FromConfig(_config);
        const string endpoint = "/return_refund/202602/cancellations/search";

        var queryParams  = TikTokRequestBuilder.CreateShopParams(cfg.AppKey, tenant.ShopCipher);
        string body      = JsonSerializer.Serialize(new { order_ids = new[] { orderId }, page_size = 10 });
        string url       = TikTokRequestBuilder.BuildSignedPostUrl(cfg.BaseUrl, endpoint, cfg.AppSecret, queryParams, body);

        var apiClient = new TikTokApiClient(_httpClientFactory, _logger);
        var rawJson = await apiClient.PostJsonAsync(url, tenant.AccessToken, body, "[CancellationSearch]");
        LogTikTokOrderPayload("[CancellationSearch]", "POST /return_refund/202602/cancellations/search", rawJson);
        return rawJson;
    }

    // ════════════════════════════════════════════════════════════
    // SearchReturnByOrderIdAsync — ดึงรายละเอียด Return/Refund
    // ════════════════════════════════════════════════════════════
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
        var tenant = await ResolveTenantAsync(shopId, "[ReturnSearch]");
        if (tenant == null) return null;

        var cfg = TikTokAppConfig.FromConfig(_config);
        const string endpoint = "/return_refund/202602/returns/search";

        var queryParams = TikTokRequestBuilder.CreateShopParams(cfg.AppKey, tenant.ShopCipher);
        string body     = BuildReturnSearchBody(orderId, returnStatus, returnType, createFrom, createTo, pageToken, pageSize);
        string url      = TikTokRequestBuilder.BuildSignedPostUrl(cfg.BaseUrl, endpoint, cfg.AppSecret, queryParams, body);

        var apiClient = new TikTokApiClient(_httpClientFactory, _logger);
        var rawJson = await apiClient.PostJsonAsync(url, tenant.AccessToken, body, "[ReturnSearch]");
        LogTikTokOrderPayload("[ReturnSearch]", "POST /return_refund/202602/returns/search", rawJson);
        return rawJson;
    }

    // ════════════════════════════════════════════════════════════
    // ProcessCancellationWebhookAsync — ประมวลผล Cancellation Event
    // ════════════════════════════════════════════════════════════
    public async Task ProcessCancellationWebhookAsync(
        string shopId,
        string orderId,
        string cancelStatus,
        long   webhookTimestamp,
        string rawWebhookJson)
    {
        _logger.LogWarning(
            "[CancellationService] เริ่มประมวลผล | ShopId={ShopId} | OrderId={OrderId} | Status={Status}",
            shopId, orderId, cancelStatus);
        LogTikTokWebhookPayload("[CancellationService]", shopId, orderId, cancelStatus, webhookTimestamp, rawWebhookJson);

        await FetchAndPrintOrderDetailAsync(shopId, orderId);

        var cancellationRawJson = await SearchCancellationByOrderIdAsync(shopId, orderId);

        if (string.IsNullOrWhiteSpace(cancellationRawJson))
            _logger.LogWarning("[CancellationService] ไม่พบรายละเอียด | OrderId={OrderId}", orderId);
        else
            _logger.LogWarning("[CancellationService] Cancellation JSON: {Json}", cancellationRawJson);

        if (cancelStatus is "CANCELLATION_REQUEST_SUCCESS" or "CANCELLATION_REQUEST_COMPLETE")
        {
            _logger.LogWarning(
                "[CancellationService] Cancellation สำเร็จ | OrderId={OrderId} → เตรียม REVERSE แต้ม",
                orderId);

            // TODO: Parse JSON → เก็บ DB → ตรวจสอบแต้มเดิม → สร้าง PointTransaction REVERSE
        }
    }

    // ════════════════════════════════════════════════════════════
    // ProcessReturnWebhookAsync — ประมวลผล Return/Refund Event
    // ════════════════════════════════════════════════════════════
    public async Task ProcessReturnWebhookAsync(
        string shopId,
        string orderId,
        string returnStatus,
        long   webhookTimestamp,
        string rawWebhookJson)
    {
        _logger.LogWarning(
            "[ReturnService] เริ่มประมวลผล | ShopId={ShopId} | OrderId={OrderId} | Status={Status}",
            shopId, orderId, returnStatus);
        LogTikTokWebhookPayload("[ReturnService]", shopId, orderId, returnStatus, webhookTimestamp, rawWebhookJson);

        await FetchAndPrintOrderDetailAsync(shopId, orderId);

        var returnRawJson = await SearchReturnByOrderIdAsync(shopId, orderId);

        if (string.IsNullOrWhiteSpace(returnRawJson))
            _logger.LogWarning("[ReturnService] ไม่พบรายละเอียด | OrderId={OrderId}", orderId);
        else
            _logger.LogWarning("[ReturnService] Return JSON: {Json}", returnRawJson);

        if (TikTokHelper.IsPossibleFinalRefundStatus(returnStatus))
            _logger.LogWarning("[ReturnService] Return สำเร็จ | OrderId={OrderId} → เตรียม REVERSE แต้ม", orderId);
    }

    // ════════════════════════════════════════════════════════════
    // Private: Token Validation
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// หา Tenant จาก ShopId, ตรวจสอบ Refresh Token, และ Auto-refresh AccessToken ถ้าใกล้หมด
    /// Returns null ถ้าไม่พบ Tenant (พร้อม log แล้ว)
    /// </summary>
    private async Task<ShopTenant?> ResolveTenantAsync(string shopId, string logTag)
    {
        var tenant = _tenantStore.FindByShopId(shopId);
        if (tenant == null)
        {
            _logger.LogError("{Tag} ShopId not found: {ShopId}", logTag, shopId);
            return null;
        }

        await EnsureValidAccessTokenAsync(tenant);
        return tenant;
    }

    /// <summary>
    /// ตรวจสอบและ Refresh AccessToken ถ้าใกล้หมดอายุ (ล่วงหน้า 30 นาที)
    /// Throws ถ้า RefreshToken หมดอายุแล้ว
    /// </summary>
    private async Task EnsureValidAccessTokenAsync(ShopTenant tenant)
    {
        if (tenant.IsRefreshTokenExpired)
        {
            tenant.Status = "REAUTHORIZE_REQUIRED";
            throw new InvalidOperationException(
                $"RefreshToken ของร้าน {tenant.TenantCode} หมดอายุแล้ว — ต้องให้ร้านเชื่อมต่อ TikTok ใหม่");
        }

        // Token ยังเหลือเกิน 30 นาที — ใช้ได้ปกติ
        if (!tenant.ShouldRefreshAccessToken) return;

        _logger.LogWarning(
            "[Token] AccessToken ใกล้หมดอายุ | Tenant={TenantCode} | ExpireAt={ExpireAt:u} | RemainingMinutes={Min}",
            tenant.TenantCode, tenant.AccessTokenExpireAt, tenant.AccessTokenRemainingMinutes);

        await _authService.RefreshAccessTokenAsync(tenant.TenantCode);

        _logger.LogInformation(
            "[Token] Refresh สำเร็จ | Tenant={TenantCode} | NewExpireAt={ExpireAt:u}",
            tenant.TenantCode, tenant.AccessTokenExpireAt);
    }

    // ════════════════════════════════════════════════════════════
    // Private: Body Builders
    // ════════════════════════════════════════════════════════════

    /// <summary>สร้าง JSON body สำหรับ Search Orders (POST /order/202309/orders/search)</summary>
    private Dictionary<string, object> BuildOrderSearchBody(SearchOrderListRequestModel request)
    {
        var tz   = GetInputTimeZone();
        var body = new Dictionary<string, object>
        {
            { "sort_field", string.IsNullOrWhiteSpace(request.SortField) ? "create_time" : request.SortField },
            { "sort_order", string.IsNullOrWhiteSpace(request.SortOrder) ? "DESC"        : request.SortOrder }
        };

        if (request.CreateTimeFrom.HasValue) body["create_time_ge"] = ToUnixSeconds(request.CreateTimeFrom.Value, tz);
        if (request.CreateTimeTo.HasValue)   body["create_time_lt"] = ToUnixSeconds(request.CreateTimeTo.Value,   tz);
        if (request.UpdateTimeFrom.HasValue) body["update_time_ge"] = ToUnixSeconds(request.UpdateTimeFrom.Value, tz);
        if (request.UpdateTimeTo.HasValue)   body["update_time_lt"] = ToUnixSeconds(request.UpdateTimeTo.Value,   tz);

        // TikTok API 202309 ต้องมี time range เสมอ — fallback ย้อนหลัง 1 ปี
        bool hasTimeFilter =
            request.CreateTimeFrom.HasValue || request.CreateTimeTo.HasValue ||
            request.UpdateTimeFrom.HasValue || request.UpdateTimeTo.HasValue;

        if (!hasTimeFilter)
        {
            var now  = DateTimeOffset.UtcNow;
            var from = now.AddDays(-365);
            body["create_time_ge"] = from.ToUnixTimeSeconds();
            body["create_time_lt"] = now.ToUnixTimeSeconds();

            _logger.LogInformation(
                "[OrderList] No time filter → fallback create_time 1 year | from={From} to={To}",
                from.ToString("yyyy-MM-dd HH:mm:ss"),
                now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (!string.IsNullOrWhiteSpace(request.OrderStatus))  body["order_status"]   = request.OrderStatus;
        if (!string.IsNullOrWhiteSpace(request.BuyerUserId))  body["buyer_user_id"]  = request.BuyerUserId;
        if (!string.IsNullOrWhiteSpace(request.ShippingType)) body["shipping_type"]  = request.ShippingType;

        return body;
    }


    /// <summary>สร้าง JSON body สำหรับ Search Returns</summary>
    private static string BuildReturnSearchBody(
        string        orderId,
        List<string>? returnStatus,
        List<string>? returnType,
        DateTime?     createFrom,
        DateTime?     createTo,
        string?       pageToken,
        int           pageSize)
    {
        var body = new Dictionary<string, object>
        {
            { "order_ids", new[] { orderId } },
            { "page_size", pageSize }
        };

        if (returnStatus is { Count: > 0 }) body["return_status"] = returnStatus;
        if (returnType   is { Count: > 0 }) body["return_type"]   = returnType;

        if (createFrom.HasValue)
            body["create_time_ge"] = new DateTimeOffset(createFrom.Value, TimeSpan.FromHours(7)).ToUnixTimeSeconds();

        if (createTo.HasValue)
            body["create_time_le"] = new DateTimeOffset(createTo.Value, TimeSpan.FromHours(7)).ToUnixTimeSeconds();

        if (!string.IsNullOrWhiteSpace(pageToken))
            body["page_token"] = pageToken;

        return JsonSerializer.Serialize(body);
    }

    // ════════════════════════════════════════════════════════════
    // Private: Timezone Helpers
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// โหลด TimeZoneInfo จาก Config "TikTok:InputTimeZone"
    /// Fallback ตามลำดับ: Asia/Bangkok → SE Asia Standard Time → UTC
    /// </summary>
    private TimeZoneInfo GetInputTimeZone()
    {
        var tzId = _config["TikTok:InputTimeZone"];
        if (string.IsNullOrWhiteSpace(tzId)) tzId = "Asia/Bangkok";

        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { /* Linux/Mac IANA → Windows fallback */ }

        try { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        catch { return TimeZoneInfo.Utc; }
    }

    /// <summary>แปลง DateTime (อาจเป็น Unspecified) → Unix Seconds โดยใช้ inputTimeZone</summary>
    private static long ToUnixSeconds(DateTime value, TimeZoneInfo inputTimeZone)
    {
        if (value.Kind == DateTimeKind.Utc || value.Kind == DateTimeKind.Local)
            return new DateTimeOffset(value).ToUnixTimeSeconds();

        // Unspecified → ถือว่าเป็นเวลาตาม inputTimeZone (เช่น Asia/Bangkok)
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), inputTimeZone);
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    private void LogTikTokWebhookPayload(
        string logTag,
        string shopId,
        string orderId,
        string status,
        long webhookTimestamp,
        string rawWebhookJson)
    {
        _logger.LogInformation(
            """
            {Tag} TikTok Webhook Summary
            --------------------------------------------------
            ShopId    : {ShopId}
            OrderId   : {OrderId}
            UserId    : {UserId}
            Status    : {Status}
            Total     : {Total}
            Timestamp : {Timestamp}
            --------------------------------------------------
            """,
            logTag,
            shopId,
            orderId,
            ExtractFirstValue(rawWebhookJson, "user_id", "buyer_user_id", "buyer_uid") ?? "-",
            status,
            ExtractFirstTotal(rawWebhookJson) ?? "-",
            webhookTimestamp);

        LogTikTokOrderPayload(logTag, "Webhook payload", rawWebhookJson);
    }

    private void LogTikTokOrderPayload(string logTag, string source, string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            _logger.LogWarning("{Tag} TikTok returned empty payload | Source={Source}", logTag, source);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            var summaries = new List<OrderLogSummary>();
            CollectOrderSummaries(root, summaries);

            _logger.LogInformation(
                """
                {Tag} TikTok Response Summary
                --------------------------------------------------
                Source    : {Source}
                Code      : {Code}
                Message   : {Message}
                RequestId : {RequestId}
                Orders    : {OrderCount}
                --------------------------------------------------
                """,
                logTag,
                source,
                GetString(root, "code") ?? "-",
                GetString(root, "message") ?? "-",
                GetString(root, "request_id") ?? "-",
                summaries.Count);

            if (summaries.Count == 0)
            {
                _logger.LogInformation("{Tag} No order summary fields found in TikTok payload.", logTag);
            }
            else
            {
                for (var i = 0; i < summaries.Count; i++)
                {
                    var summary = summaries[i];
                    _logger.LogInformation(
                        """
                        {Tag} Important Order #{Index}
                          OrderId : {OrderId}
                          UserId  : {UserId}
                          Status  : {Status}
                          Total   : {Total}
                        """,
                        logTag,
                        i + 1,
                        summary.OrderId ?? "-",
                        summary.UserId ?? "-",
                        summary.Status ?? "-",
                        summary.Total ?? "-");
                }
            }

            _logger.LogInformation(
                "{Tag} Pretty TikTok JSON ({Source}):{NewLine}{Json}",
                logTag,
                source,
                Environment.NewLine,
                JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "{Tag} Cannot parse TikTok payload for pretty logging | Source={Source}", logTag, source);
            _logger.LogInformation("{Tag} Raw TikTok payload ({Source}): {Json}", logTag, source, rawJson);
        }
    }

    private static void CollectOrderSummaries(JsonElement element, List<OrderLogSummary> summaries)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                AddOrderSummaryIfPresent(element, summaries);

                foreach (var property in element.EnumerateObject())
                    CollectOrderSummaries(property.Value, summaries);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectOrderSummaries(item, summaries);
                break;
        }
    }

    private static void AddOrderSummaryIfPresent(JsonElement element, List<OrderLogSummary> summaries)
    {
        var orderId = GetString(element, "order_id")
                      ?? GetString(element, "orderId")
                      ?? GetString(element, "id");

        var status = GetString(element, "status")
                     ?? GetString(element, "order_status")
                     ?? GetString(element, "cancel_status")
                     ?? GetString(element, "return_status");

        var total = ExtractTotal(element);

        if (orderId == null && status == null) return;

        summaries.Add(new OrderLogSummary(
            orderId,
            GetString(element, "user_id")
                ?? GetString(element, "buyer_user_id")
                ?? GetString(element, "buyer_uid")
                ?? GetString(element, "buyer_id"),
            status,
            total));
    }

    private static string? ExtractFirstValue(string rawJson, params string[] names)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return FindFirstString(document.RootElement, names);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractFirstTotal(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return FindFirstTotal(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindFirstTotal(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var total = ExtractTotal(element);
            if (total != null) return total;

            foreach (var property in element.EnumerateObject())
            {
                total = FindFirstTotal(property.Value);
                if (total != null) return total;
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var total = FindFirstTotal(item);
                if (total != null) return total;
            }
        }

        return null;
    }

    private static string? ExtractTotal(JsonElement element)
    {
        return GetMoney(element, "payment")
               ?? GetMoney(element, "payment_info")
               ?? GetMoney(element, "refund_amount")
               ?? GetMoney(element, "total_amount")
               ?? GetString(element, "total_amount")
               ?? GetString(element, "total")
               ?? GetString(element, "buyer_paid_total");
    }

    private static string? GetMoney(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;

        if (value.ValueKind == JsonValueKind.Object)
        {
            var amount = GetString(value, "total_amount")
                         ?? GetString(value, "amount")
                         ?? GetString(value, "value");
            var currency = GetString(value, "currency");

            if (amount == null) return null;
            return currency == null ? amount : $"{amount} {currency}";
        }

        return JsonElementToString(value);
    }

    private static string? FindFirstString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                var value = GetString(element, name);
                if (value != null) return value;
            }

            foreach (var property in element.EnumerateObject())
            {
                var value = FindFirstString(property.Value, names);
                if (value != null) return value;
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = FindFirstString(item, names);
                if (value != null) return value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return JsonElementToString(value);
    }

    private static string? JsonElementToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var longValue)
                ? longValue.ToString(CultureInfo.InvariantCulture)
                : value.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private sealed record OrderLogSummary(
        string? OrderId,
        string? UserId,
        string? Status,
        string? Total);
}
