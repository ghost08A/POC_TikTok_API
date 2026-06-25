namespace TikTokShop.Domain.ResponseModels;

/// <summary>
/// Clean DTO ที่เราส่งกลับให้ Client — เน้นเฉพาะข้อมูล E-Commerce
/// ไม่มีข้อมูลระบบคะแนน/ส่วนลด เพื่อ Privacy และความเรียบง่าย
/// </summary>
public class CleanOrderDto
{
    /// <summary>Order ID ของ TikTok</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Customer Identifier — ดักจาก buyer_uid หรือ buyer_email
    /// ใช้สำหรับระบุตัวตนลูกค้าในระบบ Loyalty ของเรา
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>ยอดรวมสุทธิของออเดอร์ (สกุลเงิน THB)</summary>
    public decimal TotalAmountTHB { get; set; }

    /// <summary>สถานะออเดอร์ เช่น UNPAID, ON_HOLD, AWAITING_SHIPMENT, COMPLETED</summary>
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>Timestamp Unix ที่สร้างออเดอร์ (แปลงเป็น String เพื่อ JSON compatibility)</summary>
    public string CreatedTimestamp { get; set; } = string.Empty;

    /// <summary>รหัส Tenant เพื่อบอกว่า Order นี้มาจากร้านไหน</summary>
    public string TenantCode { get; set; } = string.Empty;
}
