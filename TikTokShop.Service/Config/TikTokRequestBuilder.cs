using TikTokShop.Service.Helpers;

namespace TikTokShop.Service.Config;

// ================================================================
// TikTokRequestBuilder.cs — URL + Signature Builder
//
// รวม pattern การสร้าง Signed Request URL ไว้ที่เดียว
// แทนการเขียนซ้ำใน OrderService / ShopService / AuthService
//
// สิ่งที่ทำ:
//   1. รับ base params (app_key, shop_cipher, timestamp)
//   2. เพิ่ม sign ด้วย TikTokSignHelper.GenerateSign
//   3. Build URL สำหรับ GET หรือ POST
// ================================================================

/// <summary>
/// Static helper สำหรับสร้าง Signed URL ตาม TikTok API Spec
/// </summary>
public static class TikTokRequestBuilder
{
    // ── Query String ──────────────────────────────────────────────

    /// <summary>
    /// แปลง Dictionary → Query String (URL-encoded, ไม่มี ? นำหน้า)
    /// </summary>
    public static string BuildQueryString(Dictionary<string, string> queryParams)
        => string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

    // ── Signed URL (GET) ──────────────────────────────────────────

    /// <summary>
    /// สร้าง Full URL พร้อม Signature สำหรับ GET Request
    /// </summary>
    /// <param name="baseUrl">TikTok Base URL (เช่น https://open-api-sandbox.tiktokglobalshop.com)</param>
    /// <param name="endpointPath">Path เช่น /order/202507/orders</param>
    /// <param name="appSecret">App Secret สำหรับ HMAC-SHA256</param>
    /// <param name="queryParams">Query params ที่จะ sign (ไม่รวม sign key)</param>
    public static string BuildSignedGetUrl(
        string baseUrl,
        string endpointPath,
        string appSecret,
        Dictionary<string, string> queryParams)
    {
        queryParams["sign"] = TikTokSignHelper.GenerateSign(appSecret, endpointPath, queryParams);
        return $"{baseUrl}{endpointPath}?{BuildQueryString(queryParams)}";
    }

    // ── Signed URL (POST) ─────────────────────────────────────────

    /// <summary>
    /// สร้าง Full URL พร้อม Signature สำหรับ POST Request (รวม body ใน sign)
    /// </summary>
    /// <param name="baseUrl">TikTok Base URL</param>
    /// <param name="endpointPath">Path เช่น /order/202309/orders/search</param>
    /// <param name="appSecret">App Secret สำหรับ HMAC-SHA256</param>
    /// <param name="queryParams">Query params ที่จะ sign</param>
    /// <param name="requestBody">JSON body ที่จะนำมา sign ด้วย</param>
    public static string BuildSignedPostUrl(
        string baseUrl,
        string endpointPath,
        string appSecret,
        Dictionary<string, string> queryParams,
        string requestBody)
    {
        queryParams["sign"] = TikTokSignHelper.GenerateSign(appSecret, endpointPath, queryParams, requestBody);
        return $"{baseUrl}{endpointPath}?{BuildQueryString(queryParams)}";
    }

    // ── Base Query Params ─────────────────────────────────────────

    /// <summary>
    /// สร้าง base query params ที่ทุก API ต้องใช้ (app_key + timestamp)
    /// </summary>
    public static Dictionary<string, string> CreateBaseParams(string appKey)
        => new()
        {
            { "app_key",   appKey },
            { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
        };

    /// <summary>
    /// สร้าง base query params พร้อม shop_cipher (ใช้กับ API ที่ต้องการ Shop context)
    /// </summary>
    public static Dictionary<string, string> CreateShopParams(string appKey, string shopCipher)
    {
        var p = CreateBaseParams(appKey);
        p["shop_cipher"] = shopCipher;
        return p;
    }
}
