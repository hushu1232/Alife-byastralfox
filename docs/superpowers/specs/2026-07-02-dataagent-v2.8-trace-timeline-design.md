# DataAgent V2.8 Trace Timeline Design

## Purpose

DataAgent V2.8 adds an in-memory, owner-only trace timeline for DataAgent analysis orchestration.
The goal is to make each analysis chain explainable and replayable without adding new authority,
new SQL execution paths, model calls, or persistent storage.

V2.7 made recent diagnostics usable at the QChat session level. V2.8 builds on that by recording a
compact event timeline for the latest DataAgent analysis turns:

```text
RouteGate -> SchemaContext -> Planner -> SqlSafety -> Execute -> EvidencePack -> Checkpoint
```

The trace is observational only. It records what happened inside the existing orchestrator path, but
it never changes routing, permission checks, planning, SQL safety, execution, or answer generation.

## Success Criteria

V2.8 is successful when an owner can ask for a recent DataAgent trace and receive a safe, stable,
session-scoped timeline that explains the current analysis state:

```text
DataAgent trace diagnostics
session=qq:xiayu:2905391496:private:3045846738
turn=2
status=active
terminal=false
events=7
1 RouteGate Succeeded reason=route_allowed query=false
2 SchemaContext Succeeded tables=documents,query_audit
3 Planner Succeeded plan=read_only_query confidence=0.82
4 SqlSafety Succeeded read_only=true
5 Execute Succeeded rows=3 sql=redacted
6 EvidencePack Succeeded risk=0.21
7 Checkpoint Succeeded can_continue=true can_summarize=true
```

The output must not expose raw SQL, hidden context blocks, bearer tokens, API keys, connection
strings, tool prompt content, or data-agent evidence-pack tags.

## Non-Goals

V2.8 intentionally does not include:

- Database persistence for traces.
- Frontend UI.
- Streaming trace progress to a UI.
- LangGraph integration.
- New DataAgent query capability.
- New XML tools beyond owner diagnostics commands.
- Any relaxation of Tool Broker decisions.
- Any dependency from DataAgent to QChat.

Persistence and real-time streaming are better handled after the in-memory trace contract is stable.
The recommended follow-up is V2.9 for stream progress and a later V3 milestone for persisted audit
history through the store boundary.

## Current Context

The project already has:

- `DataAgentAnalysisOrchestrator` for native analysis orchestration.
- `DataAgentOrchestrationStep` and checkpoint models for node-level state.
- `DataAgentEvidencePackBuilder` and `DataAgentEvidenceDiagnosticsFormatter` for compact owner
  diagnostics.
- `QChatRecentDiagnosticsCache` for session-scoped recent diagnostics.
- Owner-only diagnostics commands in `QChatDiagnosticsService`.
- Readiness gates in `tools/check-dataagent-readiness.ps1` and
  `tools/check-qchat-engineering-map.ps1`.

V2.8 should reuse those concepts instead of creating a separate monitoring subsystem.

## Proposed Architecture

### DataAgent Trace Models

Add DataAgent-owned trace models:

```text
DataAgentTraceEvent
DataAgentTraceTimeline
DataAgentTraceEventKind
DataAgentTraceEventStatus
```

`DataAgentTraceEvent` represents one observable node result. It should contain only safe metadata:

- Event kind.
- Status.
- Reason code.
- Relative or absolute timestamp.
- Safe key-value facts.
- Whether SQL was executed.
- Whether a query was allowed.
- Whether the event was terminal.

`DataAgentTraceTimeline` represents one analysis turn or terminal operation:

- Session id.
- Turn count.
- Started at.
- Ended at.
- Session status.
- Terminal flag.
- Event list.

Trace models belong to `Alife.Function.DataAgent`.

### Trace Recorder

Add a small in-memory recorder:

```text
DataAgentTraceRecorder
IDataAgentTraceRecorder
```

The recorder stores recent timelines by session id. It should support:

- Start or record timeline.
- Get latest timeline for a session.
- Get recent timelines for a session.
- Session isolation.
- Capacity limit per session.
- TTL filtering.
- Non-mutating reads.

Reads must not prune or mutate the trace state. This mirrors the V2.7 recent diagnostics cache
decision and keeps diagnostics safe to inspect repeatedly.

### Trace Formatter

Add a formatter:

```text
DataAgentTraceDiagnosticsFormatter
```

The formatter converts a timeline to owner-readable text. It must be deterministic and conservative:

- Stable line order.
- Safe enum names.
- Bounded output length.
- Safe unavailable state.
- SQL always redacted.
- Unsafe key/value text redacted or removed.
- Hidden context tags removed or replaced.

The formatter should prefer compact facts over raw payloads:

```text
sql=redacted
rows=3
dataset=documents
route_allowed=true
route_reason=route_allowed
```

It should not emit:

```text
SELECT * FROM documents
[data_agent_evidence_pack]
[tool_route_context]
Bearer token-...
Server=...;Uid=...;Pwd=...
```

### Orchestrator Integration

The trace should be recorded near the existing orchestration path:

```text
DataAgentAnalysisToolHandler
  -> DataAgentAnalysisOrchestrator
  -> DataAgentTraceRecorder
```

The simplest V2.8 design is to build the timeline from the existing `DataAgentOrchestrationResult`
and its steps after the orchestrator returns. This minimizes production risk because it avoids
threading trace callbacks through every node during execution.

The recorder can receive:

- The final `DataAgentOrchestrationResult`.
- Query audit records already available to the evidence pack builder.
- Tool Broker audit records already available to the evidence pack builder.
- A stable clock for tests.

This gives enough information for V2.8 while keeping the trace observational.

### QChat Diagnostics Bridge

DataAgent must not reference QChat. The bridge should remain string-based or callback-based, as in
V2.6/V2.7 evidence diagnostics.

Owner commands:

```text
/dataagent diag trace
/dataagent diagnostics trace
/qchat diag dataagent trace
/qchat diagnostics dataagent trace
```

The commands should be owner-only through the existing QChat owner command path. They should not be
handled for unknown `/dataagent` commands.

QChat can receive a safe recent trace diagnostic string and store it in `QChatRecentDiagnosticsCache`
as a new kind:

```text
QChatRecentDiagnosticKind.DataAgentTrace
```

Then `/qchat diag recent` can report:

```text
dataagent_trace_recent=available age_seconds=...
```

The full timeline is still returned by the dedicated trace diagnostics command.

## Event Kinds

V2.8 should start with the event kinds that already map to the orchestrator:

```text
RouteGate
SchemaContext
Planner
SqlSafety
Execute
EvidencePack
Checkpoint
Summarize
End
Answer
```

The implementation can map existing `DataAgentOrchestrationNodeKind` values to these trace kinds.
If a node is unavailable, it should not invent a fake event. The trace should say what actually
happened.

## Error Handling

Trace recording must be best-effort and fail closed:

- If trace recording throws, DataAgent analysis must still return its normal result.
- If a trace is missing, diagnostics return a stable unavailable response.
- If an event contains unsafe text, the event is redacted instead of partially shown.
- If a session key is missing, diagnostics use the route session key fallback when QChat has it.
- If the trace is too long, the formatter truncates safely at event boundaries when possible.

Unavailable output:

```text
DataAgent trace diagnostics
state=unavailable
reason=trace_unavailable
```

## Security Rules

V2.8 must preserve these rules:

- No raw SQL in diagnostics.
- No hidden context blocks.
- No evidence-pack tags.
- No tool-route-context tags.
- No bearer tokens.
- No API keys.
- No connection strings.
- No model prompt text.
- No XML function execution from diagnostics.
- No model calls from diagnostics.
- No SQL calls from diagnostics.
- No Tool Broker bypass.

False-positive redaction is acceptable in owner diagnostics. Leaking sensitive text is not.

## State And Capacity

Recommended defaults:

```text
maxTimelinesPerSession=4
maxEventsPerTimeline=32
ttl=30 minutes
maxFormattedChars=1800
```

These are intentionally small. The feature is for recent troubleshooting, not historical storage.

## Readiness And Engineering Gates

DataAgent readiness should gain one required Analysis check:

```text
DataAgentTraceTimelinePresent
```

The check should prove:

- Trace model exists.
- Trace recorder exists.
- Trace formatter exists.
- Orchestrator/tool handler publishes a trace diagnostic.
- Tests cover accepted, denied, and terminal trace paths.

QChat engineering map should gain one required Harness check:

```text
DataAgent trace diagnostics
```

The check should prove:

- Owner command exists.
- Recent cache kind exists.
- Diagnostics redaction uses the shared QChat sanitizer or an equivalent fail-closed path.

## Testing Strategy

Use TDD. The main test groups should be:

### DataAgentTraceRecorderTests

- Latest timeline is returned for the correct session.
- Sessions are isolated.
- Capacity evicts oldest timeline within a session.
- TTL hides expired timelines.
- Reads are non-mutating.

### DataAgentTraceDiagnosticsFormatterTests

- Formats stable available timeline.
- Formats unavailable state.
- Redacts raw SQL.
- Redacts bearer token.
- Redacts connection string.
- Redacts hidden context tags.
- Bounds long traces.

### DataAgentAnalysisOrchestrator Or Tool Handler Tests

- Route denied trace records `RouteGate` and no `Execute`.
- Accepted query trace records route, schema, planner, safety, execute, evidence, checkpoint.
- Summarize trace records terminal node and no query execution.
- End trace records terminal node and no query execution.

### QChatDiagnosticsServiceTests

- `/dataagent diag trace` returns recent trace for owner.
- `/qchat diag dataagent trace` alias works.
- Unknown `/dataagent` commands remain unhandled.
- Unsafe legacy trace fallback is redacted.
- Cache-first behavior prefers session cache over legacy string.

### QChatServiceAdapterTests

- Real owner command path can display recent trace.
- Private and group sessions remain isolated.
- Non-owner cannot read trace diagnostics.

## Interview Framing

After V2.8, the project can be described as having an observability layer for multi-node DataAgent
analysis:

```text
I added a structured Trace Timeline around the DataAgent orchestrator. Each natural-language analysis
records RouteGate, SchemaContext, Planner, SQL Safety, Execute, EvidencePack, and Checkpoint events
with stable reason codes. The trace is owner-only, session-scoped, and fail-closed: it never executes
tools, never calls the model, and never exposes raw SQL or credentials. This makes the multi-agent
chain replayable and debuggable while keeping permissions and data-access boundaries separate from
AI reasoning.
```

This is a stronger engineering story than a raw NL2SQL demo because it shows the operational harness
around the AI feature: permission traceability, checkpoint visibility, failure localization, and
safe diagnostics.

## V2.9 And V3 Direction

V2.9 should consider real-time progress streaming once the trace contract is stable:

```text
RouteGate started -> succeeded
Planner started -> succeeded
Execute started -> succeeded
Checkpoint created
```

V3 can persist trace history through the store boundary after PostgreSQL is the main production
target:

```text
DataAgentTraceStore
PostgresDataAgentTraceStore
Trace retention policy
Audit query commands
Frontend trace viewer
```

V2.8 should not pre-build those surfaces. It should keep the in-memory trace contract small and
testable so later versions can persist or stream it without changing the orchestrator semantics.

## Open Decisions Resolved

- Use in-memory storage only for V2.8.
- Keep owner diagnostics text-based.
- Reuse QChat recent diagnostics cache for trace summary availability.
- Keep raw SQL fully redacted.
- Treat false-positive redaction as acceptable.
- Delay LangGraph and streaming until after trace semantics are stable.
