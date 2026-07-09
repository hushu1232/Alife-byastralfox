# DataAgent V3.10 LangGraph Runtime Readiness Contract Design

## Purpose

DataAgent V3.10 defines the readiness contract a real LangGraph runtime must satisfy before it can be introduced behind the existing DataAgent graph sidecar boundary.

V3.10 is a contract and admission-gate milestone. It does not add a real LangGraph runtime, start Python, install dependencies, create a virtual environment, bind ports, call a live sidecar from default tests, or put LangGraph into the default DataAgent chain.

The main outcome is a stable, reviewable gate between the V3.9 offline replay runbook and the earliest real LangGraph runtime milestone in V3.11.

## Current Context

The project already has a conservative DataAgent graph preparation path:

- V2.14 introduced a disabled-by-default LangGraph sidecar contract and C# authority boundary.
- V3.0 added the graph handshake boundary, scoped node manifests, validator, fallback behavior, and sanitized diagnostics.
- V3.1 added the optional local HTTP sidecar adapter.
- V3.2 added C#-owned sidecar progress bridging.
- V3.3 added optional NDJSON streaming transport while deferring SSE.
- V3.4 added a manual loopback live smoke harness for an already running dev sidecar.
- V3.6 added stable graph sidecar observability reason codes and diagnostics.
- V3.8 proved the DataAgent route/policy/analysis/diagnostics chain end to end.
- V3.9 packaged that chain into an offline replay runbook with Markdown and JSON output.

V3.10 uses those pieces to define the final pre-runtime admission rules. It should not skip directly to a real LangGraph implementation.

## Design Goals

1. Define the exact contract a real LangGraph sidecar must satisfy before V3.11 can introduce it.
2. Preserve C# authority over validation, SQL compilation, SQL safety, query execution, checkpoint mutation, diagnostics publication, Tool Broker routing, QChat visible text, and QQ ingress.
3. Keep V3.10 deterministic, offline, and safe for default tests.
4. Make the V3.11 and V3.12 handoff explicit:
   - V3.11 may add a manual loopback real LangGraph sidecar skeleton.
   - V3.12 must compare real sidecar output against V3.9 replay fixtures before advisory integration.
5. Add readiness markers that future agents can inspect before touching runtime code.

## Non-Goals

V3.10 does not:

- Add `langgraph`, Python runtime code, package installation, or dependency management.
- Replace `tools/dataagent-graph-sidecar/app.py` with a real LangGraph graph.
- Start FastAPI, uvicorn, Python, or any background process.
- Add process supervision, port allocation, or runtime lifecycle management.
- Change default DataAgent runtime behavior.
- Change QChat production routing or visible response behavior.
- Add live runtime tests to the default test suite.
- Grant the sidecar SQL, checkpoint, Tool Broker, diagnostics, evidence, audit, QChat, QQ, file, browser, desktop, plugin, or visible-text authority.
- Introduce SSE or browser-facing streaming.
- Modify upload flows or touch `D:\FOXD`.

## Selected Direction

Use a static readiness contract plus tests.

The implementation should add a DataAgent-facing contract document and readiness marker that prove the repository contains the V3.10 admission rules. It should avoid production runtime changes unless a very small constant or model addition is needed to make the readiness gate less brittle.

Recommended new marker:

```text
LangGraphRuntimeReadinessContractPresent
```

Recommended detail:

```text
manual_only=true;advisory_only=true;loopback_only=true;starts_runtime=false;installs_dependencies=false;no_sql_authority=true;no_checkpoint_mutation=true;no_visible_text=true;fallback_required=true;replay_parity_required=true;default_tests_live_runtime=false
```

The static DataAgent readiness count should increase by exactly one. The dynamic core readiness count should remain unchanged unless the implementation introduces a meaningful C# production marker with deterministic validation value.

## Runtime Admission Contract

A real LangGraph sidecar is admissible only after it can satisfy the existing C# graph sidecar contract without special casing.

The allowed endpoint surface remains:

```text
GET  /health
POST /handshake
POST /handshake-stream
```

The sidecar must be loopback-only for V3.11 and V3.12 manual runs:

```text
http://127.0.0.1:<port>
http://localhost:<port>
https://127.0.0.1:<port>
https://localhost:<port>
```

The sidecar must not require default tests to reach a live port. Any live check remains explicit and manual.

The response shape must remain compatible with the current C# models and validators:

- `DataAgentGraphHandshakeRequest`
- `DataAgentGraphHandshakeResponse`
- `DataAgentGraphHandshakeStreamEvent`
- `DataAgentGraphHandshakeValidator`
- `DataAgentGraphSidecarProgressBridge`
- `DataAgentGraphHandshakeDiagnosticsFormatter`

The runtime must not introduce an alternate JSON contract that bypasses C# validation.

## Authority Boundary

The real LangGraph sidecar may only be advisory.

Allowed advisory surfaces:

- Propose orchestration intent.
- Request an existing C# safety service.
- Return bounded trace or progress suggestions.
- Report deterministic fallback.

Forbidden authority surfaces:

- Authorize datasets, fields, operators, or limits.
- Provide executable SQL.
- Execute SQL.
- Decide Tool Broker route state.
- Mutate checkpoints.
- Write evidence.
- Write audit.
- Write progress.
- Write diagnostics.
- Send visible QChat text.
- Own QQ ingress.
- Read or write files through sidecar authority.
- Control browser, desktop, plugin, or external RAG management authority.

Every sidecar response remains untrusted input. C# remains responsible for validation, deterministic fallback, persistence, and any user-visible result.

## Runtime Lifecycle Boundary

V3.10 freezes these runtime lifecycle rules:

```text
starts_runtime=false
installs_dependencies=false
creates_venv=false
binds_port=false
supervises_process=false
default_tests_live_runtime=false
```

V3.11 may introduce a real LangGraph sidecar skeleton only as:

```text
manual_only=true
loopback_only=true
default_disabled=true
```

V3.11 must not make `DataAgentGraphSidecarContract.IsRuntimeAvailable` or default DataAgent behavior imply that LangGraph is part of normal execution. Runtime availability should remain an explicit manual/live-smoke condition until V4.0 advisory integration is designed and approved.

## Replay Parity Handoff

V3.9's offline replay runbook becomes the baseline for real runtime comparison.

V3.12 should add shadow/parity checks that compare the V3.9 default replay fixture against the real LangGraph sidecar output. The comparison should focus on contract fields rather than exact natural-language text:

- endpoint health contract,
- handshake accepted/rejected status,
- reason codes,
- selected node names,
- no-SQL authority flags,
- checkpoint and visible-text denial flags,
- bounded trace/progress shape,
- validator acceptance,
- fallback behavior,
- offline replay marker equivalence.

V3.12 parity must remain manual or explicitly live-gated. It must not become a default test dependency on Python, LangGraph, FastAPI, uvicorn, network, model calls, QQ, Postgres, browser automation, or external services.

## Documentation

Add a DataAgent document:

```text
docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md
```

The document should explain:

- V3.10 is not runtime integration.
- Real LangGraph earliest appears in V3.11 as manual loopback skeleton.
- Replay parity is required in V3.12 before advisory integration.
- C# remains the authority.
- Default tests remain offline.
- The allowed endpoints are `/health`, `/handshake`, and `/handshake-stream`.
- The sidecar may be advisory only.

The existing dev sidecar README may be updated with a short V3.10 note, but only if it improves handoff clarity. It should not imply the current dev sidecar is already a real LangGraph runtime.

## Readiness

Add required static readiness marker:

```text
LangGraphRuntimeReadinessContractPresent
```

The marker should prove:

- V3.10 contract doc exists.
- The contract says real LangGraph is not introduced in V3.10.
- The contract says V3.11 is manual-only and loopback-only.
- The contract says V3.12 replay parity is required.
- The contract names `/health`, `/handshake`, and `/handshake-stream`.
- The contract preserves C# validator authority.
- The contract forbids SQL, checkpoint mutation, visible text, Tool Broker route authority, QChat authority, and QQ ingress.
- The contract says default tests do not require a live runtime.

Static readiness should move from:

```text
93 required passed, 0 required missing
```

to:

```text
94 required passed, 0 required missing
```

Dynamic readiness should stay at the V3.9 count unless the implementation adds a deterministic production marker. If it does, that marker must prove contract presence only, not live runtime availability.

## Tests

Required deterministic tests:

- Static readiness script contains `LangGraphRuntimeReadinessContractPresent`.
- Static readiness expected required count is updated from `93` to `94`.
- The marker detail includes `manual_only=true`, `advisory_only=true`, `loopback_only=true`, `starts_runtime=false`, `installs_dependencies=false`, `no_sql_authority=true`, `no_checkpoint_mutation=true`, `no_visible_text=true`, `fallback_required=true`, `replay_parity_required=true`, and `default_tests_live_runtime=false`.
- The V3.10 DataAgent doc contains the endpoint names `/health`, `/handshake`, and `/handshake-stream`.
- The V3.10 DataAgent doc names V3.11 as the earliest real runtime milestone and V3.12 as the replay parity milestone.
- The V3.10 DataAgent doc does not describe starting Python, installing packages, or making live runtime tests default.
- Existing V3.8 and V3.9 markers remain present.

Recommended new test file:

```text
Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs
```

Focused verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV310ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Readiness verification:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Full DataAgent verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

No V3.10 default test may require:

- Python,
- LangGraph,
- FastAPI,
- uvicorn,
- a live port,
- network access,
- live QChat,
- QQ or NapCat,
- PostgreSQL,
- browser automation,
- model calls.

## Implementation Scope

Expected files:

- Add `docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md`.
- Add `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`.
- Modify `tools/check-dataagent-readiness.ps1`.
- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`.
- Optionally modify `tools/dataagent-graph-sidecar/README.md` with a short V3.10 handoff note.

Files that should remain unchanged unless a readiness-test gap proves otherwise:

- `sources/Alife.Function/Alife.Function.QChat/**`
- `tools/dataagent-graph-sidecar/app.py`
- `tools/run-dataagent-graph-sidecar-smoke.ps1`
- `tools/replay-dataagent-chain.ps1`
- `tools/dataagent-replay/**`
- upload scripts
- Python dependency files

Production C# should remain unchanged by default. If implementation adds a production marker, it must not imply real runtime availability.

## Version Handoff

The intended sequence is:

```text
V3.10  LangGraph runtime readiness contract
       No real runtime.

V3.11  Real LangGraph sidecar skeleton
       Manual-only, loopback-only, default-disabled.

V3.12  Replay parity / shadow comparison
       Compare real LangGraph output against V3.9 replay fixture expectations.

V4.0   Advisory runtime integration
       LangGraph may influence suggestions only; C# remains authority.
```

Earliest real LangGraph touch:

```text
V3.11
```

Earliest replay comparison:

```text
V3.12
```

Earliest default chain involvement:

```text
V4.0 advisory mode only
```

## Acceptance Criteria

V3.10 is complete when:

1. The V3.10 DataAgent contract doc exists.
2. The doc clearly states that V3.10 does not introduce a real LangGraph runtime.
3. The doc defines the allowed endpoint surface: `/health`, `/handshake`, `/handshake-stream`.
4. The doc states that V3.11 is the earliest real runtime milestone and is manual-only, loopback-only, and default-disabled.
5. The doc states that V3.12 replay parity against the V3.9 replay baseline is required before advisory integration.
6. The doc preserves C# authority and forbids sidecar authority over SQL, checkpoints, Tool Broker routing, diagnostics writing, visible QChat text, and QQ ingress.
7. Static readiness includes `LangGraphRuntimeReadinessContractPresent`.
8. Static readiness reports `94 required passed, 0 required missing`.
9. Focused readiness tests pass.
10. Full DataAgent tests pass with live runtime tests skipped or absent by default.

## Self-Review

- Scope is limited to a pre-runtime readiness contract and deterministic tests.
- The design does not start Python, install LangGraph, bind ports, or change default runtime behavior.
- The authority boundary is explicit and consistent with V2.14 through V3.9.
- The V3.11, V3.12, and V4.0 handoff dates are version milestones, not automatic runtime activation.
- Readiness count behavior is explicit.
- The implementation can be completed without touching QChat production code or Python runtime files.
