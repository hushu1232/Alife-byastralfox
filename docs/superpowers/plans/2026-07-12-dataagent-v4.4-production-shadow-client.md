# DataAgent V4.4 Production Shadow Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an explicitly enabled, value-gated, loopback-only production shadow decorator with bounded concurrency, circuit breaking, and deterministic fallback.

**Architecture:** New V4.4 options and an `IDataAgentGraphSidecarClient` decorator reuse the existing V3 HTTP client/validator/coordinator. The decorator owns kill switch, value gate, semaphore, and circuit state; the coordinator maps safe V4.4 exceptions, and module wiring activates the decorator only for explicit V4.4 enablement.

**Tech Stack:** .NET 9, C# lock/SemaphoreSlim, existing graph handshake HTTP client and NUnit tests.

---

### Task 1: Fail-closed production shadow options

Create this public surface:

```csharp
public sealed record DataAgentV44ProductionShadowOptions(
    bool Enabled,
    bool KillSwitchActive,
    int ValueScore,
    string ValueStatus,
    int MaxConcurrency,
    int FailureThreshold,
    TimeSpan CircuitOpenDuration)
{
    public bool ValueGatePassed => ValueScore >= 80 && ValueStatus == "proven_useful";
    public static DataAgentV44ProductionShadowOptions FromEnvironment();
    public static DataAgentV44ProductionShadowOptions FromValues(params string?[] values);
}
```

- [ ] Write tests for default disabled/kill-active, explicit enable, value score/status gate, numeric bounds, and existing loopback HTTP option compatibility.
- [ ] Run focused tests and verify compile RED for missing V4.4 types.
- [ ] Implement `DataAgentV44ProductionShadowOptions` constants, defaults, `FromEnvironment`, and `FromValues`; invalid values fall back to safe defaults and readiness requires score at least 80/status proven useful.
- [ ] Run GREEN and commit options/tests.

### Task 2: Decorator, circuit, concurrency, and coordinator mapping

Create:

```csharp
public sealed class DataAgentV44ProductionShadowException : Exception
{
    public string ReasonCode { get; }
    public bool NetworkAttempted { get; }
}

public sealed class DataAgentV44ProductionShadowClient : IDataAgentGraphSidecarClient
{
    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request);
    public DataAgentV44ProductionShadowSnapshot GetSnapshot();
}
```

The snapshot contains only active calls, consecutive failures, circuit-open boolean, and stable reason code. It contains no endpoint, request, response, or exception text.

- [ ] Write tests using fake clients and an injected clock for success, disabled, kill switch, value failure, timeout/unavailable, threshold-open, deadline recovery, success reset, busy nonblocking rejection, and lease release.
- [ ] Require stable `DataAgentV44ProductionShadowException` reason codes and `NetworkAttempted` without endpoint/body/stack data.
- [ ] Run RED.
- [ ] Implement `DataAgentV44ProductionShadowClient` with lock-protected failure/deadline state and `SemaphoreSlim.Wait(0)`; never retry or queue.
- [ ] Add a coordinator catch that maps V4.4 reason/status/fallback while preserving validation and default-result behavior.
- [ ] Run focused coordinator/V4.4 tests and commit.

### Task 3: Runtime wiring and readiness

- [ ] Write module tests proving V4.4 disabled returns legacy client, explicit ready config decorates the HTTP client, and invalid/value/kill config fails closed without a network call.
- [ ] Modify `DataAgentModuleService` to parse V4.4 options and decorate only when explicitly enabled; the existing analysis handler remains unchanged.
- [ ] Add runtime doc and `GraphHandshakeV44ProductionShadowClientPresent`; update dynamic/static totals and V4-only projection while preserving V3 111/95.
- [ ] Run V4.4/readiness/V3 closure tests, static readiness, full DataAgent tests, and `git diff --check`.
- [ ] Commit with `feat(dataagent): wire v4.4 production shadow safety` and `docs(dataagent): add v4.4 shadow readiness`.

## Exact verification

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter DataAgentV44ProductionShadow -v:minimal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore -v:minimal
git diff --check
```

V4.4 is complete only when default-off, loopback/value/kill gates, concurrency, circuit recovery, coordinator mapping, module wiring, full regression, and readiness pass. Continue to V4.5; V4.4 alone is not production closure.
