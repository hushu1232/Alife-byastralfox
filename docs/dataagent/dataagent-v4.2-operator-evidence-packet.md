# DataAgent V4.2 Operator Evidence Packet

```text
operator_evidence_packet=v4.2
source_baseline=v4.1
statuses=accepted,rejected,fallback
max_summary_chars=320
max_evidence_refs=8
manual_shadow_only=true
agent_advisory_only=true
csharp_validation_authority=true
default_result_changed=false
fallback_required=true
starts_runtime=false
installs_dependencies=false
calls_sidecar=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

V4.2 composes the V4.0 validated manual-shadow result and V4.1 bounded context envelope into one operator evidence packet. C# derives acceptance and fallback; LangGraph cannot set authority fields or change the deterministic DataAgent result.

Accepted packets may retain a bounded safe summary and up to eight logical evidence references. Rejected and fallback packets retain stable reason codes but no advisory summary or references. SQL, secrets, hidden context, absolute paths, raw prompts, raw sidecar responses, replay identifiers, tool payloads, checkpoint state, and visible QChat text are never written.

V4.2 remains manual shadow only. It does not start or call LangGraph from the default C# path, install dependencies, execute SQL or tools, mutate state, publish visible text, or claim production closure.
