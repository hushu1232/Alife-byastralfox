namespace Alife.Function.DataAgent;

public sealed record DataAgentRealLangGraphManualShadowContextLayer(
    string Name,
    string Text);

public sealed record DataAgentRealLangGraphManualShadowInput(
    string SourceReplayId,
    bool OperatorStartedRuntime,
    bool LoopbackOnly,
    bool RuntimeStartedByAlife,
    bool DependenciesInstalledByAlife,
    bool SidecarCalledByAlife,
    IReadOnlyList<DataAgentRealLangGraphManualShadowContextLayer> ContextLayers,
    DataAgentLangGraphManualShadowResult? ManualShadowResult,
    DataAgentHarnessReplayDiffGateResult? DiffGateResult);

public sealed record DataAgentRealLangGraphManualShadowResult(
    bool Accepted,
    string ReasonCode,
    string SourceBaseline,
    string SourceReplayId,
    int ContextLayerCount,
    bool ManualOnly,
    bool OperatorStartedRuntime,
    bool LoopbackOnly,
    bool AgentAdvisoryOnly,
    bool HarnessExecutionAuthority,
    bool CSharpValidationAuthority,
    bool DefaultResultChanged,
    bool FallbackRequired,
    bool OperatorRequired,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool CallsSidecar,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext,
    IReadOnlyList<string> ReasonCodes);

public static class DataAgentRealLangGraphManualShadowIntegration
{
    const string SourceBaseline = "v3.28";

    public static DataAgentRealLangGraphManualShadowResult Evaluate(DataAgentRealLangGraphManualShadowInput? input)
    {
        if (input is null)
        {
            return Build(
                accepted: false,
                reasonCode: "real_langgraph_manual_runtime_unavailable",
                sourceReplayId: "redacted",
                contextLayerCount: 0,
                operatorStartedRuntime: false,
                loopbackOnly: false,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: ["real_langgraph_manual_runtime_unavailable"]);
        }

        if (ContainsUnsafeContext(input.ContextLayers))
        {
            return Build(
                accepted: false,
                reasonCode: "real_langgraph_manual_shadow_unsafe_context",
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: input.ContextLayers.Count,
                operatorStartedRuntime: input.OperatorStartedRuntime,
                loopbackOnly: input.LoopbackOnly,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: ["real_langgraph_manual_shadow_unsafe_context"]);
        }

        if (input.OperatorStartedRuntime == false ||
            input.ManualShadowResult is null ||
            input.DiffGateResult is null)
        {
            return Build(
                accepted: false,
                reasonCode: "real_langgraph_manual_runtime_unavailable",
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: input.ContextLayers.Count,
                operatorStartedRuntime: input.OperatorStartedRuntime,
                loopbackOnly: input.LoopbackOnly,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: ReasonCodes("real_langgraph_manual_runtime_unavailable", input.ManualShadowResult, input.DiffGateResult));
        }

        if (input.LoopbackOnly == false ||
            input.RuntimeStartedByAlife ||
            input.DependenciesInstalledByAlife ||
            input.SidecarCalledByAlife ||
            input.ManualShadowResult.StartsRuntime ||
            input.ManualShadowResult.InstallsDependencies ||
            input.ManualShadowResult.CallsSidecar ||
            input.DiffGateResult.StartsRuntime ||
            input.DiffGateResult.InstallsDependencies ||
            input.DiffGateResult.CallsSidecar)
        {
            return Build(
                accepted: false,
                reasonCode: "real_langgraph_manual_shadow_boundary_violation",
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: input.ContextLayers.Count,
                operatorStartedRuntime: input.OperatorStartedRuntime,
                loopbackOnly: input.LoopbackOnly,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: ReasonCodes("real_langgraph_manual_shadow_boundary_violation", input.ManualShadowResult, input.DiffGateResult));
        }

        if (input.ManualShadowResult.Accepted == false)
        {
            return Build(
                accepted: false,
                reasonCode: input.ManualShadowResult.ReasonCode,
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: input.ContextLayers.Count,
                operatorStartedRuntime: input.OperatorStartedRuntime,
                loopbackOnly: input.LoopbackOnly,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: ReasonCodes(input.ManualShadowResult.ReasonCode, input.ManualShadowResult, input.DiffGateResult));
        }

        if (input.DiffGateResult.GatePassed == false)
        {
            return Build(
                accepted: false,
                reasonCode: input.DiffGateResult.ReasonCode,
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: input.ContextLayers.Count,
                operatorStartedRuntime: input.OperatorStartedRuntime,
                loopbackOnly: input.LoopbackOnly,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: ReasonCodes(input.DiffGateResult.ReasonCode, input.ManualShadowResult, input.DiffGateResult));
        }

        return Build(
            accepted: true,
            reasonCode: "real_langgraph_manual_shadow_integration_accepted",
            sourceReplayId: input.SourceReplayId,
            contextLayerCount: input.ContextLayers.Count,
            operatorStartedRuntime: input.OperatorStartedRuntime,
            loopbackOnly: input.LoopbackOnly,
            fallbackRequired: false,
            operatorRequired: false,
            reasonCodes: ReasonCodes("real_langgraph_manual_shadow_integration_accepted", input.ManualShadowResult, input.DiffGateResult));
    }

    static DataAgentRealLangGraphManualShadowResult Build(
        bool accepted,
        string reasonCode,
        string sourceReplayId,
        int contextLayerCount,
        bool operatorStartedRuntime,
        bool loopbackOnly,
        bool fallbackRequired,
        bool operatorRequired,
        IReadOnlyList<string> reasonCodes)
    {
        return new DataAgentRealLangGraphManualShadowResult(
            accepted,
            reasonCode,
            SourceBaseline,
            sourceReplayId,
            contextLayerCount,
            ManualOnly: true,
            operatorStartedRuntime,
            loopbackOnly,
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false,
            fallbackRequired,
            operatorRequired,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            reasonCodes);
    }

    static bool ContainsUnsafeContext(IReadOnlyList<DataAgentRealLangGraphManualShadowContextLayer> contextLayers)
    {
        foreach (DataAgentRealLangGraphManualShadowContextLayer layer in contextLayers)
        {
            if (DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(layer.Name) ||
                DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(layer.Text))
            {
                return true;
            }
        }

        return false;
    }

    static IReadOnlyList<string> ReasonCodes(
        string primaryReasonCode,
        DataAgentLangGraphManualShadowResult? manualShadowResult,
        DataAgentHarnessReplayDiffGateResult? diffGateResult)
    {
        List<string> reasonCodes = [primaryReasonCode];
        AddReasonCode(reasonCodes, manualShadowResult?.ReasonCode);
        AddReasonCode(reasonCodes, diffGateResult?.ReasonCode);
        return reasonCodes;
    }

    static void AddReasonCode(List<string> reasonCodes, string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) ||
            reasonCodes.Contains(reasonCode, StringComparer.Ordinal))
        {
            return;
        }

        reasonCodes.Add(reasonCode);
    }
}

public static class DataAgentRealLangGraphManualShadowFormatter
{
    public static string Format(DataAgentRealLangGraphManualShadowResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Join(
            Environment.NewLine,
            "real_langgraph_manual_shadow_integration=true",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason_code={SafeToken(result.ReasonCode)}",
            $"source_baseline={SafeToken(result.SourceBaseline)}",
            $"source_replay_id={SafeToken(result.SourceReplayId)}",
            $"context_layer_count={result.ContextLayerCount}",
            $"manual_only={LowerBool(result.ManualOnly)}",
            $"operator_started_runtime={LowerBool(result.OperatorStartedRuntime)}",
            $"loopback_only={LowerBool(result.LoopbackOnly)}",
            $"agent_advisory_only={LowerBool(result.AgentAdvisoryOnly)}",
            $"harness_execution_authority={LowerBool(result.HarnessExecutionAuthority)}",
            $"csharp_validation_authority={LowerBool(result.CSharpValidationAuthority)}",
            $"default_result_changed={LowerBool(result.DefaultResultChanged)}",
            $"fallback_required={LowerBool(result.FallbackRequired)}",
            $"operator_required={LowerBool(result.OperatorRequired)}",
            $"starts_runtime={LowerBool(result.StartsRuntime)}",
            $"installs_dependencies={LowerBool(result.InstallsDependencies)}",
            $"calls_sidecar={LowerBool(result.CallsSidecar)}",
            $"stores_secrets={LowerBool(result.StoresSecrets)}",
            $"stores_sql={LowerBool(result.StoresSql)}",
            $"stores_hidden_context={LowerBool(result.StoresHiddenContext)}",
            $"reason_codes={SafeReasonCodes(result.ReasonCodes)}",
            string.Empty);
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafeReasonCodes(IReadOnlyList<string> reasonCodes)
    {
        if (reasonCodes.Count == 0)
            return "redacted";

        List<string> safeReasonCodes = [];
        foreach (string reasonCode in reasonCodes)
            safeReasonCodes.Add(SafeToken(reasonCode));

        return string.Join(",", safeReasonCodes);
    }

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
