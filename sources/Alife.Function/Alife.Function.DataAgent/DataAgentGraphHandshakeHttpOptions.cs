using System.Globalization;

namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeHttpOptions(
    Uri? Endpoint,
    TimeSpan Timeout,
    bool Configured,
    bool RuntimeStarted)
{
    public const string EndpointEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT";
    public const string TimeoutEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS";
    public const int DefaultTimeoutMs = 800;
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 5000;

    public static DataAgentGraphHandshakeHttpOptions Disabled { get; } = new(
        Endpoint: null,
        Timeout: TimeSpan.FromMilliseconds(DefaultTimeoutMs),
        Configured: false,
        RuntimeStarted: false);

    public static DataAgentGraphHandshakeHttpOptions FromEnvironment()
    {
        return FromValues(
            Environment.GetEnvironmentVariable(EndpointEnvironmentVariable),
            Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable));
    }

    public static DataAgentGraphHandshakeHttpOptions FromValues(string? endpoint, string? timeoutMs)
    {
        TimeSpan timeout = ParseTimeout(timeoutMs);

        if (TryParseLoopbackEndpoint(endpoint, out Uri? parsedEndpoint) == false)
            return Disabled with { Timeout = timeout };

        return new DataAgentGraphHandshakeHttpOptions(
            parsedEndpoint,
            timeout,
            Configured: true,
            RuntimeStarted: false);
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
