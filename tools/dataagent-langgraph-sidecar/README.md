# DataAgent LangGraph V4.6 Sidecar

This is a manual-only, operator-owned loopback advisory runtime. Alife does not start it, install its dependencies, supervise it, or require it for default tests.

## Prepare explicitly

Use Python 3.11-3.13 in an operator-selected environment and install the checked-in lock explicitly:

```powershell
python -m pip install -r tools/dataagent-langgraph-sidecar/requirements.lock
```

Automated tests never run that command. The default runtime mode is `langgraph`; missing dependency, wrong version, or graph compilation failure stops startup. `--runtime-mode deterministic-stub` is developer-only and reports `ready=false`.

## Start and verify manually

```powershell
python tools/dataagent-langgraph-sidecar/server.py --host 127.0.0.1 --port 8765 --runtime-mode langgraph
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/run-dataagent-langgraph-manual-smoke.ps1 -Endpoint http://127.0.0.1:8765/handshake
```

The operator who starts the process owns shutdown. The smoke script does not start, restart, stop, or supervise it.

Production-canary health requires `ready=true`, `runtimeMode=langgraph`, `langGraphLoaded=true`, `langGraphVersion=0.3.34`, `graphCompiled=true`, `contractVersion=v4.6`, and `graphVersion=dataagent-advisory-v1`.

## Boundaries

- Request and response bodies are capped at 65536 bytes before JSON deserialization.
- HTTP failures use fixed safe error codes; bodies, exceptions, endpoints, SQL, tokens, hidden context, caller/session identifiers, and local paths are not returned or logged.
- Accepted responses use `FallbackRequired=false`, but C# remains final validation and execution authority.
- The sidecar does not execute SQL or tools, mutate checkpoints or state, publish visible text, or control browser, desktop, memory, QQ, or NapCat.
- V4.6 is a production-canary candidate only. V4.7 must collect the real 20-request window, seven live drills, runtime/config identity, safe aggregates, and closure artifact.
