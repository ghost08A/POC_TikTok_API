using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.X509Certificates;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.RequestModels;

namespace TikTokShop.WebAPI.Controllers;

// Endpoints:
//   GET /api/orders/list/{shopId}                → Order List (กรองด้วย query params)
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


    [HttpGet("detail/{shopId}/{orderId}")]
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

    /// <summary>
    /// ค้นหารายการออเดอร์ — ส่งแค่ shopId ก็ได้ params อื่นเป็น optional ทั้งหมด
    /// </summary>
    [HttpGet("list/{shopId}")]
    public async Task<IActionResult> SearchOrderList(
        string shopId,
        [FromQuery] string?   order_status      = null,
        [FromQuery] DateTime? create_time_from  = null,
        [FromQuery] DateTime? create_time_to    = null,
        [FromQuery] DateTime? update_time_from  = null,
        [FromQuery] DateTime? update_time_to    = null,
        [FromQuery] string?   buyer_user_id     = null,
        [FromQuery] string?   shipping_type     = null,
        [FromQuery] string?   sort_field        = "update_time",
        [FromQuery] string?   sort_order        = "DESC",
        [FromQuery] int       page_size         = 50,
        [FromQuery] string?   page_token        = null)
    {
        var request = new SearchOrderListRequestModel
        {
            OrderStatus     = order_status,
            CreateTimeFrom  = create_time_from,
            CreateTimeTo    = create_time_to,
            UpdateTimeFrom  = update_time_from,
            UpdateTimeTo    = update_time_to,
            BuyerUserId     = buyer_user_id,
            ShippingType    = shipping_type,
            SortField       = sort_field ?? "update_time",
            SortOrder       = sort_order ?? "DESC",
            PageSize        = page_size,
            PageToken       = page_token,
        };

        _logger.LogInformation(
            "🔍 [OrderList] ShopId={ShopId} | Status={Status} | CreateFrom={CreateFrom} | CreateTo={CreateTo} | UpdateFrom={UpdateFrom} | UpdateTo={UpdateTo} | Sort={Sort} {Order} | PageSize={PageSize}",
            shopId,
            order_status      ?? "(any)",
            create_time_from?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            create_time_to?.ToString("yyyy-MM-dd HH:mm:ss")   ?? "-",
            update_time_from?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            update_time_to?.ToString("yyyy-MM-dd HH:mm:ss")   ?? "-",
            sort_field ?? "update_time",
            sort_order ?? "DESC",
            page_size);

        try
        {
            var rawJson = await _orderService.SearchOrderListAsync(shopId, request);

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return NotFound(new
                {
                    success = false,
                    message = "ไม่พบข้อมูล order list หรือเรียก TikTok API ไม่สำเร็จ",
                    shopId
                });
            }

            return Content(rawJson, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [OrderList] Error | ShopId={ShopId}", shopId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { success = false, message = ex.Message, shopId });
        }
    }

    [HttpGet("cancellation/{shopId}/{orderId}")]
    public async Task<IActionResult> GetOrderCancellation(string shopId, string orderId)
    {
        _logger.LogInformation(
            "[OrderCancellation] ShopId={ShopId} | OrderId={OrderId}",
            shopId,
            orderId);

        try
        {
            var rawJson = await _orderService.SearchCancellationByOrderIdAsync(
                shopId,
                orderId);

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return NotFound(new
                {
                    success = false,
                    message = "ไม่พบข้อมูลการยกเลิก หรือเรียก TikTok API ไม่สำเร็จ",
                    shopId,
                    orderId
                });
            }

            return Content(rawJson, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "💥 [OrderCancellation] Error | ShopId={ShopId} | OrderId={OrderId}",
                shopId,
                orderId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message = ex.Message,
                    shopId,
                    orderId
                });
        }
    }

    /// <summary>
    /// Search Return/Refund ของ order ตาม TikTok Search Returns API (202602)
    /// </summary>
    /// <param name="shopId">Shop ID</param>
    /// <param name="orderId">Order ID ที่ต้องการค้นหา return</param>
    /// <param name="return_status">กรองตาม status (ใส่ได้หลายค่า) เช่น REFUND_COMPLETE, RETURN_OR_REFUND_REQUEST_PENDING</param>
    /// <param name="return_type">กรองตามประเภท (ใส่ได้หลายค่า): REFUND | RETURN_AND_REFUND | REPLACEMENT</param>
    /// <param name="create_from">วันเวลาเริ่มต้น — กรองเฉพาะ return ที่สร้างหลังจากเวลานี้ (รูปแบบ: 2025-01-15 หรือ 2025-01-15T00:00:00)</param>
    /// <param name="create_to">วันเวลาสิ้นสุด — กรองเฉพาะ return ที่สร้างก่อนเวลานี้ (รูปแบบ: 2025-01-31 หรือ 2025-01-31T23:59:59)</param>
    /// <param name="page_token">Page token สำหรับ pagination (ได้จาก response ก่อนหน้า)</param>
    /// <param name="page_size">จำนวนรายการต่อหน้า (default=10, max=100)</param>
    [HttpGet("returns/{shopId}/{orderId}")]
    public async Task<IActionResult> GetOrderReturns(
        string shopId,
        string orderId,
        [FromQuery] List<string>? return_status = null,
        [FromQuery] List<string>? return_type   = null,
        [FromQuery] DateTime?     create_from   = null,
        [FromQuery] DateTime?     create_to     = null,
        [FromQuery] string?       page_token    = null,
        [FromQuery] int           page_size     = 10)
    {
        _logger.LogInformation(
            "🔍 [OrderReturns] ShopId={ShopId} | OrderId={OrderId} | Status={Status} | Type={Type} | From={From} | To={To} | PageSize={PageSize}",
            shopId, orderId,
            return_status is { Count: > 0 } ? string.Join(",", return_status) : "(any)",
            return_type   is { Count: > 0 } ? string.Join(",", return_type)   : "(any)",
            create_from?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            create_to?.ToString("yyyy-MM-dd HH:mm:ss")   ?? "-",
            page_size);

        try
        {
            var rawJson = await _orderService.SearchReturnByOrderIdAsync(
                shopId,
                orderId,
                return_status,
                return_type,
                create_from,
                create_to,
                page_token,
                page_size);

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return NotFound(new
                {
                    success = false,
                    message = "ไม่พบข้อมูล return/refund หรือเรียก TikTok API ไม่สำเร็จ",
                    shopId,
                    orderId
                });
            }

            return Content(rawJson, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "💥 [OrderReturns] Error | ShopId={ShopId} | OrderId={OrderId}",
                shopId,
                orderId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message = ex.Message,
                    shopId,
                    orderId
                });
        }
    }


}
