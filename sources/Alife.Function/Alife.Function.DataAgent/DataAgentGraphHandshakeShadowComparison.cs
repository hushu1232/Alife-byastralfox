namespace Alife.Function.DataAgent;

public enum DataAgentGraphHandshakeShadowComparisonStatus
{
    Match,
    AcceptedAdvisoryDifference,
    RejectedAuthorityClaim,
    FallbackUsed,
    InvalidSchema,
    TimeoutOrTransportFailure
}

public sealed record DataAgentGraphHandshakeShadowComparison(
    DataAgentGraphHandshakeShadowComparisonStatus Status,
    string ReasonCode,
    string DeterministicReasonCode,
    string SidecarReasonCode,
    DataAgentGraphHandshakeStatus DeterministicStatus,
    DataAgentGraphHandshakeStatus SidecarStatus,
    bool DeterministicFallbackRequired,
    bool SidecarFallbackRequired,
    bool DefaultResultChanged);

public sealed record DataAgentGraphHandshakeShadowComparisonReport(
    string ReplayId,
    IReadOnlyList<DataAgentGraphHandshakeShadowComparison> Comparisons,
    IReadOnlyDictionary<string, int> StatusCounts,
    int ComparisonCount,
    bool DefaultResultChanged,
    bool Passed)
{
    public static DataAgentGraphHandshakeShadowComparisonReport Create(
        string replayId,
        IReadOnlyList<DataAgentGraphHandshakeShadowComparison> comparisons)
    {
        string safeReplayId = string.IsNullOrWhiteSpace(replayId)
            ? "shadow-replay"
            : replayId.Trim();
        DataAgentGraphHandshakeShadowComparison[] safeComparisons =
            comparisons?.Where(comparison => comparison is not null).ToArray() ?? [];
        Dictionary<string, int> statusCounts = safeComparisons
            .GroupBy(comparison => comparison.ReasonCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        bool defaultResultChanged = safeComparisons.Any(comparison => comparison.DefaultResultChanged);

        return new DataAgentGraphHandshakeShadowComparisonReport(
            safeReplayId,
            safeComparisons,
            statusCounts,
            safeComparisons.Length,
            defaultResultChanged,
            defaultResultChanged == false);
    }
}

public static class DataAgentGraphHandshakeShadowComparer
{
    static readonly HashSet<string> AuthorityRejectionReasonCodes = new(StringComparer.Ordinal)
    {
        "sql_authority_requested",
        "checkpoint_mutation_requested",
        "visible_text_requested",
        "unknown_tool",
        "forbidden_tool",
        "execution_authority_requested"
    };

    public static DataAgentGraphHandshakeShadowComparison Compare(
        DataAgentGraphHandshakeOutcome deterministic,
        DataAgentGraphHandshakeOutcome sidecar)
    {
        DataAgentGraphHandshakeShadowComparisonStatus status = Classify(deterministic, sidecar);
        return new DataAgentGraphHandshakeShadowComparison(
            status,
            ReasonCodeFor(status),
            deterministic.ReasonCode,
            sidecar.ReasonCode,
            deterministic.Status,
            sidecar.Status,
            deterministic.FallbackRequired,
            sidecar.FallbackRequired,
            DefaultResultChanged: false);
    }

    static DataAgentGraphHandshakeShadowComparisonStatus Classify(
        DataAgentGraphHandshakeOutcome deterministic,
        DataAgentGraphHandshakeOutcome sidecar)
    {
        if (deterministic.Status == sidecar.Status &&
            string.Equals(deterministic.ReasonCode, sidecar.ReasonCode, StringComparison.Ordinal) &&
            deterministic.FallbackRequired == sidecar.FallbackRequired)
        {
            return DataAgentGraphHandshakeShadowComparisonStatus.Match;
        }

        if (sidecar.Status is DataAgentGraphHandshakeStatus.Timeout or DataAgentGraphHandshakeStatus.Unavailable)
            return DataAgentGraphHandshakeShadowComparisonStatus.TimeoutOrTransportFailure;

        if (sidecar.Status == DataAgentGraphHandshakeStatus.Invalid ||
            string.Equals(sidecar.ReasonCode, "invalid_response_schema", StringComparison.Ordinal) ||
            string.Equals(sidecar.ReasonCode, "invalid_request_schema", StringComparison.Ordinal))
        {
            return DataAgentGraphHandshakeShadowComparisonStatus.InvalidSchema;
        }

        if (sidecar.Status == DataAgentGraphHandshakeStatus.Rejected &&
            AuthorityRejectionReasonCodes.Contains(sidecar.ReasonCode))
        {
            return DataAgentGraphHandshakeShadowComparisonStatus.RejectedAuthorityClaim;
        }

        if (sidecar.Status == DataAgentGraphHandshakeStatus.Accepted)
            return DataAgentGraphHandshakeShadowComparisonStatus.AcceptedAdvisoryDifference;

        if (sidecar.FallbackRequired ||
            sidecar.Status == DataAgentGraphHandshakeStatus.Disabled ||
            sidecar.ReasonCode.Contains("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return DataAgentGraphHandshakeShadowComparisonStatus.FallbackUsed;
        }

        return DataAgentGraphHandshakeShadowComparisonStatus.AcceptedAdvisoryDifference;
    }

    static string ReasonCodeFor(DataAgentGraphHandshakeShadowComparisonStatus status)
    {
        return status switch
        {
            DataAgentGraphHandshakeShadowComparisonStatus.Match => "match",
            DataAgentGraphHandshakeShadowComparisonStatus.AcceptedAdvisoryDifference => "accepted_advisory_difference",
            DataAgentGraphHandshakeShadowComparisonStatus.RejectedAuthorityClaim => "rejected_authority_claim",
            DataAgentGraphHandshakeShadowComparisonStatus.FallbackUsed => "fallback_used",
            DataAgentGraphHandshakeShadowComparisonStatus.InvalidSchema => "invalid_schema",
            DataAgentGraphHandshakeShadowComparisonStatus.TimeoutOrTransportFailure => "timeout_or_transport_failure",
            _ => "accepted_advisory_difference"
        };
    }
}

public static class DataAgentGraphHandshakeShadowComparisonFormatter
{
    public static string Format(DataAgentGraphHandshakeShadowComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        return string.Join(
            Environment.NewLine,
            "DataAgent graph shadow comparison",
            $"graph_shadow_status={comparison.ReasonCode}",
            $"default_result_changed={LowerBool(comparison.DefaultResultChanged)}",
            $"deterministic_status={comparison.DeterministicStatus}",
            $"sidecar_status={comparison.SidecarStatus}",
            $"deterministic_reason={SafeToken(comparison.DeterministicReasonCode)}",
            $"sidecar_reason={SafeToken(comparison.SidecarReasonCode)}",
            $"deterministic_fallback_required={LowerBool(comparison.DeterministicFallbackRequired)}",
            $"sidecar_fallback_required={LowerBool(comparison.SidecarFallbackRequired)}");
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "reason_missing";

        string trimmed = value.Trim();
        if (trimmed.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength)
            return "reason_redacted";

        foreach (char ch in trimmed)
        {
            if (ch is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-'
                or '.')
            {
                continue;
            }

            return "reason_redacted";
        }

        return trimmed;
    }
}

public static class DataAgentGraphHandshakeShadowComparisonReportFormatter
{
    public static string FormatMarkdown(DataAgentGraphHandshakeShadowComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<string> lines =
        [
            $"# DataAgent Graph Shadow Comparison: {SafeToken(report.ReplayId)}",
            "",
            "## Summary",
            "shadow_only=true",
            $"default_result_changed={LowerBool(report.DefaultResultChanged)}",
            "replay_parity_required=true",
            $"comparison_count={report.ComparisonCount}",
            $"passed={LowerBool(report.Passed)}",
            "",
            "## Categories"
        ];

        foreach ((string status, int count) in report.StatusCounts.OrderBy(item => item.Key, StringComparer.Ordinal))
            lines.Add($"{SafeToken(status)}={count}");

        lines.Add("");
        lines.Add("## Authority Boundary");
        lines.Add("no_sql_authority=true");
        lines.Add("no_checkpoint_mutation=true");
        lines.Add("no_visible_text=true");
        lines.Add("fallback_required=true");

        return string.Join(Environment.NewLine, lines);
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        string trimmed = value.Trim();
        if (trimmed.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(trimmed))
        {
            return "redacted";
        }

        foreach (char current in trimmed)
        {
            if (current is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-'
                or '.')
            {
                continue;
            }

            return "redacted";
        }

        return trimmed;
    }
}
