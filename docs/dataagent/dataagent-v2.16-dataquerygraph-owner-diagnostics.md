# DataAgent V2.16 DataQueryGraph Owner Diagnostics

V2.16 surfaces the V2.15 DataQueryGraph dry-run result through owner-only diagnostics. It does not add LangGraph runtime behavior, Python sidecar code, FastAPI, HTTP calls, a sidecar process, or a new SQL execution path.

## Owner Command

The owner can inspect the latest graph diagnostics with:

```text
/dataagent diag graph
/dataagent diagnostics graph
/qchat diag dataagent graph
/qchat diagnostics dataagent graph
```

Before any graph diagnostics have been published, QChat returns:

```text
DataAgent graph diagnostics
state=unavailable
reason=graph_diagnostics_unavailable
```

After a DataAgent analysis action has published graph diagnostics, or whenever the latest graph diagnostics string is available, the command shows that latest diagnostic string. When the pilot flag is disabled, that published diagnostic reports the disabled dry-run state:

```text
DataQueryGraph dry-run
enabled=false
accepted=false
reason=dataquerygraph_disabled
fallback=pilot_disabled
runtime=no_langgraph_runtime
compared_trace=
nodes=
```

## Pilot Flag

The feature flag remains:

```text
ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED=false
```

Missing, blank, `false`, `0`, and `no` values are disabled. Explicit `true`, `1`, and `yes` values enable only C# dry-run diagnostics.

## Authority Boundary

DataQueryGraph diagnostics are observable state only. They cannot authorize datasets, fields, operators, limits, SQL generation, SQL execution, Tool Broker route state, checkpoint mutation, query audit, Tool Broker audit, evidence packs, progress events, trace timelines, visible QChat text, or QQ ingress.

The existing C# DataAgent pipeline remains authoritative:

- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles read-only parameterized SQL.
- `DataAgentSqlSafetyValidator` rejects unsafe SQL shapes.
- `IDataAgentStore` executes read-only queries.
- `IDataAgentAnalysisSessionStore` persists checkpoints and turns.
- Tool Broker route state decides whether DataAgent tools can be used.

## QChat Boundary

QChat consumes graph diagnostics as sanitized strings through FunctionCaller and the recent diagnostics cache. QChat does not import DataQueryGraph pilot model types and does not build graph plans.

## Plugin Governance

V2.16 does not force QQchat, desktop, browser, RAG, or other deterministic plugin abilities into graph nodes. Non-agentized abilities remain normal services unless a future design explicitly assigns them to a graph node with scoped capability manifests.

## Future Path

This release makes the graph projection inspectable before a real LangGraph adapter exists. A future V3 adapter should only be added after the C# contract, node scopes, owner diagnostics, no-execute behavior, and fallback behavior remain stable.
