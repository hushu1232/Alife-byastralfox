using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public enum DataAgentV42OperatorEvidenceStatus
{
    Accepted,
    Rejected,
    Fallback
}

public static class DataAgentV42AdvisoryKinds
{
    public const string DiagnosticSummary = "diagnostic_summary";
    public const string PlannerNote = "planner_note";
    public const string DiffReason = "diff_reason";
    public const string NextStep = "next_step";

    public static bool Contains(string? value) =>
        value is DiagnosticSummary or PlannerNote or DiffReason or NextStep;
}

public sealed record DataAgentV42OperatorEvidenceInput(
    DataAgentRealLangGraphManualShadowResult? IntegrationResult,
    DataAgentRealLangGraphManualShadowContextEnvelope? ContextEnvelope,
    string AdvisoryKind,
    string SafeSummary,
    IReadOnlyList<string> EvidenceRefs);

public sealed record DataAgentV42OperatorEvidencePacket(
    bool Accepted,
    string ReasonCode,
    string ContractVersion,
    string SourceBaseline,
    DataAgentV42OperatorEvidenceStatus Status,
    string AdvisoryKind,
    bool ContextBudgetPassed,
    bool ContractValidationPassed,
    bool ReplayDiffGatePassed,
    bool FallbackRequired,
    bool OperatorRequired,
    bool DefaultResultChanged,
    bool AgentAdvisoryOnly,
    bool CSharpValidationAuthority,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> EvidenceRefs,
    string SafeSummary);

public static class DataAgentV42OperatorEvidencePacketBuilder
{
    public const int MaxSummaryChars = 320;
    public const int MaxEvidenceRefs = 8;

    static readonly Regex SafeTokenPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex SafeReferencePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex AbsolutePathPattern = new(
        @"[A-Za-z]:[\\/]|(?:^|\s)/(?:Users|home|var|tmp|etc)/|\\",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DataAgentV42OperatorEvidencePacket Build(DataAgentV42OperatorEvidenceInput? input)
    {
        if (input?.IntegrationResult is null || input.ContextEnvelope is null)
            return Rejected("v4_2_operator_evidence_input_missing");

        DataAgentRealLangGraphManualShadowResult integration = input.IntegrationResult;
        DataAgentRealLangGraphManualShadowContextEnvelope envelope = input.ContextEnvelope;
        bool replayPassed = integration.ReasonCodes?.Contains(
            "harness_replay_diff_gate_passed",
            StringComparer.Ordinal) == true;

        if (IsSafeMetadata(input, integration, envelope) == false)
            return Rejected("v4_2_operator_evidence_unsafe_input");

        if (envelope.Accepted == false)
        {
            return BuildPacket(
                DataAgentV42OperatorEvidenceStatus.Rejected,
                "v4_2_operator_evidence_context_rejected",
                input,
                integration,
                envelope,
                replayPassed,
                includeAdvisory: false);
        }

        if (integration.Accepted)
        {
            return BuildPacket(
                DataAgentV42OperatorEvidenceStatus.Accepted,
                "v4_2_operator_evidence_accepted",
                input,
                integration,
                envelope,
                replayPassed,
                includeAdvisory: true);
        }

        DataAgentV42OperatorEvidenceStatus status = IsFallback(integration.ReasonCode)
            ? DataAgentV42OperatorEvidenceStatus.Fallback
            : DataAgentV42OperatorEvidenceStatus.Rejected;
        string reasonCode = status == DataAgentV42OperatorEvidenceStatus.Fallback
            ? "v4_2_operator_evidence_fallback"
            : "v4_2_operator_evidence_contract_rejected";

        return BuildPacket(status, reasonCode, input, integration, envelope, replayPassed, includeAdvisory: false);
    }

    static DataAgentV42OperatorEvidencePacket BuildPacket(
        DataAgentV42OperatorEvidenceStatus status,
        string reasonCode,
        DataAgentV42OperatorEvidenceInput input,
        DataAgentRealLangGraphManualShadowResult integration,
        DataAgentRealLangGraphManualShadowContextEnvelope envelope,
        bool replayPassed,
        bool includeAdvisory)
    {
        bool accepted = status == DataAgentV42OperatorEvidenceStatus.Accepted;
        return new DataAgentV42OperatorEvidencePacket(
            accepted,
            reasonCode,
            ContractVersion: "v4.2",
            SourceBaseline: "v4.1",
            status,
            input.AdvisoryKind,
            ContextBudgetPassed: envelope.Accepted,
            ContractValidationPassed: integration.Accepted,
            ReplayDiffGatePassed: replayPassed,
            FallbackRequired: accepted == false,
            OperatorRequired: accepted == false || integration.OperatorRequired,
            DefaultResultChanged: false,
            AgentAdvisoryOnly: true,
            CSharpValidationAuthority: true,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: CombineReasonCodes(reasonCode, integration.ReasonCodes, envelope.ReasonCodes),
            EvidenceRefs: includeAdvisory ? input.EvidenceRefs.ToArray() : [],
            SafeSummary: includeAdvisory ? input.SafeSummary.Trim() : string.Empty);
    }

    static DataAgentV42OperatorEvidencePacket Rejected(string reasonCode) =>
        new(
            Accepted: false,
            reasonCode,
            ContractVersion: "v4.2",
            SourceBaseline: "v4.1",
            Status: DataAgentV42OperatorEvidenceStatus.Rejected,
            AdvisoryKind: "redacted",
            ContextBudgetPassed: false,
            ContractValidationPassed: false,
            ReplayDiffGatePassed: false,
            FallbackRequired: true,
            OperatorRequired: true,
            DefaultResultChanged: false,
            AgentAdvisoryOnly: true,
            CSharpValidationAuthority: true,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: [reasonCode],
            EvidenceRefs: [],
            SafeSummary: string.Empty);

    static bool IsSafeMetadata(
        DataAgentV42OperatorEvidenceInput input,
        DataAgentRealLangGraphManualShadowResult integration,
        DataAgentRealLangGraphManualShadowContextEnvelope envelope)
    {
        if (DataAgentV42AdvisoryKinds.Contains(input.AdvisoryKind) == false ||
            string.IsNullOrWhiteSpace(input.SafeSummary) ||
            input.SafeSummary.Length > MaxSummaryChars ||
            ContainsUnsafe(input.SafeSummary) ||
            input.EvidenceRefs is null ||
            input.EvidenceRefs.Count > MaxEvidenceRefs)
        {
            return false;
        }

        foreach (string evidenceRef in input.EvidenceRefs)
        {
            if (string.IsNullOrWhiteSpace(evidenceRef) ||
                SafeReferencePattern.IsMatch(evidenceRef) == false ||
                ContainsUnsafe(evidenceRef))
            {
                return false;
            }
        }

        return ReasonCodesSafe(integration.ReasonCodes) &&
               ReasonCodesSafe(envelope.ReasonCodes) &&
               SafeReasonCode(integration.ReasonCode) &&
               SafeReasonCode(envelope.ReasonCode);
    }

    static bool ReasonCodesSafe(IReadOnlyList<string>? reasonCodes) =>
        reasonCodes is not null && reasonCodes.All(SafeReasonCode);

    static bool SafeReasonCode(string? value) =>
        string.IsNullOrWhiteSpace(value) == false &&
        SafeTokenPattern.IsMatch(value) &&
        ContainsUnsafe(value) == false;

    static bool ContainsUnsafe(string? value) =>
        string.IsNullOrWhiteSpace(value) == false &&
        (DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) ||
         AbsolutePathPattern.IsMatch(value) ||
         value.Any(char.IsControl));

    static IReadOnlyList<string> CombineReasonCodes(
        string primary,
        IReadOnlyList<string>? integration,
        IReadOnlyList<string>? envelope)
    {
        List<string> result = [primary];
        AddRange(result, integration);
        AddRange(result, envelope);
        return result;
    }

    static void AddRange(List<string> target, IReadOnlyList<string>? source)
    {
        if (source is null)
            return;

        foreach (string value in source)
        {
            if (target.Contains(value, StringComparer.Ordinal) == false)
                target.Add(value);
        }
    }

    static bool IsFallback(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            return false;

        return reasonCode.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
               reasonCode.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               reasonCode.Contains("transport", StringComparison.OrdinalIgnoreCase) ||
               reasonCode.Contains("circuit_open", StringComparison.OrdinalIgnoreCase);
    }
}

public static class DataAgentV42OperatorEvidencePacketFormatter
{
    public static string Format(DataAgentV42OperatorEvidencePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return string.Join(
            Environment.NewLine,
            "operator_evidence_packet=v4.2",
            $"source_baseline={SafeToken(packet.SourceBaseline)}",
            $"accepted={LowerBool(packet.Accepted)}",
            $"reason_code={SafeToken(packet.ReasonCode)}",
            $"status={packet.Status.ToString().ToLowerInvariant()}",
            $"advisory_kind={SafeToken(packet.AdvisoryKind)}",
            $"context_budget_passed={LowerBool(packet.ContextBudgetPassed)}",
            $"contract_validation_passed={LowerBool(packet.ContractValidationPassed)}",
            $"replay_diff_gate_passed={LowerBool(packet.ReplayDiffGatePassed)}",
            $"fallback_required={LowerBool(packet.FallbackRequired)}",
            $"operator_required={LowerBool(packet.OperatorRequired)}",
            $"default_result_changed={LowerBool(packet.DefaultResultChanged)}",
            $"agent_advisory_only={LowerBool(packet.AgentAdvisoryOnly)}",
            $"csharp_validation_authority={LowerBool(packet.CSharpValidationAuthority)}",
            $"reason_codes={SafeList(packet.ReasonCodes)}",
            $"evidence_refs={SafeList(packet.EvidenceRefs, allowColon: true)}",
            $"safe_summary={SafeSummary(packet.SafeSummary)}",
            $"stores_secrets={LowerBool(packet.StoresSecrets)}",
            $"stores_sql={LowerBool(packet.StoresSql)}",
            $"stores_hidden_context={LowerBool(packet.StoresHiddenContext)}");
    }

    static string SafeList(IReadOnlyList<string>? values, bool allowColon = false)
    {
        if (values is null || values.Count == 0)
            return "none";

        return string.Join(",", values.Select(value => SafeToken(value, allowColon)));
    }

    static string SafeSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > DataAgentV42OperatorEvidencePacketBuilder.MaxSummaryChars ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) ||
            value.Any(char.IsControl))
        {
            return "redacted";
        }

        return value.Trim();
    }

    static string SafeToken(string? value, bool allowColon = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        string pattern = allowColon
            ? "^[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}$"
            : "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$";
        return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant) &&
               DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) == false
            ? value
            : "redacted";
    }

    static string LowerBool(bool value) => value ? "true" : "false";
}
