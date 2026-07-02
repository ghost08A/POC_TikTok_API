using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace TikTokShop.Service.Helpers;

// ================================================================
// TikTokApiClient.cs — Shared HTTP Client Helper
//
// รวม pattern การยิง GET/POST ไป TikTok Open API ไว้ที่เดียว
// แทนการสร้าง HttpRequestMessage + ใส่ header + SendAsync + ReadAsString
// ซ้ำในทุก method ของ OrderService / ShopService
//
// การใช้งาน:
//   var client = new TikTokApiClient(_httpClientFactory, _logger);
//   string json = await client.GetAsync(url, tenant.AccessToken);
//   string json = await client.PostJsonAsync(url, tenant.AccessToken, body);
// ================================================================

/// <summary>
/// Wrapper สำหรับ HTTP calls ไป TikTok Open API
/// จัดการ Header x-tts-access-token, Content-Type, และ Response reading ไว้ที่เดียว
/// </summary>
public sealed class TikTokApiClient
{
    private readonly IHttpClientFactory               _httpClientFactory;
    private readonly ILogger                          _logger;

    private const string AccessTokenHeader = "x-tts-access-token";

    public TikTokApiClient(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    // ── GET ───────────────────────────────────────────────────────

    /// <summary>
    /// ยิง HTTP GET พร้อม Access Token header และ Return JSON string
    /// </summary>
    /// <param name="requestUrl">Full URL รวม query string แล้ว</param>
    /// <param name="accessToken">TikTok Access Token (x-tts-access-token)</param>
    /// <param name="logTag">Tag สำหรับ log เช่น "[OrderDetail]"</param>
    public async Task<string> GetAsync(string requestUrl, string accessToken, string logTag = "[TikTokAPI]")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Add(AccessTokenHeader, accessToken);
        request.Headers.Add("Accept", "application/json");

        return await SendAndReadAsync(request, logTag);
    }

    // ── POST ──────────────────────────────────────────────────────

    /// <summary>
    /// ยิง HTTP POST พร้อม JSON body, Access Token header และ Return JSON string
    /// </summary>
    /// <param name="requestUrl">Full URL รวม query string แล้ว</param>
    /// <param name="accessToken">TikTok Access Token (x-tts-access-token)</param>
    /// <param name="jsonBody">JSON string สำหรับ Request Body</param>
    /// <param name="logTag">Tag สำหรับ log เช่น "[OrderList]"</param>
    public async Task<string> PostJsonAsync(string requestUrl, string accessToken, string jsonBody, string logTag = "[TikTokAPI]")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add(AccessTokenHeader, accessToken);
        request.Headers.Add("Accept", "application/json");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        _logger.LogInformation("{Tag} POST Body: {Body}", logTag, jsonBody);

        return await SendAndReadAsync(request, logTag);
    }

    // ── Internal ──────────────────────────────────────────────────

    private async Task<string> SendAndReadAsync(HttpRequestMessage request, string logTag)
    {
        var client   = _httpClientFactory.CreateClient();
        var response = await client.SendAsync(request);
        string rawJson = await response.Content.ReadAsStringAsync();

        _logger.LogWarning("{Tag} HTTP {StatusCode}: {Body}", logTag, (int)response.StatusCode, rawJson);

        return rawJson;
    }
}
