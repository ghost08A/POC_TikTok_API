using Microsoft.Extensions.Configuration;

namespace TikTokShop.Service.Config;

// ================================================================
// TikTokAppConfig.cs — Typed Configuration Record
//
// รวมค่า Config ที่ทุก Service ต้องใช้ไว้ที่เดียว
// แทนการ _config["TikTok:AppKey"] ซ้ำในทุกไฟล์
//
// Usage:
//   var cfg = TikTokAppConfig.FromConfig(_config);
//   cfg.AppKey / cfg.AppSecret / cfg.BaseUrl
// ================================================================

/// <summary>Typed config สำหรับ TikTok App Credentials และ Endpoint</summary>
public sealed record TikTokAppConfig(
    string AppKey,
    string AppSecret,
    string BaseUrl,
    string InputTimeZone)
{
    /// <summary>
    /// โหลดค่าจาก IConfiguration Section "TikTok"
    /// Throws <see cref="InvalidOperationException"/> ถ้า AppKey หรือ AppSecret ว่าง
    /// </summary>
    public static TikTokAppConfig FromConfig(IConfiguration config)
    {
        string appKey    = config["TikTok:AppKey"]    ?? throw new InvalidOperationException("Missing config: TikTok:AppKey");
        string appSecret = config["TikTok:AppSecret"] ?? throw new InvalidOperationException("Missing config: TikTok:AppSecret");
        string baseUrl   = config["TikTok:BaseUrl"]   ?? "https://open-api.tiktokglobalshop.com";
        string tz        = config["TikTok:InputTimeZone"] ?? "Asia/Bangkok";

        return new TikTokAppConfig(appKey, appSecret, baseUrl, tz);
    }
}
