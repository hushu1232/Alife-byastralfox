# DataAgent V2.17 Diagnostics Command Contract

V2.17 is a QChat ingress hardening release for DataAgent owner diagnostics.

It centralizes the supported DataAgent diagnostics command vocabulary in one QChat-local parser:

```text
/dataagent diag evidence
/dataagent diagnostics evidence
/dataagent diag trace
/dataagent diagnostics trace
/dataagent diag progress
/dataagent diagnostics progress
/dataagent diag graph
/dataagent diagnostics graph
```

QChat diagnostics aliases remain supported:

```text
/qchat diag dataagent evidence
/qchat diagnostics dataagent evidence
/qchat diag dataagent trace
/qchat diagnostics dataagent trace
/qchat diag dataagent progress
/qchat diagnostics dataagent progress
/qchat diag dataagent graph
/qchat diagnostics dataagent graph
```

The command contract is intentionally string-only. It returns QChat-local diagnostics topics and does not reference DataAgent graph model types.

## Safety Boundary

- QChat remains a string-only consumer of DataAgent diagnostics.
- DataAgent remains the owner of QueryPlan, SQL validation, SQL compilation, SQL safety, read-only execution, checkpoint persistence, evidence, trace, progress, and DataQueryGraph dry-run projection.
- Non-owner diagnostics commands are dropped before model dispatch and do not receive a visible denial reply.
- Unknown `/dataagent` commands fail closed and are not treated as owner diagnostics commands.
- QChat must not import `DataAgentDataQueryGraph*` types.

## V3 Handoff

V2.17 closes the V2 line unless verification exposes a real boundary gap.

V3.0 can start after V2.17 is implemented and verified. The first V3 milestone should connect any LangGraph sidecar behind the existing C# contract, keep SQL authority in C#, and expose only scoped node manifests to reduce attention dilution and random tool choice.
