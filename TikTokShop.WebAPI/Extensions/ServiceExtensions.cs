using TikTokShop.Domain.Interfaces;
using TikTokShop.Service.ImplementServices;
using TikTokShop.Service.Stores;

namespace TikTokShop.WebAPI.Extensions;

// ================================================================
// ServiceExtensions.cs — DI Registration Hub
//
// รวม Service Registration ทั้งหมดไว้ที่นี่
// ทำให้ Program.cs สะอาดและอ่านง่าย (Single Responsibility)
//
// Services ที่ Register:
//   - TenantStore        (Singleton — Share ข้อมูลร้านค้าทั้งระบบ)
//   - IAuthService       (Scoped — OAuth Token Exchange)
//   - IOrderService      (Scoped — Pull Orders + Fetch Detail)
//   - IShopService       (Scoped — Token Health Check)
//   - IWebhookService    (Scoped — Push Engine)
//   - HttpClient Named   (TikTokClient, TikTokAuthClient)
// ================================================================

/// <summary>
/// Extension Methods สำหรับลงทะเบียน Services ทั้งหมดใน DI Container
/// รวม Registration ไว้ที่นี่เพื่อให้ Program.cs อ่านง่าย (Clean Architecture)
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// ลงทะเบียน TikTok-related Services, TenantStore และ HttpClients
    /// </summary>
    public static IServiceCollection AddTikTokServices(
        this IServiceCollection services,
        IConfiguration          config)
    {
        // ── 1. Named HttpClient: TikTokClient ─────────────────────
        // ใช้สำหรับ Open API ทั่วไป (Orders, Shops, etc.)
        string baseUrl = config["TikTok:BaseUrl"] ?? "https://open-api-sandbox.tiktokglobalshop.com";
        services.AddHttpClient("TikTokClient", client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout     = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // ── 2. Named HttpClient: TikTokAuthClient ────────────────
        // ใช้สำหรับ OAuth Token Exchange เท่านั้น
        // Base URL ต่างกัน: auth.tiktok-shops.com ≠ open-api.tiktokglobalshop.com
        services.AddHttpClient("TikTokAuthClient", client =>
        {
            client.BaseAddress = new Uri("https://auth.tiktok-shops.com");
            client.Timeout     = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // ── 3. TenantStore (Singleton) ────────────────────────────
        // เป็น Singleton เพื่อให้ทุก Service ใช้ Instance เดียวกัน
        // ใน Production: เปลี่ยนเป็น Repository Pattern + Database
        services.AddSingleton<TenantStore>();

        // ── 4. Application Services (Scoped) ─────────────────────
        // Scoped = ใหม่ต่อ 1 HTTP Request (เหมาะกับ Stateless API)
        services.AddScoped<IAuthService,    AuthService>();
        services.AddScoped<IOrderService,   OrderService>();
        services.AddScoped<IShopService,    ShopService>();
        services.AddScoped<IWebhookService, WebhookService>();

        return services;
    }

    /// <summary>
    /// ตั้งค่า Swagger/OpenAPI พร้อม Documentation ครบถ้วน
    /// </summary>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title       = "TikTok Shop PoC API",
                Version     = "v1",
                Description = """
                    Multi-Tenant TikTok Shop Integration — Dual-Engine Architecture

                    🔐 Authentication: POST /api/auth/callback  — รับ OAuth Code แลก Token
                    📦 Orders (Pull):  GET  /api/orders/{tenantCode}                   — รายการออเดอร์
                    📦 Orders (Manual):GET  /api/orders/detail/{shopId}/{orderId}      — รายละเอียดรายเดี่ยว
                    🏪 Shops:          GET  /api/shops/{tenantCode}                    — Token Health Check
                    📡 Webhook (Push): POST /api/webhook                               — รับ Event จาก TikTok

                    ⚠️ Sandbox Environment — ข้อมูลเป็น Test Data
                    💡 Webhook Testing: ใส่ Header Authorization: POC_PASS เพื่อ Bypass Signature
                    """
            });

            // Header สำหรับ Webhook Testing ผ่าน Swagger UI
            c.AddSecurityDefinition("WebhookSignature", new()
            {
                Name        = "Authorization",
                In          = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Type        = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                Description = "ใส่ 'POC_PASS' เพื่อ Bypass Signature Check (Dev Only)"
            });
        });

        return services;
    }
}
