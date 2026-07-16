# DataAgent LangGraph V4.7 Sidecar

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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/run-dataagent-langgraph-manual-smoke.ps1 -Endpoint http://127.0.0.1:8765 -ExpectedContractVersion v4.7
```

The operator who starts the process owns shutdown. The smoke script does not start, restart, stop, or supervise it.

Production-canary health requires `ready=true`, `runtimeMode=langgraph`, `langGraphLoaded=true`, `langGraphVersion=0.3.34`, `graphCompiled=true`, `contractVersion=v4.7`, `graphVersion=dataagent-advisory-v1`, a stable runtime UUID, a canonical configuration fingerprint, and a positive startup time.

For the owned V4.7 closure run, use `tools/run-dataagent-v47-live-canary.ps1`. It starts one hidden loopback process, waits at most ten seconds, runs the fixed five-item smoke, drives twenty governed C# requests and seven isolated live drills, writes the safe aggregate artifact, and stops only the process it owns.

## Boundaries

- Request and response bodies are capped at 65536 bytes before JSON deserialization.
- HTTP failures use fixed safe error codes; bodies, exceptions, endpoints, SQL, tokens, hidden context, caller/session identifiers, and local paths are not returned or logged.
- Accepted responses use `FallbackRequired=false`, but C# remains final validation and execution authority.
- The sidecar does not execute SQL or tools, mutate checkpoints or state, publish visible text, or control browser, desktop, memory, QQ, or NapCat.
- V4.7 proves transport, safety, lifecycle, and rollback closure. It remains advisory-only and default-off; model-backed business value is the V4.8 entry gate.
