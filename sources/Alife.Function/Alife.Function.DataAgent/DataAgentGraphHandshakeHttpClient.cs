using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphSidecarInvalidResponseException : Exception
{
    public DataAgentGraphSidecarInvalidResponseException(string message)
        : base(message)
    {
    }

    public DataAgentGraphSidecarInvalidResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DataAgentGraphHandshakeHttpClient : IDataAgentGraphSidecarClient
{
    const int MaxResponseBodyBytes = 65536;
    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    readonly HttpClient httpClient;
    readonly DataAgentGraphHandshakeHttpOptions options;

    public DataAgentGraphHandshakeHttpClient(HttpClient httpClient, DataAgentGraphHandshakeHttpOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));

        if (options.Configured == false || options.Endpoint is null)
            throw new ArgumentException("Graph handshake HTTP endpoint is not configured.", nameof(options));
    }

    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using CancellationTokenSource cancellation = new(options.Timeout);

        try
        {
            using HttpResponseMessage response = httpClient.PostAsync(
                    options.Endpoint,
                    JsonContent.Create(request, options: JsonOptions),
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();

            if (response.IsSuccessStatusCode == false)
                throw new InvalidOperationException("sidecar_unavailable");

            byte[] payload = ReadBoundedResponse(response.Content, cancellation.Token);
            if (HasRequiredSchema(payload) == false)
                throw new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema");
            DataAgentGraphHandshakeResponse? handshakeResponse =
                JsonSerializer.Deserialize<DataAgentGraphHandshakeResponse>(payload, JsonOptions);

            return handshakeResponse
                ?? throw new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema");
        }
        catch (JsonException)
        {
            throw new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema");
        }
        catch (TaskCanceledException exception)
        {
            throw new TimeoutException("sidecar_timeout", exception);
        }
    }

    static byte[] ReadBoundedResponse(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxResponseBodyBytes)
            throw new DataAgentGraphSidecarInvalidResponseException("response_body_too_large");

        using Stream stream = content.ReadAsStreamAsync(cancellationToken).GetAwaiter().GetResult();
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = stream.ReadAsync(chunk.AsMemory(), cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (read == 0)
                break;
            if (buffer.Length + read > MaxResponseBodyBytes)
                throw new DataAgentGraphSidecarInvalidResponseException("response_body_too_large");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    static bool HasRequiredSchema(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 13)
            return false;

        return HasKind(root, "RequestId", JsonValueKind.String)
            && HasKind(root, "Accepted", JsonValueKind.True, JsonValueKind.False)
            && HasKind(root, "ReasonCode", JsonValueKind.String)
            && HasKind(root, "SelectedNodes", JsonValueKind.Array)
            && HasKind(root, "NodeProgress", JsonValueKind.Array)
            && HasKind(root, "TraceSummary", JsonValueKind.String)
            && HasKind(root, "ContextContribution", JsonValueKind.String)
            && HasKind(root, "FallbackRequired", JsonValueKind.True, JsonValueKind.False)
            && HasKind(root, "NoSqlAuthority", JsonValueKind.True, JsonValueKind.False)
            && HasKind(root, "ReadOnly", JsonValueKind.True, JsonValueKind.False)
            && HasKind(root, "RequestedToolNames", JsonValueKind.Array)
            && HasKind(root, "RequestsCheckpointMutation", JsonValueKind.True, JsonValueKind.False)
            && HasKind(root, "RequestsVisibleText", JsonValueKind.True, JsonValueKind.False);
    }

    static bool HasKind(JsonElement root, string propertyName, params JsonValueKind[] allowedKinds)
    {
        return root.TryGetProperty(propertyName, out JsonElement value)
            && allowedKinds.Contains(value.ValueKind);
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
