namespace TikTokShop.Service.Helpers;

// ================================================================
// TikTokHelper.cs — Shared Business Logic Helpers
//
// รวม utility methods ทั่วไปที่ใช้ข้ามหลาย Service
// ================================================================

/// <summary>Utility methods สำหรับ TikTok-specific business logic</summary>
public static class TikTokHelper
{
    /// <summary>
    /// แปลง Unix Timestamp (seconds) ที่ TikTok ส่งมา → DateTime UTC
    /// </summary>
    public static DateTime FromTikTokUnixSeconds(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

    /// <summary>
    /// ตรวจสอบว่า Return/Refund Status เป็นสถานะ "สำเร็จแล้ว" ที่ควรหักแต้ม
    /// </summary>
    public static bool IsPossibleFinalRefundStatus(string returnStatus)
        => returnStatus is
            "RETURN_OR_REFUND_REQUEST_SUCCESS" or
            "RETURN_OR_REFUND_REQUEST_COMPLETE" or
            "REFUND_COMPLETE"                  or
            "REFUND_COMPLETED";
}
