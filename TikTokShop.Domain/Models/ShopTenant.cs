namespace TikTokShop.Domain.Models;

/// <summary>
/// Entity แทนข้อมูล Credentials ของร้านค้าแต่ละร้านในระบบ (Multi-Tenant)
/// ใน Production ควรเก็บใน Database พร้อม Encrypt Token ทุกตัว
/// </summary>
public class ShopTenant
{
    public string TenantCode { get; set; } = string.Empty;

    public string ShopName { get; set; } = string.Empty;

    public string ShopId { get; set; } = string.Empty;

    public string ShopCipher { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpireAt { get; set; }

    public DateTime RefreshTokenExpireAt { get; set; }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public string Status { get; set; } = "ACTIVE";
    public bool IsAccessTokenExpired
        => DateTime.UtcNow >= AccessTokenExpireAt;

    public bool IsRefreshTokenExpired
        => DateTime.UtcNow >= RefreshTokenExpireAt;

    public double AccessTokenRemainingMinutes
        => (AccessTokenExpireAt - DateTime.UtcNow).TotalMinutes;
    public bool ShouldRefreshAccessToken
    => DateTime.UtcNow.AddMinutes(30) >= AccessTokenExpireAt
       && !IsRefreshTokenExpired;
}
