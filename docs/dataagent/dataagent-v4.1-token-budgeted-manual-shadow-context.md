# DataAgent V4.1 Token-Budgeted Manual Shadow Context

```text
manual_shadow_context_budget=true
source_baseline=v4.0
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
calls_sidecar=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
max_envelope_chars=1200
max_layer_chars=400
required_layer_count=3
```

V4.1 improves the V4.0 manual shadow lane by making context small, layered, and auditable. It does not make LangGraph autonomous. LangGraph remains advisory, the operator starts the runtime manually, and C# plus the manual harness remain authority.

## Context Layers

The manual shadow request uses three bounded layers:

- `layer_1_route`: fixture, route, and node status.
- `layer_2_evidence`: compact reason code and evidence reference.
- `layer_3_excerpt`: bounded failure excerpt only when needed.

The context budget rejects unsafe text instead of redacting and continuing. It does not include SQL text, hidden context, secrets, bearer tokens, connection strings, absolute paths, checkpoint mutation payloads, or visible QChat response text.

## Why This Helps

V4.1 helps the loop and harness engineering by reducing prompt size and making each manual shadow request inspectable. The expected benefit is better operator-facing diagnostic summaries without sending raw logs to the agent.

## Boundary

```text
LangGraph may describe, classify, and summarize bounded evidence.
LangGraph may not execute, authorize, persist, route, or publish.
```

The optional manual harness JSON artifact remains marker/status only and does not store replay identifiers, source baseline strings, hidden context, or local paths.
