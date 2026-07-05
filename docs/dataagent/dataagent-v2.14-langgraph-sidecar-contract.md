# DataAgent V2.14 LangGraph Sidecar Contract

V2.14 is a contract milestone, not a graph runtime milestone.

The repository now defines a disabled-by-default C# contract for a future LangGraph sidecar, but it does not add LangGraph runtime behavior, a Python sidecar, FastAPI, HTTP calls, a StateGraph, or a DataQueryGraph pilot.

## Default State

The feature flag is:

```text
ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED=false
```

Missing, blank, `false`, `0`, and `no` values are disabled. Explicit `true`, `1`, and `yes` values may parse as enabled for future readiness, but V2.14 still does not start a runtime because `DataAgentGraphSidecarContract.IsRuntimeAvailable` is `false`.

## Authority Boundary

A future graph sidecar may propose orchestration intent, request that C# DataAgent run an existing safe operation, return a bounded trace, or report that deterministic fallback is needed.

It cannot authorize datasets, fields, operators, limits, SQL text, SQL execution, Tool Broker route state, checkpoint mutation, audit, evidence, progress, diagnostics, QChat visible text, or QQ ingress.

The C# DataAgent pipeline remains the authority:

- `DataAgentScenarioContextBuilder` provides hint-only scenario context.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles read-only parameterized SQL.
- `DataAgentSqlSafetyValidator` rejects dangerous SQL shapes.
- `IDataAgentStore` executes read-only queries and records query/audit state.
- `IDataAgentAnalysisSessionStore` persists analysis session/checkpoint state.
- Tool Broker route state decides whether DataAgent tools may be used in the current QChat turn.

## Why This Exists

The goal is to prepare a small, testable boundary before any graph runtime exists. This prevents a future sidecar from becoming a second authority for SQL, tools, route state, checkpoints, or evidence.

V2.14 also keeps QChat as the interaction surface. QChat may consume DataAgent outputs through existing tool and diagnostics paths, but it does not import the graph sidecar contract types.

`DataAgentGraphSidecarNodeKind` and `DataAgentGraphSidecarAuthority` are part of that no-import boundary too.

## V2.15 Handoff

V2.15 may pilot a disabled-by-default DataQueryGraph only after the V2.14 readiness and QChat boundary gates pass.

The pilot should map scenario context, planning, validation, SQL safety, execution, evidence, and checkpoint steps into graph-shaped nodes. Any node that touches safety or SQL must call the existing C# DataAgent services rather than reimplementing or bypassing them.
