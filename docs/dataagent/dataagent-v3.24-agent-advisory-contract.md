# DataAgent V3.24 Agent Advisory Contract

agent_advisory_contract=true
contract_version=v3.24
token_budget_context_layers=true
evidence_first_response=true
agent_advisory_only=true
harness_execution_authority=true
csharp_validation_authority=true
langgraph_provider_only=true
starts_runtime=false
installs_dependencies=false
default_result_changed=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false

V3.24 freezes the bounded advisory contract used before real LangGraph manual shadow integration. The contract is intentionally provider-neutral: LangGraph can produce advisory responses later, but the contract belongs to the C# and harness boundary.

## Request Packet

The request packet is the token-saving boundary. It carries only selected, redacted, bounded context:

```text
contract_version
run_id
task
current_state
allowed_advisory_actions
forbidden_authorities
last_successful_step
failure_category
evidence_refs
artifact_index_token
expected_response_schema
```

It does not include full logs, full hidden context, full SQL rows, full replay reports, full tool manifests, or full conversation history.

## Response Packet

The response packet is advisory-only:

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

C# validates the response before it can be recorded or shown. Any request for execution, state write, visible text, readiness override, SQL authority, or default-result mutation is rejected.

## Boundary

Agent suggests. Harness executes. C# validates. Artifact records. Readiness gates. Operator decides.

This version does not start LangGraph, does not install dependencies, does not call a sidecar, does not execute SQL, does not store secrets, does not store hidden context, and does not change the default DataAgent result.
