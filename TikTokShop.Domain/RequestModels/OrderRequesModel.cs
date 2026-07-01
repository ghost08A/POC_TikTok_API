using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TikTokShop.Domain.RequestModels
{
    public class SearchOrderListRequestModel
    {
        // เลือกใช้ช่วงเวลา created หรือ updated ได้
        // ถ้า webhook มีปัญหา แนะนำใช้ UpdateTimeFrom / UpdateTimeTo
        public DateTime? CreateTimeFrom { get; set; }
        public DateTime? CreateTimeTo { get; set; }

        public DateTime? UpdateTimeFrom { get; set; }
        public DateTime? UpdateTimeTo { get; set; }

        // เช่น UNPAID, ON_HOLD, AWAITING_SHIPMENT, IN_TRANSIT, DELIVERED, COMPLETED, CANCELLED
        // ถ้า null = ไม่ filter status
        public string? OrderStatus { get; set; }

        // ถ้ามี buyer id จาก TikTok Shop แล้วอยากค้นเฉพาะ buyer
        public string? BuyerUserId { get; set; }

        // เช่น TIKTOK / SELLER ถ้ายังไม่ใช้ปล่อย null
        public string? ShippingType { get; set; }

        // paging
        public int PageSize { get; set; } = 50;
        public string? PageToken { get; set; }

        // แนะนำใช้ update_time สำหรับ sync webhook หลุด
        public string SortField { get; set; } = "update_time";

        // ASC / DESC
        public string SortOrder { get; set; } = "DESC";
    }
}
