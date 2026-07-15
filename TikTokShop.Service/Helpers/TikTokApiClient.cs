using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace TikTokShop.Service.Helpers;


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

    public async Task<string> GetAsync(string requestUrl, string accessToken, string logTag = "[TikTokAPI]")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Add(AccessTokenHeader, accessToken);
        request.Headers.Add("Accept", "application/json");

        return await SendAndReadAsync(request, logTag);
    }

    // ── POST ──────────────────────────────────────────────────────
    /// ยิง HTTP POST พร้อม JSON body, Access Token header และ Return JSON string
    public async Task<string> PostJsonAsync(string requestUrl, string accessToken, string jsonBody, string logTag = "[TikTokAPI]")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add(AccessTokenHeader, accessToken);
        request.Headers.Add("Accept", "application/json");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return await SendAndReadAsync(request, logTag);
    }
    // ── Internal ──────────────────────────────────────────────────
    private async Task<string> SendAndReadAsync(HttpRequestMessage request, string logTag)
    {
        var client     = _httpClientFactory.CreateClient();
        var response   = await client.SendAsync(request);
        string rawJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("{Tag} HTTP {StatusCode} (non-2xx)", logTag, (int)response.StatusCode);

        return rawJson;
    }
}
