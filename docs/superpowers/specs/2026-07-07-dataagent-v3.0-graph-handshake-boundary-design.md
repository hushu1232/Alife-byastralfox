# DataAgent V3.0 Graph Handshake Boundary Design

## Purpose

V3.0 starts the LangGraph era without handing runtime authority to LangGraph.

The goal is to add a conservative graph handshake boundary around the existing
C# DataAgent orchestration path. DataAgent should be able to construct a
bounded graph request, pass it to a disabled-by-default sidecar client, validate
the returned graph response, and fall back to the existing deterministic
orchestrator whenever the sidecar is disabled, unavailable, invalid, or
overreaching.

This is intentionally not a full Python sidecar rollout. It is the first V3
step that proves the C# boundary can host a LangGraph-shaped collaborator
without moving SQL authority, QQ ingress, Tool Broker policy, checkpoint
mutation, or diagnostics ownership out of the current Alife services.

## Non-Overengineering Rule

V3.0 must not become a project-showcase rewrite.

This milestone must not:

- Add a production Python process manager.
- Require FastAPI, uvicorn, HTTP calls, or network access for normal tests.
- Add a live LangGraph runtime dependency to the C# build.
- Give a sidecar SQL execution authority.
- Let a sidecar read from or write to PostgreSQL, SQLite, QChat, QQ, files, or
  Tool Broker state directly.
- Replace `DataAgentAnalysisOrchestrator`, `DataAgentAnalysisService`, or the
  QueryPlan-first SQL safety pipeline.
- Move permission checks, SQL safety, read-only execution, evidence, progress,
  trace, or query audit out of C#.
- Agentize deterministic plugins such as QChat message delivery, owner command
  handling, voice, desktop pet, browser control, file upload, or external RAG
  management.
- Let QChat import DataAgent graph or sidecar model types.

The useful outcome is smaller and sharper: a graph handshake can suggest node
progress and orchestration shape, while C# keeps all authority.

## Current Foundation

V2 closed the critical prerequisites for a careful V3:

- QueryPlan-first NL2SQL safety already validates dataset, field, operator, and
  limit before compiling read-only parameterized SQL.
- SQL Safety Validator rejects dangerous SQL forms and multi-statement shapes.
- Tool Broker route state scopes DataAgent XML tools per turn.
- DataAgent analysis sessions can persist through the analysis session store,
  with optional PostgreSQL checkpoint persistence already productized.
- Scenario knowledge packs provide deterministic business terminology context.
- Evidence, trace, progress, graph diagnostics, and query audit are inspectable.
- DataQueryGraph is currently a C# dry-run projection, not a runtime authority.
- QChat is a string-only interaction and diagnostics surface.
- V2.17 centralized QChat DataAgent diagnostics command vocabulary and proved
  the QChat production source does not import `DataAgentDataQueryGraph*`.

That means V3.0 can focus on the orchestration boundary instead of rebuilding
the safety pipeline.

## LangGraph Reference Context

Current LangGraph concepts that matter for this design are:

- Graphs are built from state, nodes, and edges.
- A compiled graph may use a checkpointer for versioned short-term memory and
  durable execution.
- Graph workflows can stream progress and route conditionally between nodes.
- Human-in-the-loop interrupts exist, but they are an advanced runtime behavior
  and should not be part of the V3.0 implementation.

V3.0 should borrow the shape of these concepts but not the runtime dependency.
The first milestone should define C# contracts that can later map to a
LangGraph `StateGraph` and checkpointer without requiring them today.

## Selected Approach

Add a C# graph handshake boundary with five focused pieces:

1. `DataAgentGraphHandshakeContract`
2. `DataAgentGraphNodeManifest`
3. `IDataAgentGraphSidecarClient`
4. `DataAgentGraphHandshakeValidator`
5. readiness and owner diagnostics for handshake state

The default sidecar client should be disabled and local. It should not start a
process or make HTTP calls. Tests can use a fake sidecar client to prove
accepted, rejected, unavailable, and fallback behavior.

The C# DataAgent path remains authoritative. A sidecar response can be used only
as a bounded orchestration suggestion: selected nodes, node progress, trace
summary, fallback reason, and capability scoping evidence. It cannot authorize
datasets, fields, SQL, tools, checkpoint writes, QChat replies, QQ ingress, or
visible user text.

## Alternatives Considered

### Direct Runtime Sidecar

This would add Python, FastAPI, LangGraph, process lifecycle, HTTP contracts,
timeouts, streaming, deployment checks, and integration tests in one step.

It would provide a stronger demo, but it creates too much risk at the exact
boundary this project is trying to protect. Runtime sidecar work belongs in a
later V3 milestone after the request and response contract is stable.

This option is rejected for V3.0.

### Pure C# Graph Runtime

This would continue the V2 dry-run pilot into a C# graph executor without
introducing LangGraph at all.

It is safe, but it weakens the V3 story. The project needs a real seam where a
future LangGraph graph can connect. A C# handshake boundary gives that seam
without taking on runtime risk.

This option is rejected for V3.0.

### C# Main Control With Disabled Sidecar Handshake

This option adds explicit request and response contracts, node manifests,
response validation, fallback behavior, diagnostics, and readiness gates while
leaving the actual sidecar disabled by default.

It gives the project a truthful V3 architecture step, protects existing plugin
boundaries, and reduces model tool-selection ambiguity through scoped node
manifests.

This is the selected approach.

## Component Design

### DataAgentGraphHandshakeContract

The contract should live in the DataAgent project, not QChat.

Recommended request shape:

```text
request_id
session_id
turn_id
caller_id
goal_or_question
scenario_context_summary
route_scope
query_constraints
node_manifests
no_sql_authority=true
read_only=true
fallback_available=true
trace_budget_chars
progress_budget
```

Recommended response shape:

```text
request_id
accepted
reason_code
selected_nodes
node_progress
trace_summary
context_contribution
fallback_required
no_sql_authority=true
read_only=true
```

The contract should be serializable with standard .NET JSON tooling but does
not need live HTTP transport in V3.0.

### DataAgentGraphNodeManifest

Node manifests are the main tool-choice control surface. They make each graph
node see a small capability set instead of the whole Alife tool universe.

Initial node candidates:

- `scenario_context`
  - Reads scenario knowledge pack terms and data-domain vocabulary.
  - Does not generate SQL or call tools.
- `permission_gate`
  - Emits allow, deny, and reason information for the current route scope.
  - Does not generate SQL.
- `query_planner`
  - Emits QueryPlan-shaped intent candidates.
  - Does not compile or execute SQL.
- `sql_safety`
  - Describes safety validation status.
  - Does not execute SQL and does not override C# validators.
- `result_interpreter`
  - Summarizes already controlled result state.
  - Does not fetch new data.
- `diagnostics_router`
  - Summarizes evidence, trace, progress, and graph status.
  - Does not expose hidden context or raw SQL.

Each manifest should include:

```text
node_name
purpose
allowed_tool_names
denied_capability_markers
input_shape
output_shape
business_terms
safety_notes
```

The initial manifest set should be generated from existing DataAgent capability
and scope concepts, not from free-form prompt text.

### IDataAgentGraphSidecarClient

The sidecar client interface should be narrow:

```text
TryHandshake(request) -> response
```

V3.0 should provide:

- a disabled implementation that returns `sidecar_disabled`;
- a fake test implementation for accepted responses;
- a fake test implementation for rejected, timeout, invalid, and unavailable
  responses.

No production Python process should be started in this milestone.

### DataAgentGraphHandshakeValidator

The validator is the real authority boundary.

It should reject a response when:

- `request_id` does not match.
- `no_sql_authority` is false or missing.
- `read_only` is false or missing.
- selected nodes are not declared in the request manifests.
- node progress references unknown nodes.
- progress states are invalid.
- trace summary exceeds the configured budget.
- trace summary contains raw SQL-like text or unsafe diagnostic markers.
- response asks for a tool not allowed by the node manifest.
- response requests checkpoint mutation.
- response requests QQ, QChat, file, browser, desktop, voice, or RAG actions.
- response returns visible user text instead of a controlled context
  contribution.

Rejected responses should not fail the user request. They should produce a
diagnostic reason and fall back to deterministic C# orchestration.

### Diagnostics And Readiness

V3.0 should expose owner-visible diagnostics for the handshake:

```text
sidecar_enabled=false
handshake_status=disabled|accepted|rejected|unavailable|timeout
fallback_required=true|false
reason_code=...
selected_nodes=...
no_sql_authority=true
scoped_node_manifest=true
runtime_required=false
```

The readiness gate should prove:

- the handshake contract exists;
- the default sidecar is disabled;
- the validator rejects SQL authority;
- the validator rejects unknown nodes;
- fallback remains available;
- node manifests are scoped;
- QChat production source does not import DataAgent graph handshake or
  DataQueryGraph model types.

## Data Flow

The accepted path should be:

```text
Incoming natural language question
-> QChat / Tool Broker routes to DataAgent
-> DataAgent builds scenario context and route scope
-> DataAgent builds graph handshake request
-> disabled or fake sidecar client returns response
-> C# validator accepts bounded graph suggestion
-> C# deterministic orchestrator remains responsible for QueryPlan, SQL safety,
   execution, evidence, audit, progress, and diagnostics
```

The fallback path should be:

```text
Incoming natural language question
-> DataAgent builds graph handshake request
-> sidecar disabled, unavailable, invalid, or rejected
-> C# records reason_code and owner diagnostics
-> current deterministic DataAgent path continues
```

The non-goal path should remain unchanged:

```text
QChat owner diagnostics, QQ ingress, visible reply policy, Tool Broker policy,
SQL execution, checkpoint persistence, and plugin services keep their current
owners.
```

## Attention Dilution And Tool Choice

V3.0 should reduce random model tool choice structurally, not rhetorically.

The mechanism is scoped manifests:

- A graph node receives only the capabilities relevant to its job.
- Similar tool names are not presented together unless the node needs them.
- Business terms come from deterministic scenario knowledge packs.
- Each node has explicit input and output shapes.
- Tool Broker remains the final execution gate even if a graph response
  suggests a tool.
- The sidecar never sees raw global plugin authority.

This turns the model-facing problem from "choose among every Alife tool" into
"complete one node with a small allowed vocabulary."

## Plugin And Agentization Policy

V3.0 must preserve deterministic plugin boundaries.

Appropriate graph-scoped capabilities:

- DataAgent scenario context lookup.
- DataAgent permission and route scope summary.
- QueryPlan candidate planning.
- SQL safety status summarization.
- result summary interpretation for already controlled data.
- evidence, trace, progress, and graph diagnostics summarization.

Not appropriate for V3.0 graph nodes:

- QChat message send and receive.
- QQ owner command access policy.
- visible text policy.
- voice and TTS.
- desktop pet actions.
- browser control.
- file upload and download.
- external RAG source management.
- PostgreSQL checkpoint store internals.
- SQL execution.
- Tool Broker execution policy.

These services may appear in a manifest as denied or unavailable capabilities,
but they should not become sidecar-callable tools in this milestone.

## Checkpoint And Persistence

V3.0 should not replace the existing PostgreSQL checkpoint implementation.

The graph handshake may include session and turn identifiers so a future
LangGraph checkpointer can map to them, but V3.0 should not let LangGraph write
checkpoint state. C# remains the checkpoint owner.

If a future sidecar runtime adds checkpointer support, it should be reconciled
with C# checkpoint state through an explicit V3.x design. V3.0 should only
record handshake diagnostics and fallback reasons through existing C# stores or
diagnostics caches.

## Error Handling

Required fallback reasons:

- `sidecar_disabled`
- `sidecar_unavailable`
- `sidecar_timeout`
- `invalid_response_schema`
- `request_id_mismatch`
- `sql_authority_requested`
- `unknown_node`
- `unknown_tool`
- `unsafe_trace`
- `progress_invalid`
- `checkpoint_mutation_requested`
- `visible_text_requested`

All of these should be non-fatal to the user-facing DataAgent flow. The system
should record diagnostics, reject the sidecar response, and continue with the
current deterministic C# orchestration path.

## Security Boundary

The sidecar response is untrusted input.

C# must treat it like external context:

- validate before use;
- bound length;
- reject unsafe SQL-like text;
- reject unknown nodes and tools;
- reject authority claims;
- sanitize diagnostics;
- never execute returned text;
- never publish raw response text directly to QChat.

The sidecar may help explain orchestration. It cannot authorize orchestration.

## Tests

Contract tests:

- Default request includes `no_sql_authority=true`, `read_only=true`, and
  `fallback_available=true`.
- Node manifests include only scoped allowed tools.
- Unknown node names are rejected.
- Unknown tool names are rejected.
- Response with SQL authority requested is rejected.
- Response with mismatched request id is rejected.
- Response with unsafe trace text is rejected.
- Valid accepted response produces bounded graph diagnostics.
- Disabled sidecar returns `sidecar_disabled` and fallback required.

Integration tests:

- DataAgent analysis with disabled sidecar still uses deterministic C#
  orchestration.
- Fake accepted sidecar response records owner diagnostics but does not execute
  SQL or tools.
- Fake rejected sidecar response records fallback reason and preserves existing
  DataAgent result behavior.

Readiness tests:

- DataAgent readiness includes a V3.0 graph handshake boundary marker.
- Static readiness script checks contract, validator, disabled default, scoped
  manifest, and fallback markers.
- QChat engineering map continues to prove QChat does not import DataAgent
  graph handshake or DataQueryGraph model types.

No test should require network, Python, FastAPI, live LangGraph, PostgreSQL
environment variables, live QChat, model calls, browser automation, or QQ.

## Documentation

Add a developer note:

```text
docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md
```

It should explain:

- V3.0 starts the LangGraph boundary, not the runtime sidecar.
- C# keeps SQL authority and checkpoint ownership.
- The sidecar is disabled by default.
- Fake sidecar tests prove accepted, rejected, and fallback paths.
- Node manifests reduce attention dilution by scoping tools.
- Deterministic plugins remain services unless a later design explicitly
  assigns them scoped graph roles.

## Acceptance Criteria

V3.0 is complete when:

- A C# graph handshake request and response contract exists.
- Node manifests represent scoped capability surfaces.
- A disabled sidecar client is the default.
- Fake sidecar clients can exercise accepted and rejected paths.
- The handshake validator rejects authority overreach.
- Sidecar disabled, unavailable, invalid, and rejected paths fall back to the
  current deterministic DataAgent orchestration.
- Owner diagnostics expose handshake status and fallback reason.
- DataAgent readiness proves default-disabled, no-SQL-authority, scoped
  manifest, fallback, and validator behavior.
- QChat production source does not import DataAgent graph handshake or
  DataQueryGraph model types.
- Full restore, build, and tests pass.

## V3.x Outlook

V3.0 should stop at the handshake boundary.

Suggested next milestones:

- V3.1: optional local Python/FastAPI sidecar process behind the validated C#
  contract.
- V3.2: streaming graph progress mapped into existing DataAgent progress
  diagnostics.
- V3.3: human-in-the-loop interrupts mapped to QChat owner events, not direct
  sidecar messages.
- V3.4: runtime checkpointer reconciliation, still with C# checkpoint ownership
  as the authority boundary.

This keeps V3 honest: each version earns the next layer of runtime complexity
only after the previous boundary is verified.

## Self-Review

- Placeholder scan: no placeholder requirements are present.
- Scope check: this spec covers one V3.0 milestone, not a full LangGraph
  runtime rollout.
- Boundary check: SQL, checkpoint, Tool Broker, QChat, QQ, and deterministic
  plugin authority remain in C#.
- Agentization check: only DataAgent orchestration concepts become graph
  manifests; deterministic plugin services stay services.
- Ambiguity check: sidecar responses are untrusted suggestions and must be
  validated before use.
- LangGraph check: the design aligns with graph state, nodes, edges,
  checkpointer, streaming, and interrupt concepts without requiring runtime
  integration in V3.0.
