# DataAgent Manual Shadow SQLite Bridge Design

## Goal

Close the actual operator manual-shadow path by recording only C#-validated,
bounded outcome metadata in the existing DataAgent SQLite artifact store.

## Boundary

The PowerShell manual-shadow script remains the operator-owned loopback client.
It never writes SQLite. A small C# CLI bridge owns artifact construction and
persistence.

The script passes only:

- a fixed session id and replay id;
- outcome: accepted or fallback;
- a normalized reason code;
- health and handshake HTTP status codes; and
- a fixed context count.

It never passes a handshake body, advisory text, raw JSON artifact, SQL,
credentials, file path, private context, or executable instruction.

## Flow

~~~text
manual-shadow PowerShell script
  -> C# CLI bridge arguments (bounded scalar metadata only)
  -> C# token/status validation
  -> DataAgentLangGraphShadowArtifactRuntimeProvider
  -> SQLite artifact store
  -> owner /dataagent diag langgraph aggregate
~~~

## Success and Fallback

After a validated handshake, the script invokes the bridge with accepted and a
fixed success reason. In its catch block, it invokes the bridge with fallback
and the script's existing normalized failure reason.

Bridge failure is observational only. It writes a bounded marker such as
artifact_persisted=false but does not alter the script's existing PASS/FALLBACK
exit semantics, operator-started boundary, or loopback validation.

## CLI Constraints

- The CLI does not start a runtime, install dependencies, contact a sidecar, or
  perform HTTP requests.
- It does not execute SQL supplied by a caller. It uses only the existing C#
  SQLite artifact store.
- It rejects unknown outcomes, unsafe tokens, invalid status values, and any
  unexpected argument.
- It uses the same DataAgent SQLite store configuration as DataAgentModuleService.
- Store/provider failure returns a bounded nonzero bridge result; the script
  records the marker and preserves its original result.

## Tests

Tests prove:

1. A successful manual script invocation adds an accepted artifact to SQLite.
2. A loopback/handshake failure adds a fallback artifact with only a safe reason.
3. The bridge cannot accept raw JSON, advisory text, SQL, secrets, paths, or
   unknown arguments.
4. Bridge persistence failure does not change script PASS or FALLBACK behavior.
5. Owner diagnostics show the aggregate; non-owners remain silently dropped.
6. No bridge path obtains tool, SQL, audit, checkpoint, or QQ authority.

## Acceptance Criteria

- Actual operator manual-shadow runs, not only test helpers, populate the
  SQLite aggregate.
- SQLite writes remain C#-only and metadata-only.
- Existing manual-script safety and exit contracts remain unchanged.
- The owner diagnostic reports real accepted/fallback aggregate counts.
