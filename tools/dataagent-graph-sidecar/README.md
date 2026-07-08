# DataAgent Graph Dev Sidecar

This is an optional local-only development stub for DataAgent V3.1/V3.2/V3.3.

It is not a production runtime, not a LangGraph runtime, and not started by
the C# application. It exists so developers can manually exercise the V3.1
HTTP sidecar adapter, V3.2 progress shape, and V3.3 NDJSON stream smoke while
C# remains the authority boundary.

## Run Manually

```powershell
cd tools\dataagent-graph-sidecar
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8765
```

Then configure DataAgent separately:

```powershell
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED = "true"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT = "http://127.0.0.1:8765/handshake"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS = "800"
```

## Boundary

The stub returns graph-handshake suggestions only. It does not read project
files, connect to databases, execute SQL, call Tool Broker tools, mutate
checkpoints, send QChat text, own QQ ingress, control browser state, control
desktop pet state, or manage external RAG sources.

All sidecar output remains untrusted and must pass the C#
`DataAgentGraphHandshakeValidator`.

## V3.2 Progress Shape

V3.2 lets the stub return a bounded progress shape on each
`NodeProgress` item:

```json
{
  "NodeName": "query_planner",
  "Status": "Completed",
  "ReasonCode": "planner_suggested",
  "Message": "planner ready",
  "Facts": {
    "stage": "planner"
  }
}
```

C# remains the only progress recorder and diagnostics publisher. The stub does
not write to `DataAgentProgressRecorder`, does not publish owner diagnostics,
and does not send visible chat text. Sidecar-supplied `Facts` must not include
reserved C# stamped fact keys. C# stamps facts such as `source=graph_sidecar`,
`node`, and `request_id` after validation. default tests do not require Python,
FastAPI, uvicorn, a live port, network access, QChat, QQ, PostgreSQL, browser
automation, model calls, or a live sidecar.

## V3.3 NDJSON Stream

V3.3 adds an optional `/handshake-stream` endpoint for local NDJSON transport
smoke testing. It returns one JSON object per line: progress events first, then
a final response event.

Configure DataAgent separately when manually exercising the stream path:

```powershell
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED = "true"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT = "http://127.0.0.1:8765/handshake-stream"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS = "800"
```

Progress events are untrusted and are buffered until the final response is accepted
by `DataAgentGraphHandshakeValidator`. After acceptance, C# may publish the
buffered progress through `DataAgentGraphSidecarProgressBridge`. Rejected,
invalid, timed out, unavailable, malformed, incomplete, or over-budget streams do
not publish sidecar progress.

SSE is deferred. This stub does not implement `text/event-stream`, event ids,
heartbeats, reconnect behavior, or browser-facing stream behavior.

The default tests do not require Python, FastAPI, uvicorn, a live port, network
access, QChat, QQ, PostgreSQL, browser automation, model calls, or a live
sidecar.

## V3.4 Manual Live Smoke

V3.4 adds a manual live smoke harness for checking an already running sidecar on
loopback. Run the dev sidecar manually first and leave that sidecar process
running.

Then, from the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-graph-sidecar-smoke.ps1 -BaseUri "http://127.0.0.1:8765" -TimeoutMs 2000
```

If your shell is still in `tools\dataagent-graph-sidecar` from the manual run
steps above, return to the repository root before invoking
`tools\run-dataagent-graph-sidecar-smoke.ps1`. The smoke script does not start Python,
does not create a virtual environment, does not install dependencies,
does not launch uvicorn, does not bind ports, and does not manage background
processes. It only calls the already running sidecar at the supplied loopback
`BaseUri`.

This is manual live smoke coverage only; default tests do not call a live
sidecar. They do not require Python, FastAPI, uvicorn, a live port, network
access, QChat, QQ, PostgreSQL, browser automation, model calls, or a live
sidecar.

The harness checks `/health`, `/handshake`, and `/handshake-stream`. The stream
response must use `application/x-ndjson`; SSE is deferred and this V3.4 harness
does not implement event ids, heartbeats, reconnect behavior, or browser
streaming semantics.
