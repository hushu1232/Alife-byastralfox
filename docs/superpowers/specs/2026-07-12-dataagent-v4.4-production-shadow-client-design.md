# DataAgent V4.4 Production Shadow Client Design

**Date:** 2026-07-12
**Source baseline:** V4.3

## Goal

Allow an explicitly configured production DataAgent process to send a bounded post-result shadow handshake to a loopback LangGraph runtime while preserving the deterministic C# result under every success and failure mode.

## Architecture choice

V4.4 decorates the existing `IDataAgentGraphSidecarClient` and reuses `DataAgentGraphHandshakeHttpClient`, request models, validator, coordinator, and analysis-handler diagnostics path. It does not create a second protocol or replace the deterministic orchestrator. The existing coordinator runs after the C# analysis result is built, so the shadow response remains diagnostic/advisory.

Alternatives rejected:

- a separate V4 HTTP protocol would duplicate the validator and drift from the tested V3 handshake;
- replacing the coordinator with a new interface would create a broad runtime refactor without improving the shadow boundary.

## Configuration

`DataAgentV44ProductionShadowOptions` is fail-closed and reads:

```text
ALIFE_DATAAGENT_V44_PRODUCTION_SHADOW_ENABLED
ALIFE_DATAAGENT_V44_KILL_SWITCH
ALIFE_DATAAGENT_V44_VALUE_SCORE
ALIFE_DATAAGENT_V44_VALUE_STATUS
ALIFE_DATAAGENT_V44_MAX_CONCURRENCY
ALIFE_DATAAGENT_V44_FAILURE_THRESHOLD
ALIFE_DATAAGENT_V44_CIRCUIT_OPEN_MS
```

The existing V3 HTTP options continue to own endpoint and request timeout. Production shadow is ready only when enabled is explicitly true, kill switch is explicitly false, V4.3 status is `proven_useful`, score is at least 80, and the existing endpoint parser accepts a loopback HTTP(S) URI. Defaults are disabled, kill switch active, max concurrency 2, failure threshold 3, and circuit-open duration 30 seconds.

## Client decorator

`DataAgentV44ProductionShadowClient` implements `IDataAgentGraphSidecarClient` and wraps the existing configured client.

Before calling the inner client it checks, in order:

1. production-shadow enabled;
2. kill switch inactive;
3. V4.3 value gate passed;
4. circuit not open;
5. a nonblocking concurrency lease is available.

Failure returns a stable exception reason consumed by the existing coordinator:

```text
production_shadow_disabled
production_shadow_kill_switch_active
production_shadow_value_gate_failed
production_shadow_circuit_open
production_shadow_busy
production_shadow_timeout
production_shadow_unavailable
```

The exception includes only a reason code and whether a network attempt occurred. The coordinator maps it to a fallback outcome and never exposes endpoint, response body, stack trace, request content, or configuration values.

## Circuit and concurrency

- Concurrency uses a per-client semaphore and never queues; excess calls fall back as `busy`.
- Timeout, invalid-response, and unavailable failures increment a consecutive-failure counter.
- Reaching the configured threshold opens the circuit until the injected monotonic clock passes the deadline.
- An accepted transport call resets consecutive failures and closes the circuit. Contract rejection remains authoritative but does not count as transport instability.
- All leases release in `finally`; circuit state is lock-protected.

## Runtime wiring

`DataAgentModuleService` creates the normal V3 HTTP client, then decorates it only when the V4.4 production-shadow option is explicitly enabled. Legacy disabled/default behavior and manual/dev harnesses remain unchanged. The analysis handler still publishes only sanitized handshake diagnostics after the deterministic result exists.

## Tests and readiness

Tests cover default disabled, invalid configuration, loopback/value gate, kill switch, success, timeout/unavailable, threshold opening, time-based recovery, successful reset, nonblocking concurrency rejection, lease release, coordinator reason mapping, module wiring, and proof that default result/SQL/checkpoint/tool/QChat authority never changes.

Add `GraphHandshakeV44ProductionShadowClientPresent` dynamic/static readiness while preserving V3 frozen counts. No default test calls a live endpoint; loopback HTTP behavior uses in-process handlers.

## Non-goals

V4.4 does not start or supervise LangGraph, install dependencies, call non-loopback endpoints, retry requests, queue shadow calls, execute SQL/tools, mutate state, publish visible text, make LangGraph authoritative, enable itself by default, or claim V4.5 production closure.
