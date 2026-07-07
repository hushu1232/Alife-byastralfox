# DataAgent V3.0 Graph Handshake Boundary

V3.0 starts the LangGraph integration path by adding a C# graph handshake boundary.

It does not add a production Python sidecar, FastAPI service, HTTP transport, process manager, LangGraph runtime dependency, SQL execution path, or QChat graph ownership.

## What It Adds

- A disabled-by-default graph handshake option.
- Scoped DataAgent graph node manifests.
- A sidecar client interface for future runtime integration.
- A validator that treats sidecar responses as untrusted input.
- A coordinator that falls back to deterministic C# orchestration when the sidecar is disabled, unavailable, timed out, invalid, or overreaching.
- Owner diagnostics showing handshake status, readiness, `NoSqlAuthority`, and fallback reason.

## Disabled Default

The graph handshake boundary is off unless explicitly enabled by a future runtime integration path.

When disabled, DataAgent continues through deterministic C# orchestration. No Python process, FastAPI endpoint, HTTP transport, graph runtime, or sidecar tool surface is started by V3.0.

## Authority Boundary

C# remains the authority for:

- QueryPlan validation.
- SQL compilation.
- SQL Safety Validator decisions.
- Read-only query execution.
- Tool Broker route state.
- checkpoint persistence.
- evidence, trace, progress, and query audit.
- QChat and QQ ingress.

The sidecar can suggest orchestration shape. It cannot authorize datasets, fields, operators, limits, tools, checkpoint mutation, visible QChat text, QQ messages, executable SQL, or SQL execution.

Sidecar responses have no SQL authority. `NoSqlAuthority` is both the contract stance and the readiness signal owners should expect from this boundary.

## Attention Dilution Control

The handshake uses scoped node manifests. Each node receives only the small capability vocabulary needed for its role.

This is the structural answer to random tool choice caused by overlapping tool names and descriptions. The model should not see every Alife plugin tool when it is only planning DataAgent query steps.

## Owner Diagnostics And Readiness

Owner diagnostics should stay high level in V3.0: handshake status, readiness, disabled or fallback reason, timeout or validation failure category, and whether the sidecar response preserved `NoSqlAuthority`.

Readiness does not mean runtime availability. In V3.0, readiness means the C# contract can report the boundary state clearly while keeping deterministic fallback and all authority decisions inside C#.

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

V3.2 can map sidecar progress into existing DataAgent progress diagnostics.

V3.3 can map human-in-the-loop interrupts into QChat owner events.

V3.4 can design checkpointer reconciliation while preserving C# checkpoint authority.
