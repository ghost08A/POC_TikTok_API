using TikTokShop.Domain.ResponseModels;

namespace TikTokShop.Domain.Interfaces;

// ================================================================
// IOrderService.cs — Contract สำหรับ Order Management
//
// รับผิดชอบ:
//   1. ดึงรายการออเดอร์ (Pull Engine)
//   2. ดึงรายละเอียดออเดอร์รายเดี่ยว (Manual Fetch)
// ================================================================
public interface IOrderService
{

    /// <summary>
    /// ดึงรายละเอียดออเดอร์รายเดี่ยวจาก TikTok API แล้ว Print ออก Console
    /// ใช้เมื่อต้องการดูข้อมูลออเดอร์เองโดยไม่รอ Webhook
    /// เรียก TikTok: GET /order/202507/orders?ids={orderId}
    /// </summary>
    /// <param name="shopId">Shop ID (ใช้ค้นหา Tenant จาก TenantStore)</param>
    /// <param name="orderId">Order ID ของ TikTok ที่ต้องการดูรายละเอียด</param>
    Task FetchAndPrintOrderDetailAsync(string shopId, string orderId);

    
}
