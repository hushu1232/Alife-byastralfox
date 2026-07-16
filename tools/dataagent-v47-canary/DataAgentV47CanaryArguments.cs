using System.Globalization;

namespace Alife.Tools.DataAgentV47Canary;

public sealed record DataAgentV47CanaryArguments(
    Uri Endpoint,
    string OutputDirectory,
    int RequestCount,
    int TimeoutMs,
    int RuntimeRestartCount)
{
    public static DataAgentV47CanaryArgumentResult Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0 || args.Count % 2 != 0)
            return Rejected("canary_arguments_missing");

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Count; index += 2)
        {
            string key = args[index];
            if (key is not ("--endpoint" or "--output" or "--request-count" or "--timeout-ms" or "--runtime-restart-count") ||
                values.TryAdd(key, args[index + 1]) == false)
                return Rejected("canary_argument_invalid");
        }
        if (values.Count != 5)
            return Rejected("canary_arguments_missing");
        if (Uri.TryCreate(values["--endpoint"], UriKind.Absolute, out Uri? endpoint) == false ||
            endpoint.IsLoopback == false ||
            endpoint.Scheme != Uri.UriSchemeHttp)
            return Rejected("canary_endpoint_invalid");
        if (string.IsNullOrWhiteSpace(values["--output"]))
            return Rejected("canary_output_invalid");
        if (ParseBounded(values["--request-count"], 20, 256, out int requestCount) == false)
            return Rejected("canary_request_count_invalid");
        if (ParseBounded(values["--timeout-ms"], 100, 10_000, out int timeoutMs) == false)
            return Rejected("canary_timeout_invalid");
        if (ParseBounded(values["--runtime-restart-count"], 0, 1, out int restartCount) == false)
            return Rejected("canary_restart_count_invalid");

        return new(true, "canary_arguments_accepted",
            new(endpoint, values["--output"], requestCount, timeoutMs, restartCount));
    }

    static bool ParseBounded(string value, int minimum, int maximum, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
        parsed >= minimum && parsed <= maximum;

    static DataAgentV47CanaryArgumentResult Rejected(string reasonCode) =>
        new(false, reasonCode, null);
}

public sealed record DataAgentV47CanaryArgumentResult(
    bool Accepted,
    string ReasonCode,
    DataAgentV47CanaryArguments? Value);
