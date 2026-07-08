# DataAgent V3.4 Dev Sidecar Live Smoke Harness

DataAgent V3.4 adds a manual live smoke harness for the optional graph
handshake development sidecar. The smoke harness is for a developer who has
already started the dev sidecar by hand and wants to verify the loopback HTTP
surface before using it with C# graph handshake integration.

This is manual live smoke coverage only. The default tests do not call a live
sidecar. In marker form: default tests do not call a live sidecar. They do not
require Python, FastAPI, uvicorn, a live port, network, QChat, QQ, PostgreSQL,
browser automation, model calls, or any already running sidecar.

## Manual Flow

Start the dev sidecar manually first, using the local instructions in
`tools\dataagent-graph-sidecar\README.md`. After the already running sidecar is
listening on loopback, return to the repository root and run the smoke command
from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-graph-sidecar-smoke.ps1 -BaseUri "http://127.0.0.1:8765" -TimeoutMs 2000
```

Expected PASS output has this shape:

```text
DataAgent graph sidecar live smoke
BaseUri: http://127.0.0.1:8765
PASS health status=ok runtime=dev_sidecar
PASS handshake accepted=true selected_nodes=3 progress=3
PASS handshake-stream progress=3 final_response=true
Summary: 3 passed, 0 failed
```

The exact counts may change if the stub contract changes, but a successful
smoke run must report PASS for health, handshake, and handshake-stream, then
finish with zero failed checks.

## Checks

The harness checks `/health` first so a missing or wrong process fails quickly.
It then posts the sample graph handshake request to `/handshake` and validates
the accepted response shape. Finally it posts the same request to
`/handshake-stream`, verifies the response is `application/x-ndjson`, rejects
SSE media type, parses progress events, and requires one final response event.

A health failure usually means no already running sidecar is bound to the
specified loopback endpoint, the port is wrong, or a different process answered.
A handshake failure means the JSON response no longer satisfies the C# accepted
sidecar contract. A handshake-stream failure means the NDJSON transport shape,
content type, event ordering, or final response contract is invalid.

## Runtime Boundary

The smoke script does not start Python, does not create venv, does not install
dependencies, does not launch uvicorn, does not bind ports, and does not manage
background processes. It only visits an already running loopback endpoint on
`127.0.0.1` or `localhost`.

The sidecar has no SQL authority, checkpoint authority, Tool Broker authority,
QChat authority, QQ authority, file authority, browser authority, desktop
authority, plugin authority, diagnostics authority, evidence authority, audit
authority, or visible-text authority. Sidecar output remains untrusted
suggestion input; C# remains authority boundary for validation, progress
publication, diagnostics, checkpoint state, visible text, and every runtime side
effect.

## Deferred SSE

SSE is deferred for V3.4. The smoke harness does not implement or require
`text/event-stream`, event ids, heartbeats, reconnect behavior, browser
streaming semantics, or browser-facing streaming behavior. The live smoke target
for `/handshake-stream` is NDJSON only.
