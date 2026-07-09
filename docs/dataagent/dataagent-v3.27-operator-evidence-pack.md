# DataAgent V3.27 Operator Evidence Pack

V3.27 adds a pure, in-memory operator evidence pack that aggregates the manual DataAgent evidence chain from V3.18 through V3.26. It is a review artifact for the operator, not a runtime integration point.

Boundary:

- Agent suggests.
- Harness executes.
- C# validates.
- Artifact records.
- Readiness gates.
- Operator decides.

Markers:

```text
operator_evidence_pack=true
source_versions=v3.18-v3.26
manual_audit_bundle=true
agent_advisory_contract=v3.24
real_langgraph_manual_shadow_provider=true
harness_replay_diff_gate=true
operator_decides=true
agent_advisory_only=true
harness_execution_authority=true
csharp_validation_authority=true
default_result_changed=false
manual_only=true
starts_runtime=false
installs_dependencies=false
calls_sidecar=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

The pack may summarize replay counts, artifact file names, advisory acceptance, diff-gate status, fallback requirements, and operator requirements. It must not include absolute local paths, SQL text, secrets, bearer tokens, or hidden context.

Non-goals:

- Do not start LangGraph.
- Do not install dependencies.
- Do not call sidecar or network.
- Do not execute agent suggestions.
- Do not mutate state or change the default result.
- Do not persist SQL, secrets, or hidden context.
