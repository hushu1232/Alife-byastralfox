# DataAgent V2.15 DataQueryGraph Dry-Run Pilot

V2.15 is a C# dry-run pilot only. It documents and models a graph-shaped DataQueryGraph flow inside the existing DataAgent boundary, but it does not add a production graph runtime or a second execution path.

The pilot is default-off and exists to learn whether the graph shape helps developers reason about DataAgent orchestration before taking on a real LangGraph or Python sidecar dependency.

## Default State

The feature flag is:

```text
ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED=false
```

Missing, blank, `false`, `0`, and `no` values are disabled. Accepted enable values are `true`, `1`, and `yes`.

Even when enabled, V2.15 remains a C# dry-run pilot. There is no LangGraph runtime behavior, no Python sidecar code, no FastAPI service, no HTTP calls, no sidecar process, and no new SQL execution path.

## Graph-Shaped Nodes

The dry-run pilot describes these node names:

- `route_gate`
- `scenario_knowledge`
- `query_planner`
- `query_plan_validator`
- `sql_compiler`
- `sql_safety`
- `read_only_execute`
- `result_explainer`
- `evidence_audit`
- `checkpoint_progress`
- `diagnostics_router`
- `terminal`
- `reject`

Each node uses `DataAgentToolScopePolicy` to reduce future tool-selection ambiguity. The policy keeps planner, deterministic safety, read-only execution, diagnostics, terminal, and reject responsibilities separated so a future graph does not blur which step may describe intent, validate data access, run existing read-only work, report diagnostics, finish successfully, or reject deterministically.

This is intentionally practical rather than over-designed: node names are a developer map for the existing DataAgent flow, not a promise that every future runtime must expose these names as public API.

## Authority Boundary

DataQueryGraph may describe expected node order, scoped capabilities, transitions, bounded traces, and deterministic fallback reasons.

DataQueryGraph cannot authorize datasets, fields, operators, limits, executable SQL, SQL execution, Tool Broker route state, checkpoint mutation, query audit, Tool Broker audit, evidence packs, progress events, trace timelines, diagnostics, QChat visible text, or QQ ingress.

The C# DataAgent pipeline remains authoritative:

- `DataAgentScenarioContextBuilder` provides scenario context.
- `IDataAgentQueryPlanner` produces query plans.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles SQL through the existing compiler path.
- `DataAgentSqlSafetyValidator` enforces deterministic SQL safety.
- `IDataAgentStore` owns read-only execution and query audit persistence.
- `IDataAgentAnalysisSessionStore` owns analysis session and checkpoint persistence.
- Tool Broker route state decides whether DataAgent tools may be used for the current turn.

The pilot may point at those responsibilities, but it must not replace them.

## QChat Boundary

QChat remains the interaction surface and consumer boundary. QChat does not import DataQueryGraph pilot types and does not own graph internals.

Any QChat-visible behavior continues through the existing DataAgent and Tool Broker surfaces. The dry-run pilot may produce developer diagnostics for evaluation, but it cannot decide QChat text or bypass the current interaction boundary.

## V2.16 Handoff

Use V2.15 pilot results before deciding whether a real LangGraph or Python sidecar is worth the dependency.

Any future sidecar must pass both the V2.14 sidecar contract and the V2.15 node-scope boundary. In practice, that means a future runtime can orchestrate intent and traces only after it preserves the C# DataAgent authority over planning validation, SQL compilation, SQL safety, read-only execution, route state, audits, evidence, progress, diagnostics, and QChat boundaries.
