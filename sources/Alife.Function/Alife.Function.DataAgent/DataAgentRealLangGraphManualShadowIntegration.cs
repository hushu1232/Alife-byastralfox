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

        IReadOnlyList<DataAgentRealLangGraphManualShadowContextLayer>? inputContextLayers = input.ContextLayers;
        if (inputContextLayers is null)
        {
            return Build(
                accepted: false,
                reasonCode: "real_langgraph_manual_shadow_context_missing",
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: 0,
                operatorStartedRuntime: input.OperatorStartedRuntime,
                loopbackOnly: input.LoopbackOnly,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: ["real_langgraph_manual_shadow_context_missing"]);
        }

        IReadOnlyList<DataAgentRealLangGraphManualShadowContextLayer> contextLayers = inputContextLayers;
        if (ContainsUnsafeContext(contextLayers))
        {
            return Build(
                accepted: false,
                reasonCode: "real_langgraph_manual_shadow_unsafe_context",
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: contextLayers.Count,
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
                contextLayerCount: contextLayers.Count,
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
            input.ManualShadowResult.StoresSecrets ||
            input.ManualShadowResult.StoresSql ||
            input.ManualShadowResult.StoresHiddenContext ||
            input.ManualShadowResult.DefaultResultChanged ||
            input.ManualShadowResult.ManualShadowOnly == false ||
            input.DiffGateResult.StartsRuntime ||
            input.DiffGateResult.InstallsDependencies ||
            input.DiffGateResult.CallsSidecar ||
            input.DiffGateResult.StoresSecrets ||
            input.DiffGateResult.StoresSql ||
            input.DiffGateResult.StoresHiddenContext ||
            input.DiffGateResult.DefaultResultChanged ||
            input.DiffGateResult.AgentAdvisoryOnly == false ||
            input.DiffGateResult.HarnessExecutionAuthority == false ||
            input.DiffGateResult.CSharpValidationAuthority == false)
        {
            return Build(
                accepted: false,
                reasonCode: "real_langgraph_manual_shadow_boundary_violation",
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: contextLayers.Count,
                operatorStartedRuntime: input.OperatorStartedRuntime,
                loopbackOnly: input.LoopbackOnly,
                fallbackRequired: true,
                operatorRequired: true,
                reasonCodes: BoundaryReasonCodes("real_langgraph_manual_shadow_boundary_violation", input));
        }

        if (input.ManualShadowResult.Accepted == false)
        {
            return Build(
                accepted: false,
                reasonCode: input.ManualShadowResult.ReasonCode,
                sourceReplayId: input.SourceReplayId,
                contextLayerCount: contextLayers.Count,
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
                contextLayerCount: contextLayers.Count,
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
            contextLayerCount: contextLayers.Count,
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

    static IReadOnlyList<string> BoundaryReasonCodes(
        string primaryReasonCode,
        DataAgentRealLangGraphManualShadowInput input)
    {
        List<string> reasonCodes = new(ReasonCodes(primaryReasonCode, input.ManualShadowResult, input.DiffGateResult));

        if (input.LoopbackOnly == false)
            AddReasonCode(reasonCodes, "real_langgraph_manual_shadow_loopback_missing");
        if (input.RuntimeStartedByAlife)
            AddReasonCode(reasonCodes, "real_langgraph_manual_shadow_runtime_started_by_alife_violation");
        if (input.DependenciesInstalledByAlife)
            AddReasonCode(reasonCodes, "real_langgraph_manual_shadow_dependencies_installed_by_alife_violation");
        if (input.SidecarCalledByAlife)
            AddReasonCode(reasonCodes, "real_langgraph_manual_shadow_sidecar_called_by_alife_violation");

        DataAgentLangGraphManualShadowResult? manualShadowResult = input.ManualShadowResult;
        if (manualShadowResult is not null)
        {
            if (manualShadowResult.StartsRuntime)
                AddReasonCode(reasonCodes, "manual_shadow_starts_runtime_violation");
            if (manualShadowResult.InstallsDependencies)
                AddReasonCode(reasonCodes, "manual_shadow_installs_dependencies_violation");
            if (manualShadowResult.CallsSidecar)
                AddReasonCode(reasonCodes, "manual_shadow_calls_sidecar_violation");
            if (manualShadowResult.StoresSecrets)
                AddReasonCode(reasonCodes, "manual_shadow_stores_secrets_violation");
            if (manualShadowResult.StoresSql)
                AddReasonCode(reasonCodes, "manual_shadow_stores_sql_violation");
            if (manualShadowResult.StoresHiddenContext)
                AddReasonCode(reasonCodes, "manual_shadow_stores_hidden_context_violation");
            if (manualShadowResult.DefaultResultChanged)
                AddReasonCode(reasonCodes, "manual_shadow_default_result_changed_violation");
            if (manualShadowResult.ManualShadowOnly == false)
                AddReasonCode(reasonCodes, "manual_shadow_only_missing");
        }

        DataAgentHarnessReplayDiffGateResult? diffGateResult = input.DiffGateResult;
        if (diffGateResult is not null)
        {
            if (diffGateResult.StartsRuntime)
                AddReasonCode(reasonCodes, "diff_gate_starts_runtime_violation");
            if (diffGateResult.InstallsDependencies)
                AddReasonCode(reasonCodes, "diff_gate_installs_dependencies_violation");
            if (diffGateResult.CallsSidecar)
                AddReasonCode(reasonCodes, "diff_gate_calls_sidecar_violation");
            if (diffGateResult.StoresSecrets)
                AddReasonCode(reasonCodes, "diff_gate_stores_secrets_violation");
            if (diffGateResult.StoresSql)
                AddReasonCode(reasonCodes, "diff_gate_stores_sql_violation");
            if (diffGateResult.StoresHiddenContext)
                AddReasonCode(reasonCodes, "diff_gate_stores_hidden_context_violation");
            if (diffGateResult.DefaultResultChanged)
                AddReasonCode(reasonCodes, "diff_gate_default_result_changed_violation");
            if (diffGateResult.AgentAdvisoryOnly == false)
                AddReasonCode(reasonCodes, "diff_gate_agent_advisory_only_missing");
            if (diffGateResult.HarnessExecutionAuthority == false)
                AddReasonCode(reasonCodes, "diff_gate_harness_execution_authority_missing");
            if (diffGateResult.CSharpValidationAuthority == false)
                AddReasonCode(reasonCodes, "diff_gate_csharp_validation_authority_missing");
        }

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

    static string SafeReasonCodes(IReadOnlyList<string>? reasonCodes)
    {
        if (reasonCodes is null || reasonCodes.Count == 0)
            return "redacted";

        List<string> safeReasonCodes = [];
        foreach (string? reasonCode in reasonCodes)
            safeReasonCodes.Add(SafeToken(reasonCode));

        return string.Join(",", safeReasonCodes);
    }

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        string trimmed = value.Trim();
        if (trimmed.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(trimmed) ||
            ContainsUnsafeTokenFragment(trimmed))
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

    static bool ContainsUnsafeTokenFragment(string value)
    {
        if (value.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("connection_string", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] tokenParts = value.Split(['_', '-', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string tokenPart in tokenParts)
        {
            if (IsUnsafeSqlCommandToken(tokenPart))
                return true;
        }

        return false;
    }

    static bool IsUnsafeSqlCommandToken(string value)
    {
        return string.Equals(value, "select", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "insert", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "update", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "delete", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "drop", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "alter", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "create", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "truncate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "execute", StringComparison.OrdinalIgnoreCase);
    }
}
