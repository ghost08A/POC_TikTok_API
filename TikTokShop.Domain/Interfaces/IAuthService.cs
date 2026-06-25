using TikTokShop.Domain.Models;

namespace TikTokShop.Domain.Interfaces;

// ================================================================
// IAuthService.cs — Contract สำหรับ OAuth 2.0 Authentication
//
// รับผิดชอบ:
//   1. แลก Authorization Code → Access Token (OAuth Code Flow)
//
// หมายเหตุ:
//   - Auth Code มีอายุสั้นมาก ต้องแลกทันทีที่รับจาก Callback
//   - ใช้ https://auth.tiktok-shops.com (คนละ Base URL กับ Open API)
// ================================================================
public interface IAuthService
{
    Task<TikTokTokenData> ExchangeCodeForTokenAsync(string authCode);
}
