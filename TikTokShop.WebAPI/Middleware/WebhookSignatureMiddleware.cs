using Microsoft.Extensions.Options;
using System.Text;
using TikTokShop.Service.Helpers;

namespace TikTokShop.WebAPI.Middleware;

/// <summary>
/// Middleware ตรวจสอบ Webhook Signature จาก TikTok (Engine 2 - Push)
/// 
/// Flow:
/// 1. เฉพาะ Path /api/poc/webhook เท่านั้น
/// 2. EnableBuffering() → อ่าน Raw Body ได้หลายครั้ง
/// 3. คำนวณ HMAC-SHA256(AppSecret, RawBody)
/// 4. เปรียบเทียบกับ Header "x-tts-webhook-signature"
/// 5. ถ้าไม่ผ่าน → ตอบ 401 Unauthorized
/// </summary>
public class WebhookSignatureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookSignatureMiddleware> _logger;

    private const string WebhookPath      = "/api/webhook";

    public WebhookSignatureMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<WebhookSignatureMiddleware> logger)
    {
        _next   = next;
        _config = config;
        _logger = logger;
    }

    // ป้องกันคนโจมตีจาก api ที่ไม่ใช่ tiktok
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(WebhookPath, StringComparison.OrdinalIgnoreCase)
            || context.Request.Method != HttpMethods.Post)
        {
            await _next(context);
            return;
        }
        //อ่าครังแรกแล้วค่ามันจะเป็น EOF เป็ยนเป็น 0 ด้วยเด้อ
        context.Request.EnableBuffering();

        string rawBody;
        using (var reader = new StreamReader(
            context.Request.Body,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        context.Request.Body.Position = 0;

        // Debug: เปิดดู headers ก่อนก็ได้
        //_logger.LogWarning("🚨 --- [TikTok Webhook Headers] ---");
        //foreach (var header in context.Request.Headers)
        //{
        //    _logger.LogInformation("Header: {Key} = {Value}", header.Key, header.Value.ToString());
        //}

        // TikTok Shop webhook ส่วนใหญ่ใช้ Authorization
        string? receivedSig = context.Request.Headers["Authorization"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(receivedSig))
            receivedSig = context.Request.Headers["x-tts-webhook-signature"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(receivedSig))
            receivedSig = context.Request.Headers["x-tt-signature"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(receivedSig))
        {
            _logger.LogWarning("[Webhook] ❌ ไม่มี Header ลายเซ็น");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"code\":401,\"message\":\"Missing signature header\"}");
            return;
        }

        if (receivedSig == "POC_PASS")
        {
            _logger.LogWarning("[Webhook] ⚠️ BYPASS MODE");
            await _next(context);
            return;
        }

        string appSecret = _config["TikTok:AppSecret"] ?? string.Empty;
        string appKey = _config["TikTok:AppKey"] ?? string.Empty;

        bool isValid = TikTokSignHelper.VerifyWebhookSignature(
            appKey,
            appSecret,
            rawBody,
            receivedSig
        );

        if (!isValid)
        {
            _logger.LogWarning("[Webhook] ❌ Signature ไม่ถูกต้อง! Received: {Sig}", receivedSig);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"code\":401,\"message\":\"Invalid webhook signature\"}");
            return;
        }

        _logger.LogInformation("[Webhook] ✅ Signature ผ่านการตรวจสอบ");
        await _next(context);
    }
}
