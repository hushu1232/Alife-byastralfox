# DataAgent V4.2 Operator Evidence Packet Design

**Date:** 2026-07-12
**Source baseline:** V4.1

## Goal

Turn every V4 manual LangGraph shadow outcome into one bounded, safe, machine-comparable operator evidence packet. The packet must explain whether the advisory was accepted, rejected, or fell back, without changing the deterministic DataAgent result or granting LangGraph execution authority.

## Inputs and ownership

The V4.2 builder consumes existing authoritative objects:

- V4.0 `DataAgentRealLangGraphManualShadowResult` for contract and replay-diff acceptance;
- V4.1 `DataAgentRealLangGraphManualShadowContextEnvelope` for context-budget acceptance;
- an allowlisted advisory kind;
- a bounded safe operator summary;
- safe logical evidence references.

C# constructs and validates the packet. LangGraph may propose summary text and evidence references, but it cannot set acceptance, fallback, authority, or default-result fields.

## Packet model

Create `DataAgentV42OperatorEvidencePacket` with:

```text
contract_version=v4.2
source_baseline=v4.1
status=accepted|rejected|fallback
advisory_kind=diagnostic_summary|planner_note|diff_reason|next_step
context_budget_passed=true|false
contract_validation_passed=true|false
replay_diff_gate_passed=true|false
fallback_required=true|false
operator_required=true|false
default_result_changed=false
agent_advisory_only=true
csharp_validation_authority=true
reason_codes=[safe machine tokens]
evidence_refs=[safe logical references]
safe_summary=<bounded text>
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

Status is derived, not supplied. `accepted` requires both the V4.0 result and V4.1 envelope to be accepted with no fallback. A rejected context budget or unsafe/invalid packet input produces `rejected`. Runtime unavailable, timeout, transport failure, circuit-open, or another deterministic fallback condition produces `fallback`.

## Safety rules

- Summary length is at most 320 characters.
- At most eight evidence references are accepted; each is a logical token, never an absolute path.
- Advisory kind is one of four fixed values.
- Reason codes come from validated source results and are deduplicated.
- SQL-like text, hidden context, Token/Bearer/API-key material, connection strings, absolute paths, control characters, and visible-response payloads reject the packet rather than being stored.
- Rejection returns stable reason codes and an empty summary/reference collection.
- Formatters never emit source replay identifiers, raw context layers, prompt text, raw advisory responses, stack traces, or local paths.

## Persistence

Create a V4.2 artifact writer that writes only the formatted safe packet to an operator-provided directory. The body may contain the bounded safe summary and logical evidence references because V4.2 validates both before writing. The returned file path is an in-process result only and is never included in the artifact body or safe formatter.

## Readiness and integration

Add one dynamic and one static readiness check named `GraphHandshakeV42OperatorEvidencePacketPresent`. The check proves packet status derivation, budget enforcement, unsafe-input rejection, authority markers, formatter safety, and artifact safety. It does not call or require a live LangGraph runtime.

The V4.0/V4.1 types remain unchanged. V4.2 composes them so V4.3 quality scoring and V4.4 production shadow observation can reuse a stable packet contract.

## Tests

Default NUnit tests cover:

1. accepted result plus accepted budget creates an accepted packet;
2. contract rejection creates a rejected packet;
3. unavailable/timeout-style result creates a fallback packet;
4. rejected context budget prevents acceptance;
5. unsafe summary, evidence reference, advisory kind, reason code, or absolute path is rejected;
6. formatter and artifact contain only allowlisted fields and no unsafe source data;
7. null and over-budget inputs fail closed;
8. default result and all execution/persistence authorities remain false.

## Non-goals

V4.2 does not call LangGraph from C#, start Python, install dependencies, supervise a runtime, execute SQL or tools, modify checkpoints, affect QChat output, change the default DataAgent result, add production enablement, or claim V4.5 production closure.
