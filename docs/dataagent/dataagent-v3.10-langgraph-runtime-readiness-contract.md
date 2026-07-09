# DataAgent V3.10 LangGraph Runtime Readiness Contract

V3.10 is not runtime integration. It defines the contract a real LangGraph sidecar must satisfy before it can be introduced behind the existing DataAgent graph sidecar boundary.

V3.10 does not add LangGraph runtime code, start Python, create a virtual environment, install dependencies, bind ports, call a live sidecar from default tests, or put LangGraph into the default DataAgent chain.

## Admission Surface

The future real LangGraph sidecar must satisfy the existing C# graph sidecar contract. The allowed endpoint surface is:

```text
GET /health
POST /handshake
POST /handshake-stream
```

The endpoint must be loopback-only for V3.11 and V3.12 manual runs:

```text
http://127.0.0.1:<port>
http://localhost:<port>
https://127.0.0.1:<port>
https://localhost:<port>
```

The response shape must remain compatible with:

- `DataAgentGraphHandshakeRequest`
- `DataAgentGraphHandshakeResponse`
- `DataAgentGraphHandshakeStreamEvent`
- `DataAgentGraphHandshakeValidator`
- `DataAgentGraphSidecarProgressBridge`
- `DataAgentGraphHandshakeDiagnosticsFormatter`

The runtime must not introduce an alternate JSON shape that bypasses C# validation.

## Authority Boundary

C# remains the authority. LangGraph may be advisory only.

Allowed advisory behavior:

- propose orchestration intent,
- request an existing C# safety service,
- return bounded trace or progress suggestions,
- report deterministic fallback.

Forbidden authority behavior:

- SQL execution,
- executable SQL generation authority,
- dataset, field, operator, or limit authorization,
- checkpoint mutation,
- Tool Broker route decisions,
- evidence writes,
- audit writes,
- progress writes,
- diagnostics writes,
- QChat visible text,
- QQ ingress,
- file, browser, desktop, plugin, or external RAG management authority.

Every sidecar response remains untrusted input. C# validates, executes, persists, records diagnostics, and owns any user-visible result.

## Runtime Lifecycle Boundary

V3.10 freezes these lifecycle markers:

```text
manual_only=true
advisory_only=true
loopback_only=true
starts_runtime=false
installs_dependencies=false
creates_venv=false
binds_port=false
default_tests_live_runtime=false
```

Default tests must not require Python, LangGraph, FastAPI, uvicorn, a live port, network access, QChat, QQ, NapCat, PostgreSQL, browser automation, or model calls.

## Version Handoff

```text
V3.10  LangGraph runtime readiness contract
       No real runtime.

V3.11  Real LangGraph sidecar skeleton
       manual-only, loopback-only, default-disabled.

V3.12  Replay parity / shadow comparison
       Compare real LangGraph output against V3.9 replay fixture expectations.

V4.0   Advisory runtime integration
       LangGraph may influence suggestions only; C# remains the authority.
```

The earliest real LangGraph touch is V3.11.

The earliest replay parity milestone is V3.12. V3.12 must compare real sidecar output against the V3.9 replay baseline before advisory integration.

The earliest default DataAgent chain involvement is V4.0 advisory mode. Even then, LangGraph must not gain SQL, checkpoint, Tool Broker, QChat visible text, QQ ingress, file, browser, desktop, plugin, evidence, audit, progress, or diagnostics authority.

## Readiness Marker

The static readiness marker is:

```text
LangGraphRuntimeReadinessContractPresent
```

Expected detail:

```text
manual_only=true;advisory_only=true;loopback_only=true;starts_runtime=false;installs_dependencies=false;no_sql_authority=true;no_checkpoint_mutation=true;no_visible_text=true;fallback_required=true;replay_parity_required=true;default_tests_live_runtime=false
```

This marker proves the admission contract is present. It does not prove that a real LangGraph runtime exists.
