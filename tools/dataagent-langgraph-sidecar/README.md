# DataAgent LangGraph Sidecar Skeleton

This tool is manual-only. Alife does not start it, install dependencies for it, supervise it, or require it for default tests.

When `langgraph` is already available in the manually selected Python environment, `server.py` routes the advisory response through a tiny `StateGraph`. When it is not available, the server still returns the same advisory response without requiring default tests or C# runtime startup.

## Run Manually

```powershell
python tools/dataagent-langgraph-sidecar/server.py --host 127.0.0.1 --port 8765
```

## Configure Alife Manually

```text
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED=true
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT=http://127.0.0.1:8765/handshake
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS=800
```

## Boundary

The server returns only advisory handshake responses. It does not execute SQL, call tools, mutate checkpoints, write diagnostics, publish QChat text, or control browser/desktop/memory.
