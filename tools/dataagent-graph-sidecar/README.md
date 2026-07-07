# DataAgent Graph Dev Sidecar

This is an optional local-only development stub for DataAgent V3.1.

It is not a production runtime, not a LangGraph runtime, and not started by
the C# application. It exists so developers can manually exercise the V3.1
HTTP sidecar adapter while C# remains the authority boundary.

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
    "source": "graph_sidecar",
    "stage": "planner"
  }
}
```

C# remains the only progress recorder and diagnostics publisher. The stub does
not write to `DataAgentProgressRecorder`, does not publish owner diagnostics,
and does not send visible chat text. default tests do not require Python,
FastAPI, uvicorn, a live port, network access, QChat, QQ, PostgreSQL, browser
automation, model calls, or a live sidecar.
