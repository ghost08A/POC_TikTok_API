using Microsoft.Extensions.Logging;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.RequestModels;

namespace TikTokShop.Service.ImplementServices;

// ================================================================
// WebhookService.cs — Webhook Processing Service (Push Engine)
//
// รับผิดชอบ:
//   1. Parse Webhook Payload จาก TikTok
//   2. Route ไปยัง Handler ตาม Event Type
//   3. Fire-and-Forget Background Task เพื่อไม่ให้บล็อก Response
//
// หมายเหตุสำคัญ:
//   - Signature ถูกตรวจสอบแล้วโดย WebhookSignatureMiddleware
//   - ต้องตอบกลับ TikTok ภายใน 0.05 วินาที (ห้ามรอ Background Job)
//   - ถ้า Response ช้า TikTok จะส่ง Webhook ซ้ำ (Retry Logic)
//
// TikTok Webhook Event Types:
//   Type 1 — Order Status Changed
//   (เพิ่ม Type อื่นๆ ตามต้องการใน switch-case ด้านล่าง)
// ================================================================
public class WebhookService : IWebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly IOrderService           _orderService;

    public WebhookService(
        ILogger<WebhookService> logger,
        IOrderService           orderService)
    {
        _logger       = logger;
        _orderService = orderService;
    }

    // ════════════════════════════════════════════════════════════
    // ProcessWebhookAsync — จุดรับ Webhook Event
    // ════════════════════════════════════════════════════════════
    /// <inheritdoc />
    public Task ProcessWebhookAsync(string rawBody)
    {
        // ── Step 1: Parse Webhook Envelope (ซองนอก) ──────────────
        var payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody);

        if (payload == null)
        {
            LogIncomingWebhookPayload(null, rawBody);
            _logger.LogWarning("[Webhook] ⚠️ Parse Payload ได้ null — ไม่ดำเนินการต่อ");
            return Task.CompletedTask;
        }

        LogIncomingWebhookPayload(payload, rawBody);

        _logger.LogInformation("[Webhook] Event Type={Type} | ShopId={ShopId} | Timestamp={Ts}",
            payload.Type, payload.ShopId, payload.Timestamp);

        // ── Step 2: Route ตาม Event Type ─────────────────────────
        switch (payload.Type)
        {
            // Type 1: Order Status Changed — ออเดอร์มีการเปลี่ยนสถานะ
            case 1:
                HandleOrderStatusChanged(payload);
                break;
            case 2:
                HandleReverseStatusChanged(payload, rawBody);
                break;

            case 11:
                HandleCancellationStatusChanged(payload, rawBody);
                break;
            case 12:
                HandleReturnStatusChanged(payload, rawBody);
                break;
            default:
                _logger.LogInformation("[Webhook] ℹ️ Event Type={Type} ยังไม่มี Handler", payload.Type);
                break;
        }

        // ตอบ TikTok ทันที — ไม่รอ Background Task
        return Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════
    private void LogIncomingWebhookPayload(WebhookPayload? payload, string rawBody)
    {
        var type = payload?.Type;
        var typeName = type.HasValue ? GetWebhookTypeName(type.Value) : "Invalid Payload";

        _logger.LogInformation(
            """
            [Webhook] Incoming TikTok Payload
            --------------------------------------------------
            Type      : {Type}
            TypeName  : {TypeName}
            ShopId    : {ShopId}
            Timestamp : {Timestamp}
            Size      : {Size} bytes
            --------------------------------------------------
            """,
            type?.ToString() ?? "-",
            typeName,
            payload?.ShopId ?? "-",
            payload?.Timestamp.ToString() ?? "-",
            rawBody.Length);

        _logger.LogInformation(
            "[Webhook] Payload from {TypeName}:{NewLine}{Payload}",
            typeName,
            Environment.NewLine,
            TryFormatJson(rawBody));
    }

    private static string GetWebhookTypeName(int type)
    {
        return type switch
        {
            1 => "Order Status Changed",
            2 => "Reverse / After-Sales Status Changed",
            11 => "Cancellation Status Changed",
            12 => "Return / Refund Status Changed",
            _ => $"Unknown Type {type}"
        };
    }

    private static string TryFormatJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "(empty)";

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }

    // Private: HandleOrderStatusChanged — Handler สำหรับ Type 1
    // ════════════════════════════════════════════════════════════
    private void HandleOrderStatusChanged(WebhookPayload payload)
    {
        // Parse ไส้ในของ Event (WebhookOrderData)
        var orderInfo = payload.Data.Deserialize<WebhookOrderData>();

        if (orderInfo == null)
        {
            _logger.LogWarning("[Webhook] ⚠️ ไม่สามารถ Parse OrderData จาก Webhook");
            return;
        }

        string orderId = orderInfo.OrderId;
        string shopId  = payload.ShopId;

        _logger.LogInformation("[Webhook] 📦 Order Status Changed | OrderId={OrderId} | Status={Status}",
            orderId, orderInfo.OrderStatus);

        _ = Task.Run(async () =>
        {
            try
            {
                // ส่ง ShopId (ที่แกะได้จากซองนอก) กับ OrderId ให้ OrderService ไปดึงข้อมูล
                await _orderService.FetchAndPrintOrderDetailAsync(shopId, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Webhook] 💥 Background Task ดึงออเดอร์ {OrderId} ล้มเหลว", orderId);
            }
        });
    }

    private void HandleCancellationStatusChanged(WebhookPayload payload, string rawBody)
    {
        var data = payload.Data.Deserialize<WebhookCancellationData>();

        if (data == null)
        {
            _logger.LogWarning("[Webhook:Cancel] Data เป็น null");
            return;
        }

        _logger.LogWarning(
            "[Webhook:Cancel] ShopId={ShopId}, OrderId={OrderId}, CancelStatus={CancelStatus}",
            payload.ShopId,
            data.OrderId,
            data.CancelStatus);

        _ = Task.Run(async () =>
        {
            try
            {
                await _orderService.ProcessCancellationWebhookAsync(
                    payload.ShopId,
                    data.OrderId,
                    data.CancelStatus,
                    payload.Timestamp,
                    rawBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Webhook:Cancel] 💥 ประมวลผล cancellation ล้มเหลว OrderId={OrderId}",
                    data.OrderId);
            }
        });
    }

    private void HandleReturnStatusChanged(WebhookPayload payload, string rawBody)
    {
        var data = payload.Data.Deserialize<WebhookReturnData>();

        if (data == null)
        {
            _logger.LogWarning("[Webhook:Return] Data เป็น null");
            return;
        }

        _logger.LogWarning(
            "[Webhook:Return] ShopId={ShopId}, OrderId={OrderId}, ReturnId={ReturnId}, ReturnStatus={ReturnStatus}",
            payload.ShopId,
            data.OrderId,
            data.ReturnId,
            data.ReturnStatus);

        _ = Task.Run(async () =>
        {
            try
            {
                await _orderService.ProcessReturnWebhookAsync(
                    payload.ShopId,
                    data.OrderId,
                    data.ReturnStatus,
                    payload.Timestamp,
                    rawBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Webhook:Return] 💥 ประมวลผล return/refund ล้มเหลว OrderId={OrderId}",
                    data.OrderId);
            }
        });
    }

    private void HandleReverseStatusChanged(WebhookPayload payload, string rawBody)
    {
        var data = payload.Data.Deserialize<WebhookReverseOrderData>();

        if (data == null)
        {
            _logger.LogWarning("[Webhook:Reverse] Data เป็น null");
            return;
        }

        if (string.IsNullOrWhiteSpace(data.OrderId))
        {
            _logger.LogWarning("[Webhook:Reverse] ไม่มี OrderId ใน payload | RawBody={RawBody}", rawBody);
            return;
        }

        _logger.LogWarning(
            "[Webhook:Reverse] ShopId={ShopId}, OrderId={OrderId}, ReverseOrderId={ReverseOrderId}, ReverseEventType={ReverseEventType}, ReverseType={ReverseType}, ReverseStatus={ReverseStatus}, ReverseUser={ReverseUser}",
            payload.ShopId,
            data.OrderId,
            data.ReverseOrderId,
            data.ReverseEventType,
            data.ReverseType,
            data.ReverseOrderStatus,
            data.ReverseUser);

        _ = Task.Run(async () =>
        {
            try
            {
                // 1. ดู order หลักก่อน เช่น COMPLETED / CANCELLED
                await _orderService.FetchAndPrintOrderDetailAsync(
                    payload.ShopId,
                    data.OrderId);

                // 2. type 2 เป็น reverse/after-sales trigger
                //    ยังไม่รู้แน่ชัดว่าเป็น cancel หรือ refund
                //    ดังนั้นให้ค้น returns/refunds ก่อน เพราะเคสล่าสุดของคุณ refund complete แต่ออเดอร์ยัง COMPLETED
                var returnRawJson = await _orderService.SearchReturnByOrderIdAsync(
                    payload.ShopId,
                    data.OrderId);

                if (string.IsNullOrWhiteSpace(returnRawJson))
                {
                    _logger.LogInformation(
                        "[Webhook:Reverse] ไม่พบ return/refund จาก Search Returns | OrderId={OrderId}",
                        data.OrderId);
                }
                else
                {
                    _logger.LogWarning(
                        "[Webhook:Reverse] พบข้อมูล return/refund จาก Search Returns | OrderId={OrderId}",
                        data.OrderId);
                }

                // 3. ค้น cancellation เผื่อเป็นเคสยกเลิกก่อน order จบ
                var cancellationRawJson = await _orderService.SearchCancellationByOrderIdAsync(
                    payload.ShopId,
                    data.OrderId);

                if (string.IsNullOrWhiteSpace(cancellationRawJson))
                {
                    _logger.LogInformation(
                        "[Webhook:Reverse] ไม่พบ cancellation จาก Search Cancellations | OrderId={OrderId}",
                        data.OrderId);
                }
                else
                {
                    _logger.LogWarning(
                        "[Webhook:Reverse] พบข้อมูล cancellation จาก Search Cancellations | OrderId={OrderId}",
                        data.OrderId);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Webhook:Reverse] 💥 ประมวลผล reverse status ล้มเหลว | OrderId={OrderId}",
                    data.OrderId);
            }
        });
    }


}
