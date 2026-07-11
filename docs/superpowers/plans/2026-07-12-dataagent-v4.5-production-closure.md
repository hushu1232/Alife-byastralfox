# DataAgent V4.5 Production Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded production-shadow observation, live kill-switch enforcement, deterministic fault-drill evidence, and a safe V4.5 production-closure acceptance artifact.

**Architecture:** The existing coordinator remains the C# authority and records exactly one aggregate-safe observation for each final handshake outcome. A fixed-capacity/time-window recorder produces aggregate snapshots; a deterministic evaluator combines the snapshot with V4.3 value evidence and seven exact fault drills. V4.4 reads only live enable/kill/value gates per call, while all concurrency and circuit limits remain fixed.

**Tech Stack:** .NET 9, C# records/enums, lock/queue aggregation, NUnit, PowerShell static readiness.

---

### Task 1: Bounded production observation recorder

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV45ProductionObservation.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV45ProductionClosureTests.cs`

- [ ] Write failing tests for exact outcome classification, capacity eviction, 15-minute expiry, fallback ratio, average/P95 network latency, max observations per minute, retry-storm threshold, safe reason-code handling, and null rejection.
- [ ] Run `dotnet test` filtered to `DataAgentV45ProductionClosureTests` and verify RED because V4.5 observation types do not exist.
- [ ] Implement:

```csharp
public enum DataAgentV45ProductionObservationStatus
{
    Accepted, Rejected, Fallback, Timeout, Unavailable, Busy, CircuitOpen
}

public sealed record DataAgentV45ProductionObservationOptions(
    int Capacity,
    TimeSpan Window,
    int RetryStormThresholdPerMinute)
{
    public static DataAgentV45ProductionObservationOptions Default { get; } =
        new(256, TimeSpan.FromMinutes(15), 60);
}

public sealed record DataAgentV45ProductionObservationSnapshot(
    int Capacity,
    int ObservationCount,
    int AcceptedCount,
    int RejectedCount,
    int FallbackCount,
    int TimeoutCount,
    int UnavailableCount,
    int BusyCount,
    int CircuitOpenCount,
    int NetworkAttemptCount,
    int AverageLatencyMs,
    int P95LatencyMs,
    int FallbackRatioBasisPoints,
    int MaxObservationsPerMinute,
    bool RetryStormDetected,
    bool StoresSensitiveData);

public sealed class DataAgentV45ProductionObservationRecorder
{
    public void Record(DataAgentGraphHandshakeOutcome outcome, TimeSpan elapsed, DateTimeOffset recordedAt);
    public DataAgentV45ProductionObservationSnapshot GetSnapshot(DateTimeOffset now);
}
```

Use a lock-protected queue, cap latency at 300000 ms, evict expired/oldest observations, compute nearest-rank P95, and map only stable coordinator status/reason fields. Store no request, response, endpoint, SQL, exception, caller, session, or free-form text.
- [ ] Run focused tests and verify GREEN.
- [ ] Commit with `feat(dataagent): add bounded v4.5 shadow observations`.

### Task 2: Coordinator observation and live kill switch

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV44ProductionShadowClient.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV44ProductionShadowTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] Write a failing V4.4 test that constructs the client with an options provider, changes kill switch from false to true after one success, and proves the next call returns `production_shadow_kill_switch_active` with `NetworkAttempted=false` and no new inner call.
- [ ] Write failing coordinator tests proving accepted/rejected/timeout/busy each produce exactly one V4.5 observation, injected clock latency is captured, and a throwing recorder never changes the outcome.
- [ ] Run the focused tests and verify RED for missing constructor/provider/recorder integration.
- [ ] Change the V4.4 constructor to accept optional `Func<DataAgentV44ProductionShadowOptions>`. Evaluate the provider before every call for only enable/kill/value gates; retain construction-time semaphore/circuit limits.
- [ ] Refactor coordinator `TryHandshake` into a public timing/observation wrapper and private deterministic core. Add optional recorder and clock parameters at the end of the constructor, catch recorder failures, and record one final outcome only.
- [ ] In module wiring, create a V4.5 recorder, pass it to the coordinator, and supply `DataAgentV44ProductionShadowOptions.FromEnvironment` as the V4.4 live options provider. Production shadow must continue disabling the legacy stream bypass.
- [ ] Run all V4.4, coordinator, module, and V4.5 observation tests and verify GREEN.
- [ ] Commit with `feat(dataagent): observe v4.5 outcomes and enforce live kill switch`.

### Task 3: Exact seven-drill evidence and production acceptance

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV45ProductionClosure.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV45ProductionClosureTests.cs`

- [ ] Write failing tests that exercise the real decorator/coordinator/validator boundaries for runtime unavailable, timeout, invalid response schema, unsafe SQL authority request, concurrency saturation, circuit open/recovery, and live kill switch.
- [ ] Require exact unique drill kinds through:

```csharp
public enum DataAgentV45FaultDrillKind
{
    RuntimeUnavailable, Timeout, InvalidSchema, UnsafeAuthority,
    ConcurrencySaturation, CircuitOpenRecovery, LiveKillSwitch
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
```

Reject missing, duplicate, unsafe, failed, or extra evidence. The live-kill-switch drill must have `NetworkAttempted=false`.
- [ ] Write failing evaluator tests for accepted closure and every stable failure: value gate, incomplete observations, fallback ratio, P95 latency, retry storm, restart budget, and fault drill.
- [ ] Implement `DataAgentV45ProductionClosureEvaluator.Evaluate` with hard gates: score/status eligibility, at least 20 observations, fallback ratio <=2500 basis points, P95 <=2000 ms, no retry storm, restart count <=1, and all seven drills accepted. Return fixed authority/storage booleans and never allow a score to compensate for a safety failure.
- [ ] Run focused tests and verify GREEN.
- [ ] Commit with `feat(dataagent): evaluate v4.5 production closure drills`.

### Task 4: Safe formatter, artifact, runbook, and readiness

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV45ProductionClosureArtifactWriter.cs`
- Create: `docs/dataagent/dataagent-v4.5-production-closure.md`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV45ProductionClosure.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3ClosureManifest.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV45ProductionClosureTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: readiness count tests that assert the current total

- [ ] Write failing formatter/artifact tests requiring fixed aggregate fields, exact seven drill fields, no free-form values, file name `dataagent-v4.5-production-closure.txt`, and absence of endpoint/request/response/SQL/token/hidden-context/path text.
- [ ] Implement formatter and explicit artifact writer. Rejected writer inputs return safe reason codes and no path.
- [ ] Write the runbook with markers `production_closure=v4.5`, `source_baseline=v4.4`, `observation_capacity=256`, `observation_window_minutes=15`, `minimum_observations=20`, `fallback_ratio_basis_points_max=2500`, `p95_latency_ms_max=2000`, `fault_drill_count=7`, `live_kill_switch=true`, and all authority/storage fields false.
- [ ] Add dynamic/static `GraphHandshakeV45ProductionClosurePresent`; advance totals from 101/117 to 102/118 and add V4.5 to V4-only/post-V3 sets. Preserve frozen V3 111/95 exactly.
- [ ] Run V4.5 and readiness tests plus `tools/check-dataagent-readiness.ps1`; verify 102 dynamic checks and 118 static checks pass.
- [ ] Commit with `docs(dataagent): close v4.5 production readiness`.

### Task 5: Completion audit and full verification

**Files:**
- Verify all files named above and the V4.4/V4.5 design/plan/runbook.

- [ ] Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter 'DataAgentV44ProductionShadow|DataAgentV45ProductionClosure' -v:minimal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore -v:minimal
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
git diff --check
```

- [ ] Audit every V4.5 requirement against direct evidence: bounded recorder, all status counters, latency, fallback ratio, retry storm, restart budget, live kill switch, seven drills, V4.3 threshold, authority/storage invariants, artifact safety, readiness totals, and V3 freeze.
- [ ] Confirm no automated test starts Python/LangGraph/QQ, installs dependencies, binds a port, or accesses a non-loopback endpoint.
- [ ] Commit any audit-only documentation correction, then report exact verification counts. Do not push or merge.

V4.5 is complete only after every hard gate and the full completion audit pass. Source compilation or a readiness marker alone is insufficient.
