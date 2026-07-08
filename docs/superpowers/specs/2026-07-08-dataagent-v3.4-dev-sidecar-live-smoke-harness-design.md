# DataAgent V3.4 Dev Sidecar Live Smoke Harness Design

## Purpose

DataAgent V3.4 adds a manual, opt-in live smoke harness for the existing graph
dev sidecar. The goal is to let a developer who has already started the local
FastAPI sidecar verify the V3.1 `/handshake` request/response path and the V3.3
`/handshake-stream` NDJSON path with one PowerShell command.

V3.4 is not a runtime manager. It does not start Python, create a virtual
environment, install packages, launch uvicorn, supervise background processes,
or reserve ports. It also does not add SSE, production LangGraph behavior,
QChat behavior, SQL execution behavior, browser behavior, file behavior, or
Tool Broker authority.

The useful outcome is a low-side-effect harness that proves the live stub's
wire contract when a developer explicitly asks for it, while the normal build,
test, readiness, and QChat boundary flows remain deterministic and free of
live runtime dependencies.

## Selected Direction

Implement a PowerShell endpoint-only smoke script:

```text
tools/run-dataagent-graph-sidecar-smoke.ps1
```

The script verifies an already running sidecar at a loopback base URI:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-graph-sidecar-smoke.ps1 `
  -BaseUri "http://127.0.0.1:8765" `
  -TimeoutMs 2000
```

The script must fail closed if the sidecar is not running or the endpoint is
not loopback. It should print actionable failure details and exit non-zero on
any contract failure.

Successful output should be concise and script-friendly, for example:

```text
DataAgent graph sidecar live smoke
PASS health
PASS handshake
PASS handshake-stream
Summary: 3 passed, 0 failed
```

## Explicit Non-Goals

V3.4 must not:

- Start Python, FastAPI, uvicorn, or any sidecar process.
- Create or modify a Python virtual environment.
- Run `pip install`.
- Leave a background process behind.
- Bind a port.
- Add default tests that call a live port.
- Add SSE parsing, `text/event-stream`, event ids, heartbeats, reconnect
  behavior, or browser-facing stream semantics.
- Add production LangGraph runtime behavior.
- Modify QChat production code to import DataAgent graph handshake, sidecar
  progress, stream client, or stream model types.
- Grant SQL, checkpoint, Tool Broker, QChat, QQ, file, browser, desktop,
  plugin, evidence, audit, diagnostics, or visible-text authority to the
  sidecar.
- Change the default disabled graph handshake behavior.
- Change existing `/handshake` or `/handshake-stream` C# client semantics
  except where tests prove the smoke harness needs a pure helper or fixture.

## Alternatives Considered

### PowerShell Endpoint-Only Smoke

This is the selected approach. It matches the current Windows-first local
workflow, keeps live runtime behavior outside default tests, avoids process
supervision risk, and gives developers an explicit command for manual checks.

### NUnit Explicit Live Tests

Explicit NUnit tests would make the smoke look like the rest of the test
suite, but they blur the boundary between deterministic tests and live runtime
checks. They also make endpoint configuration and failure output less friendly
for a developer who is simply checking a sidecar instance.

### Optional Sidecar Startup

A script with `-StartSidecar` could eventually create a venv, install
requirements, launch uvicorn, run smoke checks, and stop the process. That is a
larger task because process cleanup, dependency installation, port conflicts,
and long-path Windows behavior need careful handling. It should be deferred
until the endpoint-only smoke harness is stable.

### Always Start Sidecar

Always starting the sidecar is intentionally rejected. It would violate the
current DataAgent graph sidecar boundary: default workflows do not own Python
runtime startup, dependency installation, or background process lifecycle.

## Script Contract

The script should expose these parameters:

```text
-BaseUri
-TimeoutMs
```

Recommended defaults:

```text
BaseUri: http://127.0.0.1:8765
TimeoutMs: 2000
```

The script should accept only loopback HTTP(S) base URIs:

```text
http://127.0.0.1:<port>
http://localhost:<port>
https://127.0.0.1:<port>
https://localhost:<port>
```

It should reject non-loopback hosts such as `example.com`, wildcard hosts such
as `0.0.0.0`, file URIs, and blank values before issuing any request.

The script should construct endpoint URIs from the base URI:

```text
GET  /health
POST /handshake
POST /handshake-stream
```

The script should use PowerShell-native JSON and HTTP primitives where
possible, with a per-request timeout derived from `-TimeoutMs`. It should avoid
shelling out to Python, curl, node, or dotnet.

## Request Fixture

The smoke request should be deterministic and match the existing C# handshake
shape:

```json
{
  "RequestId": "graph-sidecar-smoke-session-1-turn-1",
  "SessionId": "graph-sidecar-smoke-session-1",
  "TurnId": "turn-1",
  "CallerId": "owner",
  "GoalOrQuestion": "Which readiness gates should the dev sidecar suggest?",
  "ScenarioContextSummary": "scenario_context=dev_sidecar_smoke",
  "RouteScope": "route_present=true;route_allows_query=true;route_reason_code=route_allowed",
  "QueryConstraints": "status=Active;executed_sql=false;terminal=false",
  "NodeManifests": [
    {
      "NodeName": "scenario_knowledge",
      "Purpose": "Read scenario context",
      "AllowedToolNames": ["dataagent.scenario_context.read"],
      "DeniedCapabilityMarkers": ["sql.execute", "qchat", "browser", "file", "checkpoint.write"],
      "InputShape": "scenario_context",
      "OutputShape": "scenario_summary",
      "BusinessTerms": ["readiness", "scenario"],
      "SafetyNotes": "No SQL or runtime side effects"
    },
    {
      "NodeName": "query_planner",
      "Purpose": "Suggest read-only query plan",
      "AllowedToolNames": ["dataagent.query_plan.propose"],
      "DeniedCapabilityMarkers": ["sql.execute", "qchat", "browser", "file", "checkpoint.write"],
      "InputShape": "question",
      "OutputShape": "query_plan",
      "BusinessTerms": ["planner", "readiness"],
      "SafetyNotes": "No SQL execution authority"
    },
    {
      "NodeName": "diagnostics_router",
      "Purpose": "Suggest diagnostics route",
      "AllowedToolNames": ["dataagent.diagnostics.progress.read"],
      "DeniedCapabilityMarkers": ["sql.execute", "qchat", "browser", "file", "checkpoint.write"],
      "InputShape": "diagnostics_request",
      "OutputShape": "diagnostics_hint",
      "BusinessTerms": ["diagnostics", "progress"],
      "SafetyNotes": "No owner diagnostics publishing authority"
    }
  ],
  "NoSqlAuthority": true,
  "ReadOnly": true,
  "FallbackAvailable": true,
  "TraceBudgetChars": 1800,
  "ProgressBudget": 16
}
```

The sidecar response should echo `RequestId`. The script should reject
responses that mutate authority flags or request checkpoint/visible-text
behavior.

## Health Check

`GET /health` must return JSON with:

```text
status=ok
runtime=dev_sidecar
```

If the request fails because the connection is refused, times out, or returns
non-JSON, the script should fail with a message that tells the developer to
start the sidecar manually by following `tools/dataagent-graph-sidecar/README.md`.

The script must not attempt to start the sidecar after a health failure.

## Handshake Check

`POST /handshake` must return a JSON object compatible with
`DataAgentGraphHandshakeResponse`.

Required response checks:

- `RequestId` equals the request fixture id.
- `Accepted` is `true`.
- `NoSqlAuthority` is `true`.
- `ReadOnly` is `true`.
- `FallbackRequired` is `false`.
- `RequestsCheckpointMutation` is `false`.
- `RequestsVisibleText` is `false`.
- `SelectedNodes` is non-empty.
- `NodeProgress` is non-empty.
- `RequestedToolNames` does not include forbidden authority markers.
- Each progress item has `NodeName`, `Status`, and `ReasonCode`.
- Sidecar progress `Facts` does not contain reserved C# stamped keys:
  `source`, `node`, or `request_id`.

Forbidden requested-tool markers should include at least:

```text
qchat
qq
browser
file
rag.manage
checkpoint.write
dataagent.query.execute_readonly
```

The script should print a compact PASS line on success and an actionable FAIL
line on the first violated check.

## Handshake Stream Check

`POST /handshake-stream` must return NDJSON with media type
`application/x-ndjson`.

The script should read the full response body after the request completes and
split it into non-empty lines. This is acceptable because V3.4 is a manual
smoke harness for the current finite stub, not real-time streaming UX.

Required NDJSON checks:

- The response content type starts with or includes `application/x-ndjson`.
- The response content type is not `text/event-stream`.
- Each non-empty line parses as JSON.
- Every event has a string `Kind`.
- `Kind` is exactly `Progress` or `FinalResponse`.
- Numeric, missing, whitespace-padded, or composite `Kind` values are rejected
  if observed.
- `Progress` events require `Progress` and forbid `Response`.
- `FinalResponse` events require `Response` and forbid `Progress`.
- At least one `Progress` event appears before the final response.
- Exactly one `FinalResponse` event appears.
- No event appears after `FinalResponse`.
- The final response passes the same authority checks as `/handshake`.
- Stream progress facts do not include `source`, `node`, or `request_id`.

The script should not parse SSE framing. If a response contains `event:` or
`data:` lines instead of NDJSON JSON objects, the check should fail as invalid
NDJSON and the docs should explain that SSE remains deferred.

## Output And Exit Codes

The script should be suitable for both a human and a simple CI/manual command:

- Exit `0` only when health, handshake, and handshake-stream all pass.
- Exit `1` on validation, HTTP, timeout, JSON, non-loopback URI, or contract
  failure.
- Print PASS/FAIL lines with a final summary.
- Avoid printing raw response bodies unless a small, sanitized field is useful
  for troubleshooting.
- Never print environment variables, access tokens, filesystem paths outside
  the repository, or local account state.

Recommended output:

```text
DataAgent graph sidecar live smoke
BaseUri: http://127.0.0.1:8765
PASS health status=ok runtime=dev_sidecar
PASS handshake accepted=true selected_nodes=3 progress=3
PASS handshake-stream progress=3 final_response=true
Summary: 3 passed, 0 failed
```

Failure output should name the failed check:

```text
FAIL handshake-stream expected exactly one FinalResponse event, got 0
Summary: 2 passed, 1 failed
```

## Documentation

Update `tools/dataagent-graph-sidecar/README.md` with a V3.4 section:

- Start the sidecar manually using the existing venv/uvicorn instructions.
- Run `tools/run-dataagent-graph-sidecar-smoke.ps1`.
- Explain that the smoke script does not start Python, install dependencies,
  or manage processes.
- Explain that `/handshake-stream` is NDJSON and SSE is deferred.
- Explain that default tests do not call this script.

Add a short DataAgent doc:

```text
docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md
```

The doc should describe:

- Purpose and non-goals.
- Example command.
- Expected PASS output.
- What failures usually mean.
- Authority and QChat boundaries.
- Why live smoke is manual and not part of default tests.

## Readiness

Add a required static DataAgent readiness marker:

```text
GraphHandshakeDevSidecarLiveSmokeHarnessPresent
```

Recommended detail:

```text
manual_only=true;starts_runtime=false;installs_dependencies=false;loopback_only=true;handshake=true;ndjson_stream=true;sse_deferred=true;qchat_boundary=true;default_tests_live_runtime=false
```

The DataAgent static required readiness count should increase from `89` to
`90`.

The dynamic core readiness count should not increase. V3.4 should not add a
runtime-dependent dynamic check, because a live sidecar remains manual and
optional.

QChat engineering map required count should remain `63`. The existing QChat
source-boundary rules should continue to prove that production QChat source
does not import or depend on DataAgent graph sidecar implementation types.

## Testing Strategy

Default tests must remain deterministic and must not call a live sidecar.

Required deterministic tests:

- Script static guard: the smoke script exists and contains `/health`,
  `/handshake`, `/handshake-stream`, `application/x-ndjson`, loopback URI
  validation, and explicit non-startup markers.
- Script helper tests if helpers are implemented in C# or as parseable
  PowerShell functions. These tests should not call a live port.
- README/doc marker tests: V3.4 manual smoke instructions mention that Python
  startup, venv creation, and dependency installation are manual and not owned
  by the script.
- Readiness tests assert `GraphHandshakeDevSidecarLiveSmokeHarnessPresent`,
  required count `90`, and the exact detail markers.
- QChat engineering map remains at `63`.
- QChat production source boundary scan remains clean for graph handshake,
  sidecar progress, and stream types.

Manual validation may include:

```powershell
cd tools\dataagent-graph-sidecar
.\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8765
```

Then in another terminal:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-graph-sidecar-smoke.ps1 `
  -BaseUri "http://127.0.0.1:8765" `
  -TimeoutMs 2000
```

The implementation plan should treat this as manual verification only. It
should not be required for default completion unless the user explicitly asks
to run live Python.

## Acceptance Criteria

V3.4 is complete when:

- `tools/run-dataagent-graph-sidecar-smoke.ps1` exists.
- The smoke script validates only loopback base URIs.
- The smoke script performs health, `/handshake`, and `/handshake-stream`
  checks against an already running sidecar.
- The smoke script exits `0` only when all three checks pass.
- The smoke script exits non-zero on invalid URI, connection failure, timeout,
  non-JSON health/handshake response, invalid NDJSON stream, invalid authority
  flags, forbidden requested tools, missing final response, duplicate final
  response, or reserved sidecar fact keys.
- The smoke script does not start Python, create venvs, install packages, bind
  ports, or supervise processes.
- Docs show manual sidecar startup followed by the smoke command.
- DataAgent readiness reports `90 required passed, 0 required missing`.
- DataAgent dynamic readiness core count remains unchanged from V3.3.
- QChat engineering map remains `63 required passed`.
- QChat production source has no graph sidecar stream/progress/handshake
  imports.
- Restore/build/focused DataAgent tests/readiness scripts/QChat engineering map
  and full solution tests pass.
- No default test requires Python, FastAPI, uvicorn, network, PostgreSQL,
  QChat, QQ, browser automation, or model calls.

## Future Handoff

After V3.4, a later V3.5 task can add an optional startup wrapper if the manual
smoke proves useful. That task should separately design venv creation, pip
install behavior, uvicorn startup, port conflict handling, process cleanup, and
Windows long-path behavior.

SSE remains deferred. A future SSE milestone should reuse the existing V3.3
stream authority boundary and should not be mixed with the V3.4 endpoint-only
smoke harness.

## Self-Review

- Completeness scan: the design covers purpose, non-goals, script contract,
  request fixture, health, handshake, NDJSON stream, output, docs, readiness,
  tests, and acceptance criteria.
- Scope check: the design is one bounded subsystem: a manual endpoint-only
  smoke harness. Startup management and SSE are explicitly deferred.
- Boundary check: SQL, checkpoint, Tool Broker, QChat, QQ, file, browser,
  desktop, plugin, diagnostics, evidence, audit, and visible-text authority
  remain outside the sidecar.
- Testability check: default verification can use static tests and readiness
  markers without a live sidecar. Live Python remains manual.
- Ambiguity check: V3.4 chooses PowerShell-first, already-running-sidecar-only,
  loopback-only, and static-readiness-only for the new marker.
