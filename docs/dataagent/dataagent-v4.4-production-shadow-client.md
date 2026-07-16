# DataAgent V4.4 Production Shadow Client

This checkpoint introduces the guarded production shadow transport used after the deterministic C# result already exists.

```text
production_shadow_client=v4.4
source_baseline=v4.3
default_enabled=false
kill_switch_default=true
loopback_only=true
value_gate_score=80
value_gate_status=proven_useful
bounded_concurrency=true
circuit_breaker=true
no_retry=true
starts_runtime=false
installs_dependencies=false
allows_execution=false
allows_state_write=false
allows_visible_text=false
default_result_changed=false
```

## Activation

V4.4 is active only when the graph handshake is enabled, the HTTP endpoint is configured as loopback, production shadow is explicitly enabled, the kill switch is off, and the V4.3 value gate is `proven_useful` with score at least 80. The module never starts Python, LangGraph, or another process.

Configuration:

```text
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED=true
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT=http://127.0.0.1:8765/handshake
ALIFE_DATAAGENT_V44_PRODUCTION_SHADOW_ENABLED=true
ALIFE_DATAAGENT_V44_KILL_SWITCH=false
ALIFE_DATAAGENT_V44_VALUE_SCORE=80
ALIFE_DATAAGENT_V44_VALUE_STATUS=proven_useful
ALIFE_DATAAGENT_V44_MAX_CONCURRENCY=2
ALIFE_DATAAGENT_V44_FAILURE_THRESHOLD=3
ALIFE_DATAAGENT_V44_CIRCUIT_OPEN_MS=30000
```

## Failure behavior

Calls never queue and are never retried. Saturation returns `production_shadow_busy`. Consecutive transport failures open the circuit; calls then return `production_shadow_circuit_open` until the configured deadline. Timeout and availability failures return stable safe reason codes. Every failure preserves the deterministic C# result and requires fallback.

The exception and snapshot surfaces contain no endpoint, request, response, SQL, token, hidden context, original exception text, or stack trace. The client has advisory authority only and cannot execute tools, mutate state, decide routing, or publish visible text.

V4.4 is not the production closure. V4.5 must add bounded production observations, failure drills, kill-switch verification, acceptance evidence, and the final regression audit.
