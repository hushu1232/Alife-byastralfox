# DataAgent V4.7 live canary closure

V4.7 is an operator-owned loopback production-shadow proof. It does not enable LangGraph by default and does not grant Python SQL, tool, checkpoint, state, or visible-text authority.

## Run

Prepare Python 3.11–3.13 with the checked-in `langgraph==0.3.34` lock before the run. Then execute:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/run-dataagent-v47-live-canary.ps1 `
  -Python python `
  -Port 8765 `
  -OutputDirectory Outputs/dataagent-v4.7-live-canary `
  -RequestCount 20 `
  -RuntimeRestartCount 0
```

The wrapper owns exactly one hidden `server.py --runtime-mode langgraph` process on `127.0.0.1`. It waits at most ten seconds for V4.7 health and stops only that owned PID in `finally`.

## Evidence

The five smoke checks are health attestation, valid advisory, malformed JSON, oversized request, and unsupported content type. The canary then sends twenty real requests through the HTTP client, V4.4 governed shadow client, coordinator, and V4.5 observation recorder.

Seven isolated loopback drills prove unavailable transport, timeout, malformed response, unsafe authority rejection, concurrency saturation, circuit-open recovery, and the live kill switch. No production-sidecar fault route is added.

The untracked artifact is `dataagent-v4.7-live-canary-closure.txt`. It contains fixed aggregate counts, latency/fallback budgets, runtime UUID/fingerprint/start time, seven drill booleans, authority invariants, and restoration booleans. It contains no endpoint, request or response body, SQL, token, hidden context, exception, identifier, or local path.

## Shutdown and rollback

The wrapper does not install packages, create a virtual environment, or start/stop QQ or NapCat. It does not set process-global Alife feature variables. On exit it reports `kill_switch_restored=true` and `production_shadow_restored_disabled=true` after stopping its owned sidecar.

V4.8 may start only after the independently verified V4.7 artifact is accepted. V4.8 must add real model-backed business advisory evidence without transferring C# execution or validation authority.
