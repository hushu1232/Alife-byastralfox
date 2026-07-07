# DataAgent V3.1 Dev Sidecar Adapter Design

## Purpose

V3.1 continues the V3 graph integration path by adding an optional local
development sidecar adapter behind the validated V3.0 C# graph handshake
boundary.

The milestone proves that an external Python/FastAPI process can be connected
as an untrusted graph-runtime-shaped collaborator without receiving SQL,
checkpoint, Tool Broker, QChat, QQ, file, browser, RAG, or plugin authority.

This is not the real LangGraph runtime milestone. V3.1 should make the
transport seam real, keep runtime startup manual and optional, and preserve
the deterministic C# DataAgent path as the only execution authority.

## Non-Overengineering Rule

V3.1 must not turn the project into a sidecar-first rewrite.

This milestone must not:

- Start Python automatically from the C# application.
- Require Python, FastAPI, uvicorn, or network access for default tests.
- Add a production process manager.
- Add a live LangGraph dependency to the C# build or default test suite.
- Let the sidecar execute SQL, compile SQL, authorize datasets, authorize
  fields, authorize operators, authorize limits, decide Tool Broker route
  state, mutate checkpoints, write evidence, write audit, write progress,
  send QChat text, own QQ ingress, read files, control browser state, control
  desktop pet state, or manage external RAG sources.
- Replace `DataAgentGraphHandshakeValidator`,
  `DataAgentGraphHandshakeCoordinator`, `DataAgentAnalysisOrchestrator`,
  `DataAgentAnalysisService`, the QueryPlan-first safety pipeline, or existing
  DataAgent diagnostics.
- Add new QChat commands or make QChat import DataAgent graph sidecar or
  handshake model types.
- Agentize deterministic plugin services for demonstration value.

The useful V3.1 outcome is narrow: C# can optionally POST the V3.0 handshake
request to a local dev endpoint, receive a bounded response, validate it, and
fall back safely when anything is missing, slow, invalid, unsafe, or
overreaching.

## Current Foundation

V3.0 already provides the authority boundary V3.1 must reuse:

- `IDataAgentGraphSidecarClient` is the narrow sidecar client interface.
- `DataAgentGraphHandshakeCoordinator` builds a bounded request and handles
  disabled, accepted, rejected, unavailable, timeout, and invalid outcomes.
- `DataAgentGraphHandshakeValidator` treats sidecar responses as untrusted
  input.
- `DataAgentGraphHandshakeUnsafeDiagnosticDetector` rejects raw SQL-like text,
  unsafe diagnostic markers, and secret-like content.
- `DataAgentGraphHandshakeDiagnosticsFormatter` emits bounded owner
  diagnostics through the existing DataAgent graph diagnostics channel.
- `DataAgentGraphHandshakeManifestFactory` exposes scoped node manifests
  without SQL execution authority.
- `DataAgentReadiness` and `tools/check-dataagent-readiness.ps1` prove
  default-disabled, no-SQL-authority, scoped-manifest, fallback, and
  runtime-not-required behavior.
- QChat production source is guarded against importing
  `DataAgentDataQueryGraph*` and `DataAgentGraphHandshake*` model types.

V3.1 should add transport around this boundary, not weaken the boundary.

## Selected Approach

Add a disabled-by-default C# HTTP adapter for `IDataAgentGraphSidecarClient`
and a minimal Python/FastAPI development stub under `tools/`.

The C# adapter is used only when graph handshake is explicitly enabled and a
local endpoint is explicitly configured. It never starts a process. It never
interprets a sidecar response as authority. It only returns a
`DataAgentGraphHandshakeResponse` to the existing coordinator and validator.

The Python stub is a manually run developer/demo aid. It accepts the V3.0
handshake request JSON and returns a safe handshake response with selected
nodes, bounded node progress, a safe trace summary, a controlled context
contribution, `NoSqlAuthority=true`, `ReadOnly=true`, and no requested
checkpoint mutation or visible text.

Default unit tests use fake HTTP handlers and do not start the Python stub.

## Alternatives Considered

### C# HTTP Adapter Only

This is the smallest implementation. It would prove the C# side can call an
HTTP endpoint and keep fallback behavior safe.

It is useful but too abstract for V3.1. Without a runnable dev stub, the
project still lacks a concrete local sidecar shape that future LangGraph work
can replace.

This option is rejected for V3.1.

### Dev HTTP Adapter With Manual Python Stub

This adds a concrete but manually run Python/FastAPI stub. It gives developers
and interview/demo readers a real endpoint without making Python part of
normal tests or runtime startup.

It preserves the C# authority boundary, avoids process lifecycle complexity,
and creates a clean handoff to V3.2 streaming.

This is the selected approach.

### Environment-Gated Live Sidecar Tests

This would add optional live tests that start or call the Python stub when
environment variables are present.

It improves demo confidence, but it also adds port management, dependency
setup, and environment noise. V3.1 should first land the adapter and stub with
fake-handler tests. A future hardening task can add live smoke tests if they
prove useful.

This option is deferred.

### Real Minimal LangGraph Runtime

This would introduce LangGraph and implement an actual graph in Python.

It is too much for V3.1. The project should first prove the HTTP sidecar
adapter, timeout behavior, invalid JSON handling, and validator handoff before
adding graph runtime behavior.

This option is deferred to a later V3.x milestone.

## Configuration Design

V3.1 should keep the existing handshake enable flag:

```text
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED
```

V3.1 should add endpoint and timeout configuration:

```text
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS
```

Recommended behavior:

- Missing, blank, malformed, non-loopback, or non-HTTP endpoint values do not
  create a live HTTP sidecar client.
- Endpoint values should be limited to local development addresses in V3.1,
  such as `http://127.0.0.1:8765/handshake` or
  `http://localhost:8765/handshake`.
- Timeout defaults to a short value such as 800 milliseconds.
- Timeout parsing should fail closed to the default when missing, invalid,
  zero, negative, or too large.
- Enabling the handshake flag without configuring an endpoint still does not
  start a runtime.

Readiness should distinguish these states:

```text
default_enabled=false
dev_http_adapter_present=true
runtime_started=false
endpoint_required=true
loopback_only=true
fallback=true
validator=true
no_sql_authority=true
```

## C# Component Design

### DataAgentGraphHandshakeHttpClient

Add a C# client that implements `IDataAgentGraphSidecarClient`.

Responsibilities:

- Serialize `DataAgentGraphHandshakeRequest` using standard .NET JSON tooling.
- POST the request to the configured endpoint.
- Apply a short timeout.
- Deserialize `DataAgentGraphHandshakeResponse`.
- Throw or fail in a way the existing coordinator maps to
  `sidecar_timeout`, `sidecar_unavailable`, or `invalid_response_schema`.
- Never validate authority itself except for basic transport shape; the
  existing `DataAgentGraphHandshakeValidator` remains the authority boundary.

Non-responsibilities:

- It must not start Python.
- It must not execute SQL.
- It must not execute tools.
- It must not mutate checkpoints.
- It must not publish diagnostics directly.
- It must not import or call QChat.

### DataAgentGraphHandshakeHttpOptions

Add a small options type for endpoint and timeout parsing.

Responsibilities:

- Parse endpoint from `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT`.
- Parse timeout from `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS`.
- Expose whether the HTTP adapter is configured.
- Enforce loopback-only endpoint policy for V3.1.
- Provide deterministic defaults.

This should be separate from `DataAgentGraphHandshakeOptions` so the existing
enabled flag remains about the handshake boundary, while the new options are
about the optional dev HTTP transport.

### Module Wiring

`DataAgentModuleService` should keep the default disabled behavior.

Recommended wiring:

- If `DataAgentGraphHandshakeOptions.FromEnvironment().Enabled` is false,
  continue wiring `DisabledDataAgentGraphSidecarClient.Instance`.
- If enabled is true but HTTP endpoint options are not configured, still wire
  `DisabledDataAgentGraphSidecarClient.Instance` or a no-endpoint fallback
  client that produces `sidecar_disabled` or `sidecar_unavailable`.
- If enabled is true and a valid loopback endpoint is configured, wire
  `DataAgentGraphHandshakeHttpClient`.

No C# path should launch Python.

## Python Dev Stub Design

Add:

```text
tools/dataagent-graph-sidecar/app.py
tools/dataagent-graph-sidecar/requirements.txt
tools/dataagent-graph-sidecar/README.md
```

The stub should expose:

```text
POST /handshake
GET /health
```

`POST /handshake` should:

- Accept the V3.0 handshake request JSON shape.
- Echo the request id.
- Return `Accepted=true`.
- Return a safe reason code such as `dev_sidecar_accepted`.
- Select only known safe nodes such as `scenario_context`, `query_planner`,
  and `diagnostics_router`.
- Return bounded progress events.
- Return a safe trace summary without SQL-like fragments.
- Return a safe context contribution such as
  `graph_handshake_dev_sidecar=accepted`.
- Return `FallbackRequired=false`.
- Return `NoSqlAuthority=true`.
- Return `ReadOnly=true`.
- Return only requested tool names allowed by the selected node manifests, or
  an empty list.
- Return `RequestsCheckpointMutation=false`.
- Return `RequestsVisibleText=false`.

`GET /health` should return a tiny static JSON payload showing the dev stub is
running. It must not expose local paths, environment variables, credentials, or
runtime internals.

The README should explain that the stub is optional, local-only, manually run,
and not a production runtime.

## Data Flow

Default path:

```text
QChat or Tool Broker routes to DataAgent
-> DataAgent deterministic C# orchestration runs
-> Graph handshake is disabled or endpoint is absent
-> coordinator records fallback-required handshake diagnostics
-> existing DataAgent result behavior is unchanged
```

Configured dev path:

```text
QChat or Tool Broker routes to DataAgent
-> DataAgent deterministic C# orchestration runs
-> coordinator builds DataAgentGraphHandshakeRequest
-> HTTP adapter POSTs request to loopback dev sidecar
-> dev sidecar returns DataAgentGraphHandshakeResponse-shaped JSON
-> C# validator accepts or rejects the untrusted response
-> owner diagnostics show accepted/rejected/unavailable/timeout state
-> deterministic C# remains execution authority either way
```

Failure path:

```text
HTTP timeout, connection failure, non-2xx, malformed JSON, unsafe text,
unknown nodes, unknown tools, SQL authority, checkpoint mutation, or visible
text request
-> existing coordinator returns timeout, unavailable, invalid, or rejected
-> raw unsafe response is not retained
-> existing deterministic DataAgent result behavior continues
```

## Error Handling

Required V3.1 outcomes:

- Endpoint missing: fallback required, no runtime started.
- Endpoint invalid or non-loopback: fallback required, no runtime started.
- Connection refused: `sidecar_unavailable`.
- Timeout: `sidecar_timeout`.
- Non-success HTTP status: `sidecar_unavailable`.
- Malformed JSON: `invalid_response_schema`.
- Valid JSON but unsafe sidecar response: validator reason such as
  `unsafe_trace`, `unknown_node`, `unknown_tool`, or
  `sql_authority_requested`.
- Safe response: `handshake_accepted`.

All outcomes must remain non-fatal to the user-facing DataAgent flow.

## Security Boundary

The sidecar remains untrusted input in V3.1.

C# must:

- Use loopback-only endpoint policy for V3.1.
- Apply short timeouts.
- Bound request and response text through existing models.
- Reject unsafe diagnostic text through
  `DataAgentGraphHandshakeUnsafeDiagnosticDetector`.
- Reject SQL authority, checkpoint mutation, visible text, unknown nodes, and
  unknown tools through `DataAgentGraphHandshakeValidator`.
- Avoid retaining raw rejected responses.
- Avoid publishing raw sidecar payloads to QChat or owner diagnostics.

The Python stub must:

- Avoid reading local files.
- Avoid reading environment variables beyond what is needed to run the server.
- Avoid SQL, database, Tool Broker, QChat, QQ, browser, file, RAG, and
  checkpoint integration.
- Avoid logging raw request payloads by default.
- Return only safe demo text.

## Diagnostics And Readiness

V3.1 should reuse the existing DataAgent graph diagnostics channel.

Diagnostics should be high-level and bounded:

```text
status=accepted|disabled|rejected|unavailable|timeout|invalid
reason=...
fallback_required=true|false
no_sql_authority=true
read_only=true
scoped_node_manifest=true
runtime_required=false
dev_http_adapter=true
runtime_started=false
```

Readiness should prove:

- HTTP adapter type exists.
- HTTP options parse defaults and loopback endpoint values.
- Non-loopback endpoints fail closed.
- Default runtime is not started.
- Timeout and unavailable paths fall back.
- Validator still gates all sidecar output.
- QChat production source still does not import DataAgent graph sidecar or
  handshake model types.

## Testing Strategy

Default tests must not require Python, FastAPI, uvicorn, network access,
PostgreSQL, live QChat, live model calls, browser automation, or QQ.

Tests should use fake HTTP handlers for C# behavior:

- Options default to unconfigured and parse explicit loopback endpoints.
- Non-loopback endpoints are rejected.
- Timeout values default safely.
- HTTP adapter serializes a request containing `NoSqlAuthority=true`,
  `ReadOnly=true`, `FallbackAvailable=true`, and scoped node manifests.
- HTTP 200 with safe JSON returns a response accepted by the existing
  validator.
- HTTP 200 with unsafe SQL-like trace is rejected by the coordinator and raw
  response is not retained.
- HTTP 200 with hidden diagnostic marker context is rejected and raw response
  is not retained.
- HTTP 500 maps to `sidecar_unavailable`.
- Timeout maps to `sidecar_timeout`.
- Malformed JSON maps to `invalid_response_schema`.
- Disabled or endpoint-missing configuration does not call HTTP.
- Diagnostics remain bounded and sanitized.
- Readiness includes V3.1 static and dynamic markers.

Python stub checks should be static by default:

- `app.py` contains `/handshake` and `/health`.
- `app.py` has no SQL, database, QChat, QQ, file, browser, RAG, checkpoint, or
  process-control integration markers.
- `README.md` states the stub is optional, local-only, manually run, and not a
  production runtime.

## Documentation

Add a developer note:

```text
docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md
```

It should explain:

- V3.1 adds a dev HTTP adapter, not a production sidecar runtime.
- The Python/FastAPI stub is optional and manually run.
- Default tests do not require Python.
- C# keeps SQL, Tool Broker, checkpoint, diagnostics, QChat, and QQ authority.
- Sidecar output is still validated by the V3.0 boundary.
- V3.2 can add streaming progress only after this request/response transport
  is stable.

## Acceptance Criteria

V3.1 is complete when:

- A C# HTTP sidecar client implements `IDataAgentGraphSidecarClient`.
- HTTP endpoint and timeout options are parsed fail-closed.
- Only loopback endpoints are accepted in V3.1.
- Default DataAgent module wiring still starts no runtime.
- Configured dev endpoint wiring can call the HTTP client.
- Timeout, unavailable, malformed JSON, and unsafe sidecar responses fall back
  safely.
- Safe dev sidecar responses can be accepted only after the existing validator
  passes them.
- Rejected unsafe sidecar responses are not retained in coordinator outcomes.
- Owner diagnostics remain bounded and sanitized.
- DataAgent readiness proves V3.1 adapter presence, default no-runtime,
  loopback-only endpoint policy, fallback, and validator behavior.
- QChat production source still does not import DataAgent graph sidecar or
  handshake model types.
- A minimal optional Python/FastAPI dev stub exists under `tools/`.
- Full restore, build, and default tests pass without Python or FastAPI.

## V3.2 Handoff

V3.1 should stop at request/response transport.

V3.2 can add streaming sidecar progress mapped into the existing DataAgent
progress diagnostics channel. V3.2 should not start until V3.1 proves that
sidecar request/response transport remains optional, local-only, bounded,
validated, and safe to ignore.

## Self-Review

- Completeness scan: no incomplete requirements are present.
- Scope check: the spec covers one V3.1 milestone, not a production LangGraph
  runtime.
- Boundary check: SQL, checkpoint, Tool Broker, QChat, QQ, plugin, and
  deterministic execution authority remain in C#.
- Testability check: default tests avoid Python, FastAPI, network, live model,
  live QChat, PostgreSQL, browser, and QQ dependencies.
- Agentization check: deterministic plugin services remain services; V3.1 only
  adds a dev transport for graph-handshake suggestions.
- Handoff check: streaming progress, human interrupts, and checkpointer
  reconciliation remain future V3.x work.
