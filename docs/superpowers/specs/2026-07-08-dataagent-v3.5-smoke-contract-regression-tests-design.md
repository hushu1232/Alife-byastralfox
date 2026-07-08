# DataAgent V3.5 Smoke Contract Regression Tests Design

## Purpose

DataAgent V3.5 adds deterministic regression coverage for the V3.4 dev sidecar
smoke harness contract. V3.4 introduced a manual PowerShell live smoke script
that validates an already running local graph sidecar. During final review, the
script needed additional hardening so malformed responses such as SQL authority
tools, unknown nodes, non-string selected nodes, and invalid progress facts could
not be reported as passing.

V3.5 preserves that hardening by turning the reviewed no-network probes into
default test coverage. The useful outcome is a fast offline test that fails if
`tools/run-dataagent-graph-sidecar-smoke.ps1` stops rejecting malformed
handshake or NDJSON responses.

## Selected Direction

Add a C# NUnit test file that runs a no-network PowerShell function harness
against the smoke script:

```text
Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs
```

The test should load only the function declarations from
`tools/run-dataagent-graph-sidecar-smoke.ps1`, not the script's main execution
flow. It should then call `New-SmokeHandshakeRequest`,
`Test-HandshakeResponse`, and `Test-NdjsonStream` with in-memory JSON objects
and strings.

This approach keeps V3.5 in the deterministic test layer. It does not call a
live sidecar, does not open sockets, does not start Python, does not launch
uvicorn, and does not require FastAPI, network access, QChat, QQ, PostgreSQL,
browser automation, or model calls.

## Explicit Non-Goals

V3.5 must not:

- Start Python, FastAPI, uvicorn, or any sidecar process.
- Create or modify a Python virtual environment.
- Run `pip install`.
- Bind a port or call `http://127.0.0.1:8765`.
- Add live NUnit tests.
- Implement SSE or parse `text/event-stream`.
- Modify QChat production source.
- Modify DataAgent runtime behavior.
- Modify the V3.4 smoke script unless a failing regression test proves the
  script has drifted from the current contract.
- Change DataAgent readiness count `90`, dynamic core readiness count `75`, or
  QChat engineering map count `63`.

## Test Harness Contract

The test harness should execute PowerShell in a subprocess with:

```text
-NoLogo
-NoProfile
-ExecutionPolicy Bypass
-Command <generated harness>
```

The generated harness should:

1. Read `tools/run-dataagent-graph-sidecar-smoke.ps1`.
2. Parse it with the PowerShell parser.
3. Extract only function definitions.
4. Invoke the extracted definitions.
5. Build a request with `New-SmokeHandshakeRequest`.
6. Build valid and malformed response objects in memory.
7. Call script functions directly.
8. Print compact `PASS ...` lines for expected accept/reject behavior.
9. Exit non-zero if any malformed response is accepted or any valid response is
   rejected.

The harness must not dot-source the full script, because dot-sourcing the whole
file would run the main execution flow and could attempt HTTP requests.

## Required Positive Case

The test should prove a minimal valid response is accepted. The valid response
should use the V3.4 request fixture's manifest nodes and allowed tools:

```text
SelectedNodes:
- scenario_knowledge
- query_planner
- diagnostics_router

RequestedToolNames:
- dataagent.scenario_context.read
- dataagent.query_plan.propose
- dataagent.diagnostics.progress.read
```

It should include node progress entries with valid statuses and safe string
facts. `Facts` may also be omitted or null in targeted positive checks.

## Required Malformed Handshake Cases

The test must prove `Test-HandshakeResponse` rejects:

- `RequestedToolNames = ["sql.execute"]`.
- `RequestedToolNames = ["unknown.tool"]`.
- `RequestedToolNames = []`.
- A non-string object entry in `SelectedNodes`.
- `SelectedNodes = ["unknown_node"]`.
- `NodeProgress.NodeName = "unknown_node"`.
- `Facts = "not-an-object"`.
- `Facts = []`.
- `Facts.source = "graph_sidecar"`.
- `Facts.safe = 123`.

These cases protect the important V3.4 contract: the smoke script must not
report PASS for sidecar output that requests SQL authority, invents nodes or
tools outside the request manifest, or supplies facts that the C# progress
bridge would not treat as safe untrusted input.

## Required NDJSON Final Response Case

The test must also prove `Test-NdjsonStream` rejects a malformed final response
inside an NDJSON stream when that final response contains forbidden authority,
such as:

```json
{"Kind":"Progress","Progress":{"NodeName":"scenario_knowledge","Status":"Completed","ReasonCode":"ok","Facts":{"safe":"true"}}}
{"Kind":"FinalResponse","Response":{"RequestedToolNames":["sql.execute"],"...":"..."}}
```

The exact JSON can be generated in PowerShell from cloned response objects. The
important behavior is that the final response path uses the same strict
manifest-derived contract as `/handshake`.

## Static Boundary Assertions

The new C# test file should also include static guard assertions that its own
source and harness do not contain live runtime behavior. At minimum it should
avoid:

```text
Invoke-WebRequest
127.0.0.1:8765
Start-Process
uvicorn
text/event-stream
EventSource
```

The broader repository checks from V3.4 remain the authority for QChat and
readiness boundaries.

## Error Handling And Output

The C# test should capture both PowerShell stdout and stderr. On failure, NUnit
should print enough harness output to show which case failed. The expected
successful harness output is compact, for example:

```text
PASS valid response accepted
PASS rejects sql.execute requested tool
PASS rejects unknown requested tool
PASS rejects empty requested tool list
PASS rejects SelectedNodes object entry
PASS rejects SelectedNodes unknown node
PASS rejects NodeProgress unknown node
PASS rejects scalar Facts
PASS rejects array Facts
PASS rejects Facts reserved source key
PASS rejects Facts non-string value
PASS rejects NDJSON final response sql.execute
```

The test should fail if any expected `PASS ...` line is missing or if the
PowerShell process exits non-zero.

## Documentation And Readiness

No new user-facing documentation is required for V3.5 unless the implementation
finds that a short note makes the test boundary clearer. The existing V3.4 docs
already explain that the live smoke harness is manual and that default tests do
not call a live sidecar.

No readiness marker or count change is required. V3.5 is test hardening for the
V3.4 smoke harness, not a new product capability.

## Testing Strategy

Required deterministic verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarSmokeScriptContractTests" -v:minimal
```

Expected: PASS.

Also run the adjacent V3.4 and readiness tests:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentGraphSidecarSmokeScriptContractTests" -v:minimal
```

Expected: PASS.

Run existing static readiness and QChat boundary scripts:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 90 required passed, 0 required missing
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

Run the default runtime boundary scan:

```powershell
rg -n "run-dataagent-graph-sidecar-smoke|Invoke-WebRequest|127\.0\.0\.1:8765|uvicorn|Start-Process" Tests sources
```

Expected: no default test invokes the live smoke script or introduces live
runtime startup behavior. Static references inside the new no-network contract
test must be reviewed carefully if this scan finds them.

## Acceptance Criteria

V3.5 is complete when:

- `DataAgentGraphSidecarSmokeScriptContractTests` exists.
- The tests load smoke script functions without running the script main flow.
- A valid in-memory smoke response is accepted.
- The required malformed handshake cases are rejected.
- The NDJSON final response malformed authority case is rejected.
- No test calls a live sidecar, opens a socket, starts Python, launches uvicorn,
  creates a venv, installs dependencies, or binds a port.
- DataAgent readiness remains `90 required passed, 0 required missing`.
- QChat engineering map remains `63 required passed`.
- QChat production source remains free of graph sidecar handshake, stream, and
  progress type coupling.

## Future Handoff

After V3.5, a later task can revisit optional sidecar startup design if the
manual smoke workflow needs more ergonomics. That should remain a separate
design because process lifecycle, dependency installation, port conflict
handling, and cleanup are materially different from this no-network regression
test hardening.

SSE remains deferred.

## Self-Review

- Placeholder scan: no TBD/TODO placeholders remain.
- Scope check: the design is one bounded test-hardening task.
- Boundary check: default tests stay offline and do not start runtime
  dependencies.
- Consistency check: counts remain DataAgent static `90`, dynamic core `75`,
  and QChat engineering map `63`.
- Ambiguity check: malformed cases and expected verification commands are
  explicitly listed.
