using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.WebBridge;

public class WebApiClient
{
    public WebApiClient(HttpClient httpClient, WebBridgeServiceConfig config)
    {
        this.httpClient = httpClient;
        this.config = config;

        if (httpClient.BaseAddress == null && string.IsNullOrWhiteSpace(config.ApiBaseUrl) == false)
            httpClient.BaseAddress = new Uri(config.ApiBaseUrl);
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds));
    }

    public async Task<WebAvatarConfig> PullConfig(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "api/pet/config");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<WebAvatarConfig>(json, jsonOptions) ?? new WebAvatarConfig();
    }

    public async Task PushState(WebAvatarConfig avatarConfig, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(avatarConfig, jsonOptions);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "api/pet/sync");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetAvatar(string avatarId, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(new SetAvatarRequest(avatarId), jsonOptions);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "api/pet/set-avatar");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<WebAssetManifest> PullAssets(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "api/pet/assets");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<WebAssetManifest>(json, jsonOptions) ?? new WebAssetManifest();
    }

    HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        if (string.IsNullOrWhiteSpace(config.ApiToken) == false)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken);
        return request;
    }

    readonly HttpClient httpClient;
    readonly WebBridgeServiceConfig config;
    readonly record struct SetAvatarRequest(string AvatarId);
    static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
