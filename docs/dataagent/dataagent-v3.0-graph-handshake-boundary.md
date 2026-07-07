# DataAgent V3.0 Graph Handshake Boundary

V3.0 continues the existing LangGraph preparation path by adding a C# graph handshake boundary.

It does not add a Python sidecar, FastAPI service, HTTP transport, process manager, LangGraph runtime dependency, SQL execution path, or QChat graph ownership.

## What It Adds

- A disabled-by-default graph handshake option.
- Scoped DataAgent graph node manifests.
- A sidecar client interface for future runtime integration.
- A validator that treats sidecar responses as untrusted input.
- A coordinator that reports fallback-required handshake outcomes when the sidecar is disabled, unavailable, timed out, invalid, or overreaching, while deterministic C# orchestration remains the execution path.
- Owner diagnostics exposing bounded, sanitized handshake state, readiness, `NoSqlAuthority`, and fallback reason.

## Disabled Default

The graph handshake boundary is off by default.

The option is controlled by `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED`; in V3.0, enabling it does not start a runtime and still requires a separately designed sidecar client/runtime integration.

When disabled, DataAgent continues through deterministic C# orchestration. No Python process, FastAPI endpoint, HTTP transport, graph runtime, or sidecar tool surface is started by V3.0.

## Authority Boundary

C# remains the authority for:

- QueryPlan validation.
- SQL compilation.
- SQL Safety Validator decisions.
- Read-only query execution.
- Tool Broker route state.
- Checkpoint persistence.
- Evidence, trace, progress, and query audit.
- QChat and QQ ingress.

The sidecar can suggest orchestration shape. It cannot authorize datasets, fields, operators, limits, tools, checkpoint mutation, visible QChat text, QQ messages, executable SQL, or SQL execution.

Sidecar responses have no SQL authority. `NoSqlAuthority` is both the contract stance and the readiness signal owners should expect from this boundary.

## Attention Dilution Control

The handshake uses scoped node manifests. Each node receives only the small capability vocabulary needed for its role.

This is the structural answer to accidental tool selection caused by overlapping tool names and descriptions. The model should not see every Alife plugin tool when it is only planning DataAgent query steps.

## Owner Diagnostics And Readiness

Owner diagnostics should stay high level and sanitized in V3.0: handshake status, bounded readiness state, disabled or fallback reason, timeout or validation failure category, and whether the sidecar response preserved `NoSqlAuthority`.

Readiness does not mean runtime availability. In V3.0, readiness proves `runtime_required=false`, fallback behavior, scoped manifests, and no-SQL-authority status through `NoSqlAuthority`, while all authority decisions remain inside deterministic C#.

## Plugin Policy

These remain deterministic services in V3.0:

- QChat message send and receive.
- owner command access policy.
- visible reply policy.
- voice and TTS.
- desktop pet actions.
- browser control.
- file transfer.
- external RAG source management.
- PostgreSQL checkpoint store internals.
- SQL execution.
- Tool Broker execution policy.

Future V3.x work may expose selected capabilities as graph-scoped manifests, but not as sidecar-callable tools without a separate design.

QChat and other deterministic plugin services remain deterministic services in V3.0. They are not forcibly agentized by the graph handshake boundary.

## V3.x Handoff

V3.1 can add an optional local Python/FastAPI sidecar behind this validated C# contract.

V3.2 can map sidecar streaming progress into existing DataAgent progress diagnostics.

V3.3 can design C#-owned, owner-only interrupt diagnostics or commands that QChat displays through existing deterministic owner-event surfaces.

V3.4 can design checkpointer reconciliation while preserving C# checkpoint authority.
