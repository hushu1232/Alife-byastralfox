# DataAgent V4.0 Real LangGraph Manual Shadow Integration

```text
real_langgraph_manual_shadow_integration=true
source_baseline=v3.28
manual_only=true
operator_started_runtime=true
loopback_only=true
agent_advisory_only=true
harness_execution_authority=true
csharp_validation_authority=true
default_result_changed=false
fallback_required=true
starts_runtime=false
installs_dependencies=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

V4.0 manual shadow integration connects a real LangGraph runtime only when an operator has already started it on a loopback endpoint. It is not automatic startup, not dependency installation, and not a production switch to real LangGraph. The V3.28 deterministic chain remains the source baseline and the default DataAgent result remains unchanged.

## Boundary

The integration is a manual shadow lane:

- The operator manually starts the LangGraph runtime before the harness runs.
- The runtime must be reachable only on loopback.
- C# validation and the manual harness remain authoritative.
- LangGraph, sidecar code, and agent output are advisory only.
- The harness records fallback when the manual runtime is unavailable, times out, returns invalid schema, asks for forbidden authority, or fails the replay diff gate.
- Default production behavior does not switch to LangGraph.
- No secrets, SQL text, hidden context, bearer tokens, connection strings, or absolute local paths are stored in V4.0 manual artifacts.

LangGraph may summarize replay evidence, classify a bounded failure, and propose operator next steps. It may not start processes, install packages, execute SQL, authorize a route, mutate checkpoints, write hidden state, publish visible QChat text, or override readiness gates.

## Manual Harness

Run the manual harness only after the operator has explicitly started a loopback LangGraph runtime:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-v4-manual-shadow.ps1
```

The default `BaseUri` is:

```text
http://127.0.0.1:8765
```

Use `-BaseUri` only for another loopback URI, and use `-OutputDirectory` only when an operator wants the optional manual JSON artifact:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-v4-manual-shadow.ps1 -BaseUri http://127.0.0.1:8765 -OutputDirectory .tmp\dataagent-v4-shadow
```

The optional JSON artifact intentionally uses a minimal marker schema and does not include `source_baseline` or replay identifiers. The C# formatter and readiness detail carry `source_baseline=v3.28`; the optional harness artifact stays small and avoids storing hidden context or local paths.

## When To Run Real LangGraph

Run the real LangGraph manual shadow only when all of these are true:

- An operator explicitly starts the runtime.
- The runtime is bound to loopback.
- The run is a manual shadow evaluation, not production default behavior.
- The harness can fail closed and preserve deterministic fallback.
- The operator accepts that C# validation is the authority and LangGraph output is advisory.

Do not connect or run real LangGraph from default tests, production defaults, service startup, package restore, readiness checks, or QChat visible-response paths. If the manual shadow helps operator review, it can inform later work; it does not change the V3.28 authority boundary by itself.
