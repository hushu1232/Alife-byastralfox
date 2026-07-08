# DataAgent V3.7 Reason-Code Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the DataAgent graph sidecar observability reason-code contract by locking all nine reason codes to exact literals in tests and readiness checks.

**Architecture:** Keep the V3.6 observability model and runtime behavior unchanged. Add exact-literal test coverage, extend existing dynamic/static readiness checks to include every reason-code literal, and verify boundaries remain offline, no-SSE, no-runtime-startup, and QChat-production clean.

**Tech Stack:** C#/.NET 9, NUnit, existing DataAgent graph handshake tests, existing DataAgent readiness C# checks, existing PowerShell readiness script.

---

## File Structure

- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`
  - Upgrade `ObservabilityReasonCodesAreStableMachineTokens` to assert the exact literal for all nine `DataAgentGraphSidecarObservabilityReasonCodes` constants while preserving uniqueness and machine-token assertions.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Extend `StaticReadinessScriptContainsV36SidecarObservabilityMarkers` so the static marker must contain all nine reason-code literals.
  - Add a focused source-level readiness test proving `DataAgentReadiness.cs` locks all nine exact reason-code literals in `graphHandshakeObservabilityReasonCodesReady`.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Extend `graphHandshakeObservabilityReasonCodesReady` to check all nine exact literals.
  - Keep dynamic readiness count at `76`.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Extend the existing `GraphHandshakeDevSidecarObservabilityContractPresent` marker to include all nine reason-code literals.
  - Keep `$expectedRequired = 91`.

Do not modify:

- `sources/Alife.Function/Alife.Function.QChat/**`
- `tools/dataagent-graph-sidecar/**`
- `tools/run-dataagent-graph-sidecar-smoke.ps1`
- Python runtime files
- upload scripts
- QChat engineering map count

---

### Task 1: Lock Exact Observability Reason-Code Literals

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`

- [ ] **Step 1: Strengthen the reason-code test**

Replace the body of `ObservabilityReasonCodesAreStableMachineTokens` in `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs` with this exact-literal table and assertions:

```csharp
[Test]
public void ObservabilityReasonCodesAreStableMachineTokens()
{
    Dictionary<string, string> reasonCodes = new()
    {
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.Disabled)] =
            DataAgentGraphSidecarObservabilityReasonCodes.Disabled,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured)] =
            DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable)] =
            DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected)] =
            DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected)] =
            DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.Accepted)] =
            DataAgentGraphSidecarObservabilityReasonCodes.Accepted,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed)] =
            DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing)] =
            DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing,
        [nameof(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected)] =
            DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected
    };

    Assert.Multiple(() =>
    {
        Assert.That(reasonCodes.Values, Is.Unique);
        Assert.That(reasonCodes, Has.Count.EqualTo(9));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.Disabled)], Is.EqualTo("graph_sidecar_disabled"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured)], Is.EqualTo("graph_sidecar_not_configured"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable)], Is.EqualTo("graph_sidecar_runtime_unavailable"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected)], Is.EqualTo("graph_sidecar_response_rejected"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected)], Is.EqualTo("graph_sidecar_progress_rejected"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.Accepted)], Is.EqualTo("graph_sidecar_accepted"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed)], Is.EqualTo("graph_sidecar_fallback_used"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing)], Is.EqualTo("graph_sidecar_stream_final_response_missing"));
        Assert.That(reasonCodes[nameof(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected)], Is.EqualTo("graph_sidecar_stream_final_response_rejected"));

        foreach ((string name, string reasonCode) in reasonCodes)
        {
            Assert.That(reasonCode, Does.Match("^[a-z][a-z0-9_]*$"), name);
            Assert.That(reasonCode, Does.StartWith("graph_sidecar_"), name);
        }
    });
}
```

- [ ] **Step 2: Run the focused test after adding assertions**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.ObservabilityReasonCodesAreStableMachineTokens" -v:minimal
```

Expected: pass, because production constants already have the correct values. This confirms the strengthened test matches current behavior but does not yet prove it can catch a regression.

- [ ] **Step 3: Perform a temporary mutation RED check**

Temporarily change one literal in `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs`:

```csharp
public const string ProgressRejected = "graph_sidecar_progress_rejected_MUTATION";
```

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.ObservabilityReasonCodesAreStableMachineTokens" -v:minimal
```

Expected: fail because the exact literal assertion expects `graph_sidecar_progress_rejected`.

- [ ] **Step 4: Restore the temporary mutation**

Restore the constant in `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs`:

```csharp
public const string ProgressRejected = "graph_sidecar_progress_rejected";
```

Run the focused test again:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.ObservabilityReasonCodesAreStableMachineTokens" -v:minimal
```

Expected: pass.

- [ ] **Step 5: Commit exact reason-code test hardening**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs
git commit -m "Lock graph sidecar observability reason codes"
```

---

### Task 2: Strengthen Dynamic And Static Readiness Reason-Code Coverage

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`

- [ ] **Step 1: Add failing readiness test assertions**

In `StaticReadinessScriptContainsV36SidecarObservabilityMarkers`, add assertions for the four missing literals after the existing `graph_sidecar_response_rejected` assertion:

```csharp
Assert.That(declaration, Does.Contain("graph_sidecar_progress_rejected"));
Assert.That(declaration, Does.Contain("graph_sidecar_fallback_used"));
```

Add assertions for the stream-specific literals after the existing `graph_sidecar_accepted` assertion:

```csharp
Assert.That(declaration, Does.Contain("graph_sidecar_stream_final_response_missing"));
Assert.That(declaration, Does.Contain("graph_sidecar_stream_final_response_rejected"));
```

Then add this new test immediately after `StaticReadinessScriptContainsV36SidecarObservabilityMarkers`:

```csharp
[Test]
public void DynamicReadinessSourceContainsV37ExactObservabilityReasonCodeChecks()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string source = File.ReadAllText(Path.Combine(
        repoRoot,
        "sources",
        "Alife.Function",
        "Alife.Function.DataAgent",
        "DataAgentReadiness.cs"));
    string declaration = FindSourceBlock(
        source,
        "bool graphHandshakeObservabilityReasonCodesReady",
        "bool graphHandshakeObservabilityFallbackReasonReady");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("graph_sidecar_disabled"));
        Assert.That(declaration, Does.Contain("graph_sidecar_not_configured"));
        Assert.That(declaration, Does.Contain("graph_sidecar_runtime_unavailable"));
        Assert.That(declaration, Does.Contain("graph_sidecar_response_rejected"));
        Assert.That(declaration, Does.Contain("graph_sidecar_progress_rejected"));
        Assert.That(declaration, Does.Contain("graph_sidecar_accepted"));
        Assert.That(declaration, Does.Contain("graph_sidecar_fallback_used"));
        Assert.That(declaration, Does.Contain("graph_sidecar_stream_final_response_missing"));
        Assert.That(declaration, Does.Contain("graph_sidecar_stream_final_response_rejected"));
    });
}
```

Add this helper near the existing `FindNewCheckDeclaration` helper:

```csharp
static string FindSourceBlock(string source, string startMarker, string endMarker)
{
    int start = source.IndexOf(startMarker, StringComparison.Ordinal);
    if (start < 0)
        return string.Empty;

    int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
    return end < 0
        ? source[start..]
        : source[start..end];
}
```

- [ ] **Step 2: Run readiness tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.StaticReadinessScriptContainsV36SidecarObservabilityMarkers|FullyQualifiedName~DataAgentReadinessTests.DynamicReadinessSourceContainsV37ExactObservabilityReasonCodeChecks" -v:minimal
```

Expected: fail because `tools/check-dataagent-readiness.ps1` and `DataAgentReadiness.cs` do not yet include the four additional exact literals in the V3.6 observability readiness contract.

- [ ] **Step 3: Extend dynamic readiness exact-literal checks**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, replace `graphHandshakeObservabilityReasonCodesReady` with this full nine-code check:

```csharp
bool graphHandshakeObservabilityReasonCodesReady =
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.Disabled, "graph_sidecar_disabled", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured, "graph_sidecar_not_configured", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable, "graph_sidecar_runtime_unavailable", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected, "graph_sidecar_response_rejected", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected, "graph_sidecar_progress_rejected", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.Accepted, "graph_sidecar_accepted", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed, "graph_sidecar_fallback_used", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing, "graph_sidecar_stream_final_response_missing", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected, "graph_sidecar_stream_final_response_rejected", StringComparison.Ordinal);
```

Do not change the dynamic readiness count. `DataAgentReadinessTests.CoreReadinessChecksAllPass` must remain:

```csharp
Assert.That(checks, Has.Count.EqualTo(76));
```

- [ ] **Step 4: Extend static readiness marker**

In `tools/check-dataagent-readiness.ps1`, extend the `Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs"` marker array inside `GraphHandshakeDevSidecarObservabilityContractPresent` so it includes all nine literals:

```powershell
@("DataAgentGraphSidecarObservabilitySnapshot", "DataAgentGraphSidecarObservabilityStatus", "DataAgentGraphSidecarObservabilityReasonCodes", "graph_sidecar_disabled", "graph_sidecar_not_configured", "graph_sidecar_runtime_unavailable", "graph_sidecar_response_rejected", "graph_sidecar_progress_rejected", "graph_sidecar_accepted", "graph_sidecar_fallback_used", "graph_sidecar_stream_final_response_missing", "graph_sidecar_stream_final_response_rejected", "DataAgentGraphSidecarObservabilityContext", "NetworkAttempted", "RuntimeStartedByAlife")
```

Do not change the static readiness count. The script must remain:

```powershell
$expectedRequired = 91
```

- [ ] **Step 5: Run readiness tests and script**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: pass with `24` tests or more if the local test count changes only because this task added one test.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 91 required passed, 0 required missing
```

- [ ] **Step 6: Commit readiness hardening**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1
git commit -m "Harden DataAgent V3.7 observability readiness codes"
```

---

### Task 3: Verify V3.7 Boundaries And Full DataAgent Tests

**Files:**
- Read only unless verification reveals a V3.7 regression.

- [ ] **Step 1: Run focused V3.7 tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.ObservabilityReasonCodesAreStableMachineTokens|FullyQualifiedName~DataAgentGraphHandshakeStreamCoordinatorTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: pass.

- [ ] **Step 2: Run DataAgent readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
PASS     GraphHandshakeDevSidecarObservabilityContractPresent
Summary: 91 required passed, 0 required missing
```

- [ ] **Step 3: Run QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Confirm QChat production boundary**

Run:

```powershell
rg --no-ignore -n "DataAgentGraphSidecarObservabilitySnapshot|DataAgentGraphSidecarObservabilityStatus|DataAgentGraphHandshakeStream|DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no output and exit code `1`.

- [ ] **Step 5: Confirm no SSE/runtime startup expansion**

Run:

```powershell
rg --no-ignore -n "EventSource|text/event-stream|uvicorn app:app|Start-Process|python -m venv|pip install" sources\Alife.Function\Alife.Function.DataAgent Tests\Alife.Test.DataAgent tools\check-dataagent-readiness.ps1
```

Expected:

- No production DataAgent runtime startup or SSE implementation.
- Existing matches are allowed only when they are defensive checks, such as `Does.Not.Contain`, `Test-FileOmitsMarker`, docs/static marker strings, or reverse probes.
- No V3.7 test starts Python, uvicorn, venv, pip, ports, or SSE.

- [ ] **Step 6: Run diff hygiene**

Run:

```powershell
git diff --check
```

Expected: exit code `0`.

- [ ] **Step 7: Run full DataAgent test project**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: pass. Existing live PostgreSQL tests may be skipped when `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is absent.

- [ ] **Step 8: Commit only if verification requires a fix**

If Task 3 requires no changes, do not create a commit. If a small V3.7 verification correction is required, run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1
git commit -m "Harden DataAgent V3.7 reason-code verification"
```

---

## Self-Review

- Spec coverage: Task 1 locks all nine exact reason-code literals in unit tests; Task 2 locks all nine exact literals in dynamic and static readiness; Task 3 verifies readiness counts, QChat boundary, no-SSE/no-runtime behavior, and full DataAgent tests.
- Scope check: the plan does not connect real LangGraph, implement SSE, start Python, install dependencies, bind ports, change sidecar transport behavior, modify QChat production source, or alter upload workflow.
- Count consistency: dynamic DataAgent readiness remains `76`; static DataAgent readiness remains `91`; QChat engineering map remains `63`.
- TDD discipline: Task 1 uses a mutation RED check because the production constants are already correct; Task 2 has natural failing tests because the readiness markers currently omit four literals.
- Type consistency: all referenced constants already exist in `DataAgentGraphSidecarObservabilityReasonCodes`; new helper `FindSourceBlock` is local to `DataAgentReadinessTests`.
