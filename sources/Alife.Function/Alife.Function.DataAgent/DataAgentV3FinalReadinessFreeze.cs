namespace Alife.Function.DataAgent;

public sealed record DataAgentV3FinalReadinessFreeze(
    string FreezeId,
    string FinalV3Version,
    string SourceVersions,
    int FrozenRequiredCheckCount,
    int FrozenCoreCheckCount,
    bool AllFrozenChecksPassed,
    bool OperatorEvidencePackPresent,
    bool ReadinessGatesFrozen,
    bool FallbackRequired,
    bool OperatorRequired,
    bool OperatorDecides,
    bool AgentAdvisoryOnly,
    bool HarnessExecutionAuthority,
    bool CSharpValidationAuthority,
    bool DefaultResultChanged,
    bool ManualOnly,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool CallsSidecar,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext);

public static class DataAgentV3FinalReadinessFreezeBuilder
{
    public const string FreezeId = "v3.28-final-readiness-freeze";
    public const string FinalV3Version = "v3.28";
    public const string SourceVersions = "v3.0-v3.27";

    public static DataAgentV3FinalReadinessFreeze Build(
        IReadOnlyCollection<DataAgentReadinessCheck> frozenChecks,
        int frozenRequiredCheckCount,
        int frozenCoreCheckCount)
    {
        ArgumentNullException.ThrowIfNull(frozenChecks);

        bool operatorEvidencePackPresent = frozenChecks.Any(check =>
            string.Equals(check.Name, "GraphHandshakeOperatorEvidencePackPresent", StringComparison.Ordinal) &&
            check.Passed &&
            check.Detail.Contains("operator_evidence_pack=true", StringComparison.Ordinal) &&
            check.Detail.Contains("operator_decides=true", StringComparison.Ordinal));
        bool countsFrozen =
            frozenRequiredCheckCount == 108 &&
            frozenCoreCheckCount == 93;
        bool allFrozenChecksPassed =
            frozenChecks.All(check => check.Passed) &&
            operatorEvidencePackPresent &&
            countsFrozen;
        bool readinessGatesFrozen =
            allFrozenChecksPassed &&
            operatorEvidencePackPresent;

        return new DataAgentV3FinalReadinessFreeze(
            FreezeId,
            FinalV3Version,
            SourceVersions,
            frozenRequiredCheckCount,
            frozenCoreCheckCount,
            allFrozenChecksPassed,
            operatorEvidencePackPresent,
            readinessGatesFrozen,
            FallbackRequired: readinessGatesFrozen == false,
            OperatorRequired: readinessGatesFrozen == false,
            OperatorDecides: true,
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }
}

public static class DataAgentV3FinalReadinessFreezeFormatter
{
    public static string Format(DataAgentV3FinalReadinessFreeze freeze)
    {
        ArgumentNullException.ThrowIfNull(freeze);

        return string.Join(
            Environment.NewLine,
            "v3_final_readiness_freeze=true",
            $"freeze_id={SafeToken(freeze.FreezeId)}",
            $"final_v3_version={SafeToken(freeze.FinalV3Version)}",
            $"source_versions={SafeToken(freeze.SourceVersions)}",
            $"frozen_required_check_count={freeze.FrozenRequiredCheckCount}",
            $"frozen_core_check_count={freeze.FrozenCoreCheckCount}",
            $"all_frozen_checks_passed={LowerBool(freeze.AllFrozenChecksPassed)}",
            $"operator_evidence_pack_present={LowerBool(freeze.OperatorEvidencePackPresent)}",
            $"readiness_gates_frozen={LowerBool(freeze.ReadinessGatesFrozen)}",
            $"fallback_required={LowerBool(freeze.FallbackRequired)}",
            $"operator_required={LowerBool(freeze.OperatorRequired)}",
            $"operator_decides={LowerBool(freeze.OperatorDecides)}",
            $"agent_advisory_only={LowerBool(freeze.AgentAdvisoryOnly)}",
            $"harness_execution_authority={LowerBool(freeze.HarnessExecutionAuthority)}",
            $"csharp_validation_authority={LowerBool(freeze.CSharpValidationAuthority)}",
            $"default_result_changed={LowerBool(freeze.DefaultResultChanged)}",
            $"manual_only={LowerBool(freeze.ManualOnly)}",
            $"starts_runtime={LowerBool(freeze.StartsRuntime)}",
            $"installs_dependencies={LowerBool(freeze.InstallsDependencies)}",
            $"calls_sidecar={LowerBool(freeze.CallsSidecar)}",
            $"stores_secrets={LowerBool(freeze.StoresSecrets)}",
            $"stores_sql={LowerBool(freeze.StoresSql)}",
            $"stores_hidden_context={LowerBool(freeze.StoresHiddenContext)}",
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
                or '.'
                or '=')
            {
                continue;
            }

            return "redacted";
        }

        return trimmed;
    }
}
