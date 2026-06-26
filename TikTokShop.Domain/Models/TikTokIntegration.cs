using System.Text.Json.Serialization;
namespace TikTokShop.Domain.Models
{
    public class TikTokTokenResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("data")]
        public TikTokTokenData Data { get; set; }
    }

    public class TikTokTokenData
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        // จริง ๆ เป็น Unix timestamp เวลาหมดอายุ
        [JsonPropertyName("access_token_expire_in")]
        public long AccessTokenExpireIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        // จริง ๆ เป็น Unix timestamp เวลาหมดอายุ
        [JsonPropertyName("refresh_token_expire_in")]
        public long RefreshTokenExpireIn { get; set; }

        [JsonPropertyName("open_id")]
        public string OpenId { get; set; } = string.Empty;

        [JsonPropertyName("seller_name")]
        public string SellerName { get; set; } = string.Empty;
    }
}
