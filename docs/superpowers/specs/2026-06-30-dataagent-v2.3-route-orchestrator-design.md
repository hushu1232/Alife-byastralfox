# DataAgent V2.3 Tool Broker Route Orchestrator Design

## Goal

DataAgent V2.3 closes the runtime gap between Tool Broker routing and the DataAgent analysis orchestrator.

V2.2 made `dataagent_analysis_*` XML tools enter through `IDataAgentAnalysisOrchestrator`, but the handler still builds `DataAgentOrchestrationRequest` with `RouteAllowsQuery: true`. That means the orchestrator is real, but its route gate is still fed by a hard-coded value instead of the current Tool Broker decision.

The goal of V2.3 is to make the real Tool Broker route decision become sanitized orchestration request context while preserving the current fail-closed XML execution policy and the existing `DataAgentAnalysisService` state machine.

V2.3 is not the LangGraph release. It is the route-to-orchestrator integration step that makes later LangGraph or multi-agent linked execution credible.

## Current Baseline

The project already has these required pieces:

- `ToolCapabilityRouter` creates `ToolRouteDecision` values from current utterance, owner/private chat status, trusted runtime status, and active DataAgent session state.
- `XmlFunctionCaller.RouteCurrentTurn(...)` stores the current `ToolRouteDecision` in `XmlFunctionExecutionPolicy.CurrentRoute`.
- `XmlFunctionExecutionPolicy.TryConsume(...)` fails closed when governed tools execute without a route, when the route does not allow a tool, or when a session-scoped DataAgent tool targets the wrong session.
- `QChatToolRouteStateWiringTests` prove QChat creates route state and scopes it during chat calls.
- `DataAgentAnalysisToolHandler` registers `dataagent_analysis_start`, `dataagent_analysis_continue`, `dataagent_analysis_summarize`, and `dataagent_analysis_end`.
- `DataAgentAnalysisToolHandler` now calls `IDataAgentAnalysisOrchestrator` after V2.2.
- `DataAgentAnalysisOrchestrator` accepts `DataAgentOrchestrationRequest.RouteAllowsQuery` and rejects query-producing paths when the route gate says no.
- `DataAgentAnalysisService` owns the analysis session state machine.
- `DataAgentOrchestrationContextProvider` appends orchestration trace and checkpoint context.

The remaining weakness is that `DataAgentAnalysisToolHandler` still supplies `RouteAllowsQuery: true` for start and continue requests. In the real runtime, XML policy may already have allowed the call, but the orchestrator does not receive evidence of that route decision. Direct handler tests also cannot prove fail-closed behavior when route context is absent.

## Selected Approach

Use a conservative route context accessor at the FunctionCaller-to-DataAgent boundary.

The handler should receive a small DataAgent-facing abstraction that can answer:

- is there a current Tool Broker route?
- which XML tool is being executed?
- does the current route allow that tool?
- what route intent and reason code were selected?
- which DataAgent session id was in the route state?
- is this request allowed to produce a query?

The handler should not depend on global mutable route state directly. It should ask the accessor for a sanitized snapshot for the specific XML tool being executed, then build the orchestration request from that snapshot.

Recommended shape:

```csharp
public interface IDataAgentToolRouteContextAccessor
{
    DataAgentToolRouteContext Get(string toolName, string? sessionId);
}
```

```csharp
public sealed record DataAgentToolRouteContext(
    bool Present,
    string ToolName,
    bool AllowsTool,
    bool AllowsQuery,
    string RouteId,
    string Intent,
    string ReasonCode,
    string RouteSessionId);
```

The concrete runtime implementation can adapt `XmlFunctionExecutionPolicy.CurrentRoute` or an equivalent scoped FunctionCaller route source into this DataAgent-safe record. Tests can use deterministic fake accessors.

This keeps Tool Broker policy in the XML execution layer and keeps DataAgent orchestration focused on workflow behavior.

## Rejected Alternatives

### Enrich XML Invocation Context

One option is to push route metadata into `XmlContext` and pass it through the XML reflection invocation path.

This is attractive as a long-term framework improvement, but it is too broad for V2.3. It would touch the general XML function runtime instead of only the DataAgent route boundary, increasing regression risk for unrelated tools.

### Audit-Only Integration

Another option is to keep `RouteAllowsQuery: true` and only record `RecentToolRouteDecision` in audit or trace.

This is not sufficient. It would make the logs look better while the orchestrator request still cannot distinguish real route permission from a hard-coded default.

## Non-Goals

V2.3 does not introduce LangGraph, Python sidecars, distributed workers, streaming front-end progress, live PostgreSQL requirements, chart rendering, report publishing, or new business datasets.

V2.3 does not move analysis session status transitions out of `DataAgentAnalysisService`.

V2.3 does not replace `XmlFunctionExecutionPolicy` as the first fail-closed Tool Broker gate.

V2.3 does not make the DataAgent orchestrator responsible for deciding user ownership, private chat status, trusted runtime status, or route intent classification.

V2.3 does not weaken SQL safety validation, planner validation, prompt-leak controls, store-boundary rules, or terminal no-query behavior.

V2.3 does not use `D:\FOXD` or any GitHub target other than `git@github.com:hushu1232/Alife-byastralfox.git`.

## Runtime Contract

### Tool Broker

`ToolCapabilityRouter` remains the source of route decisions. It should continue to produce stable reason codes such as:

- `route_allowed`
- `tool_route_required`
- `tool_not_allowed_in_current_route`
- `tool_session_not_allowed_in_current_route`
- `owner_private_required`
- `trusted_runtime_required`
- `dataagent_analysis_session_missing`

V2.3 should not duplicate this decision logic inside DataAgent.

### XML Execution Policy

`XmlFunctionExecutionPolicy` remains the front gate.

For governed tools, a missing route should still deny execution before the handler runs. A route that does not allow the function should deny execution before the handler runs. A session-scoped DataAgent route mismatch should deny execution before the handler runs.

V2.3 adds defense-in-depth after that gate: if the handler is invoked directly in tests, or by a future runtime path without a current route, the orchestration request must not silently default to query-allowed.

### Route Context Accessor

The route context accessor should convert the current route decision into a compact DataAgent route context.

For a present route:

- `Present=true`
- `ToolName=<current XML function name>`
- `AllowsTool=route.Allows(toolName)`
- `AllowsQuery=route.Allows(toolName)` for query-producing DataAgent tools
- `RouteId=route.RouteId`
- `Intent=route.Intent`
- `ReasonCode=route.ReasonCode`
- `RouteSessionId=route.State.ActiveDataAgentSessionId`

For a missing route:

- `Present=false`
- `ToolName=<current XML function name>`
- `AllowsTool=false`
- `AllowsQuery=false`
- `RouteId=empty`
- `Intent=empty`
- `ReasonCode=tool_route_required`
- `RouteSessionId=empty`

For a session mismatch, XML policy should normally block before the handler runs. If a direct test invokes the handler with mismatched route context, the accessor should return `AllowsQuery=false` and a stable mismatch reason code.

### DataAgent XML Handler

`DataAgentAnalysisToolHandler` should stop hard-coding route permission.

For `dataagent_analysis_start`, it should build:

```csharp
DataAgentOrchestrationRequest(
    callerId,
    goalOrQuestion,
    null,
    RouteAllowsQuery: routeContext.AllowsQuery,
    RouteContext: routeContext)
```

For `dataagent_analysis_continue`, it should build:

```csharp
DataAgentOrchestrationRequest(
    "local",
    question,
    sessionId,
    RouteAllowsQuery: routeContext.AllowsQuery,
    RouteContext: routeContext)
```

For `dataagent_analysis_summarize` and `dataagent_analysis_end`, route context may be captured for trace evidence, but query permission must not be required because these methods do not produce SQL queries. They must remain governed by XML policy and valid-session checks.

The handler should keep the existing XML method names, one-shot mode, argument validation, and `resultPublisher` behavior.

### Orchestrator

`DataAgentAnalysisOrchestrator` should continue to enforce query-producing route gates from `DataAgentOrchestrationRequest.RouteAllowsQuery`.

If `RouteAllowsQuery=false` for `Start`, the orchestrator must reject before calling `DataAgentAnalysisService.Start`.

If `RouteAllowsQuery=false` for a query-producing `Continue`, the orchestrator must reject before calling query execution paths and must not mutate the persisted session.

If `Continue` resolves to summarize or end intent, terminal behavior should remain no-query. Terminal actions should not require `RouteAllowsQuery=true`.

The orchestrator should not call `ToolCapabilityRouter` directly.

### State Machine

`DataAgentAnalysisService` remains the state machine owner.

V2.3 must preserve these transitions:

- `Start` creates an active, awaiting-clarification, ready-to-summarize, or rejected session according to existing answer behavior.
- `Continue` can add query turns, answer clarification, summarize, end, or reject invalid sessions.
- `Summarize` produces a terminal summary turn without query execution.
- `End` produces a terminal summary turn and prevents future continuation.
- Route-denied query-producing requests fail closed without creating hidden fallback sessions.

## Context Output

The returned `[data_agent_analysis_session_context]` should keep all V2.2 fields:

```text
orchestration_trace=...
checkpoint_session_id=...
checkpoint_status=...
checkpoint_turn_count=...
checkpoint_can_continue=...
checkpoint_can_summarize=...
checkpoint_terminal=...
```

V2.3 should append sanitized route evidence for orchestrated analysis calls:

```text
route_present=<true|false>
route_tool=<xml-tool-name>
route_allows_tool=<true|false>
route_allows_query=<true|false>
route_id=<route-id-or-empty>
route_intent=<intent-or-empty>
route_reason_code=<reason-code>
route_session_id=<session-id-or-empty>
```

These fields should not include raw user utterances, prompt text, planner prompts, SQL connection strings, Authorization headers, Bearer tokens, API keys, or arbitrary model output.

If a route-denied branch occurs, the route evidence should explain the denial through stable reason codes, not through raw prompt content.

## Safety Invariants

V2.3 must preserve these invariants:

- governed DataAgent XML tools still fail closed when route policy is missing;
- the orchestrator no longer receives hard-coded route permission for query-producing analysis requests;
- direct handler invocation without route context does not create query-allowed requests;
- session-scoped analysis tools cannot continue a session different from the route state;
- terminal summarize and end paths do not execute SQL;
- the state machine remains centralized in `DataAgentAnalysisService`;
- SQL execution still flows through `DataAgentService` and `IDataAgentStore`;
- readiness gates treat this integration as required, not optional.

## Testing Strategy

Use test-first implementation.

Required focused tests:

1. Handler start consumes route context.
   - Use a fake route accessor that allows `dataagent_analysis_start`.
   - Assert the orchestrator receives `RouteAllowsQuery=true`.
   - Assert the request carries route evidence.

2. Handler continue consumes route context.
   - Use a fake route accessor that allows `dataagent_analysis_continue` for the active session.
   - Assert the orchestrator receives `RouteAllowsQuery=true`.
   - Assert the request route session matches the continued session.

3. Missing route fails closed at the handler-to-orchestrator boundary.
   - Use a fake route accessor that returns `Present=false`.
   - Assert start or query-producing continue requests use `RouteAllowsQuery=false`.

4. Route-denied orchestrator path remains no-query.
   - Start an accepted session.
   - Continue with a query-producing request where `RouteAllowsQuery=false`.
   - Assert no answer/query call occurs.
   - Assert no additional query turn is persisted.
   - Assert trace contains `RouteGate:Rejected`.

5. Session-scoped XML policy remains authoritative.
   - Keep or extend existing XML policy tests for session mismatch.
   - Assert mismatched route/session continues are denied before handler effects.

6. Terminal paths remain no-query.
   - Summarize and end should not call query execution.
   - Terminal traces should avoid query path nodes.
   - Route context, if emitted, must not make terminal nodes require query permission.

7. Context includes route evidence.
   - Assert returned analysis context contains `route_present`, `route_tool`, `route_allows_query`, and `route_reason_code`.
   - Assert route fields are sanitized and stable.

8. Readiness gates cover V2.3 as required.
   - Readiness should fail if the handler returns to hard-coded `RouteAllowsQuery: true`.
   - Readiness should fail if route evidence fields disappear.

## Readiness Gates

Add required readiness markers for V2.3:

- `AnalysisHandlerConsumesToolRouteContext`
- `OrchestrationRequestUsesRuntimeRouteDecision`
- `RouteMissingRequestFailsClosed`
- `RouteEvidenceContextPresent`
- `RouteSessionScopePreserved`
- `TerminalRouteDoesNotQuery`

These gates should be checked by:

- `DataAgentReadiness`
- `DataAgentReadinessTests`
- `tools/check-dataagent-readiness.ps1`

The QChat engineering map should remain passing and may continue to treat DataAgent readiness as the detailed DataAgent harness authority.

## Implementation Shape

Expected file changes:

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
  - inject a route context accessor
  - build start and continue requests from real route context
  - preserve XML function names, one-shot mode, validation, and result publishing

- Add a compact DataAgent route context model and accessor contract
  - likely under `sources/Alife.Function/Alife.Function.DataAgent`
  - expose sanitized route facts only

- Add a runtime accessor implementation
  - adapt the current FunctionCaller/XML execution route into `DataAgentToolRouteContext`
  - avoid leaking raw prompts or untrusted text

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs` or the relevant orchestration model file
  - extend `DataAgentOrchestrationRequest` with route context evidence while preserving existing constructor ergonomics where useful

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs`
  - append sanitized route evidence fields

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - wire the real route context accessor when registering DataAgent analysis capabilities

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
  - pass the accessor through to the analysis handler if that remains the registration boundary

- Modify readiness code and scripts
  - add required V2.3 markers

- Modify or add tests under `Tests/Alife.Test.DataAgent` and `Tests/Alife.Test.Interpreter`
  - handler request tests
  - route-denied runtime tests
  - context-provider route evidence tests
  - readiness tests

## Acceptance Criteria

V2.3 is complete when:

- `DataAgentAnalysisToolHandler` no longer hard-codes query permission for start or continue;
- start and continue orchestration requests use the real current Tool Broker route context;
- missing route context produces `RouteAllowsQuery=false`;
- route-denied query-producing requests fail closed without SQL execution or hidden session mutation;
- XML policy remains the authoritative first gate for governed tools and session-scoped route mismatch;
- summarize and end remain terminal no-query operations;
- returned analysis context includes sanitized route evidence;
- DataAgent readiness reports all V2.3 gates as required and passing;
- QChat engineering map remains passing;
- full solution tests pass under the local .NET 9 SDK;
- no LangGraph, Python sidecar, live PostgreSQL requirement, or DataAgent state-machine migration is introduced.

Required verification commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
git diff --check
```

## Interview Framing

V2.3 can be described as the point where DataAgent's tool-use permission becomes runtime-real instead of documentation-real.

Strong interview framing:

```text
After building the NL2SQL analysis service, store boundary, session state machine, and native orchestrator, I connected the Tool Broker route decision into the actual orchestration request. XML policy remains the first fail-closed gate, but the orchestrator also receives sanitized route evidence such as route intent, reason code, session scope, and query permission. This prevents direct handler paths from silently defaulting to query-allowed behavior, keeps terminal summarize/end actions no-query, and gives readiness tests a concrete way to prove that permission, orchestration, and state-machine boundaries are truly linked.
```

This demonstrates Harness Engineering, Loop Engineering, Prompt Engineering, tool governance, state machine preservation, and incremental agent architecture migration.

## Future Work

V2.4 can make route state fully scoped per async execution if runtime concurrency testing shows that global `CurrentRoute` needs a stronger isolation model.

V2.5 can expose orchestration progress to UI or WebBridge consumers after the route evidence contract is stable.

V3 can map the stable C# route, checkpoint, and node contracts into LangGraph or another multi-agent runtime without rewriting DataAgent safety or store boundaries.
