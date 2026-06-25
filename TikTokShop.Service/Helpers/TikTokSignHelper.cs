using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
//   4. ห่อ Sandwich: {AppSecret}{ConcatString}{AppSecret}
//   5. HMAC-SHA256(key=AppSecret, message=SandwichString)
//   6. แปลงเป็น Hex Lowercase
// ================================================================
public static class TikTokSignHelper
{


    public static string GenerateSign(string appSecret,string endpointPath, Dictionary<string, string> queryParams)
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
        string stringToSign = $"{appSecret}{endpointPath}{concat}{appSecret}";

        // Step 4: HMAC-SHA256 โดยใช้ AppSecret เป็น Key
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));

        // Step 5: แปลงเป็น Hex Lowercase
        return Convert.ToHexString(hash).ToLowerInvariant();
    }


    public static bool VerifyWebhookSignature(
        string appKey,
        string appSecret,
        string rawBody,
        string receivedSignature)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            throw new ArgumentException("Missing appKey");

        if (string.IsNullOrWhiteSpace(appSecret))
            throw new ArgumentException("Missing appSecret");

        if (string.IsNullOrWhiteSpace(receivedSignature))
            return false;

        var target = receivedSignature.Trim().ToLowerInvariant();

        // TikTok Shop Webhook:
        // stringToSign = appKey + rawBody
        // sign = HMAC_SHA256(appSecret, stringToSign)
        var stringToSign = appKey + rawBody;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var calculated = Convert.ToHexString(hash).ToLowerInvariant();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n🔐 --- [TikTok Shop Webhook Signature Check] ---");
        Console.WriteLine($"Target     : {target}");
        Console.WriteLine($"Calculated : {calculated}");
        Console.WriteLine(calculated == target ? "✅ MATCH" : "❌ NOT MATCH");
        Console.WriteLine("-----------------------------------------------\n");
        Console.ResetColor();

        return FixedTimeEquals(calculated, target);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        if (aBytes.Length != bBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
