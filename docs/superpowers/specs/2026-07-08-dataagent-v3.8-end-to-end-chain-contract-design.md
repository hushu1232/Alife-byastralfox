# DataAgent V3.8 End-to-End Chain Contract Design

## Purpose

DataAgent V3.8 proves the existing DataAgent runtime chain as a stable offline contract. It does not expand graph sidecar authority, start a production LangGraph runtime, add Python process management, change QChat production routing, or introduce live QQ/NapCat requirements.

The V3.8 goal is to make the full path auditable and regression-safe:

```text
QChat route state
-> Tool Broker route decision
-> XML execution policy
-> DataAgent analysis tool handler
-> analysis orchestrator
-> DataAgentService planner/validator/SQL safety/store query
-> analysis context publication
-> active DataAgent session routing state
-> evidence/trace/progress/graph diagnostics
-> owner diagnostics command formatting
```

V3.8 should make this path testable with deterministic local tests and readiness markers while preserving the authority boundaries established in V3.0 through V3.7.

## Current Chain Summary

DataAgent module startup is owned by `DataAgentModuleService.AwakeAsync`. It initializes the store, imports fixtures, creates `DataAgentService`, `DataAgentAnalysisService`, `DataAgentAnalysisOrchestrator`, progress diagnostics, trace recording, graph handshake coordination, and registers DataAgent XML handlers with `XmlFunctionCaller`.

QChat enters the route chain through `QChatService.DispatchToModelAsync`. For each model dispatch, it creates a `ToolRouteState` from sender role, private/group surface, and trusted runtime status, then scopes that state through `XmlFunctionCaller.UseToolRouteState`.

`XmlFunctionCaller.OnChatSend` calls `RouteCurrentTurn`, stores the current `ToolRouteDecision`, and injects a `[tool_route_context]` guide containing only the XML tools allowed for the current turn. `XmlFunctionExecutionPolicy.TryConsume` enforces the same route at execution time and separately checks session-scoped DataAgent tools.

`DataAgentAnalysisToolHandler` exposes `dataagent_analysis_start`, `dataagent_analysis_continue`, `dataagent_analysis_summarize`, and `dataagent_analysis_end`. Each handler reads `DataAgentToolRouteContext`, calls the orchestrator, builds an analysis session context block, publishes diagnostics, and updates the active route session through the module-level publisher.

`DataAgentAnalysisOrchestrator` owns the route-gated node sequence. Successful query-producing turns move through `RouteGate`, `SchemaContext`, `Plan`, `Validate`, `Execute`, `Explain`, and `Checkpoint`. Denied routes produce `RouteGate`, `Reject`, and `Checkpoint` without `Execute`.

`DataAgentService.Answer` owns query authority. It validates planner envelopes, validates query plans, compiles SQL, applies SQL safety, executes only through the configured store, and records accepted or rejected audit inputs.

Owner-facing diagnostics are currently cached in `XmlFunctionCaller` and `QChatService`, then exposed through `/dataagent diag evidence`, `/dataagent diag trace`, `/dataagent diag progress`, and `/dataagent diag graph`.

## Design Goals

1. Prove the DataAgent chain end to end without live QQ, live sidecar, network, Python, PostgreSQL, browser automation, or model calls.
2. Lock the Tool Broker and XML execution policy boundary for DataAgent tools.
3. Prove active analysis session propagation from DataAgent context back into route state.
4. Prove `continue`, `summarize`, and `end` stay session-scoped.
5. Prove accepted analysis emits evidence, trace, progress, and graph diagnostics usable by owner diagnostics commands.
6. Prove route denial cannot execute SQL.
7. Keep sidecar output advisory and preserve no-SQL, no-checkpoint, no-QChat, no-QQ, no-visible-text authority.
8. Add readiness markers so future agents can quickly see that the full chain contract is present.

## Non-Goals

V3.8 does not:

- Start a Python sidecar.
- Install Python, FastAPI, uvicorn, LangGraph, or external dependencies.
- Add SSE or browser-facing streaming.
- Add a production graph runtime.
- Grant the sidecar SQL authority, checkpoint authority, Tool Broker authority, diagnostics authority, evidence authority, QChat authority, QQ authority, plugin authority, file authority, browser authority, or visible-text authority.
- Modify QChat production behavior unless tests reveal an existing seam cannot be exercised without a small, targeted testability hook.
- Change DataAgent planner semantics, datasets, SQL compiler behavior, or store query behavior except where needed to expose stable diagnostics in tests.
- Change upload scripts or GitHub workflow.

## Proposed Architecture

### 1. Offline Chain Contract Test

Add a focused NUnit test file:

```text
Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs
```

This file owns the V3.8 contract. It should construct the chain with fake or in-memory collaborators rather than invoking QChat, network, sidecar processes, or the live model.

The tests should exercise the real core types wherever practical:

- `ToolCapabilityRouter`
- `XmlFunctionExecutionPolicy`
- `XmlFunctionCaller` route state behavior when feasible
- `DataAgentAnalysisToolHandler`
- `DataAgentAnalysisOrchestrator`
- `DataAgentAnalysisService`
- `DataAgentService`
- in-memory or fixture-backed `IDataAgentStore`
- `DataAgentEvidencePackBuilder`
- `DataAgentTraceTimelineBuilder`
- `DataAgentDataQueryGraphPilot`
- `DataAgentGraphHandshakeCoordinator` in disabled or fake-sidecar mode
- `QChatDiagnosticsService` formatting for `/dataagent diag ...`

If a full `XmlFunctionCaller` instance is too heavy for the focused tests, the contract can use the same public route-state and execution-policy APIs directly, then add source-level assertions that QChat still uses `CreateToolRouteState` and `UseToolRouteState` in `DispatchToModelAsync`.

### 2. Route And Execution Boundary Contract

The contract should cover these route cases:

- Trusted owner private new analysis: `dataagent_query` and `dataagent_analysis_start` are allowed.
- Trusted owner private with active analysis session: `dataagent_query`, `dataagent_analysis_continue`, `dataagent_analysis_summarize`, and `dataagent_analysis_end` are allowed.
- Non-owner private: DataAgent tools are denied with owner/private reason semantics.
- Owner group: DataAgent tools are denied with owner/private reason semantics.
- Untrusted runtime: DataAgent tools are denied with trusted-runtime reason semantics.
- Ordinary chat text: DataAgent tools are denied as not matched.

Execution policy coverage should prove governed DataAgent tools cannot execute when:

- there is no current route,
- the current route does not allow the tool,
- a session-scoped tool is called without `sessionid`,
- a session-scoped tool is called with a different `sessionid`.

The positive path should prove a session-scoped tool is allowed only when the route has a live active session and the XML context carries the same `sessionid`.

### 3. Analysis Session Propagation Contract

After a successful `dataagent_analysis_start`, the returned context should contain a `session_id` and live `status`. Passing that context through `XmlFunctionCaller.UpdateDataAgentAnalysisRouteSessionFromContext` should make `CreateToolRouteState(isOwner: true, isPrivateChat: true)` report a live active session.

The follow-up route should then allow session-scoped DataAgent tools. If the analysis context reports a terminal or rejected status, the active session should be cleared.

This proves the runtime can move from "start analysis" to "continue existing analysis" without hidden global state outside the existing context publication path.

### 4. Orchestration And SQL Authority Contract

The successful query-producing path should assert:

- `RouteGate` succeeds.
- `Plan` appears.
- `Validate` succeeds.
- `Execute` appears with `ExecutedSql=true`.
- `Explain` appears.
- `Checkpoint` appears.
- the `DataAgentAnswer` is validated.
- accepted audit is recorded by the store.

The route-denied path should assert:

- `RouteGate` is rejected.
- `Reject` appears.
- `Checkpoint` appears.
- `Execute` does not appear.
- rejected progress is published.
- no store query is executed.

The terminal path should assert:

- `Summarize` and `End` do not execute SQL.
- terminal actions require route permission for their specific tools.
- terminal actions preserve session-scoped checks.

### 5. Diagnostics Closure Contract

After an accepted analysis turn, V3.8 should prove these diagnostics exist and are bounded:

- evidence diagnostics from `DataAgentEvidenceDiagnosticsFormatter`,
- trace diagnostics from `DataAgentTraceDiagnosticsFormatter`,
- progress diagnostics from `DataAgentProgressDiagnosticsFormatter` through `DataAgentProgressDiagnosticsPublisher`,
- graph diagnostics from `DataAgentDataQueryGraphTraceFormatter` and `DataAgentGraphHandshakeDiagnosticsFormatter`.

The test should feed these diagnostics into `QChatDiagnosticsService.TryHandle` or the existing QChat owner diagnostics command surface and assert that:

- `/dataagent diag evidence` returns evidence diagnostics,
- `/dataagent diag trace` returns trace diagnostics,
- `/dataagent diag progress` returns progress diagnostics,
- `/dataagent diag graph` returns DataQueryGraph or graph handshake diagnostics,
- missing diagnostics produce stable fallback text,
- returned text stays sanitized and does not expose raw SQL authority claims from the sidecar.

If using `QChatDiagnosticsService` directly is sufficient, no QChat production code should change.

### 6. Readiness Marker

Extend dynamic and static readiness with one new marker:

```text
DataAgentEndToEndChainContractPresent
```

The marker should require evidence that:

- the V3.8 test file exists,
- route-state contract coverage exists,
- XML execution-policy coverage exists,
- analysis session propagation coverage exists,
- route-denied no-execute coverage exists,
- diagnostics closure coverage exists,
- sidecar authority remains absent,
- default tests remain offline.

`DataAgentReadiness.cs` should increase its dynamic DataAgent readiness count by one. `tools/check-dataagent-readiness.ps1` should increase its static expected required count by one. The plan should update exact expected values based on current repository state at implementation time.

## Data Flow

The V3.8 contract should prove this data flow:

1. A user utterance and route state enter the Tool Broker.
2. Tool Broker emits `ToolRouteDecision`.
3. The routed guide exposes only allowed tools to the model.
4. XML execution policy enforces the same decision at tool-call time.
5. `dataagent_analysis_start` receives route context and creates a DataAgent analysis session.
6. The orchestrator records route, planning, validation, execution, explanation, and checkpoint steps.
7. `DataAgentService` validates and executes a read-only query or fails closed.
8. Analysis context is published back to the runtime.
9. Runtime route state learns the active session id from context.
10. Follow-up DataAgent tools are allowed only for the active session.
11. Diagnostics are published and retrievable through owner diagnostics commands.
12. Graph sidecar handshake diagnostics remain advisory and bounded.

## Error Handling

V3.8 should preserve fail-closed behavior:

- Missing route context rejects DataAgent tools.
- Untrusted runtime rejects DataAgent tools.
- Non-owner or non-private surfaces reject DataAgent tools.
- Missing active session rejects `continue`, `summarize`, and `end`.
- Session id mismatch rejects session-scoped tools.
- Planner envelope errors fail before SQL execution.
- Query plan validation errors fail before SQL compilation or execution.
- SQL safety errors fail before store execution.
- Route-denied orchestration emits no `Execute` step.
- Graph sidecar disabled, unavailable, invalid, timeout, rejected, or fallback states remain diagnostics only.

Reason codes used in assertions should be stable literal strings already present in production code. If a new V3.8 reason code is needed, it should be introduced as a constant and covered by exact-literal tests.

## Testing Strategy

The primary verification command should be the DataAgent test project:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Focused V3.8 verification should include:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEndToEndChainContractTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Readiness verification should include:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

No default V3.8 test may require:

- Python,
- FastAPI,
- uvicorn,
- live sidecar,
- live port,
- network access,
- live QChat,
- QQ/NapCat,
- PostgreSQL,
- browser automation,
- model calls.

## Implementation Scope

Expected files:

- Add `Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs`.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`.
- Modify `tools/check-dataagent-readiness.ps1`.
- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`.
- Optionally add a small DataAgent-owned chain diagnostics snapshot or formatter only if direct existing formatter composition is too brittle for stable tests.

Files that should remain unchanged unless a testability gap is discovered:

- `sources/Alife.Function/Alife.Function.QChat/**`
- `tools/dataagent-graph-sidecar/**`
- `tools/run-dataagent-graph-sidecar-smoke.ps1`
- upload scripts
- Python runtime files

## Success Criteria

V3.8 is complete when:

1. DataAgent has an offline end-to-end chain contract test.
2. Route and XML execution policy boundaries are covered for positive and negative DataAgent cases.
3. Analysis session context updates active route session state.
4. Session-scoped tools are denied on missing or mismatched session ids.
5. Accepted analysis emits evidence, trace, progress, and graph diagnostics.
6. Owner diagnostics commands can retrieve those diagnostics.
7. Route-denied analysis emits no SQL execution.
8. Terminal analysis actions do not execute SQL.
9. Sidecar authority remains explicitly absent and tested.
10. DataAgent readiness reports the V3.8 marker.
11. Static readiness count and dynamic readiness count are updated exactly once.
12. Default test commands pass without live services.

## Design Decisions

The implementation should compose diagnostics from existing formatter outputs. A new DataAgent-owned chain snapshot type is out of scope for V3.8 unless direct formatter composition proves impossible without brittle reflection or production-only wiring.

The primary chain test should use a fake or in-memory `IDataAgentStore` with query and audit counters so route-denied no-execute behavior is proven directly. A separate focused test may use the existing SQLite fixture path only if realistic SQL compilation and query result shape cannot be covered by the fake store.
