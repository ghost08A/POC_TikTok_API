using TikTokShop.Domain.Models;

namespace TikTokShop.Domain.Interfaces;

// ================================================================
// IAuthService.cs — Contract สำหรับ OAuth 2.0 Authentication
//
// รับผิดชอบ:
//   1. ExchangeCodeForTokenAsync  — แลก Auth Code → Token (Initial Flow)
//   2. RefreshAccessTokenAsync    — ต่ออายุ AccessToken ด้วย RefreshToken
//
// หมายเหตุ:
//   - Auth Code มีอายุสั้นมาก ต้องแลกทันทีที่รับจาก Callback
//   - ใช้ https://auth.tiktok-shops.com (คนละ Base URL กับ Open API)
// ================================================================
public interface IAuthService
{
    Task<TikTokTokenData> ExchangeCodeForTokenAsync(string authCode);
    Task<TikTokTokenData> RefreshAccessTokenAsync(string tenantCode);
}
