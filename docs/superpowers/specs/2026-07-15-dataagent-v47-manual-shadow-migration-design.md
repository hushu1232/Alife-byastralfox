# DataAgent V4.7 Manual Shadow Migration Design

## Goal

Make the existing manual shadow entry point interoperate with the real V4.7
LangGraph sidecar while preserving its C#-only SQLite artifact persistence,
operator-controlled loopback execution, and existing terminal-output contract.

## Evidence and Root Cause

The post-merge staging run started the real sidecar on `127.0.0.1:8765` with
`runtimeMode=langgraph`, `langGraphLoaded=true`, `langGraphVersion=0.3.34`,
and `graphCompiled=true`. The checked-in V4.7 manual smoke passed its valid
advisory, malformed JSON, oversized-request, and unsupported-content-type
checks.

The existing `run-dataagent-v4-manual-shadow.ps1` health request succeeded,
but its handshake received HTTP 400. Its old V4.0 request contains
`ContextBudget` and `ContextLayers`, while the real V4.7 sidecar accepts only
the V4.7 strict request field set. The C# bridge correctly recorded a safe
fallback; the failure was a sidecar protocol-gate rejection, not a C# bridge
or SQLite failure.

## Chosen Approach

Migrate the existing manual script in place to the V4.7 strict request and
response schemas. Do not add a second long-lived runner and do not make the
sidecar accept the old V4.0 schema.

The script continues to expose its current external compatibility layer:

```text
tools/run-dataagent-v4-manual-shadow.ps1
PASS manual_shadow
FALLBACK manual_shadow <safe reason>
dataagent-v4.0-real-langgraph-manual-shadow.json
```

Only its internal loopback health and handshake protocol are upgraded.

## Architecture

```text
operator starts V4.7 LangGraph sidecar on loopback
  -> manual-shadow PowerShell script validates V4.7 health attestation
  -> script sends one exact V4.7 advisory request
  -> script validates one exact V4.7 advisory response
  -> script forwards five bounded scalar result fields to C# bridge
  -> C# validates and persists metadata-only artifact in DataAgent SQLite
  -> owner-only QChat diagnostics render aggregate-only metadata
```

The sidecar remains advisory-only. The PowerShell script does not write
SQLite, does not forward request or response bodies to the bridge, and does
not start, install, supervise, or restart the sidecar.

## Health Contract

Before the handshake the script must require a 200 health response whose
attestation has all of these safe values:

- `ready=true`
- `runtimeMode=langgraph`
- `langGraphLoaded=true`
- `langGraphVersion=0.3.34`
- `graphCompiled=true`
- `contractVersion=v4.7`
- `graphVersion=dataagent-advisory-v1`

It must not print or persist the health body. A missing, malformed, or
non-conforming attestation follows the existing fallback path.

## Handshake Contract

The request must contain exactly these V4.7 top-level fields:

```text
RequestId, SessionId, TurnId, CallerId, GoalOrQuestion,
ScenarioContextSummary, RouteScope, QueryConstraints,
NodeManifests, NoSqlAuthority, ReadOnly, FallbackAvailable,
TraceBudgetChars, ProgressBudget
```

It must not send V4.0-only `ContextBudget` or `ContextLayers`. The fixed
manifest remains advisory-only, uses a known V4.7 node name, declares no
execution capability, and retains `NoSqlAuthority=true`, `ReadOnly=true`, and
`FallbackAvailable=true`.

The response must contain exactly the V4.7 response field set and must prove:

- `Accepted=true` and `FallbackRequired=false`;
- `NoSqlAuthority=true` and `ReadOnly=true`;
- `RequestedToolNames=[]`;
- `RequestsCheckpointMutation=false`; and
- `RequestsVisibleText=false`.

Response content is used only for schema/authority validation. It is never
passed to the C# bridge or written into an artifact.

## Persistence and Terminal Behavior

The existing bridge invocation is unchanged in authority and input shape. On
a validated V4.7 success it records `accepted` with the fixed safe reason
`manual_shadow_handshake_accepted`; on a terminal failure it records
`fallback` with the existing closed safe fallback reason mapping.

The existing guarantees remain mandatory: missing, rejected, hung, or
cleanup-faulting bridge execution writes only `artifact_persisted=false` and
cannot change the handshake-derived `PASS manual_shadow`/exit 0 or
`FALLBACK manual_shadow`/exit 1 result. A failure writing the optional legacy
JSON artifact remains a fallback and records only fallback metadata.

## Tests and Acceptance Criteria

Tests must be updated before production code changes and prove all of the
following:

1. The real script sends an exact V4.7 request and validates an exact V4.7
   accepted response.
2. V4.0-only `ContextBudget` and `ContextLayers` are absent from the request;
   a strict V4.7 loopback server accepts it.
3. Wrong/malformed V4.7 health attestation and response fields fall back
   without exposing body text or changing authority boundaries.
4. A real `-File` script run against the operator-started V4.7 sidecar records
   accepted metadata through the existing C# SQLite bridge.
5. A V4.7 handshake rejection records fallback metadata, and all existing
   bridge failure/hang/cleanup and JSON-artifact terminal-contract tests remain
   green.
6. Full DataAgent and QChat suites pass; QChat remains owner-only and
   aggregate-only.

The final staging acceptance run starts only a loopback V4.7 sidecar, uses a
C#-initialized ignored staging SQLite file, proves both accepted and fallback
aggregate writes, then stops the process it started.
