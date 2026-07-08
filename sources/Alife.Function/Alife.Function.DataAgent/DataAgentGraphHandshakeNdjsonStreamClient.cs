using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphHandshakeNdjsonStreamClient : IDataAgentGraphHandshakeStreamClient
{
    const int MaxLineLengthChars = 16384;
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
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
            using HttpRequestMessage requestMessage = new(HttpMethod.Post, options.Endpoint)
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            using HttpResponseMessage response = httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
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
        catch (DecoderFallbackException exception)
        {
            throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema", exception);
        }
        catch (DataAgentGraphSidecarInvalidStreamException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("sidecar_timeout", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("sidecar_unavailable", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("sidecar_unavailable", exception);
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
        using StreamReader reader = new(stream, StrictUtf8, detectEncodingFromByteOrderMarks: false);

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

        return new DataAgentGraphHandshakeStreamResult(finalResponse, progress.ToArray());
    }

    static string? ReadLine(StreamReader reader, CancellationToken cancellationToken)
    {
        StringBuilder builder = new();
        char[] buffer = new char[1];
        bool pendingCarriageReturn = false;

        while (true)
        {
            int charsRead = reader
                .ReadAsync(buffer.AsMemory(0, 1), cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            if (charsRead == 0)
            {
                if (pendingCarriageReturn)
                    AppendBounded(builder, '\r');

                return builder.Length == 0 ? null : builder.ToString();
            }

            char current = buffer[0];
            if (pendingCarriageReturn)
            {
                pendingCarriageReturn = false;
                if (current == '\n')
                    return builder.ToString();

                AppendBounded(builder, '\r');
            }

            if (current == '\r')
            {
                pendingCarriageReturn = true;
                continue;
            }

            if (current == '\n')
                return builder.ToString();

            AppendBounded(builder, current);
        }
    }

    static void AppendBounded(StringBuilder builder, char current)
    {
        if (builder.Length >= MaxLineLengthChars)
            throw InvalidStreamSchema();

        builder.Append(current);
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
