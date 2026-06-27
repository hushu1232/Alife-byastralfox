using System;
using System.Linq;
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
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "api/pet/sync");
        request.Content = new StringContent(
            "{\"clientVersion\":\"alife-webbridge\",\"capabilities\":[\"config\",\"assets\",\"avatar\"]}",
            Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return DeserializeAvatarConfig(json);
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

    public async Task<WebBridgePackageManifest> PullPackageManifest(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"api/webbridge/packages/{Uri.EscapeDataString(packageId)}/manifest");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return DeserializeEnvelope<WebBridgePackageManifest>(json);
    }

    HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        if (string.IsNullOrWhiteSpace(config.ApiToken) == false)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken);
        return request;
    }

    static WebAvatarConfig DeserializeAvatarConfig(string json)
    {
        JsonElement payload = DeserializeEnvelope(json);

        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("petName", out _))
            return MapPetExport(payload);

        return payload.Deserialize<WebAvatarConfig>(jsonOptions) ?? new WebAvatarConfig();
    }

    static T DeserializeEnvelope<T>(string json) where T : new()
    {
        return DeserializeEnvelope(json).Deserialize<T>(jsonOptions) ?? new T();
    }

    static JsonElement DeserializeEnvelope(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement payload = document.RootElement;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("data", out JsonElement data) &&
            data.ValueKind == JsonValueKind.Object)
        {
            payload = data;
        }

        return payload.Clone();
    }

    static WebAvatarConfig MapPetExport(JsonElement data)
    {
        string petName = GetString(data, "petName");
        string personality = GetString(data, "personality");
        string backstory = GetString(data, "backstory");
        string characterExtra = GetString(data, "characterExtra");

        return new WebAvatarConfig
        {
            Id = GetString(data, "avatarId"),
            Name = petName,
            Description = string.IsNullOrWhiteSpace(backstory) ? characterExtra : backstory,
            Prompt = BuildPrompt(personality, backstory, characterExtra)
        };
    }

    static string BuildPrompt(params string[] parts)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            parts.Where(part => string.IsNullOrWhiteSpace(part) == false).Select(part => part.Trim()));
    }

    static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
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
