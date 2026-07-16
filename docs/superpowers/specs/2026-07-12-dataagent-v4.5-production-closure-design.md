# DataAgent V4.5 Production Closure Design

## Objective

V4.5 closes the production-advisory loop around the V4.4 loopback shadow client. It proves that a real DataAgent request can retain its deterministic C# result while a bounded LangGraph advisory call is observed, validated, classified, safely degraded, and evaluated against deterministic production acceptance rules.

The closure does not grant LangGraph execution authority. V5 or later must redesign the permission model before LangGraph may execute SQL, tools, routing, checkpoint changes, state writes, or user-visible text.

## Selected approach

Use an in-memory, fixed-capacity, time-bounded observation recorder and write only an explicit operator-requested safe acceptance artifact. Do not add a database table or an append-only per-call log.

Alternatives rejected:

- A file event log survives restarts but adds continuous disk writes, rotation, concurrency, and sensitive-data review.
- Database telemetry supports long-term queries but expands schema and state-write scope beyond the V4.x advisory boundary.

## Production flow

```text
real DataAgent request
  -> deterministic C# orchestration result
  -> V4.4 loopback/value/kill/concurrency/circuit guard
  -> existing HTTP handshake and C# response validator
  -> accepted/rejected/fallback outcome
  -> V4.5 bounded safe observation
  -> deterministic V4.5 acceptance evaluator
  -> safe production-closure artifact and readiness gate
```

The recorder observes the final coordinator outcome, not an unvalidated sidecar response. This ensures unsafe authority requests and invalid responses are counted as C#-determined rejection or fallback rather than sidecar self-reported success.

## Components

### Observation recorder

`DataAgentV45ProductionObservationRecorder` receives only:

- a fixed status enum;
- a stable machine reason code;
- bounded elapsed milliseconds;
- whether C# required fallback;
- whether a network call was attempted;
- a minute bucket used for bounded rate detection.

It never receives or stores the request, response, endpoint, SQL, token, exception, stack trace, hidden context, caller, session, QQ identifier, visible text, or local evidence path.

Default bounds:

```text
capacity=256
window=15 minutes
max_latency_ms=300000
retry_storm_threshold=60 observations/minute
```

The recorder evicts expired and oldest records under a lock. Its snapshot reports only aggregate counts, fallback ratio in basis points, bounded latency statistics, max observations per minute, and retry-storm state.

Final status mapping:

- coordinator `Accepted` -> `accepted`;
- coordinator `Rejected` or network-attempted `Invalid` -> `rejected`;
- `Timeout` -> `timeout`;
- reason `production_shadow_busy` -> `busy`;
- reason `production_shadow_circuit_open` -> `circuit_open`;
- `Unavailable` -> `unavailable`;
- all remaining disabled/invalid/fallback outcomes -> `fallback`.

### Coordinator integration

`DataAgentGraphHandshakeCoordinator.TryHandshake` becomes a thin timing wrapper around its existing deterministic core. It records exactly one final observation in a `finally`-safe path. Observation failures are diagnostic only and must never change the handshake outcome or deterministic DataAgent result.

When no V4.5 recorder is supplied, behavior is unchanged.

### Live kill switch

V4.4 gains an optional options provider evaluated before every call. Module wiring supplies `DataAgentV44ProductionShadowOptions.FromEnvironment` for production shadow, while existing constructors retain fixed options for tests and manual callers.

Only enablement, kill switch, and V4.3 value gate are live gates. Semaphore capacity, failure threshold, and circuit duration remain fixed at client construction to avoid unsafe concurrent reconfiguration. Changing the kill-switch environment value to true therefore rejects the next call before network access without rebuilding the module.

### Fault-drill evidence

`DataAgentV45ProductionFaultDrillResult` contains seven fixed booleans and stable reason codes. Tests and the operator runbook exercise the real V4.4 decorator, coordinator, and validator for:

1. runtime unavailable;
2. timeout;
3. invalid schema;
4. unsafe authority request;
5. concurrency saturation;
6. circuit open and deadline recovery;
7. live kill switch before network.

The drill result contains no payload or exception text. All seven drills must pass.

### Deterministic acceptance evaluator

`DataAgentV45ProductionClosureEvaluator` consumes:

- one V4.3 value result;
- one V4.5 observation snapshot;
- one fault-drill result;
- external runtime restart count supplied by the operator harness.

Production closure is accepted only when all conditions hold:

```text
V4.3 accepted=true
V4.3 status=proven_useful
V4.3 total_score>=80
V4.3 production_shadow_eligible=true
observation_count>=20
observation window/capacity valid
fallback_ratio_basis_points<=2500
p95_latency_ms<=2000
retry_storm_detected=false
runtime_restart_count<=1
all seven fault drills passed
kill switch drill attempted no network
agent_advisory_only=true
csharp_validation_authority=true
allows_execution=false
allows_state_write=false
allows_visible_text=false
default_result_changed=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

The accepted reason is `v4_5_production_closure_accepted`. Failures use one stable primary reason:

- `v4_5_value_gate_failed`;
- `v4_5_observation_window_incomplete`;
- `v4_5_fallback_ratio_exceeded`;
- `v4_5_latency_budget_exceeded`;
- `v4_5_retry_storm_detected`;
- `v4_5_restart_budget_exceeded`;
- `v4_5_fault_drill_failed`.

No weighted score can compensate for a failed safety gate.

### Formatter and artifact

The formatter emits a fixed key-value document with aggregate numbers, fixed boolean authority fields, drill booleans, and stable reason codes. The artifact writer writes `dataagent-v4.5-production-closure.txt` only when explicitly called with an output directory. The formatted body cannot contain free-form summaries, paths, endpoint text, SQL, tokens, exceptions, or hidden context.

## Readiness and operator runbook

Add `GraphHandshakeV45ProductionClosurePresent` to dynamic and static readiness. Current readiness totals advance from 101/117 to 102/118. Frozen V3 projections remain exactly 111/95.

The runbook documents:

- default-off startup;
- loopback endpoint prerequisite;
- enabling shadow only after a V4.3 proven-useful score;
- observing a bounded 15-minute/256-record window;
- running all seven drills;
- activating the kill switch and proving the next call is pre-network;
- writing and reviewing the safe artifact;
- reverting to default-off configuration.

The runbook does not install dependencies, create environments, start Python, start LangGraph, bind ports, or expose non-loopback endpoints.

## Verification

V4.5 is complete only when:

- every new behavior has a witnessed RED then GREEN test;
- observation classification, eviction, latency statistics, rate detection, and safety are tested;
- live kill-switch behavior is tested;
- all seven fault drills use real production boundaries and pass;
- exact acceptance and every fail-closed reason are tested;
- formatter and artifact safety are tested;
- dynamic readiness is 102/102 and static readiness is 118/118;
- V3 frozen readiness remains 111/95;
- the full DataAgent test project and solution-level relevant regression pass;
- `git diff --check` passes;
- no live sidecar, QQ client, Python runtime, dependency installer, or external network is started by automated verification.
