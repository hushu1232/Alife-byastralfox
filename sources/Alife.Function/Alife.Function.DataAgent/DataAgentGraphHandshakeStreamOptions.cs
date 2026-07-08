using System.Globalization;

namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeStreamOptions(
    bool Enabled,
    Uri? Endpoint,
    TimeSpan Timeout,
    bool Configured,
    bool RuntimeStarted)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED";
    public const string EndpointEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT";
    public const string TimeoutEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS";
    public const int DefaultTimeoutMs = 800;
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 5000;

    public static DataAgentGraphHandshakeStreamOptions Disabled { get; } = new(
        Enabled: false,
        Endpoint: null,
        Timeout: TimeSpan.FromMilliseconds(DefaultTimeoutMs),
        Configured: false,
        RuntimeStarted: false);

    public static DataAgentGraphHandshakeStreamOptions FromEnvironment()
    {
        return FromValues(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            Environment.GetEnvironmentVariable(EndpointEnvironmentVariable),
            Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable));
    }

    public static DataAgentGraphHandshakeStreamOptions FromValues(string? enabled, string? endpoint, string? timeoutMs)
    {
        bool parsedEnabled = IsEnabled(enabled);
        TimeSpan timeout = ParseTimeout(timeoutMs);

        if (parsedEnabled == false)
            return Disabled with { Timeout = timeout };

        if (TryParseLoopbackEndpoint(endpoint, out Uri? parsedEndpoint) == false)
            return Disabled with { Enabled = true, Timeout = timeout };

        return new DataAgentGraphHandshakeStreamOptions(
            Enabled: true,
            Endpoint: parsedEndpoint,
            Timeout: timeout,
            Configured: true,
            RuntimeStarted: false);
    }

    static bool IsEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            _ => false
        };
    }

    static TimeSpan ParseTimeout(string? timeoutMs)
    {
        if (int.TryParse(timeoutMs?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
            parsed >= MinTimeoutMs &&
            parsed <= MaxTimeoutMs)
        {
            return TimeSpan.FromMilliseconds(parsed);
        }

        return TimeSpan.FromMilliseconds(DefaultTimeoutMs);
    }

    static bool TryParseLoopbackEndpoint(string? value, out Uri? endpoint)
    {
        endpoint = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? candidate) == false)
            return false;

        if (string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) == false &&
            string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        if (string.Equals(candidate.Host, "0.0.0.0", StringComparison.Ordinal))
            return false;

        if (candidate.IsLoopback == false)
            return false;

        endpoint = candidate;
        return true;
    }
}
