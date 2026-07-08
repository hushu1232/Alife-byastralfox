using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentGraphHandshakeDiagnosticsFormatter
{
    const int DefaultMaxChars = 1800;
    const int MaxFieldLength = 240;
    const string Redacted = "redacted";
    const string TruncationSuffix = "...";

    public static string Format(DataAgentGraphHandshakeOutcome? outcome, int maxChars = DefaultMaxChars)
    {
        if (outcome is null)
        {
            return Bound(string.Join(Environment.NewLine,
                "DataAgent graph handshake",
                "status=unavailable",
                "reason=handshake_unavailable",
                "fallback_required=true",
                "runtime_required=false"), maxChars);
        }

        StringBuilder builder = new();
        builder.AppendLine("DataAgent graph handshake");
        builder.AppendLine($"status={SafeToken(outcome.Status.ToString().ToLowerInvariant())}");
        builder.AppendLine($"reason={SafeToken(outcome.ReasonCode)}");
        builder.AppendLine($"fallback_required={Bool(outcome.FallbackRequired)}");
        builder.AppendLine("no_sql_authority=true");
        builder.AppendLine("read_only=true");
        builder.AppendLine("scoped_node_manifest=true");
        builder.AppendLine("runtime_required=false");

        if (outcome.Observability is not null)
        {
            builder.AppendLine(FormatObservability(outcome.Observability));
        }

        if (outcome.Response is not null)
        {
            builder.AppendLine($"selected_nodes={FormatTokens(outcome.Response.SelectedNodes)}");
            builder.AppendLine($"progress={FormatProgress(outcome.Response.NodeProgress)}");
            builder.AppendLine($"trace={SafeDiagnosticText(
                outcome.Response.TraceSummary,
                DataAgentGraphHandshakeLimits.MaxTraceSummaryChars)}");
            builder.AppendLine($"context={SafeDiagnosticText(
                outcome.Response.ContextContribution,
                DataAgentGraphHandshakeLimits.MaxContextContributionChars)}");
        }

        return Bound(builder.ToString().TrimEnd(), maxChars);
    }

    static string FormatTokens(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return "empty";

        return string.Join(",", values
            .Take(DataAgentGraphHandshakeLimits.MaxNodeManifests)
            .Select(SafeToken));
    }

    static string FormatObservability(DataAgentGraphSidecarObservabilitySnapshot snapshot)
    {
        return string.Join(' ',
            "graph_sidecar",
            $"status={SafeToken(snapshot.Status.ToString().ToLowerInvariant())}",
            $"reason={SafeToken(snapshot.ReasonCode)}",
            $"enabled={Bool(snapshot.SidecarEnabled)}",
            $"endpoint_configured={Bool(snapshot.EndpointConfigured)}",
            $"runtime_started_by_alife={Bool(snapshot.RuntimeStartedByAlife)}",
            $"network_attempted={Bool(snapshot.NetworkAttempted)}",
            $"accepted={Bool(snapshot.Accepted)}",
            $"fallback={Bool(snapshot.FallbackUsed)}",
            $"summary={SafeDiagnosticText(snapshot.SafeSummary, DataAgentGraphHandshakeLimits.MaxReasonCodeLength)}");
    }

    static string FormatProgress(IReadOnlyList<DataAgentGraphHandshakeProgress>? progress)
    {
        if (progress is null || progress.Count == 0)
            return "empty";

        return string.Join(",", progress
            .Take(DataAgentGraphHandshakeLimits.MaxProgressEvents)
            .Select(item =>
        {
            if (item is null)
                return Redacted;

            return $"{SafeToken(item.NodeName)}:{SafeToken(item.Status.ToString())}:{SafeToken(item.ReasonCode)}";
        }));
    }

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Redacted;

        string trimmed = value.Trim();
        if (trimmed.Length > MaxFieldLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(trimmed))
            return Redacted;

        foreach (char current in trimmed)
        {
            if (IsTokenChar(current) == false)
                return Redacted;
        }

        return trimmed;
    }

    static string SafeDiagnosticText(string? value, int maxInputChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty";

        string boundedInput = BoundInput(value, maxInputChars);
        if (DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(boundedInput))
            return Redacted;

        string collapsed = CollapseWhitespace(boundedInput);
        StringBuilder builder = new(Math.Min(collapsed.Length, MaxFieldLength));
        foreach (char current in collapsed)
        {
            if (builder.Length >= MaxFieldLength)
                break;

            if (current == '=')
            {
                builder.Append(':');
                continue;
            }

            builder.Append(current is >= ' ' and <= '~' ? current : '_');
        }

        string sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "empty" : sanitized;
    }

    static string BoundInput(string value, int maxChars)
    {
        if (maxChars <= 0)
            return string.Empty;

        return value.Length <= maxChars ? value : value[..maxChars];
    }

    static string CollapseWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool previousWasWhiteSpace = false;

        foreach (char current in value)
        {
            if (char.IsWhiteSpace(current))
            {
                if (previousWasWhiteSpace == false)
                    builder.Append(' ');

                previousWasWhiteSpace = true;
                continue;
            }

            builder.Append(current);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim();
    }

    static bool IsTokenChar(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_'
            or '-'
            or '.';
    }

    static string Bound(string value, int maxChars)
    {
        if (maxChars <= 0 || value.Length <= maxChars)
            return maxChars <= 0 ? string.Empty : value;

        if (maxChars <= TruncationSuffix.Length)
            return TruncationSuffix[..maxChars];

        return value[..(maxChars - TruncationSuffix.Length)].TrimEnd() + TruncationSuffix;
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
