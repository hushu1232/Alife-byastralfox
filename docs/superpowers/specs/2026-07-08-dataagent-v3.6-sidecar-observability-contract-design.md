# DataAgent V3.6 Sidecar Observability Contract Design

## Goal

DataAgent V3.6 adds an offline, deterministic observability contract for the graph sidecar adapter path. The goal is to make disabled, unconfigured, unavailable, rejected, and fallback states visible through stable C# models, reason codes, formatter output, readiness markers, and tests without starting a sidecar runtime or expanding QChat coupling.

## Current Context

V3.1 introduced the graph handshake dev sidecar adapter boundary. V3.2 added C#-owned progress recording and unsafe progress rejection. V3.3 added the NDJSON stream contract while explicitly deferring SSE. V3.4 added a manual live smoke harness. V3.5 added no-network regression tests that parse the PowerShell smoke harness functions and lock the handshake/NDJSON response contract.

The current graph sidecar area is centered on these DataAgent production types:

- `DataAgentGraphHandshakeCoordinator`
- `DataAgentGraphHandshakeHttpClient`
- `DataAgentGraphHandshakeValidator`
- `DataAgentGraphHandshakeDiagnosticsFormatter`
- `DataAgentGraphSidecarContract`
- `DataAgentGraphSidecarProgressBridge`
- `DataAgentReadiness`

V3.6 should not add a new transport. It should make the existing disabled/fallback/rejection behavior easier to inspect and harder to regress.

## Non-Goals

- Do not implement SSE.
- Do not start Python, FastAPI, uvicorn, or a graph sidecar runtime.
- Do not create a virtual environment, install Python packages, or bind ports.
- Do not add live-network default tests.
- Do not change SQL authority boundaries.
- Do not give the sidecar authority to execute SQL, mutate checkpoints, write diagnostics, send visible QChat text, or own QQ ingress.
- Do not modify QChat production source for V3.6.
- Do not replace the V3.4 manual smoke script or V3.5 smoke contract regression tests.

## Proposed Architecture

V3.6 adds a small observability layer around the existing graph handshake path:

1. A stable result snapshot model records whether the sidecar path was disabled, unavailable, rejected, accepted, or fell back.
2. A fixed reason-code vocabulary makes owner diagnostics and tests compare machine-readable outcomes instead of free text.
3. The diagnostics formatter renders a compact, sanitized summary of the latest graph handshake attempt.
4. Readiness and tests assert the contract stays offline, default-disabled, no-SSE, and QChat-boundary clean.

This keeps runtime behavior conservative. The C# side still owns validation, progress acceptance, fallback, and safety. The sidecar remains advisory only.

## Observability Result Model

Add a DataAgent-owned result snapshot that is independent of HTTP and independent of QChat. The model should live under `sources/Alife.Function/Alife.Function.DataAgent`.

Suggested type name:

```csharp
public sealed record DataAgentGraphSidecarObservabilitySnapshot(
    string ReasonCode,
    DataAgentGraphSidecarObservabilityStatus Status,
    bool SidecarEnabled,
    bool EndpointConfigured,
    bool RuntimeStartedByAlife,
    bool NetworkAttempted,
    bool Accepted,
    bool FallbackUsed,
    string SafeSummary);
```

Suggested status enum:

```csharp
public enum DataAgentGraphSidecarObservabilityStatus
{
    Disabled,
    NotConfigured,
    RuntimeUnavailable,
    Rejected,
    Accepted,
    Fallback
}
```

The implementation plan may adjust names if local patterns make a nearby name cleaner, but the contract must preserve the same semantics.

## Reason Codes

V3.6 should introduce a stable list of reason-code constants. The first implementation should cover only the states already present in the codebase and tests:

- `graph_sidecar_disabled`
- `graph_sidecar_not_configured`
- `graph_sidecar_runtime_unavailable`
- `graph_sidecar_response_rejected`
- `graph_sidecar_progress_rejected`
- `graph_sidecar_accepted`
- `graph_sidecar_fallback_used`
- `graph_sidecar_stream_final_response_missing`
- `graph_sidecar_stream_final_response_rejected`

Reason codes must be lowercase machine tokens with underscores. Tests should reject blank, whitespace, unsafe, or free-form reason codes where the observability model accepts external inputs.

## Formatter Contract

`DataAgentGraphHandshakeDiagnosticsFormatter` should be extended or paired with a small formatter for the observability snapshot.

The formatted output must:

- Include `status`, `reason`, `enabled`, `endpoint_configured`, `network_attempted`, `accepted`, and `fallback`.
- Avoid raw SQL, visible QChat text, QQ identifiers, endpoint secrets, stack traces, and request payload dumps.
- Keep output bounded and stable enough for tests.
- Treat untrusted sidecar response data as diagnostic input, not as owner-visible free text.

Example output shape:

```text
graph_sidecar status=Fallback reason=graph_sidecar_response_rejected enabled=true endpoint_configured=true network_attempted=true accepted=false fallback=true
```

The exact string can be adjusted to match existing formatter style, but the named fields and safety boundary must remain.

## Coordinator Integration

The coordinator should expose or internally produce the observability snapshot at the same decision points where it already disables, validates, rejects, accepts, or falls back.

Expected mappings:

- Default disabled path -> `Disabled`, `graph_sidecar_disabled`, `NetworkAttempted=false`, `FallbackUsed=true`.
- Enabled but endpoint absent -> `NotConfigured`, `graph_sidecar_not_configured`, `NetworkAttempted=false`, `FallbackUsed=true`.
- HTTP client unavailable or timeout surfaced as sidecar unavailability -> `RuntimeUnavailable`, `graph_sidecar_runtime_unavailable`, `NetworkAttempted=true`, `FallbackUsed=true`.
- Validator rejects handshake response -> `Rejected`, `graph_sidecar_response_rejected`, `Accepted=false`, `FallbackUsed=true`.
- Validator accepts handshake response -> `Accepted`, `graph_sidecar_accepted`, `Accepted=true`, `FallbackUsed=false`.
- Stream final response missing or rejected -> stream-specific reason code, `FallbackUsed=true`.

The implementation must not add a persistent global cache unless existing code already has a natural per-call result location. Prefer returning or formatting a deterministic value over introducing new mutable shared state.

## Readiness Contract

`tools/check-dataagent-readiness.ps1` should add one V3.6 required marker only if the implementation adds production markers that are stable and meaningful.

Proposed readiness check name:

```text
GraphHandshakeDevSidecarObservabilityContractPresent
```

The readiness detail should prove:

- default-enabled remains false
- observability model exists
- reason codes exist
- fallback reason is rendered
- unsafe diagnostics are redacted or excluded
- SSE remains deferred
- QChat boundary remains clean
- default tests remain offline

If this check is added, the DataAgent readiness required count must increase by exactly one and tests must assert the new count. QChat engineering map count should remain `63` unless a separate explicit QChat task changes it.

## Test Strategy

All V3.6 tests must be deterministic and no-network.

Required test coverage:

- Disabled/default path produces `graph_sidecar_disabled`, no network attempt, fallback true.
- Missing endpoint path produces `graph_sidecar_not_configured`, no network attempt, fallback true.
- Runtime unavailable path can be simulated with an in-memory fake sidecar client, not a live endpoint.
- Rejected response path preserves validator authority and produces `graph_sidecar_response_rejected`.
- Accepted response path produces `graph_sidecar_accepted`.
- Formatter output includes stable fields and excludes unsafe payload text.
- Static boundary test confirms QChat production source still does not reference graph handshake/stream/progress concrete types.
- Readiness test confirms the V3.6 marker and expected count if the readiness script is updated.

Tests may reuse existing graph handshake model builders and fake clients. They must not start `powershell`, `uvicorn`, Python, browser automation, or any network listener.

## Safety Boundaries

V3.6 must preserve these invariants:

- The sidecar remains advisory.
- C# validation remains authoritative.
- SQL execution authority stays forbidden.
- Checkpoint mutation stays forbidden.
- Visible QChat text stays forbidden.
- Unsafe progress and unsafe diagnostics remain rejected or redacted.
- SSE remains deferred.
- Default runtime path remains disabled and offline.
- QChat production source remains uncoupled from graph sidecar transport and progress types.

## Delivery Plan

After this design is approved, create a detailed implementation plan with small tasks:

1. Add observability model and reason-code constants with unit tests.
2. Integrate snapshot creation into the graph handshake coordinator with fake-client tests.
3. Extend diagnostics formatting with safety tests.
4. Add readiness marker and readiness tests if production markers justify a new required check.
5. Run boundary scans and full DataAgent verification.

Each task should commit separately. Implementation should use an isolated worktree and Subagent-Driven Development unless the user chooses otherwise.

## Open Decision

The implementation plan should decide whether the formatter extension belongs inside `DataAgentGraphHandshakeDiagnosticsFormatter` or in a new focused `DataAgentGraphSidecarObservabilityFormatter`. The default recommendation is to extend the existing formatter if the change stays small; create the new formatter if the existing file would become hard to scan.

## Self-Review

- The scope is limited to DataAgent C# observability and tests.
- The design does not require SSE, Python runtime startup, package installation, port binding, live sidecar calls, QChat production changes, or network default tests.
- Reason codes are concrete and machine-readable.
- Readiness count behavior is explicit.
- The implementation path is testable with fake clients and static boundary scans.
