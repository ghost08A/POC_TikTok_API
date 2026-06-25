namespace TikTokShop.Domain.Interfaces;

// ================================================================
// IWebhookService.cs — Contract สำหรับ Webhook Processing (Push Engine)
//
// รับผิดชอบ:
//   1. รับ Raw JSON Body จาก TikTok Webhook
//   2. Parse Event Type แล้วส่งต่อให้ Handler ที่เหมาะสม
//   3. ตอบ {code: 0} กลับหา TikTok ภายใน 0.05 วินาที (Fire-and-Forget)
//
// หมายเหตุ:
//   - Signature ถูกตรวจสอบแล้วโดย WebhookSignatureMiddleware
//     ก่อนที่ Request จะมาถึง Method นี้
// ================================================================
public interface IWebhookService
{
    /// <summary>
    /// [Push Engine] ประมวลผล Webhook Event ที่ TikTok ส่งมา
    /// ต้องตอบกลับเร็วมาก (ไม่รอ background job เสร็จ)
    /// </summary>
    /// <param name="rawBody">Raw JSON body จาก HTTP Request ที่ผ่าน Signature Check แล้ว</param>
    Task ProcessWebhookAsync(string rawBody);
}
