using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphHandshakeNdjsonStreamClient : IDataAgentGraphHandshakeStreamClient
{
    const int MaxLineLengthChars = 16384;
    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    readonly HttpClient httpClient;
    readonly DataAgentGraphHandshakeStreamOptions options;

    public DataAgentGraphHandshakeNdjsonStreamClient(HttpClient httpClient, DataAgentGraphHandshakeStreamOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));

        if (options.Enabled == false || options.Configured == false || options.Endpoint is null)
            throw new ArgumentException("Graph handshake NDJSON stream endpoint is not configured.", nameof(options));
    }

    public DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request)
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

            return ReadStreamResult(response, cancellation.Token);
        }
        catch (JsonException exception)
        {
            throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new TimeoutException("sidecar_timeout", exception);
        }
    }

    static DataAgentGraphHandshakeStreamResult ReadStreamResult(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using Stream stream = response.Content
            .ReadAsStreamAsync(cancellationToken)
            .GetAwaiter()
            .GetResult();
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        List<DataAgentGraphHandshakeProgress> progress = [];
        DataAgentGraphHandshakeResponse? finalResponse = null;

        while (ReadLine(reader, cancellationToken) is { } line)
        {
            if (finalResponse is not null)
                throw InvalidStreamSchema();

            if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineLengthChars)
                throw InvalidStreamSchema();

            DataAgentGraphHandshakeStreamEvent streamEvent = JsonSerializer
                .Deserialize<DataAgentGraphHandshakeStreamEvent>(line, JsonOptions)
                ?? throw InvalidStreamSchema();

            if (Enum.IsDefined(typeof(DataAgentGraphHandshakeStreamEventKind), streamEvent.Kind) == false)
                throw InvalidStreamSchema();

            switch (streamEvent.Kind)
            {
                case DataAgentGraphHandshakeStreamEventKind.Progress:
                    if (streamEvent.Progress is null || streamEvent.Response is not null)
                        throw InvalidStreamSchema();

                    progress.Add(streamEvent.Progress);
                    if (progress.Count > DataAgentGraphHandshakeLimits.MaxProgressEvents)
                        throw new DataAgentGraphSidecarInvalidStreamException("stream_progress_over_budget");
                    break;

                case DataAgentGraphHandshakeStreamEventKind.FinalResponse:
                    if (streamEvent.Response is null || streamEvent.Progress is not null)
                        throw InvalidStreamSchema();

                    finalResponse = streamEvent.Response;
                    break;
            }
        }

        if (finalResponse is null)
            throw new DataAgentGraphSidecarInvalidStreamException("missing_stream_final_response");

        return new DataAgentGraphHandshakeStreamResult(finalResponse, progress);
    }

    static string? ReadLine(StreamReader reader, CancellationToken cancellationToken)
    {
        return reader
            .ReadLineAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    static DataAgentGraphSidecarInvalidStreamException InvalidStreamSchema()
    {
        return new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
