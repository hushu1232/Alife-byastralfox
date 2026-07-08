using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphHandshakeNdjsonStreamClient : IDataAgentGraphHandshakeStreamClient
{
    const string NdjsonMediaType = "application/x-ndjson";
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
            requestMessage.Headers.Accept.ParseAdd(NdjsonMediaType);

            using HttpResponseMessage response = httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();

            if (response.IsSuccessStatusCode == false)
                throw new InvalidOperationException("sidecar_unavailable");

            return ReadStreamResult(response, request.ProgressBudget, cancellation.Token);
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
        int progressBudget,
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

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty(nameof(DataAgentGraphHandshakeStreamEvent.Kind), out JsonElement kindElement) == false ||
                TryReadEnum(kindElement, out DataAgentGraphHandshakeStreamEventKind kind) == false)
            {
                throw InvalidStreamSchema();
            }

            bool hasProgress = TryGetNonNullProperty(
                root,
                nameof(DataAgentGraphHandshakeStreamEvent.Progress),
                out JsonElement progressElement);
            bool hasResponse = TryGetNonNullProperty(
                root,
                nameof(DataAgentGraphHandshakeStreamEvent.Response),
                out JsonElement responseElement);

            switch (kind)
            {
                case DataAgentGraphHandshakeStreamEventKind.Progress:
                    if (hasProgress == false || hasResponse)
                        throw InvalidStreamSchema();

                    ValidateProgressElement(progressElement);
                    progress.Add(progressElement.Deserialize<DataAgentGraphHandshakeProgress>(JsonOptions) ?? throw InvalidStreamSchema());
                    if (progress.Count > progressBudget ||
                        progress.Count > DataAgentGraphHandshakeLimits.MaxProgressEvents)
                    {
                        throw new DataAgentGraphSidecarInvalidStreamException("stream_progress_over_budget");
                    }

                    break;

                case DataAgentGraphHandshakeStreamEventKind.FinalResponse:
                    if (hasResponse == false || hasProgress || responseElement.ValueKind != JsonValueKind.Object)
                        throw InvalidStreamSchema();

                    finalResponse = responseElement.Deserialize<DataAgentGraphHandshakeResponse>(JsonOptions) ?? throw InvalidStreamSchema();
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

    static bool TryGetNonNullProperty(JsonElement root, string propertyName, out JsonElement element)
    {
        if (root.TryGetProperty(propertyName, out element) == false ||
            element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return true;
    }

    static void ValidateProgressElement(JsonElement progressElement)
    {
        if (progressElement.ValueKind != JsonValueKind.Object ||
            TryReadRequiredString(progressElement, nameof(DataAgentGraphHandshakeProgress.NodeName), out _) == false ||
            TryReadRequiredString(progressElement, nameof(DataAgentGraphHandshakeProgress.ReasonCode), out _) == false ||
            progressElement.TryGetProperty(nameof(DataAgentGraphHandshakeProgress.Status), out JsonElement statusElement) == false ||
            TryReadEnum(statusElement, out DataAgentGraphHandshakeProgressStatus _) == false)
        {
            throw InvalidStreamSchema();
        }
    }

    static bool TryReadRequiredString(JsonElement root, string propertyName, out string value)
    {
        value = "";
        if (root.TryGetProperty(propertyName, out JsonElement element) == false ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? "";
        return string.IsNullOrWhiteSpace(value) == false;
    }

    static bool TryReadEnum<TEnum>(JsonElement element, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (element.ValueKind != JsonValueKind.String)
            return false;

        string? rawValue = element.GetString();
        return string.IsNullOrEmpty(rawValue) == false &&
               Enum.TryParse(rawValue, ignoreCase: false, out value) &&
               Enum.IsDefined(value) &&
               string.Equals(Enum.GetName(value), rawValue, StringComparison.Ordinal);
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
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
