# DataAgent V3.16 LangGraph Live Smoke Readiness

V3.16 adds the operator runbook for manually smoke testing the real LangGraph sidecar without changing default runtime behavior. The sidecar remains loopback-only, manually started, and advisory-only.

## Safety Markers

operator_runbook=true
manual_start=true
loopback_check=true
smoke_valid_advisory=true
smoke_forbidden_authority_rejected=true
smoke_timeout_fallback=true
kill_switch=true
default_tests_live_runtime=false
starts_runtime=false
installs_dependencies=false

## Runbook

how to start sidecar manually

From `tools/dataagent-langgraph-sidecar`, install Python dependencies outside the default test path, then start the server manually on a loopback endpoint. Alife does not start it, supervise it, create a venv, bind a port, or install dependencies during normal tests.

how to verify loopback binding

Use only `127.0.0.1` or `localhost` endpoints. Confirm non-loopback addresses are not configured before enabling `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED=true`.

how to run smoke tests

Run the existing manual smoke harness against the already-running sidecar endpoint. The valid advisory smoke should return a safe advisory response with `NoSqlAuthority=true`, no requested execution tools, and fallback available.

how to inspect diagnostics

Inspect DataAgent graph handshake observability, bounded diagnostic explanation, shadow comparison, and progress diagnostics. Diagnostics must stay sanitized and must not expose SQL, hidden context, credentials, visible QChat text, or checkpoint mutation.

how to stop sidecar

Stop the manually started process from the operator terminal. Do not add a C# shutdown path or process supervisor in V3.x.

how to confirm fallback works

Disable the sidecar endpoint or stop the manual process, then confirm the coordinator reports timeout or unavailable and `FallbackRequired=true`.

how to prove default chain is unchanged

Run the DataAgent tests with no sidecar endpoint and no live runtime. The deterministic C# chain must still pass, default tests must not call the live sidecar, and accepted/rejected results must not depend on LangGraph.

## Boundary

LangGraph may produce advisory planning signals only. C# continues to validate, gate, execute, record, expose diagnostics, publish visible text, mutate checkpoints, and own fallback.
