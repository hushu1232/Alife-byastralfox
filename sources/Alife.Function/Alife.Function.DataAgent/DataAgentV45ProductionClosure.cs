using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public enum DataAgentV45FaultDrillKind
{
    RuntimeUnavailable,
    Timeout,
    InvalidSchema,
    UnsafeAuthority,
    ConcurrencySaturation,
    CircuitOpenRecovery,
    LiveKillSwitch
}

public sealed record DataAgentV45FaultDrillObservation(
    DataAgentV45FaultDrillKind Kind,
    bool Passed,
    string ReasonCode,
    bool NetworkAttempted);

public sealed record DataAgentV45ProductionFaultDrillResult(
    bool Accepted,
    string ReasonCode,
    IReadOnlyList<DataAgentV45FaultDrillObservation> Drills);

public static class DataAgentV45ProductionFaultDrillEvaluator
{
    static readonly Regex SafeReasonCodePattern = new(
        "^[a-z][a-z0-9_]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly IReadOnlyDictionary<DataAgentV45FaultDrillKind, string[]> ExpectedReasonCodes =
        new Dictionary<DataAgentV45FaultDrillKind, string[]>
        {
            [DataAgentV45FaultDrillKind.RuntimeUnavailable] = ["production_shadow_unavailable"],
            [DataAgentV45FaultDrillKind.Timeout] = ["production_shadow_timeout"],
            [DataAgentV45FaultDrillKind.InvalidSchema] = ["invalid_response_schema", "request_id_mismatch"],
            [DataAgentV45FaultDrillKind.UnsafeAuthority] = ["sql_authority_requested"],
            [DataAgentV45FaultDrillKind.ConcurrencySaturation] = ["production_shadow_busy"],
            [DataAgentV45FaultDrillKind.CircuitOpenRecovery] = ["production_shadow_circuit_open"],
            [DataAgentV45FaultDrillKind.LiveKillSwitch] = ["production_shadow_kill_switch_active"]
        };

    public static DataAgentV45ProductionFaultDrillResult Evaluate(
        IEnumerable<DataAgentV45FaultDrillObservation>? observations)
    {
        DataAgentV45FaultDrillObservation[] drills = observations?.ToArray() ?? [];
        DataAgentV45FaultDrillKind[] expectedKinds = Enum.GetValues<DataAgentV45FaultDrillKind>();
        bool exactInventory =
            drills.Length == expectedKinds.Length &&
            drills.Select(drill => drill.Kind).Distinct().Count() == expectedKinds.Length &&
            expectedKinds.All(kind => drills.Any(drill => drill.Kind == kind));
        if (exactInventory == false)
            return Rejected("v4_5_fault_drill_inventory_invalid");

        foreach (DataAgentV45FaultDrillObservation drill in drills)
        {
            if (drill.Passed == false || SafeReasonCode(drill.ReasonCode) == false)
                return Rejected("v4_5_fault_drill_failed");
            if (ExpectedReasonCodes[drill.Kind].Contains(drill.ReasonCode, StringComparer.Ordinal) == false)
                return Rejected("v4_5_fault_drill_reason_invalid");

            bool expectedNetwork = drill.Kind is
                DataAgentV45FaultDrillKind.RuntimeUnavailable or
                DataAgentV45FaultDrillKind.Timeout or
                DataAgentV45FaultDrillKind.InvalidSchema or
                DataAgentV45FaultDrillKind.UnsafeAuthority;
            if (drill.NetworkAttempted != expectedNetwork)
                return Rejected("v4_5_fault_drill_network_boundary_invalid");
        }

        return new DataAgentV45ProductionFaultDrillResult(
            Accepted: true,
            ReasonCode: "v4_5_fault_drills_passed",
            drills.OrderBy(drill => drill.Kind).ToArray());
    }

    static bool SafeReasonCode(string? value) =>
        string.IsNullOrWhiteSpace(value) == false &&
        SafeReasonCodePattern.IsMatch(value) &&
        DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) == false;

    static DataAgentV45ProductionFaultDrillResult Rejected(string reasonCode) =>
        new(false, reasonCode, []);
}

public sealed record DataAgentV45ProductionClosureInput(
    DataAgentV43CrossModuleValueResult? ValueResult,
    DataAgentV45ProductionObservationSnapshot? ObservationSnapshot,
    DataAgentV45ProductionFaultDrillResult? FaultDrillResult,
    int RuntimeRestartCount);

public sealed record DataAgentV45ProductionClosureResult(
    bool Accepted,
    string ReasonCode,
    string ContractVersion,
    string SourceBaseline,
    int ValueScore,
    DataAgentV43ValueStatus ValueStatus,
    DataAgentV45ProductionObservationSnapshot? ObservationSnapshot,
    DataAgentV45ProductionFaultDrillResult? FaultDrillResult,
    int RuntimeRestartCount,
    bool AgentAdvisoryOnly,
    bool CSharpValidationAuthority,
    bool AllowsExecution,
    bool AllowsStateWrite,
    bool AllowsVisibleText,
    bool DefaultResultChanged,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext,
    IReadOnlyList<string> ReasonCodes);

public static class DataAgentV45ProductionClosureEvaluator
{
    public const int RequiredObservationCapacity = 256;
    public const int RequiredWindowMinutes = 15;
    public const int MinimumObservations = 20;
    public const int MaximumFallbackRatioBasisPoints = 2500;
    public const int MaximumP95LatencyMs = 2000;
    public const int MaximumRuntimeRestarts = 1;

    public static DataAgentV45ProductionClosureResult Evaluate(DataAgentV45ProductionClosureInput? input)
    {
        DataAgentV43CrossModuleValueResult? value = input?.ValueResult;
        if (ValueGatePassed(value) == false)
            return Rejected(input, "v4_5_value_gate_failed");

        DataAgentV45ProductionObservationSnapshot? snapshot = input!.ObservationSnapshot;
        if (ObservationWindowComplete(snapshot) == false)
            return Rejected(input, "v4_5_observation_window_incomplete");
        if (snapshot!.FallbackRatioBasisPoints > MaximumFallbackRatioBasisPoints)
            return Rejected(input, "v4_5_fallback_ratio_exceeded");
        if (snapshot.P95LatencyMs > MaximumP95LatencyMs)
            return Rejected(input, "v4_5_latency_budget_exceeded");
        if (snapshot.RetryStormDetected)
            return Rejected(input, "v4_5_retry_storm_detected");
        if (input.RuntimeRestartCount is < 0 or > MaximumRuntimeRestarts)
            return Rejected(input, "v4_5_restart_budget_exceeded");
        DataAgentV45ProductionFaultDrillResult validatedDrills =
            DataAgentV45ProductionFaultDrillEvaluator.Evaluate(input.FaultDrillResult?.Drills);
        if (input.FaultDrillResult?.Accepted != true || validatedDrills.Accepted == false)
        {
            return Rejected(input, "v4_5_fault_drill_failed");
        }

        return Create(
            input with { FaultDrillResult = validatedDrills },
            true,
            "v4_5_production_closure_accepted");
    }

    static bool ValueGatePassed(DataAgentV43CrossModuleValueResult? value) =>
        value is not null &&
        value.Accepted &&
        value.Status == DataAgentV43ValueStatus.ProvenUseful &&
        value.TotalScore >= DataAgentV43CrossModuleValueEvaluator.ProductionShadowEligibilityScore &&
        value.ProductionShadowEligible &&
        value.AgentAdvisoryOnly &&
        value.CSharpValidationAuthority &&
        value.AllowsExecution == false &&
        value.AllowsStateWrite == false &&
        value.AllowsVisibleText == false &&
        value.DefaultResultChanged == false &&
        value.StoresSecrets == false &&
        value.StoresSql == false &&
        value.StoresHiddenContext == false;

    static bool ObservationWindowComplete(DataAgentV45ProductionObservationSnapshot? snapshot) =>
        snapshot is not null &&
        snapshot.Capacity == RequiredObservationCapacity &&
        snapshot.WindowMinutes == RequiredWindowMinutes &&
        snapshot.ObservationCount >= MinimumObservations &&
        snapshot.ObservationCount <= snapshot.Capacity &&
        StatusCount(snapshot) == snapshot.ObservationCount &&
        snapshot.NetworkAttemptCount >= 0 &&
        snapshot.NetworkAttemptCount <= snapshot.ObservationCount &&
        snapshot.AverageLatencyMs >= 0 &&
        snapshot.P95LatencyMs >= 0 &&
        snapshot.FallbackRatioBasisPoints is >= 0 and <= 10_000 &&
        snapshot.MaxObservationsPerMinute >= 0 &&
        snapshot.MaxObservationsPerMinute <= snapshot.ObservationCount &&
        snapshot.StoresSensitiveData == false;

    static int StatusCount(DataAgentV45ProductionObservationSnapshot snapshot) =>
        snapshot.AcceptedCount +
        snapshot.RejectedCount +
        snapshot.FallbackCount +
        snapshot.TimeoutCount +
        snapshot.UnavailableCount +
        snapshot.BusyCount +
        snapshot.CircuitOpenCount;

    static DataAgentV45ProductionClosureResult Rejected(
        DataAgentV45ProductionClosureInput? input,
        string reasonCode) =>
        Create(input, false, reasonCode);

    static DataAgentV45ProductionClosureResult Create(
        DataAgentV45ProductionClosureInput? input,
        bool accepted,
        string reasonCode)
    {
        return new DataAgentV45ProductionClosureResult(
            accepted,
            reasonCode,
            ContractVersion: "v4.5",
            SourceBaseline: "v4.4",
            ValueScore: input?.ValueResult?.TotalScore ?? 0,
            ValueStatus: input?.ValueResult?.Status ?? DataAgentV43ValueStatus.Rejected,
            input?.ObservationSnapshot,
            input?.FaultDrillResult,
            RuntimeRestartCount: input?.RuntimeRestartCount ?? 0,
            AgentAdvisoryOnly: true,
            CSharpValidationAuthority: true,
            AllowsExecution: false,
            AllowsStateWrite: false,
            AllowsVisibleText: false,
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: [reasonCode]);
    }
}

public static class DataAgentV45ProductionClosureFormatter
{
    static readonly Regex SafeTokenPattern = new(
        "^[a-z0-9][a-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Format(DataAgentV45ProductionClosureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        DataAgentV45ProductionObservationSnapshot? snapshot = result.ObservationSnapshot;
        DataAgentV45ProductionFaultDrillResult? drills = result.FaultDrillResult;

        return string.Join(
            Environment.NewLine,
            "production_closure=v4.5",
            $"source_baseline={SafeToken(result.SourceBaseline)}",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason_code={SafeToken(result.ReasonCode)}",
            $"value_score={result.ValueScore}",
            $"value_status={ValueStatus(result.ValueStatus)}",
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
            $"runtime_restart_count={result.RuntimeRestartCount}",
            $"fault_drill_count={drills?.Drills.Count ?? 0}",
            $"drill_runtime_unavailable={DrillPassed(drills, DataAgentV45FaultDrillKind.RuntimeUnavailable)}",
            $"drill_timeout={DrillPassed(drills, DataAgentV45FaultDrillKind.Timeout)}",
            $"drill_invalid_schema={DrillPassed(drills, DataAgentV45FaultDrillKind.InvalidSchema)}",
            $"drill_unsafe_authority={DrillPassed(drills, DataAgentV45FaultDrillKind.UnsafeAuthority)}",
            $"drill_concurrency_saturation={DrillPassed(drills, DataAgentV45FaultDrillKind.ConcurrencySaturation)}",
            $"drill_circuit_open_recovery={DrillPassed(drills, DataAgentV45FaultDrillKind.CircuitOpenRecovery)}",
            $"drill_live_kill_switch={DrillPassed(drills, DataAgentV45FaultDrillKind.LiveKillSwitch)}",
            $"agent_advisory_only={LowerBool(result.AgentAdvisoryOnly)}",
            $"csharp_validation_authority={LowerBool(result.CSharpValidationAuthority)}",
            $"allows_execution={LowerBool(result.AllowsExecution)}",
            $"allows_state_write={LowerBool(result.AllowsStateWrite)}",
            $"allows_visible_text={LowerBool(result.AllowsVisibleText)}",
            $"default_result_changed={LowerBool(result.DefaultResultChanged)}",
            $"stores_secrets={LowerBool(result.StoresSecrets)}",
            $"stores_sql={LowerBool(result.StoresSql)}",
            $"stores_hidden_context={LowerBool(result.StoresHiddenContext)}",
            $"reason_codes={SafeReasonCodes(result.ReasonCodes)}");
    }

    static string DrillPassed(
        DataAgentV45ProductionFaultDrillResult? result,
        DataAgentV45FaultDrillKind kind) =>
        LowerBool(result?.Drills.SingleOrDefault(drill => drill.Kind == kind)?.Passed == true);

    static string SafeReasonCodes(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return "none";
        string[] safe = values.Select(SafeToken).ToArray();
        return safe.All(value => value != "redacted") ? string.Join(',', safe) : "redacted";
    }

    static string SafeToken(string? value) =>
        string.IsNullOrWhiteSpace(value) == false &&
        SafeTokenPattern.IsMatch(value) &&
        DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) == false
            ? value
            : "redacted";

    static string ValueStatus(DataAgentV43ValueStatus status) => status switch
    {
        DataAgentV43ValueStatus.ProvenUseful => "proven_useful",
        DataAgentV43ValueStatus.Promising => "promising",
        DataAgentV43ValueStatus.Unproven => "unproven",
        _ => "rejected"
    };

    static string LowerBool(bool value) => value ? "true" : "false";
}
