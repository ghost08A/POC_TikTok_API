using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace TikTokShop.Service.Helpers;

// ================================================================
// TikTokSignHelper.cs
// Helper สำหรับสร้างและตรวจสอบ TikTok API Signature (HMAC-SHA256)
// ตาม Official Algorithm ของ TikTok Shop Open API V2
//
// Signature Algorithm:
//   1. คัดแยก Query Params ออก (ห้ามเอา `sign` และ `access_token` มาร่วม)
//   2. เรียงลำดับ Key ตาม ASCII/Ordinal (A-Z)
//   3. Concat Key+Value ติดกัน: "app_key{val}timestamp{val}"
//   4. ห่อ Sandwich: {AppSecret}{EndpointPath}{ConcatString}{Body}{AppSecret}
//   5. HMAC-SHA256(key=AppSecret, message=SandwichString)
//   6. แปลงเป็น Hex Lowercase
// ================================================================
public static class TikTokSignHelper
{
    // ── Generate Sign ─────────────────────────────────────────────

    /// <summary>
    /// สร้าง HMAC-SHA256 Signature สำหรับ TikTok Open API Request
    /// </summary>
    /// <param name="appSecret">App Secret จาก TikTok Developer Portal</param>
    /// <param name="endpointPath">API Path เช่น /order/202507/orders</param>
    /// <param name="queryParams">Query params ที่จะ sign (ไม่รวม sign/access_token)</param>
    /// <param name="requestBody">Request Body (เฉพาะ POST — ว่างได้สำหรับ GET)</param>
    public static string GenerateSign(
        string appSecret,
        string endpointPath,
        Dictionary<string, string> queryParams,
        string requestBody = "")
    {
        // Step 1: กรอง sign และ access_token ออก แล้วเรียง Key ตาม Ordinal (ASCII A-Z)
        var sortedKeys = queryParams.Keys
            .Where(k => k != "sign" && k != "access_token")
            .OrderBy(k => k, StringComparer.Ordinal);

        // Step 2: Concat Key+Value ทุก Entry ติดกันไม่มีตัวคั่น
        var concat = new StringBuilder();
        foreach (var key in sortedKeys)
            concat.Append(key).Append(queryParams[key]);

        // Step 3: ห่อ Sandwich ด้วย AppSecret ทั้งหน้าและหลัง
        string stringToSign = $"{appSecret}{endpointPath}{concat}{requestBody}{appSecret}";

        // Step 4: HMAC-SHA256 โดยใช้ AppSecret เป็น Key
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));

        // Step 5: แปลงเป็น Hex Lowercase
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Verify Webhook Signature ──────────────────────────────────

    /// <summary>
    /// ตรวจสอบ HMAC-SHA256 Signature จาก TikTok Webhook Header
    /// stringToSign = appKey + rawBody
    /// </summary>
    /// <param name="appKey">App Key จาก TikTok Developer Portal</param>
    /// <param name="appSecret">App Secret จาก TikTok Developer Portal</param>
    /// <param name="rawBody">Raw HTTP Body ที่รับมา</param>
    /// <param name="receivedSignature">ค่า Signature จาก Header ที่ต้องตรวจสอบ</param>
    /// <param name="logger">Optional logger สำหรับ debug output</param>
    public static bool VerifyWebhookSignature(
        string   appKey,
        string   appSecret,
        string   rawBody,
        string   receivedSignature,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            throw new ArgumentException("Missing appKey");

        if (string.IsNullOrWhiteSpace(appSecret))
            throw new ArgumentException("Missing appSecret");

        if (string.IsNullOrWhiteSpace(receivedSignature))
            return false;

        var target = receivedSignature.Trim().ToLowerInvariant();

        var stringToSign = appKey + rawBody;
        using var hmac   = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash         = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var calculated   = Convert.ToHexString(hash).ToLowerInvariant();

        bool isMatch = FixedTimeEquals(calculated, target);

        logger?.LogDebug(
            "[WebhookSign] Target={Target} | Calculated={Calculated} | Match={Match}",
            target, calculated, isMatch);

        return isMatch;
    }

    // ── Private ───────────────────────────────────────────────────

    /// <summary>เปรียบเทียบ string แบบ Constant Time (ป้องกัน Timing Attack)</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        if (aBytes.Length != bBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
