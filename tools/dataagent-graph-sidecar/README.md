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
