# DataAgent V4.5 Production Closure Runbook

```text
production_closure=v4.5
source_baseline=v4.4
observation_capacity=256
observation_window_minutes=15
minimum_observations=20
fallback_ratio_basis_points_max=2500
p95_latency_ms_max=2000
retry_storm_threshold_per_minute=60
runtime_restart_count_max=1
fault_drill_count=7
live_kill_switch=true
loopback_only=true
default_enabled=false
agent_advisory_only=true
csharp_validation_authority=true
allows_execution=false
allows_state_write=false
allows_visible_text=false
default_result_changed=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
starts_runtime=false
installs_dependencies=false
```

## Boundary

V4.5 is the production closure for advisory shadow use only. The deterministic C# orchestration result exists before the sidecar call and remains authoritative afterward. LangGraph cannot execute SQL or tools, decide Tool Broker routing, mutate checkpoint/session/queue/audit state, or publish QChat/QQ-visible text.

Automated verification does not start Python, LangGraph, QQ, or NapCat; install dependencies; create a virtual environment; bind a port; or access a non-loopback endpoint. The operator supplies an already-running loopback runtime when performing a live observation window.

## Preconditions

1. Keep V4.4 production shadow disabled and the kill switch active while preparing evidence.
2. Confirm the V4.3 result is `proven_useful`, has score at least 80, and is production-shadow eligible.
3. Configure the existing handshake endpoint as `http://127.0.0.1:<port>/handshake` or `http://localhost:<port>/handshake`. Non-loopback endpoints remain rejected.
4. Confirm the runtime is operator-managed. Alife must not start or supervise it.

## Enable the bounded observation window

Set the graph handshake and V4.4 production shadow flags explicitly, set the V4.3 score/status evidence, then turn the kill switch off. Keep the default V4.5 recorder bounds: 256 records retained for 15 minutes and retry-storm detection above 60 observations in one minute.

Run at least 20 real DataAgent requests through the normal governed route. Review only the aggregate V4.5 snapshot. Do not capture request bodies, response bodies, endpoints, SQL, tokens, hidden context, caller/session/QQ identifiers, exception text, stack traces, or absolute evidence paths.

The observation gate requires:

- fallback ratio no greater than 2500 basis points;
- network P95 latency no greater than 2000 ms;
- no retry storm;
- no more than one externally reported runtime restart;
- aggregate status counts equal the observation count;
- no sensitive data storage.

## Seven required fault drills

Run each drill once through the real V4.4/C# validator boundary and retain only its stable reason code, pass boolean, and network-attempted boolean:

1. runtime unavailable -> `production_shadow_unavailable`, network attempted;
2. timeout -> `production_shadow_timeout`, network attempted;
3. invalid schema -> `invalid_response_schema` or `request_id_mismatch`, network attempted;
4. unsafe authority -> `sql_authority_requested`, network attempted;
5. concurrency saturation -> `production_shadow_busy`, no network attempt for the rejected call;
6. circuit open and deadline recovery -> `production_shadow_circuit_open`, no network attempt while open, followed by a successful post-deadline call;
7. live kill switch -> `production_shadow_kill_switch_active`, no network attempt after activation.

Missing, duplicate, failed, unsafe, or network-boundary-inconsistent drill evidence fails closure.

## Kill switch and rollback

Change `ALIFE_DATAAGENT_V44_KILL_SWITCH` to `true` while the module remains running. The next shadow call must return `production_shadow_kill_switch_active` before network access. If this proof fails, stop the trial and keep production shadow disabled.

After the window and drills, restore the default-off posture:

```text
ALIFE_DATAAGENT_V44_KILL_SWITCH=true
ALIFE_DATAAGENT_V44_PRODUCTION_SHADOW_ENABLED=false
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED=false
```

## Acceptance artifact

Use `DataAgentV45ProductionClosureEvaluator` on the V4.3 value result, aggregate observation snapshot, exact seven-drill result, and operator-reported runtime restart count. Only an accepted result may be presented as production closure evidence.

Call `DataAgentV45ProductionClosureArtifactWriter` explicitly to write `dataagent-v4.5-production-closure.txt`. The artifact contains fixed aggregate fields and booleans only. It contains no free-form summary, payload, endpoint, SQL, token, hidden context, exception, stack trace, or path.
