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

            DataAgentGraphHandshakeResponse? handshakeResponse = response.Content
                .ReadFromJsonAsync<DataAgentGraphHandshakeResponse>(JsonOptions, cancellation.Token)
                .GetAwaiter()
                .GetResult();

            return handshakeResponse
                ?? throw new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema");
        }
        catch (JsonException exception)
        {
            throw new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new TimeoutException("sidecar_timeout", exception);
        }
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
