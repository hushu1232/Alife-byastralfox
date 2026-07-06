# DataAgent V2.15 DataQueryGraph Pilot Design

## Purpose

V2.15 adds a disabled-by-default, C# graph-shaped dry-run pilot for DataAgent. The goal is to prove the DataQueryGraph workflow shape, node scope boundaries, and fallback behavior before introducing any real LangGraph, Python sidecar, HTTP adapter, or runtime process.

This version should make DataAgent orchestration easier to audit without changing who owns authority. DataAgent remains QueryPlan-first. C# validators, compilers, stores, checkpoint/session stores, progress, trace, and evidence builders remain the system of record.

The guiding rule is:

```text
DataQueryGraph may describe node order and scoped intent, but it cannot authorize permissions, generate executable SQL, execute SQL, mutate checkpoints, or write evidence as authority.
```

## Current Foundation

V2.10 classified Alife capabilities so QChat stays the interaction surface, FunctionCaller and Tool Broker stay tool routing authority, and DataAgent remains the first workflow candidate.

V2.11 and V2.12 added scenario-pack context before planner execution while keeping scenario context hint-only.

V2.13 added optional PostgreSQL checkpoint/session persistence behind `IDataAgentAnalysisSessionStore`. PostgreSQL persists DataAgent analysis sessions and turns when explicitly configured, but it does not own SQL generation or DataAgent workflow policy.

V2.14 added a disabled-by-default sidecar contract and made the future graph boundary explicit. A graph sidecar may propose orchestration intent, request existing C# safety services, return a bounded trace, or ask for deterministic fallback. It may not authorize SQL, tools, route state, checkpoint mutation, evidence, progress, diagnostics, visible QChat text, or QQ ingress.

V2.15 should build directly on that contract. It should not skip ahead to a real LangGraph runtime.

## Non-Overengineering Rule

V2.15 is not allowed to add graph runtime complexity just to make the project look more agentic.

This version should add a small C# dry-run layer that is useful even if a LangGraph sidecar is never added. The dry-run graph should improve engineering clarity by turning the existing DataAgent pipeline into an explicit, testable node sequence with scoped capabilities.

V2.15 should not create a Python directory, install LangGraph packages, add FastAPI, open a port, add an HTTP client, spawn a process, or change QChat command behavior.

## Scope

V2.15 should add:

- DataAgent-owned DataQueryGraph pilot models.
- An environment-backed pilot option that defaults disabled.
- A deterministic graph-shaped dry-run builder.
- A node-to-capability mapping that reuses `DataAgentToolScopePolicy`.
- A bounded dry-run result and trace formatter.
- Readiness gates proving the pilot is disabled by default, dry-run only, and unable to claim SQL, route, checkpoint, evidence, diagnostics, or visible text authority.
- QChat engineering-map guards proving QChat does not directly import DataQueryGraph pilot types.
- Developer documentation explaining why V2.15 is a pilot, not LangGraph runtime integration.

V2.15 should not add:

- LangGraph runtime.
- `StateGraph`.
- Python sidecar code.
- FastAPI or an HTTP sidecar endpoint.
- HTTP client calls from DataAgent to a sidecar.
- A sidecar process manager.
- New SQL compiler or SQL executor paths.
- Any model-controlled SQL execution.
- Tool Broker route authority inside DataQueryGraph.
- Checkpoint/session mutation authority inside DataQueryGraph.
- Evidence, audit, trace, progress, or diagnostics write authority inside DataQueryGraph.
- QChat main-loop changes.
- QQ ingress changes.
- Natural-language QChat command auto-execution.

## Recommended Approach

The selected approach is a C# graph-shaped dry-run pilot.

This is safer than introducing a real Python/LangGraph sidecar now because it lets the project prove the graph value first:

- The existing DataAgent runtime remains stable.
- The graph can be tested without external dependencies.
- The model sees less ambiguous tool surface later because node scope becomes explicit.
- Future LangGraph work must target the already reviewed C# contract instead of inventing its own authority.

The pilot should run beside the existing pipeline, not inside the SQL authority path.

## Proposed Types

The implementation plan should use names close to these:

- `DataAgentDataQueryGraphOptions`
- `DataAgentDataQueryGraphPilot`
- `DataAgentDataQueryGraphPlan`
- `DataAgentDataQueryGraphNode`
- `DataAgentDataQueryGraphTransition`
- `DataAgentDataQueryGraphDryRunResult`
- `DataAgentDataQueryGraphTraceFormatter`

These types should live in `sources/Alife.Function/Alife.Function.DataAgent/`.

QChat source files should not reference these types directly.

## Configuration

V2.15 should introduce one new feature flag:

```text
ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED=false
```

Missing, blank, `false`, `0`, and `no` values should be disabled. Explicit `true`, `1`, and `yes` may enable the dry-run pilot.

Enabling this flag must not enable real LangGraph runtime behavior. It only permits C# dry-run planning and bounded diagnostics. If the flag is disabled, the pilot should return a stable disabled result such as:

```text
dataquerygraph_disabled
```

## Node Model

The pilot should not invent agent names that compete with existing tools. It should map the existing DataAgent workflow boundaries into graph nodes.

Suggested nodes:

- `RouteGate`
- `ScenarioKnowledge`
- `QueryPlanner`
- `QueryPlanValidator`
- `SqlCompiler`
- `SqlSafety`
- `ReadOnlyExecute`
- `ResultExplainer`
- `EvidenceAudit`
- `CheckpointProgress`
- `DiagnosticsRouter`
- `Terminal`
- `Reject`

These nodes should align with `DataAgentWorkflowNodeNames` and `DataAgentToolScopePolicy`.

Each node should expose only the capabilities already allowed by `DataAgentToolScopePolicy`. Unknown node names should fail closed with no capabilities and no model calls.

The pilot may include nodes that represent SQL-related phases, but it must not perform those phases itself. SQL-related nodes only document that existing C# services remain responsible for the real operation.

## Authority Model

DataQueryGraph may:

- Build an expected node plan for a DataAgent analysis turn.
- Annotate each node with its allowed capability names.
- Report whether a node would permit a model call.
- Report a bounded dry-run trace.
- Report deterministic fallback reasons.
- Compare graph shape against existing orchestration steps after the existing pipeline runs.

DataQueryGraph may not:

- Authorize datasets.
- Authorize fields.
- Authorize operators.
- Authorize limit values.
- Generate executable SQL.
- Execute SQL.
- Decide Tool Broker route permission.
- Mutate checkpoint/session state.
- Write query audit records.
- Write Tool Broker audit records.
- Write evidence packs as authority.
- Write progress events as authority.
- Write trace timelines as authority.
- Write diagnostics as authority.
- Send visible QChat text.
- Own QQ ingress.

The C# DataAgent pipeline remains authoritative:

- `DataAgentScenarioContextBuilder` provides hint-only scenario context.
- `IDataAgentQueryPlanner` proposes a QueryPlan or clarification.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles read-only parameterized SQL.
- `DataAgentSqlSafetyValidator` rejects unsafe SQL shapes.
- `IDataAgentStore` executes read-only queries and records query/audit state.
- `IDataAgentAnalysisSessionStore` persists analysis session/checkpoint state.
- Tool Broker route state decides whether DataAgent tools can be used in the current QChat turn.
- Existing progress, trace, evidence, and diagnostics builders remain the owners of externally visible diagnostic state.

## Data Flow

The pilot should be a side observation of the existing DataAgent path:

```text
Tool Broker route state
-> DataAgentAnalysisOrchestrator
-> DataQueryGraph dry-run builds expected node plan
-> Existing DataAgentService performs QueryPlan-first execution
-> Existing orchestration result is produced
-> Existing evidence, progress, and trace are produced
-> DataQueryGraph dry-run result reports graph shape, scope decisions, and fallback reason
```

The dry-run graph should be able to run before or after the existing orchestration result depending on the final implementation plan, but it must never become a prerequisite that can grant execution authority. If it fails, the existing deterministic DataAgent path should remain the fallback.

## Attention Dilution And Tool Selection

V2.15 should address the user's concern that similar tool names and descriptions can make AI tool selection random.

The graph pilot should reduce tool ambiguity by making node scope explicit:

- A node receives a small allowed capability list instead of the full project tool surface.
- Planner-like capabilities stay in `QueryPlanner`.
- Deterministic safety capabilities stay in `QueryPlanValidator`, `SqlCompiler`, and `SqlSafety`.
- `ReadOnlyExecute` is the only node shape that can describe read-only execution, but the pilot still cannot execute SQL.
- `DiagnosticsRouter` can describe reads of progress, trace, and evidence diagnostics, but it cannot execute queries.
- Unknown or blank node names fail closed.

This creates a future path where a real graph sidecar can be given a bounded per-node tool manifest instead of the whole overlapping tool catalog. The immediate V2.15 implementation should prove that boundary with deterministic C# tests.

## QChat And Plugin Boundary

QChat should remain the interaction surface and consumer boundary.

V2.15 should not require QChat to know about DataQueryGraph internals. QChat should continue consuming:

- DataAgent analysis context.
- DataAgent progress diagnostics.
- DataAgent trace diagnostics.
- DataAgent evidence diagnostics.
- Tool Broker route diagnostics.

QChat should not import:

- `DataAgentDataQueryGraphOptions`
- `DataAgentDataQueryGraphPilot`
- `DataAgentDataQueryGraphPlan`
- `DataAgentDataQueryGraphNode`
- `DataAgentDataQueryGraphTransition`
- `DataAgentDataQueryGraphDryRunResult`
- `DataAgentDataQueryGraphTraceFormatter`

The same rule applies to QQ ingress. The graph pilot should not own QQ message intake, private-visible reply decisions, persona prompts, or model dispatch loops.

Other plugins should continue to be governed through existing Tool Broker capability and route boundaries. Capabilities that are not naturally agentic should remain normal deterministic services and should not be forced into agent nodes.

## PostgreSQL And Checkpoint Boundary

V2.13 PostgreSQL checkpoint/session persistence remains the checkpoint authority when configured through `IDataAgentAnalysisSessionStore`.

V2.15 may read checkpoint metadata already present in orchestration results, such as session id, status, turn count, terminal state, and continuation flags. It must not mutate sessions or turns directly.

Checkpoint graph nodes should mean "the existing checkpoint owner should have produced or preserved checkpoint state", not "the graph writes checkpoint state".

## Trace, Progress, And Evidence

V2.15 should produce bounded dry-run diagnostics without becoming the diagnostic authority.

The dry-run result can include:

- `Enabled`
- `Accepted`
- `ReasonCode`
- `WorkflowId`
- `SessionId`
- `Nodes`
- `Transitions`
- `FallbackReason`
- `ComparedOrchestrationTrace`

The dry-run trace formatter should redact or omit SQL. It should use reason codes and node names, not raw SQL, hidden prompts, connection strings, or unbounded context.

Existing `DataAgentProgressRecorder`, `DataAgentTraceRecorder`, `DataAgentEvidencePackBuilder`, and diagnostics formatters remain authoritative for runtime-facing diagnostics. The graph pilot may be referenced in readiness and developer docs, and later it may be surfaced through existing owner diagnostics only after a separate approved design.

## Error Handling

The pilot should fail closed and degrade cleanly.

Suggested reason codes:

```text
dataquerygraph_disabled
dataquerygraph_dry_run_completed
dataquerygraph_route_rejected
dataquerygraph_scope_mismatch
dataquerygraph_unknown_node
dataquerygraph_sql_text_rejected
dataquerygraph_fallback_to_deterministic_orchestrator
```

Rules:

- Disabled flag returns a disabled result and does not throw.
- Unknown node returns no capabilities and no model-call permission.
- Trace entries containing SQL markers are rejected.
- Node capability mismatches reject the dry-run result.
- Route-denied workflows must not include a successful `ReadOnlyExecute` transition.
- Terminal summarize/end workflows must not include query execution.
- Any dry-run exception should become deterministic fallback, not a visible QChat failure.

## Readiness And Engineering Map Gates

DataAgent readiness should add a new required check, tentatively:

```text
DataQueryGraphPilotPresent
```

Expected detail markers:

```text
default_enabled=false
dry_run=true
no_langgraph_runtime=true
node_scope=true
no_sql_authority=true
fallback=true
```

QChat engineering map should add a required check, tentatively:

```text
DataAgent DataQueryGraph pilot
```

It should require the DataAgent readiness marker and omit direct QChat imports of DataQueryGraph pilot types.

The static readiness count should increase by one. The QChat engineering-map required count should increase by one.

## Tests

V2.15 tests should be deterministic and should not require live LangGraph, Python, PostgreSQL, QChat, model calls, HTTP, or a sidecar process.

Required focused tests:

- Pilot options default disabled.
- Explicit true-like option enables dry-run only.
- Disabled pilot returns `dataquerygraph_disabled`.
- Enabled pilot returns a bounded dry-run result.
- Default graph contains the expected node sequence for accepted query turns.
- Route-denied graph does not reach `ReadOnlyExecute`.
- Terminal summarize/end graph does not include query execution.
- Unknown node fails closed with no capabilities and no model call.
- Planner node cannot request `ExecuteReadOnlyQuery`.
- Diagnostics node cannot request `ExecuteReadOnlyQuery`.
- SQL-related trace text is rejected or redacted.
- Dry-run exceptions fall back to deterministic orchestrator reason codes.
- Readiness reports `DataQueryGraphPilotPresent`.
- Static readiness count increases by one.
- QChat engineering map reports `DataAgent DataQueryGraph pilot`.
- QChat source omits direct DataQueryGraph pilot type imports.

No test should require a real sidecar process.

## Documentation

Add a developer note:

```text
docs/dataagent/dataagent-v2.15-dataquerygraph-pilot.md
```

It should explain:

- V2.15 is a C# dry-run pilot.
- The pilot is disabled by default.
- No LangGraph runtime exists in V2.15.
- The pilot maps existing DataAgent workflow boundaries into graph nodes.
- Node scope reduces future tool-selection ambiguity.
- SQL, route, checkpoint, evidence, progress, trace, and diagnostics authority remain in existing C# services.
- QChat remains an interaction surface and should not import graph internals.

## Acceptance Criteria

V2.15 is complete when:

- DataQueryGraph pilot models exist in the DataAgent project.
- The pilot option defaults disabled.
- Explicit enable only enables dry-run behavior.
- The pilot does not add LangGraph, Python, FastAPI, HTTP, process management, or a sidecar runtime.
- The pilot reuses `DataAgentToolScopePolicy` for node capability boundaries.
- Unknown graph nodes fail closed.
- Route-denied and terminal graph shapes do not imply SQL execution.
- Trace formatting rejects or redacts SQL-like text.
- Existing QueryPlan-first execution remains unchanged.
- DataAgent readiness includes `DataQueryGraphPilotPresent`.
- QChat engineering map includes the DataQueryGraph boundary check.
- QChat source does not directly import DataQueryGraph pilot types.
- Focused DataAgent tests pass.
- Focused QChat engineering-map tests pass.
- `tools/check-dataagent-readiness.ps1` passes with the updated required count.
- `tools/check-qchat-engineering-map.ps1` passes with the updated required count.
- Full restore, build, and test verification pass sequentially.

## V2.16 Handoff

V2.16 should not automatically add Python/LangGraph runtime. The next step depends on what V2.15 proves.

Possible V2.16 directions:

- Surface DataQueryGraph dry-run diagnostics through existing owner-only diagnostics.
- Add a stricter per-node capability manifest for future sidecar tool prompts.
- Add a disabled Python/LangGraph sidecar dry-run adapter that must pass the V2.14 contract and V2.15 node-scope policy.

The project should only choose the sidecar adapter path if the C# dry-run pilot demonstrates real value and the added runtime dependency has a clear operational reason.

## Interview Framing

This version can be described as:

> After defining a disabled graph sidecar contract in V2.14, I still did not hand SQL or checkpoint control to LangGraph. In V2.15 I designed a C# DataQueryGraph dry-run pilot that maps the existing QueryPlan-first pipeline into explicit graph nodes with scoped capabilities. This reduces tool-selection ambiguity and attention dilution while preserving DataAgent safety authority: validation, SQL compilation, SQL safety, read-only execution, checkpoint persistence, evidence, and diagnostics remain owned by existing C# services.

## Self-Review

- Placeholder scan: no placeholder requirements are present.
- Scope check: this is one bounded dry-run pilot, not a real graph runtime.
- Boundary check: QChat remains the interaction surface; DataAgent owns graph pilot types.
- Safety check: the pilot cannot authorize SQL, tools, route state, checkpoint mutation, evidence, progress, trace, diagnostics, visible text, or QQ ingress.
- Implementation readiness: the design is focused enough for one implementation plan after user review.
