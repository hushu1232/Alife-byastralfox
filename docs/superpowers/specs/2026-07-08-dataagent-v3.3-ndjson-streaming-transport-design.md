# DataAgent V3.3 NDJSON Streaming Transport Design

## Purpose

DataAgent V3.3 adds an optional local NDJSON streaming transport smoke for the
graph handshake dev sidecar. It proves that streaming progress-shaped sidecar
data can flow into the V3.2 C# progress bridge without giving the sidecar any
execution, checkpoint, Tool Broker, QChat, QQ, file, browser, plugin, SQL, or
diagnostics authority.

V3.3 is not a production LangGraph runtime milestone. It does not start Python
automatically, does not supervise a sidecar process, does not add SSE, and does
not require Python, FastAPI, uvicorn, network access, live QChat, QQ,
PostgreSQL, browser automation, or model calls for default tests.

The useful outcome is a small, default-disabled `/handshake-stream` path that
reads NDJSON progress events and a final handshake response, buffers progress
until the final response is accepted, and then publishes accepted progress
through the already hardened V3.2 bridge.

## Selected Direction

Implement an NDJSON streaming transport smoke, not SSE and not a runtime shell.

The sidecar stream should use a dedicated endpoint:

```text
/handshake-stream
```

C# sends the existing `DataAgentGraphHandshakeRequest`. The sidecar returns
newline-delimited JSON events:

```json
{"Kind":"Progress","Progress":{"NodeName":"scenario_knowledge","Status":"Completed","ReasonCode":"scenario_context_ready","Message":"scenario context ready","Facts":{"stage":"scenario"}}}
{"Kind":"Progress","Progress":{"NodeName":"query_planner","Status":"Completed","ReasonCode":"planner_suggested","Message":"planner ready","Facts":{"stage":"planner"}}}
{"Kind":"FinalResponse","Response":{"RequestId":"graph-handshake-session-1-turn-1","Accepted":true,"ReasonCode":"handshake_accepted","SelectedNodes":["scenario_knowledge","query_planner"],"NodeProgress":[],"TraceSummary":"ScenarioKnowledge:Completed>QueryPlanner:Completed","ContextContribution":"graph_handshake=accepted","FallbackRequired":false,"NoSqlAuthority":true,"ReadOnly":true,"RequestedToolNames":["dataagent.query_plan.propose"],"RequestsCheckpointMutation":false,"RequestsVisibleText":false}}
```

Progress events are untrusted and are never published while the stream is still
open. The streaming client stores them in a bounded in-memory buffer. The final
event must carry a complete `DataAgentGraphHandshakeResponse`, which the
existing `DataAgentGraphHandshakeValidator` validates. Only after the final
response is accepted can the buffered progress be passed to
`DataAgentGraphSidecarProgressBridge`.

This is deliberately more conservative than real-time progress. V3.3 proves
the transport, schema, fallback, budget, and authority boundaries first. A later
milestone can add true real-time progress semantics if the product needs owner
diagnostics before the final response is accepted.

## Explicit Non-Goals

V3.3 must not:

- Add SSE parsing, SSE reconnect behavior, event ids, heartbeat handling, or a
  browser-facing event stream.
- Add production LangGraph runtime behavior.
- Add automatic Python process startup or sidecar supervision.
- Require a live sidecar or Python packages for default tests.
- Modify QChat production code to import DataAgent graph handshake stream,
  sidecar progress, or graph sidecar model types.
- Publish sidecar progress before the final response is accepted.
- Retain unsafe, rejected, invalid, timed out, or incomplete stream progress in
  owner diagnostics.
- Let the sidecar execute SQL, authorize SQL, prove SQL execution, mutate
  checkpoints, decide Tool Broker route state, send visible QChat text, own QQ
  ingress, write evidence, write audit logs, read files, control browser state,
  control desktop state, or manage external RAG sources.
- Replace `DataAgentGraphHandshakeHttpClient` for the existing request/response
  path.

## Alternatives Considered

### NDJSON Only

This is the selected approach. NDJSON keeps the first streaming milestone small:
one JSON object per line, simple fake-handler tests, bounded line reads, and
clear failure modes. It proves the streaming handoff without bringing in SSE
framing, reconnect, heartbeat, or browser-oriented semantics.

### SSE Now

SSE is a good future transport because it is standard and fits long-lived event
streams. It is too much for V3.3 because `event:`, `data:`, `id:`, blank-line
framing, heartbeats, retries, and partial event semantics would obscure the
more important authority boundary. SSE should be deferred to a later V3.x task
after NDJSON has proven the bridge and fallback behavior.

### Extend `/handshake` With Streaming Mode

Overloading `/handshake` with headers or query parameters would reuse an
endpoint but blur the request/response and stream contracts. V3.3 should keep
`/handshake` stable and add `/handshake-stream` for the new smoke path.

### Separate `/progress-stream`

Starting `/handshake` and `/progress-stream` separately would keep progress
physically separate, but it creates request/session correlation and lifecycle
questions too early. A single `/handshake-stream` gives one request, one stream,
and one final response.

## Component Design

### Stream Models

Add a stream-specific model file such as:

```text
sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamModels.cs
```

The model should be an envelope over untrusted stream events:

```text
DataAgentGraphHandshakeStreamEventKind
  Progress
  FinalResponse

DataAgentGraphHandshakeStreamEvent
  Kind
  Progress?
  Response?

DataAgentGraphHandshakeStreamResult
  Response?
  Progress[]
```

The progress shape should reuse `DataAgentGraphHandshakeProgress` rather than
`DataAgentProgressEvent`, because the sidecar must not construct recorder
events. The final response should reuse `DataAgentGraphHandshakeResponse` so the
existing validator remains the authority.

Each event must contain exactly the body that matches its kind:

- `Progress` requires `Progress` and forbids `Response`.
- `FinalResponse` requires `Response` and forbids `Progress`.

Unknown kinds, missing bodies, and dual-body events are invalid stream schema.

### NDJSON Stream Client

Add a focused client, for example:

```text
DataAgentGraphHandshakeNdjsonStreamClient
```

and a small interface, for example:

```text
IDataAgentGraphHandshakeStreamClient
```

The client is responsible only for transport parsing:

- POST the existing `DataAgentGraphHandshakeRequest` to `/handshake-stream`.
- Require a configured loopback endpoint and short timeout.
- Read the response as UTF-8 NDJSON, one bounded line at a time.
- Reject empty, malformed, oversized, or schema-invalid events.
- Buffer progress events up to `DataAgentGraphHandshakeLimits.MaxProgressEvents`.
- Require exactly one final response event.
- Stop reading after final response and reject any unexpected trailing event if
  the implementation observes one.
- Return `DataAgentGraphHandshakeStreamResult`.

The client must not:

- Publish progress.
- Call QChat.
- Start Python.
- Validate final response authority decisions.
- Map sidecar progress into `DataAgentProgressEvent`.

Line and event limits should align with existing handshake limits where
practical. A bad line should fail closed with `invalid_stream_schema`.

### Coordinator Integration

`DataAgentGraphHandshakeCoordinator` should gain an optional stream client or a
mode-specific dependency without disrupting the existing V3.1 request/response
client.

Recommended behavior:

- If graph handshake is disabled, keep the current disabled fallback and do not
  call any sidecar or publish progress.
- If streaming is not configured or not enabled, keep the current `/handshake`
  request/response path.
- If streaming is enabled, call `/handshake-stream`.
- If the stream client returns a final response, validate it with
  `DataAgentGraphHandshakeValidator`.
- If validation is accepted, publish buffered progress through
  `DataAgentGraphSidecarProgressBridge.PublishHandshakeProgress`.
- If validation is rejected, return the current rejected fallback behavior and
  do not publish buffered progress.
- If the stream fails, return fallback status/reason and do not publish buffered
  progress.

Progress bridge exceptions remain diagnostic side effects and must not demote
an accepted handshake. This preserves the V3.2 rule.

### HTTP Options

V3.3 can extend existing HTTP options or add a stream-specific options type.
The selected implementation should keep these properties explicit:

- Endpoint URI for `/handshake-stream`.
- Timeout.
- Configured flag.
- Runtime started flag remains false by default.
- Streaming enabled flag remains false by default.

Only loopback endpoints should be considered configured for default dev usage.
Endpoint-required behavior should mirror V3.1.

### Python Dev Stub

The optional stub under:

```text
tools/dataagent-graph-sidecar/app.py
```

should add `/handshake-stream` returning NDJSON in the confirmed envelope shape.
The stub remains manually run and local-only. Static tests should assert the
endpoint, event kinds, progress payload, final response payload, and absence of
reserved sidecar facts such as `source`, `node`, `request_id`, or `message`.

Default tests must not start uvicorn or require Python packages.

## Data Flow

### Default Path

```text
DataAgent deterministic C# orchestration
-> graph handshake disabled or stream not configured
-> existing request/response behavior or disabled fallback
-> no sidecar stream progress is read
-> existing DataAgent progress behavior is unchanged
```

### Configured NDJSON Smoke Path

```text
DataAgent deterministic C# orchestration
-> coordinator builds DataAgentGraphHandshakeRequest
-> NDJSON stream client POSTs to /handshake-stream
-> client reads bounded progress events into memory
-> client reads final DataAgentGraphHandshakeResponse
-> coordinator validates final response with existing validator
-> accepted response allows buffered progress through V3.2 bridge
-> bridge validates, maps, stamps C# authority facts, and publishes via IDataAgentProgressSink
-> deterministic C# remains execution and diagnostics authority
```

### Unsafe Or Incomplete Stream Path

```text
sidecar emits malformed JSON, unknown event kind, missing event body,
progress over budget, unsafe final response, timeout, HTTP failure, or no final
response
-> coordinator returns fallback with stable reason code
-> buffered progress is discarded
-> no raw unsafe stream payload enters owner diagnostics
```

## Failure Semantics

V3.3 should reuse existing `DataAgentGraphHandshakeStatus` values and add only
stream-specific reason codes where useful:

```text
invalid_stream_schema
missing_stream_final_response
stream_progress_over_budget
sidecar_timeout
sidecar_unavailable
```

Recommended mapping:

- Malformed JSON line: `Invalid` / `invalid_stream_schema`
- Unknown event kind: `Invalid` / `invalid_stream_schema`
- Progress event with missing body: `Invalid` / `invalid_stream_schema`
- Final response event with missing body: `Invalid` / `invalid_stream_schema`
- Event containing both progress and response: `Invalid` /
  `invalid_stream_schema`
- More progress events than budget: `Invalid` or `Rejected` /
  `stream_progress_over_budget`
- End of stream without final response: `Invalid` /
  `missing_stream_final_response`
- HTTP non-success or connection failure: `Unavailable` /
  `sidecar_unavailable`
- Timeout or cancellation due timeout: `Timeout` / `sidecar_timeout`
- Final response validator rejection: existing rejection reason from
  `DataAgentGraphHandshakeValidator`

The final response remains the only authority for accepted/rejected graph
handshake outcome.

## Readiness

Add a required DataAgent readiness check:

```text
GraphHandshakeDevSidecarStreamingTransportPresent
```

Recommended detail:

```text
default_enabled=false;ndjson_stream=true;buffer_until_accepted=true;final_response_required=true;sse_deferred=true;csharp_bridge_authority=true;qchat_boundary=true;runtime_required=false
```

The static readiness script should prove:

- Stream model/envelope exists.
- NDJSON stream client exists.
- `/handshake-stream` is documented in the Python stub.
- Progress is buffered until final response is accepted.
- Final response is required.
- V3.2 progress bridge remains the publishing authority.
- SSE is deferred.
- Runtime start is not required.
- QChat boundary remains clean.

DataAgent required readiness count should increase from `88` to `89`. QChat
engineering map count should stay unchanged unless a QChat-facing required row
is intentionally added. The preferred design keeps QChat count unchanged and
uses source-boundary scans/readiness details to prove QChat does not import
stream model types.

## Testing Strategy

Default tests must be local, deterministic, and free of live sidecar runtime
dependencies.

Required tests:

- NDJSON stream with safe progress and accepted final response buffers progress
  and then publishes through `DataAgentGraphSidecarProgressBridge`.
- Final response rejected by `DataAgentGraphHandshakeValidator` does not publish
  buffered progress.
- Progress over budget fails closed with `stream_progress_over_budget`.
- Malformed JSON line fails with `invalid_stream_schema`.
- Unknown event kind fails with `invalid_stream_schema`.
- Progress event without progress body fails with `invalid_stream_schema`.
- Final response event without response body fails with
  `invalid_stream_schema`.
- Event with both progress and response fails with `invalid_stream_schema`.
- Stream without final response fails with `missing_stream_final_response`.
- Timeout maps to `sidecar_timeout`.
- HTTP non-success or transport exception maps to `sidecar_unavailable`.
- Safe facts such as `stage=planner` still pass through the V3.2 bridge after
  final acceptance.
- Unsafe facts/messages are still rejected by the V3.2 bridge.
- Disabled handshake does not call stream client and does not publish progress.
- Unconfigured streaming falls back to the existing request/response path.
- Python stub static tests prove `/handshake-stream` shape and local-only
  optional behavior.
- Readiness tests assert the new `GraphHandshakeDevSidecarStreamingTransportPresent`
  marker and updated required count.
- QChat source boundary scan proves no production QChat source imports
  `DataAgentGraphHandshakeStream*` or graph sidecar stream types.

No default test should:

- Start Python.
- Start uvicorn.
- Bind or call a real local port.
- Require PostgreSQL.
- Require QChat or QQ runtime.
- Require browser automation.
- Require live model calls.
- Require network access.

## Documentation

Add:

```text
docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md
```

Update:

```text
tools/dataagent-graph-sidecar/README.md
```

Documentation should state:

- V3.3 adds NDJSON streaming transport smoke, not production runtime.
- `/handshake-stream` is dev-only and optional.
- SSE is deferred.
- Progress is buffered until the final response is accepted.
- Final response uses existing `DataAgentGraphHandshakeValidator`.
- Progress publishing uses V3.2 `DataAgentGraphSidecarProgressBridge`.
- Default tests do not require Python or a live sidecar.
- Future V3.x work can attach SSE or a minimal LangGraph runtime shell after
  NDJSON proves the safe transport boundary.

## Acceptance Criteria

V3.3 is complete when:

- A stream event envelope exists for progress and final response events.
- A default-disabled NDJSON stream client can parse bounded `/handshake-stream`
  responses.
- Stream progress is buffered and is not published before final response
  acceptance.
- Accepted final responses publish buffered safe progress through the V3.2
  bridge.
- Rejected, invalid, timed out, unavailable, over-budget, malformed, or
  incomplete streams publish no sidecar progress.
- Stream failures use the confirmed reason codes.
- Existing `/handshake` request/response behavior remains intact.
- Python stub documents and statically exposes `/handshake-stream`.
- Readiness reports `89 required passed, 0 required missing`.
- QChat engineering map passes.
- QChat production source has no graph stream type references.
- Restore, build, focused DataAgent tests, readiness scripts, QChat engineering
  map, and full solution tests pass.
- No default test requires live Python, FastAPI, uvicorn, network, PostgreSQL,
  QChat, QQ, browser automation, or model calls.

## Future Handoff

After V3.3, a later milestone can add SSE by reusing the same stream event
envelope and the same V3.2 progress bridge. Another later milestone can add a
minimal LangGraph runtime shell only after NDJSON has proven transport parsing,
fallback, final-response validation, progress buffering, and authority
boundaries.

The planned sequence remains:

```text
V3.1 request/response dev HTTP adapter
-> V3.2 sidecar progress bridge
-> V3.3 optional NDJSON streaming transport smoke
-> later V3.x SSE adapter
-> later V3.x minimal LangGraph runtime shell
```

## Self-Review

- Completeness scan: the design covers endpoint shape, stream envelope,
  buffering, final response validation, failure semantics, readiness,
  documentation, tests, and acceptance.
- Scope check: the design is limited to NDJSON streaming transport smoke and
  explicitly excludes SSE and production runtime behavior.
- Boundary check: SQL, checkpoint, Tool Broker, QChat, QQ, file, browser,
  plugin, and diagnostics authority remain in C#.
- Testability check: all required tests can use fake handlers or fake stream
  readers and require no live sidecar dependencies.
- Handoff check: SSE and minimal LangGraph runtime shell remain future work
  after the NDJSON transport boundary is proven.
