using Microsoft.AspNetCore.Mvc;
using TikTokShop.Domain.Interfaces;
using TikTokShop.Domain.RequestModels;

namespace TikTokShop.WebAPI.Controllers;

[ApiController]
[Route("api/orders")]
[Tags("Orders")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IOrderService orderService, ILogger<OrderController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    [HttpGet("detail/{shopId}/{orderId}")]
    public async Task<IActionResult> GetOrderDetail(string shopId, string orderId)
    {
        _logger.LogInformation("[OrderDetail] ShopId={ShopId} | OrderId={OrderId}", shopId, orderId);

        try
        {
            var rawJson = await _orderService.FetchAndPrintOrderDetailAsync(shopId, orderId);
            return ToJsonResponse(rawJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrderDetail] Error | ShopId={ShopId} | OrderId={OrderId}", shopId, orderId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = ex.Message });
        }
    }

    [HttpGet("list/{shopId}")]
    public async Task<IActionResult> SearchOrderList(
        string shopId,
        [FromQuery] string? order_status = null,
        [FromQuery] DateTime? create_time_from = null,
        [FromQuery] DateTime? create_time_to = null,
        [FromQuery] DateTime? update_time_from = null,
        [FromQuery] DateTime? update_time_to = null,
        [FromQuery] string? buyer_user_id = null,
        [FromQuery] string? shipping_type = null,
        [FromQuery] string? sort_field = "update_time",
        [FromQuery] string? sort_order = "DESC",
        [FromQuery] int page_size = 50,
        [FromQuery] string? page_token = null)
    {
        var request = new SearchOrderListRequestModel
        {
            OrderStatus = order_status,
            CreateTimeFrom = create_time_from,
            CreateTimeTo = create_time_to,
            UpdateTimeFrom = update_time_from,
            UpdateTimeTo = update_time_to,
            BuyerUserId = buyer_user_id,
            ShippingType = shipping_type,
            SortField = sort_field ?? "update_time",
            SortOrder = sort_order ?? "DESC",
            PageSize = page_size,
            PageToken = page_token,
        };

        _logger.LogInformation(
            "[OrderList] ShopId={ShopId} | Status={Status} | CreateFrom={CreateFrom} | CreateTo={CreateTo} | UpdateFrom={UpdateFrom} | UpdateTo={UpdateTo} | Sort={Sort} {Order} | PageSize={PageSize}",
            shopId,
            order_status ?? "(any)",
            create_time_from?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            create_time_to?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            update_time_from?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            update_time_to?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            sort_field ?? "update_time",
            sort_order ?? "DESC",
            page_size);

        try
        {
            var rawJson = await _orderService.SearchOrderListAsync(shopId, request);
            return ToJsonResponse(rawJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrderList] Error | ShopId={ShopId}", shopId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = ex.Message, shopId });
        }
    }

    [HttpGet("cancellation/{shopId}/{orderId}")]
    public async Task<IActionResult> GetOrderCancellation(string shopId, string orderId)
    {
        _logger.LogInformation("[OrderCancellation] ShopId={ShopId} | OrderId={OrderId}", shopId, orderId);

        try
        {
            var rawJson = await _orderService.SearchCancellationByOrderIdAsync(shopId, orderId);
            return ToJsonResponse(rawJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrderCancellation] Error | ShopId={ShopId} | OrderId={OrderId}", shopId, orderId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = ex.Message, shopId, orderId });
        }
    }

    [HttpGet("returns/{shopId}/{orderId}")]
    public async Task<IActionResult> GetOrderReturns(
        string shopId,
        string orderId,
        [FromQuery] List<string>? return_status = null,
        [FromQuery] List<string>? return_type = null,
        [FromQuery] DateTime? create_from = null,
        [FromQuery] DateTime? create_to = null,
        [FromQuery] string? page_token = null,
        [FromQuery] int page_size = 10)
    {
        _logger.LogInformation(
            "[OrderReturns] ShopId={ShopId} | OrderId={OrderId} | Status={Status} | Type={Type} | From={From} | To={To} | PageSize={PageSize}",
            shopId,
            orderId,
            return_status is { Count: > 0 } ? string.Join(",", return_status) : "(any)",
            return_type is { Count: > 0 } ? string.Join(",", return_type) : "(any)",
            create_from?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
            create_to?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
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

            return ToJsonResponse(rawJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrderReturns] Error | ShopId={ShopId} | OrderId={OrderId}", shopId, orderId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = ex.Message, shopId, orderId });
        }
    }

    private IActionResult ToJsonResponse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return NotFound();
        }

        return Content(rawJson, "application/json");
    }
}
