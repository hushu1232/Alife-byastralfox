# DataAgent V4.7-V4.9 Business Closure Design

## Objective

Complete DataAgent and LangGraph through three explicit closure stages:

1. V4.7 proves the hardened V4.6 runtime through a real loopback production-shadow canary and seven live fault drills.
2. V4.8 adds measurable model-backed business advisory value without granting execution authority.
3. V4.9 allows C# to adopt validated advisory candidates in the real DataAgent/QChat path and closes operations, rollback, audit, and user-result acceptance.

The final V4.9 state is a full-project business closure, not merely a compiled graph, a readiness marker, or an offline test fixture.

## Current value baseline

### DataAgent value and capability

The existing C# DataAgent is already the business-capable system. Its governed flow covers Tool Broker route scope, scenario/catalog context, query-plan generation and validation, SQL compilation and safety, read-only execution, result explanation, evidence, trace, progress, checkpoint, multi-turn analysis, diagnostics, SQLite/PostgreSQL boundaries, and QChat integration.

Its principal value is safe natural-language analysis over structured data while C# preserves route, validation, SQL, execution, state, audit, and visible-text authority. Route denial, terminal actions, invalid session scope, unsafe planner output, and sidecar failure fail closed.

The V4.3 score measures accepted evidence, replay alignment, manifest safety, operator disposition, and review-time reduction. It is a controlled value signal; by itself it is not proof of live production adoption or end-user business success.

### LangGraph value and capability

V4.6 provides a truthful Python runtime boundary: pinned LangGraph 0.3.34, typed state, one startup compile, strict health attestation, strict request/response contracts, 65536-byte resource limits, safe HTTP errors, and invalid-response preservation through V4.4/V4.5.

The current graph is deterministic and advisory-only. It selects a permitted advisory node and returns a fixed safe response. It does not yet call a model, perform meaningful multi-node business reasoning, use memory/checkpoints, invoke tools, execute SQL, write state, or publish visible text.

Its present value is isolation, observability, failure containment, and a safe extension point. V4.8 must prove actual business increment before V4.9 may let C# adopt any candidate.

## Rejected alternatives

### Early Python execution authority

Giving V4.8 direct SQL, tool, checkpoint, audit, or QChat authority would create two execution authorities and allow Tool Broker or C# safety boundaries to be bypassed. This is rejected.

### Permanent shadow-only LangGraph

Leaving LangGraph permanently deterministic and shadow-only would not justify its runtime and operational cost and could not produce a LangGraph business closure. This is rejected.

### Replace the C# orchestrator

Replacing the C# orchestration path would discard mature route, validation, session, diagnostics, SQL safety, and QChat integration. LangGraph remains an advisory planner behind C# authority.

## Cross-version invariants

The following hold through V4.7, V4.8, and V4.9:

- Default graph handshake and production shadow remain disabled.
- The live kill switch is evaluated before every network attempt.
- Only HTTP loopback endpoints are accepted.
- C# owns Tool Broker decisions, QueryPlan validation, SQL compilation/safety, query execution, session/checkpoint mutation, audit writes, and visible text.
- LangGraph never receives executable SQL, credentials, hidden context, QQ identifiers, or absolute evidence paths.
- Artifacts contain fixed safe fields and aggregate counts only.
- Automated tests do not start QQ/NapCat or access external networks.
- Any real model provider is operator configured; missing provider readiness cannot be reported as business-ready.
- Every adoption path has deterministic fallback and produces the same safety outcome as the existing C# path.

## V4.7: live production-shadow technical closure

### Runtime identity

The sidecar creates a random runtime instance identifier once per startup and a SHA-256 configuration fingerprint over this exact canonical inventory:

```text
runtimeMode
langGraphVersion
contractVersion
graphVersion
requestBodyMaxBytes
responseBodyMaxBytes
```

Health adds `runtimeInstanceId`, `configurationFingerprint`, and `startedAtUnixSeconds`. No endpoint, environment value, token, path, caller, session, request, response, or free-form text contributes to or accompanies identity evidence.

### Live canary runner

An operator-owned V4.7 runner may explicitly start and stop a sidecar process that it owns. This does not change Alife module startup or default configuration. The runner must restore kill-switch-on and production-shadow-off in its own execution environment even after failure.

The runner executes:

1. V4.6 health attestation and the five real smoke requests.
2. At least 20 real handshake requests through the C# HTTP client, V4.4 decorator, coordinator, and V4.5 recorder against the real compiled LangGraph sidecar.
3. Seven live boundary drills using isolated loopback responders; the production sidecar gains no fault-injection endpoint.
4. Safe aggregate evaluation and artifact persistence.

The isolated responders prove runtime unavailable, timeout, invalid JSON/schema, unsafe authority, concurrency saturation, circuit open/recovery, and live kill switch. Normal canary requests alone use the real LangGraph runtime.

### V4.7 acceptance

```text
minimum_real_requests=20
fallback_ratio_basis_points_max=2500
p95_latency_ms_max=2000
runtime_restart_count_max=1
fault_drill_count=7
runtime_identity_stable=true
configuration_fingerprint_stable=true
aggregate_counts_coherent=true
stores_sensitive_data=false
kill_switch_restored=true
production_shadow_restored_disabled=true
```

The output is `dataagent-v4.7-live-canary-closure.txt`. V4.8 cannot start from a rejected or missing V4.7 artifact.

## V4.8: model-backed business advisory value

### Multi-node advisory graph

The V4.8 graph contains bounded intake, scenario interpretation, route interpretation, advisory query-plan generation, advisory plan review, diagnostics summary, and terminal response nodes. Conditional routing is limited to the known manifest inventory.

### Provider-neutral model boundary

Python exposes an `AdvisoryModelProvider` interface. Production may use an operator-configured OpenAI-compatible or local model endpoint. Tests use a deterministic fake provider. Provider credentials are environment-only and never appear in health, errors, logs, requests to C#, or artifacts.

The model receives only bounded scenario vocabulary, route summary, query constraints, and allowed manifest names. It returns a strict structured query-plan candidate. It receives no executable SQL and has no tool client.

Health adds `businessAdvisoryReady`. It is true only when a non-stub provider is configured, its fixed readiness check passes, and the multi-node business graph is compiled. Deterministic fake/stub mode cannot satisfy production business readiness.

### V4.8 value evaluation

The business observation window records fixed aggregates:

```text
minimum_business_cases=20
validated_candidate_ratio_min=8000_basis_points
adopted_or_useful_ratio_min=6000_basis_points
replay_alignment_ratio_min=9000_basis_points
review_time_reduction_ratio_min=2000_basis_points
unsafe_candidate_count=0
execution_authority_count=0
```

Per-case payloads, model text, questions, plans, SQL, identifiers, and endpoints are not persisted. The accepted output is `dataagent-v4.8-business-advisory-value.txt`.

## V4.9: full-project business closure

### Controlled adoption path

The real DataAgent/QChat flow becomes:

```text
user/QChat request
-> Tool Broker route
-> C# DataAgent orchestration
-> LangGraph advisory candidate
-> C# QueryPlan validator
-> C# SQL compiler
-> C# SQL safety
-> C# read-only executor
-> C# result explainer
-> evidence/trace/progress/checkpoint
-> QChat-visible result
```

C# may adopt a candidate only when route scope is allowed, the response contract is valid, the candidate uses allowed dataset/fields/operators, replay parity is accepted, the QueryPlan validator accepts it, the kill switch is inactive, and the circuit is closed. Every failed gate returns to the existing C# planner without execution or state side effects from LangGraph.

### Operations and rollback

V4.9 adds aggregate SLO reporting, per-call adoption/fallback reasons, runtime/config identity correlation, restart recovery, kill-switch rehearsal, rollback rehearsal, and a fixed operator acceptance artifact. Rollback means disabling production shadow/adoption and proving the next real request uses the existing C# path without LangGraph network access.

Real QQ live operation is not required without separate authorization. Full-project automated and loopback acceptance must exercise the existing QChat/FunctionCaller boundary and prove visible text still originates from the governed C# result path.

### V4.9 acceptance

```text
minimum_end_to_end_cases=50
task_success_ratio_min=9000_basis_points
validated_plan_ratio_min=9000_basis_points
fallback_ratio_max=2000_basis_points
p95_added_latency_ms_max=2000
unsafe_execution_count=0
route_bypass_count=0
checkpoint_corruption_count=0
visible_text_bypass_count=0
rollback_drill_passed=true
kill_switch_drill_passed=true
operator_acceptance=true
```

The final artifact is `dataagent-v4.9-full-business-closure.txt`. V4.9 is complete only when the real adoption path, aggregate acceptance, rollback, full DataAgent tests, full solution tests, readiness, and V3 freeze all pass.

## Version count and closure meaning

From V4.6, three versions remain:

| Version | Closure | Meaning |
|---|---|---|
| V4.7 | Technical production-shadow closure | Real runtime, canary, live drills, safe artifact |
| V4.8 | Business advisory value closure | Model-backed advisory produces measurable incremental value |
| V4.9 | Full-project business closure | Validated adoption in DataAgent/QChat plus operations and rollback |

No readiness marker, fake provider, deterministic stub, offline-only fixture, or successful compilation can substitute for the live/aggregate evidence required by its version.

## Verification strategy

Each version uses test-first implementation, focused regression, static/dynamic readiness, full DataAgent tests, and full solution tests. V4.7 additionally runs the authorized local live sidecar/canary. V4.8 requires configured non-stub provider evidence before business acceptance. V4.9 requires real end-to-end controlled adoption evidence and rollback proof.

No version is pushed, merged, or released without explicit user direction.
