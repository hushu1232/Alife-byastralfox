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
