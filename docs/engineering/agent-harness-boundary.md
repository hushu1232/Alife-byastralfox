# Agent Harness Boundary

agent_harness_boundary=true
token_budget_context_layers=true
evidence_first_response=true
agent_advisory_only=true
harness_execution_authority=true
csharp_validation_authority=true
artifact_audit_required=true
loop_harness_reuse_required=true
dataagent_is_testbed_not_destination=true

This document defines how Alife should use DataAgent V3.x work to improve the wider project instead of building DataAgent for its own sake.

DataAgent is the testbed. The reusable product is an agent/harness engineering model:

```text
Agent suggests.
Harness executes.
C# validates.
Artifact records.
Readiness gates.
Operator decides.
```

## Purpose

V3.x must not become a private DataAgent feature line. Each remaining V3.x milestone must produce at least one reusable engineering asset for loop, harness, replay, diagnostics, artifact, readiness, or operator workflows.

The reusable assets may start inside DataAgent, but their boundaries must stay extractable. Names may remain DataAgent-specific while the implementation is young, but the design must avoid assumptions that only DataAgent can use.

## Token Budget Model

Low token usage comes from bounded context, not from asking the agent to be vague.

Alife should organize agent-visible context in layers:

```text
Level 0: deterministic C# facts
Level 1: compact run/session state
Level 2: selected evidence snippets
Level 3: structured agent advisory
Level 4: final visible response
```

C# owns context compression. Harnesses and stores may retain full logs, rows, traces, and artifacts, but an agent should receive only bounded packets:

```text
run_id
task
current_state
allowed_advisory_actions
forbidden_authorities
last_successful_step
failure_category
selected_evidence
artifact_index_token
expected_response_schema
```

The agent should not receive full raw logs, full SQL rows, full hidden context, full tool manifests, full replay reports, or full conversation history unless a C# gate explicitly selects and redacts them.

## Response Quality Model

High quality replies must be evidence-first.

Any completion, safety, readiness, or regression claim should be backed by one of:

```text
test result
readiness result
artifact marker
reason code
diff stat
commit hash
operator runbook step
```

The agent may phrase and explain the answer, but the answer should be grounded in C#-validated evidence. If evidence is missing, the response must say what is missing instead of filling the gap with speculation.

Quality also depends on stable failure categories. Harness and DataAgent flows should prefer reason codes over prose-only failures:

```text
accepted_advisory_difference
rejected_authority_claim
timeout_or_transport_failure
invalid_response_schema
unsafe_text_rejected
default_result_changed
missing_marker
dependency_unavailable
manual_runtime_not_started
```

## Agent Boundary

Agents are useful for thinking, explaining, proposing, and comparing.

Good agent work:

```text
explain a classified failure
propose the next manual check
summarize a smoke artifact
suggest replay fixture coverage
compare shadow replay differences
draft an operator runbook section
identify likely cross-module impact
compress evidence into an operator summary
```

Agents must not own execution authority:

```text
start runtime
stop runtime
execute SQL
write state
write secrets
publish final visible answer
decide tool permission
override readiness
commit or push automatically
mutate checkpoint/session state
store hidden context
```

The allowed agent output shape is advisory:

```text
advisory_id
summary
reason_code
confidence
evidence_refs
proposed_next_steps
forbidden_authority_claims
requires_operator_action
```

C# must validate schema, authority claims, unsafe text, evidence references, fallback requirements, and default-result boundaries before any advisory can be recorded or shown.

## Harness Boundary

Harnesses are responsible for deterministic execution and auditability.

A loop or harness run should produce:

```text
input fixture
execution result
failure category
comparison or diff
artifact
index
bundle
readiness check
operator summary
```

The agent may comment on the run only after the harness has produced a bounded evidence packet. This keeps token usage low and prevents the agent from becoming the runtime controller.

## DataAgent V3.x Value Gate

Every remaining V3.x milestone must pass this value gate:

```text
Does this produce a reusable contract, harness, replay fixture, artifact, audit bundle, readiness gate, operator runbook, failure taxonomy, or diagnostics formatter?
```

If the answer is no, the milestone should be reduced, merged into another milestone, or dropped.

DataAgent-specific work is acceptable only when it proves a project-level pattern that loop or harness engineering can reuse later.

## V3.x Direction

The remaining V3.x milestones should shift from DataAgent-private LangGraph integration to reusable advisory/harness infrastructure:

```text
V3.24 Agent Advisory Contract Freeze
V3.25 Real LangGraph Manual Shadow Provider
V3.26 Harness Replay Diff Gate
V3.27 Operator Evidence Pack
V3.28 Final V3 Readiness Freeze
```

V3.24 should freeze the general advisory contract first. LangGraph is one provider of that contract, not the owner of the runtime.

V3.25 may connect real LangGraph, but only as a manual shadow advisory provider. It must not change default DataAgent results.

V3.26 should make replay diff gates useful to harness engineering, not only DataAgent.

V3.27 should produce short, evidence-grounded operator summaries that save token and human attention.

V3.28 should freeze the V3 boundary and decide what, if anything, is eligible for V4 controlled enablement.

## Non-Goals

This boundary does not authorize:

```text
LangGraph as default runtime
agent-owned SQL execution
agent-owned state mutation
agent-owned visible answers
agent-owned readiness decisions
agent-owned tool permission decisions
hidden context storage in artifacts
secret storage in artifacts
```

The point is not to make more of the project agentic. The point is to use agents only where they provide clear leverage, while keeping deterministic project infrastructure in charge.

## Completion Criteria

A V3.x feature aligned with this boundary should have:

```text
bounded input packet
structured output schema
forbidden authority validation
unsafe text rejection or redaction
fallback reason code
default_result_changed=false unless explicitly approved
artifact or readiness evidence
manual/operator boundary when live runtime is involved
clear reuse path for loop or harness engineering
```

If a feature cannot meet these criteria, it should not be promoted as V3.x infrastructure.
