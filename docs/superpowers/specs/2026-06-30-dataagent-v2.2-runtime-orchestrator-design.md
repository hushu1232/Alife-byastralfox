# DataAgent V2.2 Runtime Orchestrator Design

## Goal

DataAgent V2.2 turns the V2.1 native C# analysis orchestrator from a tested internal capability into the real runtime entry path for DataAgent analysis XML tools.

The goal is to make every `dataagent_analysis_*` call pass through `IDataAgentAnalysisOrchestrator`, return inspectable orchestration trace and checkpoint context, and preserve the existing `DataAgentAnalysisService` state machine as the source of truth.

V2.2 is still not the LangGraph release. It is the runtime integration step that makes the later LangGraph or multi-agent linked execution work credible: the node boundaries, terminal semantics, route gate behavior, and checkpoint contract must be stable before an external graph engine is introduced.

## Current Baseline

V2.1 added these orchestration concepts:

- `IDataAgentAnalysisOrchestrator`
- `DataAgentAnalysisOrchestrator`
- `DataAgentOrchestrationNodeKind`
- `DataAgentOrchestrationStep`
- `DataAgentOrchestrationCheckpoint`
- `DataAgentOrchestrationRequest`
- `DataAgentOrchestrationResult`

The orchestrator already models governed analysis paths:

- accepted query path: `RouteGate -> SchemaContext -> Plan -> Validate -> Execute -> Explain -> Checkpoint`
- clarification path: `RouteGate -> SchemaContext -> Plan -> Validate(Skipped) -> Clarification -> Checkpoint`
- rejected planner output path: `RouteGate -> SchemaContext -> Plan -> Validate(Rejected) -> Reject -> Checkpoint`
- route-denied query path: `RouteGate -> Reject -> Checkpoint`
- terminal summarize/end path: `Summarize|End -> Checkpoint`

The remaining weakness is runtime integration. `DataAgentAnalysisToolHandler` still directly calls `DataAgentAnalysisService`, so the real XML tool path can bypass the orchestrator. That means V2.1 proves that the orchestrator exists and behaves correctly in isolation, but it does not yet prove that production analysis tool calls are orchestrated.

## Design Decision

Use a conservative runtime integration:

```text
XML Tool Handler
  -> IDataAgentAnalysisOrchestrator
  -> DataAgentAnalysisService
  -> DataAgentService
  -> IDataAgentStore
```

`DataAgentAnalysisToolHandler` should depend on `IDataAgentAnalysisOrchestrator`, not `DataAgentAnalysisService`.

`DataAgentAnalysisOrchestrator` should continue to delegate state mutation to `DataAgentAnalysisService`. The orchestrator records and enforces workflow boundaries; it does not duplicate the session state machine or SQL execution pipeline.

`DataAgentAnalysisService` remains the owner of:

- session creation
- follow-up intent interpretation effects
- active/awaiting/summarized/ended status transitions
- turn append behavior
- terminal summary generation
- `DataAgentAnalysisContextProvider.Build(...)` base context generation

This keeps V2.2 small, fast, and safe while making the orchestrator a real runtime boundary.

## Non-Goals

V2.2 does not introduce LangGraph, Python sidecars, distributed workers, streaming front-end progress, external dashboards, chart rendering, new business datasets, or live PostgreSQL requirements.

V2.2 does not migrate the state machine from `DataAgentAnalysisService` into the orchestrator.

V2.2 does not change `DataAgentService` planner, SQL compiler, store boundary, query executor, or result explanation responsibilities.

V2.2 does not weaken Tool Broker fail-closed behavior, SQL safety, prompt leak controls, terminal no-query behavior, or QChat engineering readiness gates.

V2.2 does not require live LLM or live PostgreSQL tests in the default suite.

## Runtime Contract

### XML Handler

`DataAgentAnalysisToolHandler` should expose the same XML functions and argument validation:

- `dataagent_analysis_start(callerId, goalOrQuestion)`
- `dataagent_analysis_continue(sessionId, question)`
- `dataagent_analysis_summarize(sessionId)`
- `dataagent_analysis_end(sessionId)`

The handler should call the orchestrator for each method and publish the final response context through the existing `resultPublisher`.

The handler should not call `DataAgentAnalysisService` directly.

### Orchestrator

`IDataAgentAnalysisOrchestrator` should remain the runtime-facing boundary for analysis sessions. It should provide methods for:

- starting a session
- continuing a session
- summarizing a session
- ending a session

Each method should return `DataAgentOrchestrationResult`.

The result must include:

- `SessionId`
- `SessionStatus`
- ordered `Steps`
- `Checkpoint`
- `Response`

### Context Output

The final context returned by XML tools should include the existing `[data_agent_analysis_session_context]` block and append orchestration fields.

Required context fields:

```text
orchestration_trace=RouteGate:Succeeded>SchemaContext:Succeeded>Plan:Succeeded>Validate:Succeeded>Execute:Succeeded>Explain:Succeeded>Checkpoint:Succeeded
checkpoint_session_id=<session-id>
checkpoint_status=<status>
checkpoint_turn_count=<count>
checkpoint_can_continue=<true|false>
checkpoint_can_summarize=<true|false>
checkpoint_terminal=<true|false>
```

If a route-denied or validation-rejected branch occurs, the trace should expose the rejected node without leaking internal prompts or untrusted text:

```text
orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded
```

If a terminal branch occurs, the trace should avoid query-path nodes:

```text
orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded
```

```text
orchestration_trace=End:Succeeded>Checkpoint:Succeeded
```

The context contract should be generated by a dedicated formatter or provider method instead of duplicating string assembly across handler methods.

## State Machine Preservation

The existing state machine must remain intact:

- `Start` creates an active or awaiting-clarification session according to the answer result.
- `Continue` can add query turns, answer clarification, summarize, end, or reject invalid sessions.
- `Summarize` produces a terminal summary turn without query execution.
- `End` produces a terminal summary turn and prevents future continuation.
- route-denied query-producing continue requests must fail closed and must not mutate the persisted session.

V2.2 should add runtime tests that prove these properties still hold when calls enter through `DataAgentAnalysisToolHandler`.

## Readiness Gates

Add required readiness markers for V2.2:

- `AnalysisToolHandlerUsesOrchestrator`
- `OrchestratorTraceContextPresent`
- `OrchestratorCheckpointContextPresent`
- `OrchestratorRuntimeStartPathCovered`
- `OrchestratorRuntimeContinuePathCovered`
- `OrchestratorRuntimeTerminalPathCovered`
- `OrchestratorRuntimeRouteDeniedFailClosed`

These gates should be checked by:

- `DataAgentReadiness`
- `DataAgentReadinessTests`
- `tools/check-dataagent-readiness.ps1`

The readiness script should report V2.2 as required capability, not optional capability.

## Testing Strategy

Use test-first implementation.

Required focused tests:

1. `DataAgentAnalysisToolHandler` uses `IDataAgentAnalysisOrchestrator`.
   - A fake orchestrator should prove handler calls cannot bypass orchestration.
   - The handler should publish exactly the returned orchestrated context.

2. Runtime start path returns trace and checkpoint context.
   - Call `handler.Start(...)`.
   - Assert the returned context contains `[data_agent_analysis_session_context]`.
   - Assert it contains `orchestration_trace`.
   - Assert it contains `checkpoint_can_continue=true`.

3. Runtime continue path returns second-turn trace.
   - Start a session.
   - Continue it.
   - Assert `turn_count=2`.
   - Assert trace contains query path nodes.

4. Terminal runtime paths do not query.
   - Start a session with one answer call.
   - Call `Summarize` and `End`.
   - Assert no additional answer calls for terminal methods.
   - Assert trace contains only terminal/checkpoint nodes for terminal calls.

5. Route-denied runtime path fails closed.
   - Start an accepted session.
   - Continue with a query-producing request while `RouteAllowsQuery=false`.
   - Assert no answer call.
   - Assert no additional turn is persisted.
   - Assert response is rejected.
   - Assert trace contains `RouteGate:Rejected`.

6. Readiness required markers cover runtime integration.
   - Readiness tests should fail if the handler stops depending on orchestrator.
   - Readiness tests should fail if trace/checkpoint context markers are removed.

## Implementation Shape

Expected file changes:

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
  - constructor dependency changes from `DataAgentAnalysisService` to `IDataAgentAnalysisOrchestrator`
  - methods call orchestrator and return formatted response context

- Modify `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
  - ensure the interface covers all runtime XML operations cleanly

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
  - expose runtime methods used by the handler
  - preserve current route/terminal behavior
  - return context with trace/checkpoint fields

- Create or modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs`
  - centralize trace/checkpoint context formatting
  - sanitize field values using existing context field conventions

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - construct a single session store
  - construct `DataAgentAnalysisService`
  - construct `DataAgentAnalysisOrchestrator`
  - register `DataAgentAnalysisToolHandler` with the orchestrator

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - add V2.2 runtime required markers

- Modify `tools/check-dataagent-readiness.ps1`
  - surface the new required V2.2 checks

- Modify or add tests under `Tests/Alife.Test.DataAgent`
  - handler runtime tests
  - orchestrator context tests
  - readiness tests

## Acceptance Criteria

V2.2 is complete when:

- all analysis XML tool methods enter through `IDataAgentAnalysisOrchestrator`
- no analysis XML tool method directly depends on `DataAgentAnalysisService`
- runtime start/continue/summarize/end return orchestration trace context
- checkpoint context is present for accepted, rejected, and terminal branches
- terminal methods do not call the answer boundary after the initial query
- route-denied query-producing continues fail closed without persisted session mutation
- DataAgent readiness reports all V2.2 gates as required and passing
- QChat engineering map remains passing
- full solution tests pass with .NET 9 SDK

Required verification commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
git diff --check
```

## Interview Framing

V2.2 can be described as the point where DataAgent stops being a plain NL2SQL tool and becomes an auditable analysis-agent runtime.

The strongest interview framing:

```text
I first built the NL2SQL capability behind store, planner, safety, and Tool Broker boundaries. Then I introduced a native orchestrator with explicit RouteGate, SchemaContext, Plan, Validate, Execute, Explain, and Checkpoint nodes. In V2.2 I wired that orchestrator into the real XML tool runtime, so every analysis call now exposes trace and checkpoint context. Terminal actions do not query, route-denied analysis fails closed, and the state machine remains centralized in the analysis service. This gives the system the same engineering shape needed for later LangGraph or multi-agent linked execution without taking on that migration too early.
```

That story demonstrates Harness Engineering, Loop Engineering, Prompt/Tool boundary discipline, state machine preservation, and incremental architecture migration.
