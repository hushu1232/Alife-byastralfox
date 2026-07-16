# DataAgent LangGraph Shadow Artifact Store Design

## Goal

Add an operator-started, loopback-only LangGraph shadow runtime that may analyze
bounded DataAgent context and persist sanitized analysis artifacts in the existing
DataAgent SQLite database. C# remains the sole authority for route selection,
permissions, SQL safety, query execution, audit decisions, checkpoint state, and
QQ-visible output.

## Scope

The feature records every completed LangGraph shadow observation after C# has
evaluated it:

- accepted advisory and replay-diff observations;
- C# gate rejections;
- protocol or contract rejections;
- timeout/unavailable outcomes; and
- deterministic fallback outcomes.

The stored record contains only bounded, sanitized metadata. It does not store
raw SQL, secrets, bearer tokens, connection strings, passwords, full private
context, executable tool instructions, or raw untrusted sidecar payloads.

## Non-Goals

- LangGraph does not receive an SQLite connection or any DataAgent write
  interface.
- LangGraph does not authorize data, fields, operators, limits, or tools.
- LangGraph does not generate executable SQL, execute SQL, write C# audit
  records, decide checkpoints, or send QQ text.
- Alife does not start a runtime, install LangGraph dependencies, or call a
  sidecar automatically.
- The default deterministic DataAgent result never changes because of a shadow
  result.

## Architecture

Introduce an independent SQLite artifact table and C# repository boundary,
rather than extending query_audit or tool_broker_audit. Query-execution audit
and tool-route audit remain semantically separate from external shadow analysis.

~~~text
operator-started loopback LangGraph
  -> bounded advisory payload
  -> C# protocol and contract validation
  -> C# safety and replay-diff gate
  -> C# outcome classification
  -> C# SQLite artifact repository
  -> owner-only aggregate diagnostics
~~~

The C# classification uses one of these bounded outcomes:

- accepted
- gate_rejected
- protocol_rejected
- timeout
- fallback

A gate_rejected artifact means C# could read the candidate result but refused it
for a policy, authority, runtime-boundary, or replay-diff reason. A
protocol_rejected artifact means the candidate cannot enter the gate because it
violates the expected schema, version, field constraints, safe-token rules, or
forbidden-content rules.

## Stored Record

Each artifact contains:

- non-sensitive artifact_id;
- bounded session_id and replay_id;
- outcome and safe reason_code;
- optional bounded, sanitized summary, present only when C# has validated its
  source;
- context character count;
- whether the replay-diff gate passed;
- whether deterministic fallback was required;
- created_at and expires_at.

The table may contain no raw payload column. Any candidate text that is unsafe,
unvalidated, or missing is represented by an empty summary and a safe reason
code only.

## Retention and Cleanup

Retention is enforced by C# during a write transaction:

1. Remove artifacts whose expires_at is on or before the current UTC time.
2. For the incoming artifact's session/replay scope, retain only the newest
   twenty records and delete older records.
3. Set every new artifact's expires_at to ninety days after created_at.

The cleanup uses deterministic ordering by creation time and artifact id. A
database write or cleanup failure is isolated: it emits only a bounded local
diagnostic and cannot change the C# gate result, query result, or fallback.

## Owner Diagnostics

Add an owner/private-only command:

~~~text
/dataagent diag langgraph
~~~

It returns a sanitized aggregate report for the retained window:

- total artifact count;
- counts by outcome;
- latest safe reason code;
- oldest and newest retained timestamps; and
- active retention policy: ninety days and twenty artifacts per session/replay.

It never returns individual raw artifacts, questions, SQL, sidecar text, local
paths, credentials, or hidden context. Non-owner callers follow the existing
silent/drop diagnostic policy and receive no DataAgent state.

## Real Runtime Boundary

The first runtime integration remains manual and observational:

- an operator starts the real LangGraph process;
- communication is loopback-only;
- the runtime receives only the existing bounded context envelope;
- Alife reports calls_sidecar=false for normal/default paths;
- the manual smoke path may collect an advisory artifact but has no authority to
  alter the deterministic result;
- a timeout, transport failure, malformed response, or rejected advisory writes
  a sanitized observation only if C# can construct safe metadata.

## Tests

Tests must prove:

1. Every outcome type can be stored and queried as bounded data.
2. Raw SQL, secrets, connection strings, bearer tokens, passwords, hidden
   context, and unsafe sidecar text never reach the artifact table.
3. Expired records are removed and a session/replay retains only its newest
   twenty records.
4. Artifact-store write failure leaves the C# decision and fallback unchanged.
5. The owner diagnostic returns only aggregate safe fields.
6. Non-owner diagnostics do not reveal LangGraph artifact state.
7. A manually started loopback shadow result is stored only after C# validation;
   it cannot grant tool, SQL, audit, checkpoint, or QQ authority.

## Acceptance Criteria

- LangGraph can support bounded data analysis and sanitized SQLite persistence.
- C# remains the only component that classifies and writes artifacts.
- The feature is not an automatic LangGraph production takeover.
- Artifact growth is bounded by the agreed ninety-day and twenty-per-scope
  retention policy.
- Owners can inspect safe aggregate health without exposing private data.
