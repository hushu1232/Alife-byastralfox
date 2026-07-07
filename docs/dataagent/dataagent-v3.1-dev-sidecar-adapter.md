# DataAgent V3.1 Dev Sidecar Adapter

V3.1 adds a dev HTTP adapter behind the existing V3.0 graph handshake boundary.

It is not a production sidecar runtime and does not add automatic Python process
management, live LangGraph runtime behavior, SQL execution, Tool Broker
authority, checkpoint ownership, QChat graph ownership, or QQ ingress.

## What It Adds

- Loopback-only HTTP endpoint options.
- A short-timeout C# HTTP client implementing `IDataAgentGraphSidecarClient`.
- Safe fallback for missing endpoint, unavailable endpoint, timeout, malformed
  JSON, and invalid sidecar responses.
- A manually run optional Python/FastAPI stub under `tools/`.
- Readiness markers proving the adapter exists while runtime startup remains
  disabled by default.

## Authority Boundary

C# keeps SQL, QueryPlan validation, SQL Safety Validator, read-only execution,
Tool Broker route state, checkpoint persistence, evidence, trace, progress,
diagnostics, QChat, and QQ authority.

The sidecar can return a bounded graph-handshake suggestion. It cannot
authorize datasets, fields, operators, limits, tools, checkpoint mutation,
visible text, SQL, SQL execution, or plugin actions.

## Testing Boundary

The default tests do not require Python, FastAPI, uvicorn, network access,
PostgreSQL, live QChat, live model calls, browser automation, or QQ.

C# tests use fake HTTP handlers. The Python stub is checked statically and can
be run manually for local demos.

## V3.2 Handoff

V3.2 can add streaming progress after V3.1 proves the sidecar request/response
transport is optional, local-only, bounded, validated, and safe to ignore.
