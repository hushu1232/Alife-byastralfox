namespace Alife.Function.DataAgent;

public sealed record DataAgentOperatorEvidencePack(
    string PackId,
    string ReplayId,
    int ComparisonCount,
    int EvidenceItemCount,
    string ReplayReportArtifactPath,
    string ArtifactIndexPath,
    string ManualAuditBundlePath,
    bool GatePassed,
    string GateReasonCode,
    bool AdvisoryAccepted,
    string AdvisoryReasonCode,
    bool FallbackRequired,
    bool OperatorRequired,
    bool DefaultResultChanged,
    bool ManualOnly,
    bool AgentAdvisoryOnly,
    bool HarnessExecutionAuthority,
    bool CSharpValidationAuthority,
    bool OperatorDecides,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool CallsSidecar,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext);

public static class DataAgentOperatorEvidencePackBuilder
{
    public const string PackId = "v3.27-operator-evidence-pack";

    public static DataAgentOperatorEvidencePack Build(
        DataAgentGraphHandshakeReplayReport report,
        DataAgentGraphHandshakeReplayReportArtifact artifact,
        DataAgentGraphHandshakeReplayReportArtifactIndex index,
        DataAgentGraphHandshakeManualAuditBundle bundle,
        DataAgentLangGraphManualShadowResult manualShadowAdvisory,
        DataAgentHarnessReplayDiffGateResult diffGate)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(manualShadowAdvisory);
        ArgumentNullException.ThrowIfNull(diffGate);

        bool manualOnly =
            artifact.ManualOnly &&
            index.ManualOnly &&
            bundle.ManualOnly &&
            manualShadowAdvisory.ManualShadowOnly;
        bool defaultResultChanged =
            report.DefaultResultChanged ||
            artifact.DefaultResultChanged ||
            index.DefaultResultChanged ||
            bundle.DefaultResultChanged ||
            manualShadowAdvisory.DefaultResultChanged ||
            diffGate.DefaultResultChanged;
        bool startsRuntime =
            artifact.StartsRuntime ||
            index.StartsRuntime ||
            bundle.StartsRuntime ||
            manualShadowAdvisory.StartsRuntime ||
            diffGate.StartsRuntime;
        bool installsDependencies =
            artifact.InstallsDependencies ||
            index.InstallsDependencies ||
            bundle.InstallsDependencies ||
            manualShadowAdvisory.InstallsDependencies ||
            diffGate.InstallsDependencies;
        bool callsSidecar =
            manualShadowAdvisory.CallsSidecar ||
            diffGate.CallsSidecar;
        bool storesSecrets =
            artifact.StoresSecrets ||
            index.StoresSecrets ||
            bundle.StoresSecrets ||
            manualShadowAdvisory.StoresSecrets ||
            diffGate.StoresSecrets;
        bool storesSql =
            artifact.StoresSql ||
            index.StoresSql ||
            bundle.StoresSql ||
            manualShadowAdvisory.StoresSql ||
            diffGate.StoresSql;
        bool storesHiddenContext =
            artifact.StoresHiddenContext ||
            index.StoresHiddenContext ||
            bundle.StoresHiddenContext ||
            manualShadowAdvisory.StoresHiddenContext ||
            diffGate.StoresHiddenContext;

        return new DataAgentOperatorEvidencePack(
            PackId,
            report.ReplayId,
            report.ComparisonCount,
            bundle.EvidenceItemCount,
            artifact.Path,
            index.Path,
            bundle.Path,
            diffGate.GatePassed,
            diffGate.ReasonCode,
            manualShadowAdvisory.Accepted,
            manualShadowAdvisory.Advisory?.ReasonCode ?? manualShadowAdvisory.ReasonCode,
            diffGate.FallbackRequired,
            diffGate.OperatorRequired,
            defaultResultChanged,
            manualOnly,
            diffGate.AgentAdvisoryOnly,
            diffGate.HarnessExecutionAuthority,
            diffGate.CSharpValidationAuthority,
            diffGate.OperatorDecides,
            startsRuntime,
            installsDependencies,
            callsSidecar,
            storesSecrets,
            storesSql,
            storesHiddenContext);
    }
}

public static class DataAgentOperatorEvidencePackFormatter
{
    public static string Format(DataAgentOperatorEvidencePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        return string.Join(
            Environment.NewLine,
            "operator_evidence_pack=true",
            "source_versions=v3.18-v3.26",
            "manual_audit_bundle=true",
            "agent_advisory_contract=v3.24",
            "real_langgraph_manual_shadow_provider=true",
            "harness_replay_diff_gate=true",
            $"pack_id={SafeToken(pack.PackId)}",
            $"replay_id={SafeToken(pack.ReplayId)}",
            $"comparison_count={pack.ComparisonCount}",
            $"evidence_item_count={pack.EvidenceItemCount}",
            $"replay_report_artifact_path={SafePathToken(pack.ReplayReportArtifactPath)}",
            $"artifact_index_path={SafePathToken(pack.ArtifactIndexPath)}",
            $"manual_audit_bundle_path={SafePathToken(pack.ManualAuditBundlePath)}",
            $"gate_passed={LowerBool(pack.GatePassed)}",
            $"gate_reason_code={SafeToken(pack.GateReasonCode)}",
            $"advisory_accepted={LowerBool(pack.AdvisoryAccepted)}",
            $"advisory_reason_code={SafeToken(pack.AdvisoryReasonCode)}",
            $"fallback_required={LowerBool(pack.FallbackRequired)}",
            $"operator_required={LowerBool(pack.OperatorRequired)}",
            $"operator_decides={LowerBool(pack.OperatorDecides)}",
            $"agent_advisory_only={LowerBool(pack.AgentAdvisoryOnly)}",
            $"harness_execution_authority={LowerBool(pack.HarnessExecutionAuthority)}",
            $"csharp_validation_authority={LowerBool(pack.CSharpValidationAuthority)}",
            $"default_result_changed={LowerBool(pack.DefaultResultChanged)}",
            $"manual_only={LowerBool(pack.ManualOnly)}",
            $"starts_runtime={LowerBool(pack.StartsRuntime)}",
            $"installs_dependencies={LowerBool(pack.InstallsDependencies)}",
            $"calls_sidecar={LowerBool(pack.CallsSidecar)}",
            $"stores_secrets={LowerBool(pack.StoresSecrets)}",
            $"stores_sql={LowerBool(pack.StoresSql)}",
            $"stores_hidden_context={LowerBool(pack.StoresHiddenContext)}",
            string.Empty);
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafePathToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        return SafeToken(Path.GetFileName(value));
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
