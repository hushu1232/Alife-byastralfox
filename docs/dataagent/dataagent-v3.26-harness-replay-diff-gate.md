# DataAgent V3.26 Harness Replay Diff Gate

V3.26 adds a C# gate between the manual LangGraph shadow advisory provider and the existing harness replay evidence.

The gate is deliberately narrow:

```text
harness_replay_diff_gate=true
agent_advisory_contract=v3.24
real_langgraph_manual_shadow_provider=true
harness_execution_authority=true
csharp_validation_authority=true
agent_advisory_only=true
gate_only=true
operator_decides=true
default_result_changed=false
starts_runtime=false
installs_dependencies=false
calls_sidecar=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

The agent may suggest, but the harness and C# gate decide whether that suggestion matches replay evidence.

## Boundary

V3.26 does not start LangGraph, install dependencies, call a sidecar, execute SQL, write state, store secrets, store hidden context, publish visible text, or change the default answer path.

Accepted manual LangGraph advisory still has to match a C# replay report category. If the advisory is rejected, missing, mismatched, or the replay report says the default result changed, the gate requires fallback and operator review.

## Gate Outcomes

```text
harness_replay_diff_gate_passed
harness_replay_diff_gate_input_missing
harness_replay_default_result_changed
harness_replay_diff_reason_mismatch
```

Rejected advisory reason codes, such as `advisory_forbidden_authority_claimed`, are preserved so the audit trail stays evidence-first.
