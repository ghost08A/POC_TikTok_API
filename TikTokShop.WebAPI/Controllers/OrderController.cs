using Microsoft.AspNetCore.Mvc;
using TikTokShop.Domain.Interfaces;

namespace TikTokShop.WebAPI.Controllers;

// Endpoints:
//   GET /api/orders/{tenantCode}                 → Pull Engine (รายการออเดอร์)
//   GET /api/orders/detail/{shopId}/{orderId}    → Fetch Detail (ดูรายละเอียดรายเดี่ยว)
// ================================================================
[ApiController]
[Route("api/orders")]
[Tags("📦 Orders")]
public class OrderController : ControllerBase
{
    private readonly IOrderService              _orderService;
    private readonly ILogger<OrderController>   _logger;

    public OrderController(IOrderService orderService, ILogger<OrderController> logger)
    {
        _orderService = orderService;
        _logger       = logger;
    }

    // ────────────────────────────────────────────────────────────
    // GET /api/orders/detail/{shopId}/{orderId}
    // Manual Fetch — ดึงรายละเอียดออเดอร์รายเดี่ยวโดยไม่รอ Webhook
    // ────────────────────────────────────────────────────────────
    /// <summary>
    /// [Manual Fetch] ดึงรายละเอียดออเดอร์รายเดี่ยวจาก TikTok API โดยตรง
    /// ใช้เมื่อต้องการดูข้อมูลออเดอร์เองโดยไม่ต้องรอ Webhook
    /// </summary>
    /// <remarks>
    /// - ผลลัพธ์จะ Print ออก Console (Server-side) พร้อม Return 200 OK<br/>
    /// - <b>shopId</b> = Shop ID ของ TikTok (ตัวเลข เช่น "7494734242450408526")<br/>
    /// - <b>orderId</b> = Order ID ที่ได้จาก TikTok (ตัวเลข เช่น "576462812989423699")
    /// </remarks>
    /// <param name="shopId">TikTok Shop ID ใช้ค้นหา Tenant (ไม่ใช่ TenantCode)</param>
    /// <param name="orderId">Order ID ที่ต้องการดูรายละเอียด</param>
    [HttpGet("detail/{shopId}/{orderId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetOrderDetail(string shopId, string orderId)
    {
        _logger.LogInformation("🔍 [OrderDetail] ShopId={ShopId} | OrderId={OrderId}", shopId, orderId);

        try
        {
            // FetchAndPrintOrderDetailAsync จะ Print ข้อมูลออก Console
            // และ Log ผ่าน ILogger เพื่อดู Raw Response
            await _orderService.FetchAndPrintOrderDetailAsync(shopId, orderId);

            return Ok(new
            {
                success = true,
                message = $"ดึงข้อมูล Order {orderId} สำเร็จ — ดูรายละเอียดได้ใน Console Log",
                shopId  = shopId,
                orderId = orderId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [OrderDetail] Error | ShopId={ShopId} | OrderId={OrderId}",
                shopId, orderId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = ex.Message });
        }
    }
}
