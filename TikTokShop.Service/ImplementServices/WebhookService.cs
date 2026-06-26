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

        _logger.LogWarning("[Webhook] Raw Body: {RawBody}", rawBody);

        if (payload == null)
        {
            _logger.LogWarning("[Webhook] ⚠️ Parse Payload ได้ null — ไม่ดำเนินการต่อ");
            return Task.CompletedTask;
        }

        _logger.LogInformation("[Webhook] Event Type={Type} | ShopId={ShopId} | Timestamp={Ts}",
            payload.Type, payload.ShopId, payload.Timestamp);

        // ── Step 2: Route ตาม Event Type ─────────────────────────
        switch (payload.Type)
        {
            // Type 1: Order Status Changed — ออเดอร์มีการเปลี่ยนสถานะ
            case 1:
                HandleOrderStatusChanged(payload);
                break;

            case 11:
                await HandleCancellationStatusChangedAsync(payload);
                break;
            default:
                _logger.LogInformation("[Webhook] ℹ️ Event Type={Type} ยังไม่มี Handler", payload.Type);
                break;
        }

        // ตอบ TikTok ทันที — ไม่รอ Background Task
        return Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════
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

        // ════════════════════════════════════════════════════════
        // 🚀 THE MAGIC: Fire-and-Forget Pattern
        //
        // สั่งลูกน้องไปหิ้วข้อมูลหลังบ้าน "แบบไม่รอ (Task.Run)"
        // เพื่อให้ Controller รีบตอบ {code: 0} กลับหา TikTok
        // ภายใน 0.05 วินาที ไม่เกิด Timeout และ Retry Storm!
        //
        // ⚠️ Production Note:
        //   ควรใช้ IHostedService / BackgroundService หรือ
        //   Message Queue (RabbitMQ/Azure Service Bus) แทน Task.Run
        //   เพื่อให้รับประกันได้ว่า Job จะรันสำเร็จแม้ Process Restart
        // ════════════════════════════════════════════════════════
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
}
