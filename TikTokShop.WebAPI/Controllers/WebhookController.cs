using System.Text;
using Microsoft.AspNetCore.Mvc;
using TikTokShop.Domain.Interfaces;

namespace TikTokShop.WebAPI.Controllers;

// ================================================================
// WebhookController.cs — Webhook Push Engine Endpoint
//
// Route Prefix: /api/webhook
//
// Endpoints:
//   POST /api/webhook → รับ Webhook Event จาก TikTok (Push Engine)
//
// Security:
//   ทุก Request ที่มาต้องผ่าน WebhookSignatureMiddleware ก่อน
//   Middleware จะตรวจสอบ HMAC-SHA256 Signature ใน Header
//   ถ้า Signature ไม่ถูกต้อง → Middleware ตอบ 401 ก่อนถึง Controller นี้
//
// TikTok Requirement:
//   ต้องตอบ {"code": 0} กลับภายใน 0.05 วินาที
//   ไม่เช่นนั้น TikTok จะ Retry ส่ง Webhook ซ้ำ
// ================================================================
[ApiController]
[Route("api/webhook")]
[Tags("📡 Webhook")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookService              _webhookService;
    private readonly ILogger<WebhookController>   _logger;

    public WebhookController(IWebhookService webhookService, ILogger<WebhookController> logger)
    {
        _webhookService = webhookService;
        _logger         = logger;
    }

    // ────────────────────────────────────────────────────────────
    // POST /api/webhook
    // Engine 2 (Push) — รับ Webhook Event จาก TikTok
    // ────────────────────────────────────────────────────────────
    /// <summary>
    /// [Push Engine] รับ Webhook Event จาก TikTok
    /// Signature ถูกตรวจสอบโดย WebhookSignatureMiddleware แล้ว
    /// </summary>
    /// <remarks>
    /// การทดสอบใน Swagger: ใส่ Header <c>Authorization: POC_PASS</c> เพื่อ Bypass Signature Check
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReceiveWebhook()
    {
        // Body ถูก Buffer ไว้แล้วโดย WebhookSignatureMiddleware
        // ต้อง Reset Position เพื่ออ่านซ้ำได้
        HttpContext.Request.Body.Position = 0;
        using var reader  = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        string    rawBody = await reader.ReadToEndAsync();

        _logger.LogInformation("📡 [Webhook] ได้รับ Event ({Length} bytes)", rawBody.Length);

        // ส่งให้ WebhookService จัดการ (Fire-and-Forget อยู่ภายใน Service)
        await _webhookService.ProcessWebhookAsync(rawBody);

        // ⚡ ตอบ TikTok ทันที — ห้ามรอ Background Job!
        // TikTok บังคับให้ตอบ code=0 เพื่อยืนยันว่ารับ Event แล้ว
        return Ok(new { code = 0, message = "success" });
    }
}
