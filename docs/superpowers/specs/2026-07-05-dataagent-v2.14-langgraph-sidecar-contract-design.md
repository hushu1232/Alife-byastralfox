# DataAgent V2.14 LangGraph Sidecar Contract Design

## Purpose

V2.14 defines a disabled-by-default contract for a future LangGraph sidecar without adding LangGraph runtime behavior. The goal is to make the next DataAgent graph step safe to plan by writing down the adapter boundary, authority model, readiness gates, and QChat separation before any graph engine or Python process exists in the runtime.

This is a contract milestone, not a graph execution milestone. V2.15 may pilot a DataQueryGraph after this contract proves that future graph nodes must call existing C# DataAgent safety services instead of bypassing QueryPlan validation, SQL safety, Tool Broker route checks, audit, evidence, or checkpoint/session persistence.

## Non-Overengineering Rule

Do not add LangGraph because the project is "supposed to be multi-agent." V2.14 adds only the smallest C# contract surface needed to prevent future overreach. There is no Python sidecar directory, no FastAPI skeleton, no LangGraph package, no StateGraph, no HTTP client, and no background sidecar process in this version.

The sidecar contract should communicate this rule:

```text
A graph sidecar may propose orchestration intent, but it cannot authorize datasets, fields, operators, SQL text, tool execution, route state, checkpoint mutation, or evidence.
```

## Current Foundation

V2.10 classified Alife capabilities so QChat remains an interaction surface, FunctionCaller remains Tool Broker authority, and DataAgent remains the first workflow candidate.

V2.11 and V2.12 added scenario-pack context before planner execution while keeping scenario context hint-only.

V2.13 added optional PostgreSQL checkpoint/session persistence behind `IDataAgentAnalysisSessionStore`. PostgreSQL persists DataAgent analysis sessions and turns, but SQL generation and execution remain in the existing C# QueryPlan-first pipeline.

V2.14 should build on that foundation by documenting and testing a future sidecar boundary without changing runtime behavior.

## Scope

V2.14 should add:

- DataAgent-owned graph sidecar contract DTOs.
- DataAgent-owned sidecar option parsing with default disabled behavior.
- A deterministic policy object that describes what a graph sidecar may and may not request.
- Readiness gates proving the contract exists, is disabled by default, and does not claim SQL or tool authority.
- QChat engineering-map gates proving QChat does not import the sidecar contract or implementation details.
- Documentation explaining why V2.14 is not DataQueryGraph execution.

V2.14 should not add:

- LangGraph runtime.
- `StateGraph`.
- Python sidecar code.
- FastAPI or any HTTP server/client.
- A new SQL execution path.
- A new model-driven SQL authority.
- QChat main-loop changes.
- Natural-language QChat command auto-execution.
- Changes to PostgreSQL checkpoint/session authority.
- A DataQueryGraph pilot.

## Proposed Contract Types

The implementation plan should use names close to these:

- `DataAgentGraphSidecarOptions`
- `DataAgentGraphSidecarPolicy`
- `DataAgentGraphSidecarContract`
- `DataAgentGraphSidecarRequest`
- `DataAgentGraphSidecarResponse`
- `DataAgentGraphSidecarNodeKind`
- `DataAgentGraphSidecarAuthority`

These types should live in `sources/Alife.Function/Alife.Function.DataAgent/`. They should not be referenced from QChat source files.

## Configuration

V2.14 should introduce one environment-backed feature flag:

```text
ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED=false
```

Default behavior is disabled. Blank, missing, `false`, `0`, and `no` should be interpreted as disabled. Explicit `true`, `1`, or `yes` may be interpreted as enabled for future pilot readiness, but enabling the option in V2.14 still must not start a process or execute a graph because no runtime adapter exists yet.

The option exists only to prove default-off behavior and provide a stable future switch. It does not enable DataQueryGraph behavior in V2.14.

## Authority Model

The graph sidecar contract should distinguish intent from authority.

Allowed future intent:

- Suggest an orchestration node kind.
- Request that C# DataAgent perform an existing safe operation.
- Return a bounded trace of proposed or completed graph steps.
- Report that it cannot proceed and needs a deterministic C# fallback.

Forbidden authority:

- Authorize datasets.
- Authorize fields.
- Authorize operators.
- Authorize limit values.
- Provide executable SQL.
- Execute SQL.
- Decide Tool Broker route permission.
- Mutate checkpoint/session state directly.
- Write audit, evidence, trace, progress, or diagnostics as authority.
- Send visible QChat text or own QQ ingress.

The C# DataAgent pipeline remains the system of record:

- `DataAgentScenarioContextBuilder` can provide hints.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles SQL.
- `DataAgentSqlSafetyValidator` rejects unsafe SQL shapes.
- `IDataAgentStore` executes read-only queries and records query/audit state.
- `IDataAgentAnalysisSessionStore` persists analysis session/checkpoint state.
- Tool Broker route state gates whether DataAgent tools may be used in the current turn.

## Request And Response Shape

The request contract should carry only bounded context. It should not carry raw SQL or unrestricted tool lists.

Suggested request fields:

- `WorkflowId`
- `SessionId`
- `CallerId`
- `Question`
- `ScenarioContext`
- `AllowedNodeKinds`
- `AllowedCapabilityNames`
- `CheckpointSessionId`
- `CheckpointStatus`
- `TraceId`

Suggested response fields:

- `WorkflowId`
- `Accepted`
- `ReasonCode`
- `Message`
- `ProposedNodeKind`
- `RequestedCapabilityName`
- `RequiresCSharpSafetyService`
- `Trace`

The response should not include a SQL field. If a future graph needs a query, it should request a C# DataAgent planner/validator/executor capability and accept the C# result.

## Data Flow

V2.14 data flow is compile-time and readiness-only:

1. DataAgent owns the sidecar contract types.
2. `DataAgentGraphSidecarOptions.FromEnvironment()` reads default-off configuration.
3. `DataAgentGraphSidecarPolicy.CreateDefault()` describes allowed and forbidden authority.
4. `DataAgentReadiness` checks that the contract, options, and policy exist and that the default option is disabled.
5. `tools/check-dataagent-readiness.ps1` adds static markers for the sidecar contract.
6. `tools/check-qchat-engineering-map.ps1` adds QChat omission guards so QChat does not import sidecar contract types.

There is no runtime call to a sidecar in V2.14.

## Error Handling

Since V2.14 has no runtime sidecar call, error handling is limited to deterministic parsing and contract rejection:

- Unknown option value should fail closed or be treated as disabled with a clear reason code, depending on the established local pattern.
- A response that claims SQL execution authority should be invalid by contract tests.
- A response that names a non-allowed capability should be invalid by contract tests.
- A request with blank workflow/session identity should be invalid by contract tests.

The implementation plan should choose the smallest validation surface that proves the boundary. It should not create a full graph runtime validator.

## Readiness And QChat Gates

DataAgent readiness should add a new required check, tentatively:

```text
GraphSidecarContractPresent
```

Expected detail markers:

- `default_enabled=false`
- `contract=true`
- `policy=true`
- `no_sql_authority=true`
- `no_runtime=true`

QChat engineering map should add a required check, tentatively:

```text
DataAgent graph sidecar contract
```

It should require the DataAgent readiness marker and omit direct QChat imports of graph sidecar contract/option/policy types.

## Tests

V2.14 tests should be lightweight and deterministic:

- Default options are disabled when the environment variable is missing or blank.
- Explicit false-like values are disabled.
- Explicit true-like values parse as enabled but do not start any runtime.
- Default policy denies SQL authority, tool authority, route authority, checkpoint authority, and evidence authority.
- Contract response cannot expose executable SQL.
- Readiness reports the new required check.
- Static readiness count increases by one.
- QChat engineering map count increases by one.
- QChat source omits direct sidecar contract imports.

No test should require live LangGraph, Python, PostgreSQL, QChat, model calls, HTTP, or a sidecar process.

## Documentation

Add a short operator/developer note:

```text
docs/dataagent/dataagent-v2.14-langgraph-sidecar-contract.md
```

It should explain:

- V2.14 is contract-only.
- The sidecar is disabled by default.
- No graph runtime exists in V2.14.
- Future graph nodes must call existing C# DataAgent safety services.
- V2.15 may pilot DataQueryGraph only after these gates pass.

## Acceptance Criteria

V2.14 is complete when:

- DataAgent has sidecar contract, options, and policy types.
- The sidecar option defaults to disabled.
- Enabling the option does not start a process or execute a graph.
- Contract/policy tests prove the sidecar cannot claim SQL, tool, route, checkpoint, or evidence authority.
- DataAgent readiness includes the new graph sidecar contract gate.
- QChat engineering map includes the new boundary gate.
- QChat source does not directly import sidecar contract/option/policy types.
- No LangGraph runtime, StateGraph, Python sidecar, HTTP client/server, new SQL path, QChat main-loop change, or DataQueryGraph pilot is added.
- Focused tests, readiness scripts, full solution verification, and forbidden-shape scans pass.

## Interview Framing

This version can be described as:

> After making DataAgent checkpoint state recoverable in V2.13, I did not immediately add LangGraph as a runtime dependency. In V2.14 I first defined a disabled-by-default sidecar contract that makes the safety boundary explicit: a future graph may propose orchestration intent, but it cannot authorize SQL, fields, tool execution, route state, checkpoint mutation, or evidence. This keeps the C# QueryPlan-first pipeline as the authority while preparing a small, testable seam for a later DataQueryGraph pilot.

## V2.15 Handoff

The next safe step after V2.14 is:

```text
V2.15 DataQueryGraph Pilot
```

V2.15 may add a tiny disabled-by-default pilot that maps scenario context, planning, validation, SQL safety, execution, evidence, and checkpoint nodes into a graph shape. Every node that touches safety or SQL must call existing C# DataAgent services rather than reimplementing or bypassing them.

## Self-Review

- Placeholder scan: no TBD/TODO placeholders are present.
- Scope check: this is one bounded contract milestone, not a runtime graph pilot.
- Boundary check: QChat remains the interaction surface; DataAgent remains the owner of the contract.
- Safety check: the sidecar cannot authorize SQL, tools, route state, checkpoint mutation, or evidence.
