using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public sealed record DataAgentV47RuntimeIdentityEvidence(
    string RuntimeInstanceId,
    string ConfigurationFingerprint,
    long StartedAtUnixSeconds,
    bool StableAcrossWindow);

public sealed record DataAgentV47LiveCanaryInput(
    DataAgentV45ProductionObservationSnapshot? ObservationSnapshot,
    DataAgentV45ProductionFaultDrillResult? FaultDrillResult,
    DataAgentV47RuntimeIdentityEvidence? RuntimeIdentity,
    int RuntimeRestartCount,
    bool KillSwitchRestored,
    bool ProductionShadowRestoredDisabled);

public sealed record DataAgentV47LiveCanaryResult(
    bool Accepted,
    string ReasonCode,
    string ContractVersion,
    string SourceBaseline,
    DataAgentV45ProductionObservationSnapshot? ObservationSnapshot,
    DataAgentV45ProductionFaultDrillResult? FaultDrillResult,
    DataAgentV47RuntimeIdentityEvidence? RuntimeIdentity,
    int RuntimeRestartCount,
    bool KillSwitchRestored,
    bool ProductionShadowRestoredDisabled,
    bool AgentAdvisoryOnly,
    bool CSharpValidationAuthority,
    bool AllowsExecution,
    bool AllowsStateWrite,
    bool AllowsVisibleText,
    bool StoresSensitiveData,
    IReadOnlyList<string> ReasonCodes);

public static class DataAgentV47LiveCanaryClosureEvaluator
{
    public const int RequiredObservationCapacity = 256;
    public const int RequiredWindowMinutes = 15;
    public const int MinimumObservations = 20;
    public const int MaximumFallbackRatioBasisPoints = 2500;
    public const int MaximumP95LatencyMs = 2000;
    public const int MaximumRuntimeRestarts = 1;

    static readonly Regex FingerprintPattern = new(
        "^[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DataAgentV47LiveCanaryResult Evaluate(DataAgentV47LiveCanaryInput? input)
    {
        DataAgentV45ProductionObservationSnapshot? snapshot = input?.ObservationSnapshot;
        if (ObservationWindowComplete(snapshot) == false)
            return Rejected(input, "v4_7_observation_window_incomplete");
        if (snapshot!.FallbackRatioBasisPoints > MaximumFallbackRatioBasisPoints)
            return Rejected(input, "v4_7_fallback_ratio_exceeded");
        if (snapshot.P95LatencyMs > MaximumP95LatencyMs)
            return Rejected(input, "v4_7_latency_budget_exceeded");
        if (snapshot.RetryStormDetected)
            return Rejected(input, "v4_7_retry_storm_detected");
        if (input!.RuntimeRestartCount is < 0 or > MaximumRuntimeRestarts)
            return Rejected(input, "v4_7_restart_budget_exceeded");

        DataAgentV45ProductionFaultDrillResult validatedDrills =
            DataAgentV45ProductionFaultDrillEvaluator.Evaluate(input.FaultDrillResult?.Drills);
        if (input.FaultDrillResult?.Accepted != true || validatedDrills.Accepted == false)
            return Rejected(input, "v4_7_fault_drill_failed");

        DataAgentV47RuntimeIdentityEvidence? identity = input.RuntimeIdentity;
        if (CanonicalUuid(identity?.RuntimeInstanceId) == false)
            return Rejected(input, "v4_7_runtime_identity_invalid");
        if (FingerprintPattern.IsMatch(identity!.ConfigurationFingerprint ?? string.Empty) == false)
            return Rejected(input, "v4_7_configuration_fingerprint_invalid");
        if (identity.StartedAtUnixSeconds <= 0)
            return Rejected(input, "v4_7_runtime_start_time_invalid");
        if (identity.StableAcrossWindow == false)
            return Rejected(input, "v4_7_runtime_identity_unstable");
        if (input.KillSwitchRestored == false)
            return Rejected(input, "v4_7_kill_switch_not_restored");
        if (input.ProductionShadowRestoredDisabled == false)
            return Rejected(input, "v4_7_production_shadow_not_restored_disabled");

        return Create(input with { FaultDrillResult = validatedDrills }, true,
            "v4_7_live_canary_closure_accepted");
    }

    static bool ObservationWindowComplete(DataAgentV45ProductionObservationSnapshot? snapshot) =>
        snapshot is not null &&
        snapshot.Capacity == RequiredObservationCapacity &&
        snapshot.WindowMinutes == RequiredWindowMinutes &&
        snapshot.ObservationCount is >= MinimumObservations and <= RequiredObservationCapacity &&
        StatusCount(snapshot) == snapshot.ObservationCount &&
        snapshot.NetworkAttemptCount == snapshot.ObservationCount &&
        snapshot.AverageLatencyMs >= 0 &&
        snapshot.P95LatencyMs >= 0 &&
        snapshot.FallbackRatioBasisPoints is >= 0 and <= 10_000 &&
        snapshot.MaxObservationsPerMinute is >= 0 &&
        snapshot.MaxObservationsPerMinute <= snapshot.ObservationCount &&
        snapshot.StoresSensitiveData == false;

    static int StatusCount(DataAgentV45ProductionObservationSnapshot snapshot) =>
        snapshot.AcceptedCount + snapshot.RejectedCount + snapshot.FallbackCount +
        snapshot.TimeoutCount + snapshot.UnavailableCount + snapshot.BusyCount +
        snapshot.CircuitOpenCount;

    static bool CanonicalUuid(string? value) =>
        Guid.TryParseExact(value, "D", out Guid parsed) &&
        string.Equals(parsed.ToString("D"), value, StringComparison.Ordinal);

    static DataAgentV47LiveCanaryResult Rejected(
        DataAgentV47LiveCanaryInput? input, string reasonCode) => Create(input, false, reasonCode);

    static DataAgentV47LiveCanaryResult Create(
        DataAgentV47LiveCanaryInput? input, bool accepted, string reasonCode) => new(
        accepted,
        reasonCode,
        ContractVersion: "v4.7",
        SourceBaseline: "v4.6",
        input?.ObservationSnapshot,
        input?.FaultDrillResult,
        input?.RuntimeIdentity,
        RuntimeRestartCount: input?.RuntimeRestartCount ?? 0,
        KillSwitchRestored: input?.KillSwitchRestored == true,
        ProductionShadowRestoredDisabled: input?.ProductionShadowRestoredDisabled == true,
        AgentAdvisoryOnly: true,
        CSharpValidationAuthority: true,
        AllowsExecution: false,
        AllowsStateWrite: false,
        AllowsVisibleText: false,
        StoresSensitiveData: false,
        ReasonCodes: [reasonCode]);
}

public static class DataAgentV47LiveCanaryClosureFormatter
{
    static readonly Regex SafeTokenPattern = new(
        "^[a-z0-9][a-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Format(DataAgentV47LiveCanaryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        DataAgentV45ProductionObservationSnapshot? snapshot = result.ObservationSnapshot;
        DataAgentV45ProductionFaultDrillResult? drills = result.FaultDrillResult;
        DataAgentV47RuntimeIdentityEvidence? identity = result.RuntimeIdentity;
        return string.Join(Environment.NewLine,
            "live_canary_closure=v4.7",
            $"source_baseline={SafeToken(result.SourceBaseline)}",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason_code={SafeToken(result.ReasonCode)}",
            $"observation_capacity={snapshot?.Capacity ?? 0}",
            $"observation_window_minutes={snapshot?.WindowMinutes ?? 0}",
            $"observation_count={snapshot?.ObservationCount ?? 0}",
            $"accepted_count={snapshot?.AcceptedCount ?? 0}",
            $"rejected_count={snapshot?.RejectedCount ?? 0}",
            $"fallback_count={snapshot?.FallbackCount ?? 0}",
            $"timeout_count={snapshot?.TimeoutCount ?? 0}",
            $"unavailable_count={snapshot?.UnavailableCount ?? 0}",
            $"busy_count={snapshot?.BusyCount ?? 0}",
            $"circuit_open_count={snapshot?.CircuitOpenCount ?? 0}",
            $"network_attempt_count={snapshot?.NetworkAttemptCount ?? 0}",
            $"average_latency_ms={snapshot?.AverageLatencyMs ?? 0}",
            $"p95_latency_ms={snapshot?.P95LatencyMs ?? 0}",
            $"fallback_ratio_basis_points={snapshot?.FallbackRatioBasisPoints ?? 0}",
            $"max_observations_per_minute={snapshot?.MaxObservationsPerMinute ?? 0}",
            $"retry_storm_detected={LowerBool(snapshot?.RetryStormDetected == true)}",
            $"runtime_instance_id={SafeToken(identity?.RuntimeInstanceId)}",
            $"configuration_fingerprint={SafeFingerprint(identity?.ConfigurationFingerprint)}",
            $"started_at_unix_seconds={identity?.StartedAtUnixSeconds ?? 0}",
            $"identity_stable_across_window={LowerBool(identity?.StableAcrossWindow == true)}",
            $"runtime_restart_count={result.RuntimeRestartCount}",
            $"fault_drill_count={drills?.Drills.Count ?? 0}",
            $"drill_runtime_unavailable={DrillPassed(drills, DataAgentV45FaultDrillKind.RuntimeUnavailable)}",
            $"drill_timeout={DrillPassed(drills, DataAgentV45FaultDrillKind.Timeout)}",
            $"drill_invalid_schema={DrillPassed(drills, DataAgentV45FaultDrillKind.InvalidSchema)}",
            $"drill_unsafe_authority={DrillPassed(drills, DataAgentV45FaultDrillKind.UnsafeAuthority)}",
            $"drill_concurrency_saturation={DrillPassed(drills, DataAgentV45FaultDrillKind.ConcurrencySaturation)}",
            $"drill_circuit_open_recovery={DrillPassed(drills, DataAgentV45FaultDrillKind.CircuitOpenRecovery)}",
            $"drill_live_kill_switch={DrillPassed(drills, DataAgentV45FaultDrillKind.LiveKillSwitch)}",
            $"kill_switch_restored={LowerBool(result.KillSwitchRestored)}",
            $"production_shadow_restored_disabled={LowerBool(result.ProductionShadowRestoredDisabled)}",
            $"agent_advisory_only={LowerBool(result.AgentAdvisoryOnly)}",
            $"csharp_validation_authority={LowerBool(result.CSharpValidationAuthority)}",
            $"allows_execution={LowerBool(result.AllowsExecution)}",
            $"allows_state_write={LowerBool(result.AllowsStateWrite)}",
            $"allows_visible_text={LowerBool(result.AllowsVisibleText)}",
            $"stores_sensitive_data={LowerBool(result.StoresSensitiveData)}",
            $"reason_codes={SafeReasonCodes(result.ReasonCodes)}");
    }

    static string DrillPassed(DataAgentV45ProductionFaultDrillResult? result, DataAgentV45FaultDrillKind kind) =>
        LowerBool(result?.Drills.SingleOrDefault(drill => drill.Kind == kind)?.Passed == true);

    static string SafeReasonCodes(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } && values.Select(SafeToken).All(value => value != "redacted")
            ? string.Join(',', values.Select(SafeToken))
            : "redacted";

    static string SafeToken(string? value) =>
        string.IsNullOrWhiteSpace(value) == false && SafeTokenPattern.IsMatch(value) &&
        DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) == false
            ? value : "redacted";

    static string SafeFingerprint(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= 'a' and <= 'f' or >= '0' and <= '9')
            ? value : "redacted";

    static string LowerBool(bool value) => value ? "true" : "false";
}
