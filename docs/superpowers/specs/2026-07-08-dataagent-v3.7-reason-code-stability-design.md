# DataAgent V3.7 Reason-Code Stability Design

## Goal

DataAgent V3.7 hardens the V3.6 graph sidecar observability contract by locking every observability reason code to an exact machine-readable literal in tests and readiness checks. This is a pre-LangGraph safety step: it makes future real LangGraph sidecar integration easier to debug without changing runtime behavior.

## Current Context

V3.6 added the graph sidecar observability model, snapshots, diagnostics output, and readiness marker. The implementation defines nine stable reason-code constants:

- `graph_sidecar_disabled`
- `graph_sidecar_not_configured`
- `graph_sidecar_runtime_unavailable`
- `graph_sidecar_response_rejected`
- `graph_sidecar_progress_rejected`
- `graph_sidecar_accepted`
- `graph_sidecar_fallback_used`
- `graph_sidecar_stream_final_response_missing`
- `graph_sidecar_stream_final_response_rejected`

The constants exist and the coordinator maps the important HTTP, fallback, and stream outcomes to them. The remaining gap is test-contract strength: current readiness logic and static markers hard-lock only the most common literals, while the shape/uniqueness test proves the full list is machine-token-like but not that every literal remains exactly stable. V3.7 should close that gap without adding any new runtime capability.

## Non-Goals

- Do not connect a real LangGraph runtime.
- Do not implement SSE.
- Do not start Python, FastAPI, uvicorn, or a graph sidecar runtime.
- Do not create a virtual environment, install Python packages, bind ports, or add live-network default tests.
- Do not modify QChat production source.
- Do not change SQL, checkpoint, visible-text, evidence, or tool-route authority boundaries.
- Do not add a new DataAgent readiness check count unless a new required check is truly necessary.
- Do not change existing sidecar transport behavior.

## Proposed Architecture

V3.7 should be a contract hardening pass over the existing V3.6 model:

1. Upgrade reason-code unit tests from shape-only proof to exact literal proof for all nine constants.
2. Strengthen dynamic readiness so `GraphHandshakeDevSidecarObservabilityContractPresent` verifies all nine literal values, not just the common subset.
3. Strengthen static readiness so the PowerShell marker includes all nine literal strings.
4. Keep readiness counts stable: dynamic DataAgent readiness remains `76`, static DataAgent readiness remains `91`, and QChat engineering map remains `63`.

This keeps V3.7 small, deterministic, and offline while making V3.8 real LangGraph manual sidecar integration safer.

## Reason-Code Contract

The V3.7 reason-code contract is exact. These constants must remain equal to these literals:

| Constant | Literal |
|---|---|
| `DataAgentGraphSidecarObservabilityReasonCodes.Disabled` | `graph_sidecar_disabled` |
| `DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured` | `graph_sidecar_not_configured` |
| `DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable` | `graph_sidecar_runtime_unavailable` |
| `DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected` | `graph_sidecar_response_rejected` |
| `DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected` | `graph_sidecar_progress_rejected` |
| `DataAgentGraphSidecarObservabilityReasonCodes.Accepted` | `graph_sidecar_accepted` |
| `DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed` | `graph_sidecar_fallback_used` |
| `DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing` | `graph_sidecar_stream_final_response_missing` |
| `DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected` | `graph_sidecar_stream_final_response_rejected` |

The existing uniqueness and machine-token format assertions should remain. V3.7 should add exact assertions rather than replacing the broader safety checks.

## Coordinator And Stream Mapping

V3.7 should not change coordinator behavior, but it should make the existing mapping evidence easier to trust:

- Local malformed request fallback remains `graph_sidecar_fallback_used`.
- Invalid sidecar response remains `graph_sidecar_response_rejected`.
- Stream progress over budget remains `graph_sidecar_progress_rejected`.
- Missing final stream response remains `graph_sidecar_stream_final_response_missing`.
- Rejected stream final response remains `graph_sidecar_stream_final_response_rejected`.
- Accepted response remains `graph_sidecar_accepted`.

Existing tests already exercise most of these paths. The implementation plan should prefer adding precise assertions to current tests over creating redundant new flows.

## Readiness Contract

`GraphHandshakeDevSidecarObservabilityContractPresent` remains the V3.6/V3.7 readiness marker. V3.7 should not add a new readiness marker unless implementation discovers a strong reason.

Dynamic readiness should update `graphHandshakeObservabilityReasonCodesReady` so it checks all nine exact literals. Its pass detail can remain:

```text
default_enabled=false;observability_model=true;reason_codes=true;fallback_reason=true;unsafe_diagnostics_redacted=true;sse_deferred=true;qchat_boundary=true;default_tests_live_runtime=false
```

Static readiness should update the existing `GraphHandshakeDevSidecarObservabilityContractPresent` PowerShell declaration to include all nine literal strings. Existing static tests should assert the full literal set.

Expected counts:

- `DataAgentReadiness.CheckCore(...)`: `76`
- `tools/check-dataagent-readiness.ps1`: `$expectedRequired = 91`
- `tools/check-qchat-engineering-map.ps1`: `$expectedRequired = 63`

## Test Strategy

All V3.7 tests must be deterministic and offline.

Required coverage:

- Unit test proves all nine reason-code constants equal their exact literals.
- Unit test still proves all reason codes are unique lowercase machine tokens.
- Stream coordinator tests continue to prove stream-specific observability mappings.
- Readiness tests prove the static PowerShell marker contains all nine reason-code literals.
- Dynamic readiness continues to pass with `GraphHandshakeDevSidecarObservabilityContractPresent`.
- Full DataAgent tests remain green with live PostgreSQL tests skipped when env vars are absent.

Boundary verification should include:

- DataAgent readiness script summary remains `91 required passed, 0 required missing`.
- QChat engineering map summary remains `63 required passed, 0 required missing, 0 optional present, 0 optional missing`.
- QChat production source still has no graph sidecar observability/handshake/stream/progress concrete references.
- SSE/runtime scan still shows only defensive tests or omission checks, not implementation.

## Safety Boundaries

V3.7 must preserve the same boundaries as V3.6:

- The sidecar remains advisory.
- C# validation remains authoritative.
- Default runtime remains disabled and offline.
- No SQL execution authority is added.
- No checkpoint mutation authority is added.
- No visible QChat text authority is added.
- No real LangGraph runtime is started or required.
- SSE remains deferred.
- QChat production source remains uncoupled from DataAgent graph sidecar internals.

## Delivery Plan

After this design is approved, create an implementation plan with small tasks:

1. Strengthen exact reason-code unit tests.
2. Strengthen dynamic and static readiness reason-code coverage without changing counts.
3. Run boundary scans and full DataAgent verification.

Each implementation task should use TDD, commit separately, and run focused tests before moving on. Use an isolated worktree and Subagent-Driven Development unless the user chooses another execution path.

## Self-Review

- The scope is intentionally narrow and does not add runtime behavior.
- The design directly addresses the V3.6 final-review minor gap.
- All nine reason-code literals are listed explicitly.
- Readiness count expectations are explicit and unchanged.
- The design excludes real LangGraph, SSE, Python startup, package installation, port binding, live sidecar calls, QChat production changes, and network default tests.
