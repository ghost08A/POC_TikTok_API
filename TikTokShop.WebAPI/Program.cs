using System.Text;
using System.Text.Json;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.RequestModels;
using TikTokShop.WebAPI.Extensions;
using TikTokShop.WebAPI.Middleware;

// ================================================================
// PROGRAM.CS — TikTok Shop PoC (Multi-Tenant, Dual-Engine)
// Architecture: 3-Layer (Domain / Service / WebAPI)
// Engine 1: Pull API  → GET  /api/poc/orders/{tenantCode}
// Engine 2: Push Hook → POST /api/poc/webhook
// ================================================================

Console.OutputEncoding = Encoding.UTF8; // รองรับภาษาไทยใน Console Log

var builder = WebApplication.CreateBuilder(args);

// ── 1. ลงทะเบียน Services ──────────────────────────────────────
builder.Services.AddTikTokServices(builder.Configuration);   // TikTok Service + HttpClient
builder.Services.AddControllers();                            // Enable Controllers
builder.Services.AddSwaggerDocumentation();                   // Swagger UI
builder.Services.AddLogging(logging =>                        // Logging พร้อมสี
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

var app = builder.Build();

// ── 2. Pipeline Middleware ─────────────────────────────────────
// Webhook Signature Middleware ต้องมาก่อน Routing
app.UseMiddleware<WebhookSignatureMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TikTok Shop PoC v1");
        c.RoutePrefix       = "swagger"; // เข้าได้ที่ /swagger
        c.DocumentTitle     = "🛍️ TikTok Shop PoC API";
        c.DisplayRequestDuration();
    });
}

app.MapControllers();

// ── Health Check Endpoint ──────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status    = "healthy",
    timestamp = DateTime.UtcNow,
    service   = "TikTok Shop PoC",
    version   = "1.0.0"
}))
.WithName("HealthCheck")
.WithTags("⚙️ System")
.ExcludeFromDescription();

app.Run();
