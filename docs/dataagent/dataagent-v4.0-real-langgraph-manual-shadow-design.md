# DataAgent V4.0 Real LangGraph Manual Shadow Design

V4.0 opens the post-V3 lane by connecting a real LangGraph runtime only through a manual shadow path. It must improve DataAgent loop and harness engineering without turning deterministic Alife surfaces into agents.

The V3.28 freeze remains the baseline:

```text
agent_suggests=true
harness_executes=true
csharp_validates=true
artifact_records=true
readiness_gates=true
operator_decides=true
```

## Goal

V4.0 should prove that LangGraph adds practical engineering value to replay, diagnostics, and operator review:

- It can produce bounded advisory output for a replayed DataAgent turn.
- The harness can compare that advisory output against deterministic replay evidence.
- C# validates the advisory contract before anything is recorded as accepted.
- The operator receives a compact evidence packet instead of raw logs or hidden context.
- The default DataAgent result remains unchanged.

V4.0 is successful only if it reduces review cost or improves failure explanation quality while preserving the V3 authority boundary.

## Non-Goals

V4.0 must not:

- Start LangGraph automatically.
- Install Python or LangGraph dependencies.
- Bind a port from default tests.
- Call a sidecar in default tests.
- Execute SQL from LangGraph output.
- Let LangGraph decide tool routing.
- Let LangGraph mutate session, checkpoint, evidence, audit, or user-visible text.
- Store secrets, SQL text, hidden context, bearer tokens, or absolute local paths.
- Change the default answer, fallback behavior, or QChat visible response.

## Agent Boundary

Good agent candidates:

- Replay failure explanation.
- Planner advisory suggestions.
- Diagnostics summarization.
- Cross-module impact notes.
- Harness diff reason proposals.
- Operator evidence packet summaries.
- Manual next-step recommendations.

Non-agent surfaces:

- SQL compiler.
- SQL safety validator.
- Read-only query executor.
- XML tool route gate.
- Tool Broker permission decision.
- QChat visible text emission.
- Session and checkpoint writes.
- Evidence and audit persistence.
- Readiness gate pass/fail decision.
- Secret, environment, and runtime discovery.
- Harness process execution.

The practical rule is:

```text
LangGraph may describe, suggest, classify, and summarize.
LangGraph may not execute, authorize, persist, route, or publish.
```

## Proposed Flow

```text
operator starts LangGraph manually
operator runs manual V4.0 shadow harness
harness replays selected DataAgent fixture
harness sends bounded replay context to LangGraph
LangGraph returns advisory packet
C# validates advisory packet with V3.24 contract
harness compares advisory to replay evidence and V3.26 diff gate
harness writes manual artifact
operator reviews compact evidence pack
default runtime path remains unchanged
```

The first implementation should reuse the existing V3.19-V3.27 evidence chain instead of inventing a new runtime. The sidecar is a provider, not an owner.

## Data Contract

The V4.0 advisory packet should be small and structured:

```text
contract_version=v4.0
source_replay_id=<stable fixture id>
advisory_kind=diagnostic_summary|planner_note|diff_reason|next_step
accepted_by_contract=true|false
fallback_required=true|false
operator_required=true
default_result_changed=false
requested_authorities=[]
reason_codes=[...]
safe_summary=<bounded text>
evidence_refs=[...]
```

Forbidden fields:

- SQL text.
- Raw hidden context.
- Secrets or tokens.
- Absolute local paths.
- Tool execution payloads.
- Checkpoint mutation payloads.
- Visible user response text.

## Harness Handling

The harness owns all execution. It should:

- Require an operator-provided loopback endpoint.
- Refuse non-loopback endpoints.
- Use bounded timeout and deterministic fallback.
- Save only sanitized manual artifacts.
- Include replay fixture id, reason codes, advisory acceptance, fallback status, and diff-gate status.
- Avoid writing runtime state or changing persisted DataAgent sessions.

If LangGraph is unavailable, times out, returns invalid schema, requests forbidden authority, or includes unsafe text, the harness should record fallback and keep the deterministic replay result as the only trusted result.

## Token Discipline

V4.0 should improve token cost by sending layered context instead of raw logs:

- Layer 1: fixture id, scenario name, route status, and node status.
- Layer 2: compact evidence markers and reason codes.
- Layer 3: bounded failure excerpt only when needed.
- Layer 4: never include SQL text, hidden context, secrets, or full trace dumps.

The expected benefit is better operator-facing explanations with less context than pasting full harness logs.

## Readiness Markers

V4.0 should introduce one dynamic readiness gate and one static script gate:

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

The readiness gate should prove the boundary, not prove that LangGraph is always available.

## Testing

Default tests:

- Validate V4.0 advisory packet schema.
- Reject forbidden authorities.
- Reject unsafe text and secret-like tokens.
- Prove fallback on timeout/unavailable/invalid response.
- Prove default result does not change.
- Prove static readiness markers are present.

Manual tests:

- Operator starts LangGraph outside Alife.
- Harness checks loopback endpoint.
- Harness runs a selected replay fixture.
- LangGraph advisory is accepted or rejected by C# contract.
- Manual artifact is written with sanitized evidence only.

## Version Plan

V4.0 should be a small version, not a broad refactor. The next versions, only if V4.0 proves useful, can be:

- V4.1: token-budgeted context layering for harness replay.
- V4.2: richer operator evidence packet for accepted/rejected advisories.
- V4.3: cross-module impact advisory using the same manual shadow boundary.

Do not expand into autonomous execution until the manual shadow path repeatedly proves value and the operator explicitly approves a new authority boundary.
