# DataAgent V4.7 Live Canary Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute and persist a real 20-request LangGraph production-shadow canary with stable runtime/config identity, seven live loopback fault drills, safe aggregates, and restored default-off controls.

**Architecture:** The Python sidecar attests one startup identity and a canonical configuration fingerprint. A dedicated operator-owned .NET 9 canary tool drives the real C# HTTP client, V4.4 decorator, coordinator, and V4.5 recorder against the real sidecar; isolated `TcpListener` responders exercise failures without adding fault endpoints to the sidecar. A PowerShell wrapper owns the Python process lifecycle and always restores the local kill-switch/default-off posture.

**Tech Stack:** Python 3.12/`unittest`, LangGraph 0.3.34, .NET 9/C#/NUnit, `TcpListener`, `HttpClient`, PowerShell 5.1-compatible process orchestration.

**Scope boundary:** V4.7 proves production-shadow transport and safety only. It adds no model provider, business planner, SQL/tool authority, checkpoint mutation, QChat-visible text, or default-enabled behavior. Those remain V4.8/V4.9 work.

---

### Task 1: Attest stable runtime identity and canonical configuration

**Files:**
- Modify: `tools/dataagent-langgraph-sidecar/runtime.py`
- Modify: `tools/dataagent-langgraph-sidecar/contracts.py`
- Modify: `tools/dataagent-langgraph-sidecar/tests/test_runtime.py`
- Modify: `tools/dataagent-langgraph-sidecar/tests/test_server.py`

- [ ] Add failing Python tests requiring one UUID-shaped `runtimeInstanceId` and one `startedAtUnixSeconds` value to remain unchanged across repeated health calls from the same `RuntimeState`, while two fresh states have different instance IDs.
- [ ] Add a failing fingerprint test requiring lowercase SHA-256 over this newline-delimited canonical value sequence only:

```text
langgraph
0.3.34
v4.7
dataagent-advisory-v1
65536
65536
```

Expected API:

```python
RuntimeState.create(
    mode: str,
    langgraph_version: str | None,
    compiled_graph: object | None,
    now_unix_seconds: Callable[[], int],
    instance_id_factory: Callable[[], str],
) -> RuntimeState
```

- [ ] Add failing tests that reject blank/non-UUID identity, non-64-hex fingerprint, non-positive startup time, stub `ready=true`, or any health field outside the fixed V4.7 schema.
- [ ] Run `python -m unittest discover tools/dataagent-langgraph-sidecar/tests -v` and verify RED for missing V4.7 identity fields.
- [ ] Implement immutable startup identity, canonical fingerprint generation, `contractVersion=v4.7`, and strict health validation. The fingerprint input must not include endpoint, environment values, credentials, paths, request data, or free-form text.
- [ ] Run all sidecar tests and verify GREEN with no port binding.
- [ ] Commit with `feat(dataagent): attest v4.7 runtime identity`.

### Task 2: Evaluate and format safe V4.7 closure evidence

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV47LiveCanaryClosure.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV47LiveCanaryArtifactWriter.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV47LiveCanaryTests.cs`

- [ ] Write failing tests for these exact public records:

```csharp
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
```

- [ ] Require exact hard gates: capacity 256, window 15 minutes, at least 20 coherent observations, fallback ratio <=2500 basis points, P95 <=2000 ms, no retry storm, restart count 0..1, all seven V4.5 drills valid, UUID identity, lowercase 64-hex fingerprint, positive start time, stable identity, kill switch restored, and production shadow restored disabled.
- [ ] Require a failure-specific stable reason for every rejected gate; no score may compensate for a failed safety gate.
- [ ] Write failing formatter/writer tests requiring file name `dataagent-v4.7-live-canary-closure.txt`, fixed aggregate/identity fields, exact seven drills, and absence of request/response/endpoint/SQL/token/hidden-context/exception/path labels.
- [ ] Run the V4.7 tests and verify RED because closure types do not exist.
- [ ] Implement the evaluator, formatter, and explicit writer. Rejected writer input must not write a file; successful return metadata may contain the operator output path, while artifact content must not.
- [ ] Run V4.7 tests and verify GREEN.
- [ ] Commit with `feat(dataagent): evaluate v4.7 live canary closure`.

### Task 3: Drive 20 real handshakes through the governed C# shadow path

**Files:**
- Create: `tools/dataagent-v47-canary/Alife.Tools.DataAgentV47Canary.csproj`
- Create: `tools/dataagent-v47-canary/Program.cs`
- Create: `tools/dataagent-v47-canary/DataAgentV47CanaryRunner.cs`
- Create: `tools/dataagent-v47-canary/DataAgentV47CanaryRequestFactory.cs`
- Modify: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV47CanaryRunnerTests.cs`

- [ ] Write failing argument tests requiring `--endpoint`, `--output`, `--request-count 20`, `--timeout-ms`, and `--runtime-restart-count`; reject non-loopback endpoints, request count below 20 or above 256, timeout outside 100..10000 ms, and restart count outside 0..1.
- [ ] Write failing runner tests using a real loopback responder and these real components:

```text
DataAgentGraphHandshakeHttpClient
-> DataAgentV44ProductionShadowClient
-> DataAgentGraphHandshakeCoordinator
-> DataAgentV45ProductionObservationRecorder
```

Require 20 accepted outcomes, 20 network attempts, zero fallback, coherent snapshot counts, and health identity equality before/after the window.
- [ ] Require every canary request to use a new bounded request/session/turn identifier, default manifest inventory, `NoSqlAuthority=true`, `ReadOnly=true`, and `FallbackAvailable=true`; persist none of those identifiers.
- [ ] Run the focused tests and verify RED because the tool/runner does not exist.
- [ ] Implement the tool as a thin CLI over a reusable runner. Use ready V4.4 options only inside the operator process; do not set process-global Alife environment variables. Query health before and after the window and reject changed identity/fingerprint.
- [ ] Make `Program.Main` print fixed safe status fields only and return zero only for an accepted V4.7 result. It must not print endpoint, payloads, exception messages, IDs other than runtime UUID, or artifact absolute path.
- [ ] Run runner tests and verify GREEN.
- [ ] Commit with `feat(dataagent): run real v4.7 shadow canary`.

### Task 4: Execute seven live drills with isolated loopback responders

**Files:**
- Create: `tools/dataagent-v47-canary/LoopbackFaultResponder.cs`
- Create: `tools/dataagent-v47-canary/DataAgentV47LiveFaultDrillRunner.cs`
- Modify: `tools/dataagent-v47-canary/DataAgentV47CanaryRunner.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV45ProductionClosure.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV47CanaryRunnerTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV45ProductionClosureTests.cs`

- [ ] Write failing tests that bind only `IPAddress.Loopback` on an OS-assigned port and prove each actual network boundary:

```text
closed port                 -> production_shadow_unavailable / network=true
delayed HTTP response       -> production_shadow_timeout / network=true
malformed JSON response     -> production_shadow_invalid_response / network=true
unsafe authority response   -> sql_authority_requested / network=true
blocked first response      -> production_shadow_busy / network=false
fail then clock advance     -> production_shadow_circuit_open / network=false, then recovery
live options kill switch    -> production_shadow_kill_switch_active / network=false
```

- [ ] Require exact seven unique `DataAgentV45FaultDrillKind` values and re-evaluate them with `DataAgentV45ProductionFaultDrillEvaluator`; no production-sidecar fault route or external endpoint may be used.
- [ ] Extend the invalid-schema drill allowlist with `production_shadow_invalid_response`; retain `invalid_response_schema` and `request_id_mismatch`, and reject every other reason.
- [ ] Add tests proving responders are disposed, listeners stop after success/failure, timeouts are bounded, no retry occurs, and response bodies/logs contain no sensitive fixture text.
- [ ] Run focused tests and verify RED for missing responder/drill runner.
- [ ] Implement minimal raw HTTP responders using `TcpListener`, bounded request draining, fixed response bytes, cancellation, and `IAsyncDisposable`. Keep injected clocks/options local to circuit/kill drills.
- [ ] Run focused tests and verify GREEN, then run all V4.4/V4.5/V4.7 tests.
- [ ] Commit with `feat(dataagent): execute v4.7 live fault drills`.

### Task 5: Orchestrate an operator-owned live sidecar and persist the artifact

**Files:**
- Create: `tools/run-dataagent-v47-live-canary.ps1`
- Modify: `tools/run-dataagent-langgraph-manual-smoke.ps1`
- Modify: `tools/dataagent-langgraph-sidecar/README.md`
- Create: `docs/dataagent/dataagent-v4.7-live-canary-closure.md`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV47LiveCanaryScriptTests.cs`

- [ ] Write failing static/script tests requiring parameters `-Python`, `-Port`, `-OutputDirectory`, `-RequestCount`, and `-RuntimeRestartCount`; default port 8765 and request count 20; reject non-loopback host and output under tracked source directories.
- [ ] Require the script to start only `tools/dataagent-langgraph-sidecar/server.py` with `--runtime-mode langgraph`, record the returned process object, wait a bounded 10 seconds for V4.7 health, run the five-item V4.6 smoke, build/run the V4.7 .NET tool, and stop only the process it owns in `finally`.
- [ ] Change the five-item smoke harness to accept `-ExpectedContractVersion` with default `v4.7`; accept only `^v4\.[0-9]+$`, keep the five checks unchanged, and have the V4.7 runner pass `v4.7` explicitly.
- [ ] Require `finally` markers and tool arguments proving kill-switch restored and production-shadow restored disabled. The script must never start/stop QQ or NapCat, install packages, create a venv, use a non-loopback endpoint, print credentials, or recursively delete/move files.
- [ ] Run script/readiness tests and verify RED because the V4.7 operator script/doc do not exist.
- [ ] Implement the PowerShell 5.1-compatible wrapper with `Start-Process -WindowStyle Hidden -PassThru`, bounded readiness polling, `Stop-Process -Id <owned id>`, and no background process leak. Use `Outputs/dataagent-v4.7-live-canary` as the default untracked artifact directory.
- [ ] Document exact operator preparation, command, five smoke checks, 20-request window, seven drill meanings, artifact schema, shutdown ownership, rollback/default-off restoration, and the V4.8 entry gate.
- [ ] Run PowerShell parser validation and script tests; verify GREEN without starting a process in automated tests.
- [ ] Commit with `docs(dataagent): add v4.7 live canary operator runbook`.

### Task 6: Run the authorized live canary and close V4.7 readiness

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3ClosureManifest.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs`
- Modify: readiness count assertions under `Tests/Alife.Test.DataAgent/`
- Generate outside Git: `Outputs/dataagent-v4.7-live-canary/dataagent-v4.7-live-canary-closure.txt`

- [ ] Add failing dynamic/static readiness tests for `GraphHandshakeV47LiveCanaryClosurePresent`; require implementation/runbook markers and an explicit artifact verifier command, while static readiness must not claim a local artifact exists.
- [ ] Add V4.7 to V4-only/post-V3 sets; advance dynamic 103->104 and static 119->120 while preserving frozen V3 111/95 exactly.
- [ ] Implement readiness and artifact verification. The verifier must parse fixed keys, reject duplicates/unknown keys, require `accepted=true`, validate all thresholds/identity/drills, and print no artifact path or payload.
- [ ] Run Python tests, focused .NET tests, and static readiness; verify GREEN before live execution.
- [ ] Execute the authorized operator runner against an owned real sidecar:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/run-dataagent-v47-live-canary.ps1 `
  -Python python `
  -Port 8765 `
  -OutputDirectory Outputs/dataagent-v4.7-live-canary `
  -RequestCount 20 `
  -RuntimeRestartCount 0
```

Expected: exactly five smoke passes, 20 accepted canary observations, seven accepted drills, accepted artifact, owned Python process stopped, and default-off restoration reported true.
- [ ] Run the artifact verifier independently and inspect only fixed safe fields. Confirm no sidecar/port remains and no QQ/NapCat process was changed.
- [ ] Commit readiness/source/test changes with `docs(dataagent): close v4.7 live canary readiness`. Do not commit the generated artifact.

### Completion audit and verification

- [ ] Map every V4.7 design requirement to current evidence: identity/fingerprint, real graph health, five smoke requests, 20 governed real requests, seven live drills, aggregate thresholds, safe artifact, owned process cleanup, kill/default restoration, readiness totals, and V3 freeze.
- [ ] Run fresh verification:

```powershell
python -m unittest discover tools/dataagent-langgraph-sidecar/tests -v
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter 'DataAgentV44ProductionShadow|DataAgentV45ProductionClosure|DataAgentV47|DataAgentReadiness|DataAgentV3ClosureManifest' -v:minimal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore -v:minimal
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
git diff --check
git status --short --branch
```

- [ ] Record exact Python, focused .NET, DataAgent, solution, static/dynamic readiness, and live canary counts. Confirm the worktree is clean and the live artifact remains untracked under `Outputs`.
- [ ] Do not start V4.8 until the independent V4.7 artifact verifier accepts the fresh live artifact. Do not push, merge, create a PR, install dependencies, or operate QQ/NapCat.

V4.7 is complete only when the real live artifact is accepted and all automated/full regressions pass. Harness code or deterministic fixtures alone are insufficient.
