namespace Alife.Function.DataAgent;

public sealed record DataAgentHarnessReplayDiffGateInput(
    DataAgentGraphHandshakeReplayReport? ReplayReport,
    DataAgentLangGraphManualShadowResult? ManualShadowAdvisory);

public sealed record DataAgentHarnessReplayDiffGateResult(
    bool GatePassed,
    string ReasonCode,
    string ReplayId,
    string AdvisoryReasonCode,
    bool ReplayEvidenceMatched,
    bool AdvisoryReasonMatched,
    bool FallbackRequired,
    bool OperatorRequired,
    bool AgentAdvisoryOnly,
    bool HarnessExecutionAuthority,
    bool CSharpValidationAuthority,
    bool GateOnly,
    bool OperatorDecides,
    bool DefaultResultChanged,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool CallsSidecar,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext);

public static class DataAgentHarnessReplayDiffGate
{
    public static DataAgentHarnessReplayDiffGateResult Evaluate(DataAgentHarnessReplayDiffGateInput? input)
    {
        if (input?.ReplayReport is null || input.ManualShadowAdvisory is null)
        {
            return Build(
                gatePassed: false,
                reasonCode: "harness_replay_diff_gate_input_missing",
                replayId: "redacted",
                advisoryReasonCode: "redacted",
                replayEvidenceMatched: false,
                advisoryReasonMatched: false,
                fallbackRequired: true,
                operatorRequired: true);
        }

        DataAgentGraphHandshakeReplayReport report = input.ReplayReport;
        DataAgentLangGraphManualShadowResult advisory = input.ManualShadowAdvisory;

        if (report.DefaultResultChanged)
        {
            return Build(
                gatePassed: false,
                reasonCode: "harness_replay_default_result_changed",
                replayId: report.ReplayId,
                advisoryReasonCode: advisory.ReasonCode,
                replayEvidenceMatched: report.ComparisonCount > 0,
                advisoryReasonMatched: false,
                fallbackRequired: true,
                operatorRequired: true);
        }

        if (advisory.Accepted == false)
        {
            return Build(
                gatePassed: false,
                reasonCode: advisory.ReasonCode,
                replayId: report.ReplayId,
                advisoryReasonCode: advisory.ReasonCode,
                replayEvidenceMatched: report.ComparisonCount > 0,
                advisoryReasonMatched: false,
                fallbackRequired: true,
                operatorRequired: true);
        }

        string advisoryReasonCode = advisory.Advisory?.ReasonCode ?? advisory.ReasonCode;
        bool replayEvidenceMatched = report.ComparisonCount > 0;
        bool advisoryReasonMatched = report.StatusCounts.ContainsKey(advisoryReasonCode);

        if (replayEvidenceMatched == false || advisoryReasonMatched == false)
        {
            return Build(
                gatePassed: false,
                reasonCode: "harness_replay_diff_reason_mismatch",
                replayId: report.ReplayId,
                advisoryReasonCode,
                replayEvidenceMatched,
                advisoryReasonMatched,
                fallbackRequired: true,
                operatorRequired: true);
        }

        return Build(
            gatePassed: true,
            reasonCode: "harness_replay_diff_gate_passed",
            replayId: report.ReplayId,
            advisoryReasonCode,
            replayEvidenceMatched: true,
            advisoryReasonMatched: true,
            fallbackRequired: false,
            operatorRequired: false);
    }

    static DataAgentHarnessReplayDiffGateResult Build(
        bool gatePassed,
        string reasonCode,
        string replayId,
        string advisoryReasonCode,
        bool replayEvidenceMatched,
        bool advisoryReasonMatched,
        bool fallbackRequired,
        bool operatorRequired)
    {
        return new DataAgentHarnessReplayDiffGateResult(
            gatePassed,
            reasonCode,
            replayId,
            advisoryReasonCode,
            replayEvidenceMatched,
            advisoryReasonMatched,
            fallbackRequired,
            operatorRequired,
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            GateOnly: true,
            OperatorDecides: true,
            DefaultResultChanged: false,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }
}

public static class DataAgentHarnessReplayDiffGateFormatter
{
    public static string Format(DataAgentHarnessReplayDiffGateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Join(
            Environment.NewLine,
            "harness_replay_diff_gate=true",
            "agent_advisory_contract=v3.24",
            "real_langgraph_manual_shadow_provider=true",
            $"gate_passed={LowerBool(result.GatePassed)}",
            $"reason_code={SafeToken(result.ReasonCode)}",
            $"replay_id={SafeToken(result.ReplayId)}",
            $"advisory_reason_code={SafeToken(result.AdvisoryReasonCode)}",
            $"replay_evidence_matched={LowerBool(result.ReplayEvidenceMatched)}",
            $"advisory_reason_matched={LowerBool(result.AdvisoryReasonMatched)}",
            $"fallback_required={LowerBool(result.FallbackRequired)}",
            $"operator_required={LowerBool(result.OperatorRequired)}",
            $"harness_execution_authority={LowerBool(result.HarnessExecutionAuthority)}",
            $"csharp_validation_authority={LowerBool(result.CSharpValidationAuthority)}",
            $"agent_advisory_only={LowerBool(result.AgentAdvisoryOnly)}",
            $"gate_only={LowerBool(result.GateOnly)}",
            $"operator_decides={LowerBool(result.OperatorDecides)}",
            $"default_result_changed={LowerBool(result.DefaultResultChanged)}",
            $"starts_runtime={LowerBool(result.StartsRuntime)}",
            $"installs_dependencies={LowerBool(result.InstallsDependencies)}",
            $"calls_sidecar={LowerBool(result.CallsSidecar)}",
            $"stores_secrets={LowerBool(result.StoresSecrets)}",
            $"stores_sql={LowerBool(result.StoresSql)}",
            $"stores_hidden_context={LowerBool(result.StoresHiddenContext)}",
            string.Empty);
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
