using System.Text;
using TikTokShop.WebAPI.Extensions;
using TikTokShop.WebAPI.Middleware;

// ================================================================
// PROGRAM.CS — TikTok Shop PoC (Multi-Tenant, Dual-Engine)
// Architecture: 3-Layer (Domain / Service / WebAPI)
//
// Launch Profiles:
//   Sandbox  → ASPNETCORE_ENVIRONMENT=Sandbox  | port 5200
//   RealShop → ASPNETCORE_ENVIRONMENT=RealShop | port 5201
//   http     → ASPNETCORE_ENVIRONMENT=Development (default)
//
// Engine 1: Pull API  → GET  /api/orders/{shopId}
// Engine 2: Push Hook → POST /api/webhook
// ================================================================

Console.OutputEncoding = Encoding.UTF8; // รองรับภาษาไทยใน Console Log

var builder = WebApplication.CreateBuilder(args);

// ── 1. ลงทะเบียน Services ──────────────────────────────────────
builder.Services.AddTikTokServices(builder.Configuration);  // TikTok Services + HttpClients
builder.Services.AddControllers();                           // Enable Controllers
builder.Services.AddSwaggerDocumentation();                  // Swagger UI
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

var app = builder.Build();

// ── 2. Startup Log — แสดง Environment ปัจจุบัน ─────────────────
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
var env           = app.Environment;
var baseUrl       = builder.Configuration["TikTok:BaseUrl"] ?? "(not set)";

startupLogger.LogInformation(
    """

    ════════════════════════════════════════════════════════
    🛍️  TikTok Shop PoC — Starting Up
    ────────────────────────────────────────────────────────
    Environment : {Environment}
    TikTok URL  : {BaseUrl}
    ════════════════════════════════════════════════════════
    """,
    env.EnvironmentName,
    baseUrl);

// ── 3. Pipeline Middleware ─────────────────────────────────────
// Webhook Signature Middleware ต้องมาก่อน Routing
app.UseMiddleware<WebhookSignatureMiddleware>();

if (env.IsDevelopment() || env.IsEnvironment("Sandbox") || env.IsEnvironment("RealShop"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", $"TikTok Shop PoC v1 [{env.EnvironmentName}]");
        c.RoutePrefix      = "swagger";
        c.DocumentTitle    = $"🛍️ TikTok Shop PoC [{env.EnvironmentName}]";
        c.DisplayRequestDuration();
    });
}

app.MapControllers();

// ── Health Check Endpoint ──────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status      = "healthy",
    environment = env.EnvironmentName,
    timestamp   = DateTime.UtcNow,
    service     = "TikTok Shop PoC",
    version     = "1.0.0"
}))
.WithName("HealthCheck")
.WithTags("⚙️ System")
.ExcludeFromDescription();

app.Run();
