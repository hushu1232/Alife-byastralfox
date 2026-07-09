# DataAgent V3.25 Real LangGraph Manual Shadow Provider

real_langgraph_manual_shadow_provider=true
langgraph_provider_only=true
manual_shadow_only=true
agent_advisory_contract=v3.24
starts_runtime=false
installs_dependencies=false
calls_sidecar=false
default_result_changed=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false

V3.25 introduces the first real LangGraph provider boundary, but only as a manual shadow advisory provider. Alife does not start LangGraph, install dependencies, call a sidecar, execute SQL, write state, publish visible text, or change the default DataAgent result.

## Flow

An operator may run or capture LangGraph outside Alife. The captured advisory payload is then passed into the C# manual shadow provider:

```text
operator captures LangGraph advisory
C# receives bounded advisory payload
C# validates with V3.24 Agent Advisory Contract
C# records accepted or rejected shadow result
fallback remains available
default result remains unchanged
```

## Boundary

LangGraph is a provider only. It can suggest, explain, or propose manual checks through the V3.24 contract. It cannot start runtime, call tools, execute SQL, mutate state, write artifacts directly, decide readiness, or send visible text.

Rejected LangGraph payloads require fallback and are classified by reason code. Accepted payloads remain advisory-only and manual-shadow-only.
