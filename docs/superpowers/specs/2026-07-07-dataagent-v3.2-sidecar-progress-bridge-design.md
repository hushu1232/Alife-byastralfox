# DataAgent V3.2 Sidecar Progress Bridge Design

## Purpose

V3.2 extends the V3 graph integration path by allowing an optional local dev
sidecar to report bounded progress events while C# remains the only authority
that records, sanitizes, publishes, and displays DataAgent progress.

V3.1 proved that a manually run loopback HTTP sidecar can return a bounded graph
handshake response through `IDataAgentGraphSidecarClient` without gaining SQL,
checkpoint, Tool Broker, QChat, QQ, file, browser, RAG, or plugin authority.
V3.2 should build the next narrow bridge: sidecar-originated progress can be
observed, validated, mapped, and published through the existing C# DataAgent
progress diagnostics pipeline.

This is still not a production LangGraph runtime milestone. V3.2 should not
start Python automatically, should not require Python for default tests, and
should not introduce live graph execution. The value is making the future graph
runtime observable before it is allowed to become operational.

## Selected Direction

Implement a C# progress bridge, not a live streaming runtime.

The bridge should define a small sidecar progress event contract, validate and
sanitize each event, map accepted events into existing `DataAgentProgressEvent`
instances, and publish those events through the already proven
`IDataAgentProgressSink` path.

The project already has:

- `DataAgentProgressRecorder`, which stores bounded recent events per session.
- `DataAgentProgressDiagnosticsPublisher`, which publishes formatted owner
  diagnostics after each event.
- `DataAgentProgressDiagnosticsFormatter`, which redacts SQL-like text,
  secrets, hidden context markers, tool-route payloads, evidence packs,
  connection strings, and unsafe facts.
- `DataAgentGraphHandshakeCoordinator`, which builds bounded graph handshake
  requests and treats sidecar output as untrusted.
- `DataAgentGraphHandshakeValidator`, which validates node names, progress
  status, reason codes, requested tools, SQL authority, checkpoint mutation,
  visible text, and unsafe trace text.
- `DataAgentGraphHandshakeHttpClient`, which proves optional loopback dev
  transport without starting a runtime.

V3.2 should reuse these pieces instead of creating a new diagnostics channel.

## Non-Goals

V3.2 must not:

- Add a production LangGraph runtime.
- Add automatic Python process startup or process supervision.
- Require Python, FastAPI, uvicorn, network access, live QChat, PostgreSQL,
  browser automation, model calls, QQ, or a live sidecar for default tests.
- Add a new QChat command or make QChat import DataAgent graph sidecar,
  handshake, or progress bridge model types.
- Let the sidecar write directly to `DataAgentProgressRecorder`.
- Let the sidecar publish QChat text or owner diagnostics directly.
- Let the sidecar execute SQL, compile SQL, authorize datasets, authorize
  fields, authorize operators, authorize limits, decide Tool Broker route
  state, mutate checkpoints, write evidence, write audit, read local files,
  control browser state, control desktop pet state, or manage external RAG
  sources.
- Treat sidecar progress as proof that C# execution happened.
- Add SSE or NDJSON live streaming as the required transport in this milestone.

The useful V3.2 outcome is deliberately smaller: sidecar progress-shaped data
can be safely accepted or rejected by C# and surfaced through existing progress
diagnostics.

## Alternatives Considered

### Response-Only Progress Mapping

This would map the existing `DataAgentGraphHandshakeResponse.NodeProgress`
collection into `DataAgentProgressEvent` after the final handshake response is
received.

It is the smallest implementation and would improve diagnostics, but it is not
really a progress bridge. It only reports progress after the request has
completed. This is useful as a fallback behavior but too weak as the V3.2
milestone.

### C# Progress Contract And Mapper

This adds an explicit sidecar progress event contract and a mapper that
validates and sanitizes events before publishing them to the existing C#
progress pipeline. Tests can use fake sidecar events and fake HTTP handlers.
The Python stub can document the shape statically without becoming a default
runtime dependency.

This is the selected approach. It creates a clean handoff to a later live graph
runtime while keeping V3.2 deterministic and safe.

### Live SSE Or NDJSON Streaming Adapter

This would make the HTTP adapter read true streaming responses from a local
sidecar.

It is closer to a real LangGraph runtime, but it introduces partial reads,
connection lifetime, stream cancellation, retry semantics, event ordering, and
test complexity too early. It should be deferred until the C# progress bridge
is stable.

## Component Design

### Sidecar Progress Contract

Add a small C# model for untrusted sidecar progress input. A likely shape is:

```text
DataAgentGraphSidecarProgressEvent
  RequestId
  SessionId
  NodeName
  Status
  ReasonCode
  Message
  CreatedAt
  Facts
```

The exact type name can be adjusted during implementation to match local naming
patterns, but the model should remain sidecar-specific. It should not replace
`DataAgentProgressEvent`, because sidecar progress is untrusted input and must
not look like already-authorized C# progress.

Limits should be explicit:

- Maximum events per bridge operation: 16.
- Maximum identity length: align with graph handshake request/session limits.
- Maximum node name length: 128.
- Maximum reason code length: 128.
- Maximum message length: 240.
- Maximum fact count: 8.
- Maximum fact key length: 64.
- Maximum fact value length: 160.

Allowed `Status` values should map to existing progress statuses or a small
sidecar enum. Undefined enum values must be rejected.

`ReasonCode` must be a machine token: letters, digits, dot, underscore, and
dash only. Free-form text reason codes are rejected.

### Progress Validator

Add a validator responsible for deciding whether a sidecar progress event is
safe enough to map.

Validation should require:

- Request id matches the current graph handshake request.
- Session id matches the current DataAgent session.
- Node name exists in the current graph handshake manifest.
- Status is defined and mappable.
- Reason code is a safe machine token.
- Message is bounded and contains no unsafe diagnostic text.
- Fact keys and values are bounded.
- Fact keys that imply hidden context, Tool Broker payloads, evidence packs,
  connections, credentials, secrets, API keys, tokens, passwords, or
  authorization are rejected or omitted.
- Fact values containing SQL-like text, bearer tokens, secret markers,
  connection strings, or hidden context markers are redacted or rejected.

The validator can reuse existing sanitizers where practical:

- `DataAgentGraphHandshakeUnsafeDiagnosticDetector` for SQL-like and hidden
  marker rejection.
- `DataAgentProgressDiagnosticsFormatter` behavior as the downstream redaction
  backstop.
- `DataAgentContextFieldSanitizer` for bounded display text.

V3.2 should not trust downstream formatting alone. Unsafe events should be
filtered before they become `DataAgentProgressEvent`.

### Progress Mapper

Add a mapper that turns safe sidecar progress events into existing
`DataAgentProgressEvent` instances.

The mapper should stamp the event as sidecar-originated through safe facts such
as:

```text
source=graph_sidecar
node=<node-name>
request_id=<bounded-request-id>
```

It should map node names to existing progress phases where possible:

- Scenario context nodes map to scenario/context progress.
- Query planner nodes map to planner progress.
- Diagnostics nodes map to diagnostics progress.
- Unknown or unsupported nodes are rejected, not guessed.

The mapper must not set fields that imply SQL execution happened. Sidecar
progress cannot set `ExecutedSql=true` or equivalent execution proof. If the
existing progress model has execution flags, V3.2 sidecar events must keep them
false.

If an event is unsafe, the bridge should either drop it silently or publish one
bounded rejected progress event with a stable reason such as
`sidecar_progress_rejected`. The safer default is to drop unsafe event details
and rely on graph diagnostics/readiness tests to prove rejection.

### Progress Bridge

Add a small bridge service, for example:

```text
DataAgentGraphSidecarProgressBridge
```

Responsibilities:

- Accept a handshake request context, sidecar progress events, and an optional
  `IDataAgentProgressSink`.
- Validate each sidecar event.
- Map safe events to `DataAgentProgressEvent`.
- Publish mapped events through the sink when present.
- Return a bounded summary of accepted/rejected event counts for diagnostics or
  tests.

Non-responsibilities:

- It does not call HTTP.
- It does not start Python.
- It does not write QChat diagnostics directly.
- It does not execute DataAgent workflow steps.
- It does not decide Tool Broker route state.

This keeps transport separate from authority and diagnostics.

### Coordinator Integration

V3.2 should integrate without rewriting V3.1.

Recommended integration:

- `DataAgentGraphHandshakeCoordinator` gains an optional progress bridge or
  progress sink dependency.
- Before or after `sidecarClient.TryHandshake`, the coordinator can publish
  progress events that are already present in the sidecar response or supplied
  by a sidecar progress source.
- In V3.2's first implementation, final response `NodeProgress` can be mapped
  through the same bridge, while the contract also supports future streaming
  event sources.

This gives immediate value without requiring SSE/NDJSON. A later V3.x task can
connect a live streaming HTTP transport to the same bridge.

The coordinator should preserve current outcome behavior:

- Disabled handshake still does not call sidecar or publish sidecar progress.
- Timeout/unavailable/invalid sidecar response still falls back.
- Unsafe sidecar progress must not turn an unsafe sidecar response into an
  accepted outcome.
- Safe sidecar progress must not change deterministic DataAgent execution
  results.

### HTTP Adapter Boundary

V3.2 should avoid making `DataAgentGraphHandshakeHttpClient` own progress
authority.

If the HTTP client receives progress data in V3.2, it should expose that data
as untrusted progress event DTOs or callbacks, and the C# bridge should remain
responsible for validation and publishing.

For the first V3.2 version, tests can avoid a real streaming HTTP parser. Fake
handlers can return a normal handshake response with progress metadata, and
unit tests can exercise the bridge directly.

### Python Dev Stub Shape

The optional stub under `tools/dataagent-graph-sidecar` should remain manually
run and local-only.

V3.2 can extend the stub with a bounded progress shape in one of two ways:

- Keep `/handshake` returning safe `NodeProgress` events that match the new C#
  bridge contract.
- Add a clearly optional `/handshake-progress` or documented progress payload
  example without adding a default live test.

The stub must continue to avoid SQL, database access, QChat, QQ, file IO,
browser control, checkpoint writes, subprocess management, and environment
credential reads.

## Data Flow

Default path:

```text
DataAgent runs deterministic C# orchestration
-> graph handshake disabled or endpoint absent
-> no sidecar progress bridge input exists
-> existing DataAgent progress behavior is unchanged
```

Configured dev path:

```text
DataAgent runs deterministic C# orchestration
-> graph handshake request is built
-> sidecar returns bounded handshake response and progress-shaped data
-> C# validates response through DataAgentGraphHandshakeValidator
-> C# validates progress through sidecar progress bridge
-> accepted progress maps to DataAgentProgressEvent
-> IDataAgentProgressSink records and publishes owner diagnostics
-> deterministic C# remains execution authority
```

Unsafe progress path:

```text
sidecar sends unknown node, undefined status, SQL-like message, hidden context,
tool-route payload, evidence pack, credential marker, QChat text, QQ/browser
marker, checkpoint-write marker, or over-budget facts
-> C# rejects or redacts the progress event
-> no raw unsafe payload is retained
-> existing DataAgent result behavior continues
```

Future live stream path:

```text
SSE or NDJSON sidecar stream
-> transport parses untrusted progress DTOs
-> same C# progress bridge validates and maps
-> existing progress diagnostics publish sanitized updates
```

V3.2 prepares this path but does not require implementing live streaming
transport.

## Diagnostics And Readiness

V3.2 should add a readiness check such as:

```text
GraphHandshakeDevSidecarProgressBridgePresent
```

Readiness detail should prove:

```text
default_enabled=false
progress_bridge=true
csharp_recorder_authority=true
unsafe_progress_rejected=true
unsafe_progress_redacted=true
qchat_boundary=true
no_sql_authority=true
runtime_required=false
```

The static readiness script should check for:

- Sidecar progress model.
- Sidecar progress validator or bridge.
- Mapper into `DataAgentProgressEvent`.
- Use of `IDataAgentProgressSink`.
- Tests covering accepted progress, rejected SQL-like progress, rejected hidden
  context progress, rejected unknown node, bounded facts, and no direct QChat
  imports.

QChat engineering map count should only change if a new QChat-facing required
check is added. The preferred V3.2 design keeps QChat count unchanged and
extends omit-pattern coverage if new DataAgent progress bridge model names
appear.

## Testing Strategy

Default tests must remain local and deterministic.

Required test categories:

- Sidecar progress defaults and limits.
- Accepted sidecar progress maps into `DataAgentProgressEvent`.
- Unknown node is rejected.
- Undefined status is rejected.
- Free-form or unsafe reason code is rejected.
- SQL-like message or fact value is rejected or redacted.
- Hidden context, Tool Broker, evidence pack, token, password, API key, and
  connection-string markers are rejected or redacted.
- Over-budget message, facts, or event count fails closed.
- Sidecar progress cannot mark SQL execution as true.
- Coordinator publishes safe mapped progress only when handshake is enabled and
  a progress sink is present.
- Disabled coordinator does not publish sidecar progress.
- Timeout/unavailable/invalid sidecar response does not publish unsafe raw
  progress.
- Existing `DataAgentProgressDiagnosticsFormatter` remains the output
  formatter and continues to redact unsafe fields.
- Readiness dynamic and static checks include V3.2 markers.
- QChat source does not import DataAgent sidecar progress bridge types.
- Python stub static tests prove optional, local-only, safe progress shape.

No default test should start uvicorn, call a live port, require Python packages,
or depend on live QQ, QChat, PostgreSQL, browser automation, model calls, or
network access.

## Documentation

Add or update:

```text
docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md
tools/dataagent-graph-sidecar/README.md
```

The documentation should explain:

- V3.2 adds a progress bridge, not a production runtime.
- Sidecar progress is untrusted input.
- C# remains the only progress recorder and diagnostics publisher.
- Existing DataAgent progress diagnostics are reused.
- Default tests do not require Python or a live sidecar.
- SSE/NDJSON live streaming and real LangGraph runtime behavior remain future
  milestones.

## Acceptance Criteria

V3.2 is complete when:

- A sidecar progress event contract exists with bounded fields.
- Sidecar progress validation rejects unknown nodes, undefined statuses, unsafe
  reason codes, unsafe messages, unsafe facts, and over-budget input.
- Safe sidecar progress maps to existing `DataAgentProgressEvent`.
- Sidecar progress can publish only through `IDataAgentProgressSink`.
- Sidecar progress cannot mark SQL execution or checkpoint mutation.
- Coordinator integration preserves disabled, fallback, timeout, unavailable,
  invalid, rejected, and accepted handshake behavior.
- Unsafe sidecar progress is not retained as raw diagnostics.
- Existing progress diagnostics format accepted sidecar progress with safe
  source and node facts.
- Readiness proves the progress bridge, C# recorder authority, redaction,
  no-SQL-authority, runtime-not-required, and QChat boundary.
- QChat production source still does not import DataAgent graph sidecar,
  handshake, or progress bridge model types.
- Optional Python stub documentation shows the progress shape without making
  Python part of default tests.
- Restore, build, focused DataAgent tests, readiness scripts, QChat engineering
  map, and full solution tests pass.

## Future Handoff

After V3.2, a later V3.x milestone can attach a true streaming transport such
as SSE or NDJSON to the same progress bridge. Another later milestone can add a
minimal LangGraph runtime shell only after transport, progress observation,
fallback, and authority boundaries are already proven.

The planned sequence remains:

```text
V3.1 request/response dev HTTP adapter
-> V3.2 sidecar progress bridge
-> V3.3 optional live streaming transport smoke
-> V3.4 minimal LangGraph runtime shell
```

## Self-Review

- Completeness scan: no open placeholders or deferred implementation details
  are required for this V3.2 milestone.
- Scope check: the design covers a progress bridge, not live streaming
  transport or production LangGraph runtime.
- Boundary check: SQL, checkpoint, Tool Broker, QChat, QQ, file, browser,
  plugin, and deterministic execution authority remain in C#.
- Testability check: all default tests can use fake events and fake handlers
  without Python, FastAPI, uvicorn, network, PostgreSQL, live QChat, model
  calls, browser automation, or QQ.
- Integration check: the design reuses existing DataAgent progress recorder,
  diagnostics publisher, formatter, graph handshake validator, and readiness
  conventions.
- Handoff check: SSE/NDJSON streaming and real LangGraph runtime behavior are
  clearly deferred to later V3.x milestones.
