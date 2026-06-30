# DataAgent V2.4 Evidence Pack Design

## Goal

DataAgent V2.4 turns the existing route, orchestration, checkpoint, and audit evidence into a stable Evidence Pack that can be used for both local diagnostics and interview demonstration.

V2.3 made Tool Broker route decisions real inside DataAgent orchestration. The system now knows whether a route was present, which tool was allowed, what the orchestrator trace did, and what checkpoint state remains after a turn. That information is valuable, but it is still scattered across runtime context fields, readiness checks, query audit rows, Tool Broker audit rows, and tests.

V2.4 should collect those facts into one compact, sanitized, deterministic report:

```text
natural-language request
-> Tool Broker route evidence
-> orchestrator node trace
-> state checkpoint
-> SQL or no-SQL audit evidence
-> final safety/interview summary
```

The purpose is to make DataAgent easier to explain, inspect, and defend. It should demonstrate that the project is not only an NL2SQL demo, but a governed analysis loop with route gating, state-machine checkpoints, SQL safety, and auditability.

## Current Baseline

The project already has the required raw materials:

- `DataAgentAnalysisToolHandler` routes `dataagent_analysis_start`, `dataagent_analysis_continue`, `dataagent_analysis_summarize`, and `dataagent_analysis_end` through `IDataAgentAnalysisOrchestrator`.
- `DataAgentAnalysisOrchestrator` records node traces such as `RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded` and `RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded`.
- `DataAgentOrchestrationCheckpoint` records `SessionId`, `SessionStatus`, `LastDataset`, `TurnCount`, continuation flags, and terminal status.
- `DataAgentToolRouteContext` records sanitized route facts such as route presence, tool name, allows-tool, allows-query, route id, intent, reason code, and route session id.
- `DataAgentOrchestrationContextProvider` emits route evidence, orchestration trace, and checkpoint fields in the returned context block.
- `IDataAgentStore`, `SqliteDataAgentStore`, and `PostgresDataAgentStore` expose store-boundary audit operations.
- `DataAgentAuditLog` and `DataAgentToolBrokerAuditLog` already persist query audit and Tool Broker audit records.
- `DataAgentReadiness` and `tools/check-dataagent-readiness.ps1` enforce required gates for DataAgent safety and orchestration behavior.

The missing piece is an explicit, human-readable Evidence Pack contract that converts these facts into one stable report.

## Selected Approach

Build a lightweight in-process Evidence Pack layer inside `Alife.Function.DataAgent`.

The Evidence Pack layer should be observational only. It should receive already-produced orchestration and audit facts, then format a deterministic summary. It must not route tools, mutate sessions, execute SQL, call planners, or decide whether a request is allowed.

The selected shape is three small components:

```csharp
public sealed record DataAgentEvidencePack(...);
```

```csharp
public sealed class DataAgentEvidencePackBuilder
{
    public DataAgentEvidencePack Build(
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentAuditRecord>? queryAudit = null,
        IReadOnlyList<DataAgentToolBrokerAuditRecord>? toolBrokerAudit = null);
}
```

```csharp
public static class DataAgentEvidencePackFormatter
{
    public static string Format(DataAgentEvidencePack pack);
}
```

`DataAgentEvidencePackBuilder` should derive evidence from `DataAgentOrchestrationResult` first, then enrich with audit rows when the caller provides them. This makes the pack usable in focused unit tests, readiness checks, and future runtime paths without forcing every call site to query a database.

`DataAgentEvidencePackFormatter` should emit a stable `[data_agent_evidence_pack]` block that is safe to include in logs, readiness output, documentation examples, or QChat context.

## Rejected Alternatives

### Full Diagnostics Subsystem

A larger option is to build a diagnostics service with history queries, filters, export commands, UI routes, and WebBridge integration.

That is useful later, but it is too heavy for V2.4. The project has just finished V2.3 route governance; adding a full diagnostics platform now would increase risk and slow down the path to a crisp, demonstrable milestone.

### Documentation-Only Demo

Another option is to write a Markdown explanation of how route, trace, checkpoint, and audit already work.

This is fast, but too weak. The user needs a real project capability that can be tested and shown, not just a narrative. V2.4 should add a small runtime artifact so the explanation is grounded in code.

### Replace Existing Context Provider

Another option is to replace `DataAgentOrchestrationContextProvider` with the Evidence Pack formatter.

This is not selected. The existing context provider is already part of the V2.2/V2.3 runtime contract. V2.4 should add a parallel evidence report, not break existing consumers.

## Non-Goals

V2.4 does not add a front-end UI, chart rendering, WebBridge page, PDF export, live PostgreSQL dependency, LangGraph runtime, Python sidecar, streaming progress feed, or multi-agent scheduler.

V2.4 does not move session state ownership out of `DataAgentAnalysisService`.

V2.4 does not replace Tool Broker, `XmlFunctionExecutionPolicy`, `ToolCapabilityRouter`, SQL safety validation, query planning, or store-boundary audit logic.

V2.4 does not make Evidence Pack output a trusted prompt instruction. If the report is ever injected into model context, it should be treated as structured diagnostic context, not as a new source of authority.

V2.4 does not use `D:\FOXD` or any upload target other than `git@github.com:hushu1232/Alife-byastralfox.git`.

## Evidence Pack Contract

The first version of `DataAgentEvidencePack` should describe one DataAgent orchestration result.

Recommended fields:

- `SessionId`
- `SessionStatus`
- `TurnCount`
- `RoutePresent`
- `RouteTool`
- `RouteAllowed`
- `RouteAllowsQuery`
- `RouteReasonCode`
- `Trace`
- `ExecutedSql`
- `Terminal`
- `CanContinue`
- `CanSummarize`
- `AuditValidated`
- `AuditDataset`
- `AuditRowCount`
- `AuditRejectedReason`
- `ToolBrokerAuditAllowed`
- `ToolBrokerAuditReasonCode`
- `SafetySummary`
- `InterviewSummary`

The `Trace` field should be derived from `DataAgentOrchestrationResult.Steps` using the same node/status vocabulary already used by `DataAgentOrchestrationContextProvider`.

The `ExecutedSql` field should be true only when at least one orchestration step has `ExecutedSql=true`.

The `Audit*` fields should be empty or false when no audit rows are supplied. Evidence Pack generation must still work without audit input.

The `SafetySummary` should be deterministic and compact. Examples:

```text
route_allowed;read_only_sql_executed;checkpoint_active
```

```text
route_rejected;sql_not_executed;checkpoint_unchanged
```

```text
terminal_no_query;checkpoint_terminal
```

The `InterviewSummary` should be a concise explanation suitable for a project walkthrough. It should not contain raw prompts, connection strings, API keys, bearer tokens, authorization headers, or unsanitized model output.

## Formatted Output

The formatter should emit a deterministic block:

```text
[data_agent_evidence_pack]
session_id=<session-id>
status=<status>
turn_count=<count>
route_present=<true|false>
route_tool=<tool-name-or-empty>
route_allowed=<true|false>
route_allows_query=<true|false>
route_reason_code=<reason-code-or-empty>
trace=<node-status-chain>
executed_sql=<true|false>
terminal=<true|false>
can_continue=<true|false>
can_summarize=<true|false>
audit_validated=<true|false>
audit_dataset=<dataset-or-empty>
audit_row_count=<count>
audit_rejected_reason=<reason-or-empty>
tool_broker_audit_allowed=<true|false>
tool_broker_audit_reason_code=<reason-or-empty>
safety_summary=<compact-summary>
interview_summary=<compact-human-summary>
[/data_agent_evidence_pack]
```

All string values must pass through `DataAgentContextFieldSanitizer`.

The formatter should use lowercase `true` and `false` to match existing DataAgent context conventions.

The field order should be stable so tests, readiness scripts, and interview examples do not churn.

## Runtime Integration

V2.4 should avoid heavy runtime wiring at first.

The builder and formatter should be used in `DataAgentReadiness` to prove the capability with real accepted, route-denied, and terminal orchestration results. That provides a real executable contract without forcing every production response to include a longer report immediately.

The formatter may later be appended to `DataAgentAnalysisToolHandler` responses or exposed through a dedicated diagnostic XML tool, but V2.4 should not change production response size unless the implementation plan explicitly chooses a small opt-in path.

This keeps the feature useful while protecting existing context consumers.

## Safety Invariants

V2.4 must preserve these invariants:

- Evidence Pack generation does not execute SQL.
- Evidence Pack generation does not mutate DataAgent sessions.
- Evidence Pack generation does not decide route permission.
- Evidence Pack generation does not bypass Tool Broker or XML policy.
- Evidence Pack generation works for accepted, rejected, and terminal paths.
- Route-denied evidence must show `executed_sql=false`.
- Terminal summarize/end evidence must show no query execution.
- Missing audit input must not fail the pack builder.
- Output must be sanitized and deterministic.
- Readiness must treat Evidence Pack as a required capability after V2.4 lands.

## Testing Strategy

Use test-first implementation.

Required focused tests:

1. Accepted query evidence.
   - Build an accepted `DataAgentOrchestrationResult` with `RouteGate`, `Execute`, and `Checkpoint` steps.
   - Include a route context and an accepted audit row.
   - Assert the pack records route allowed, executed SQL, active checkpoint, dataset, row count, and a useful safety summary.

2. Route-denied evidence.
   - Build a rejected result with `RouteGate:Rejected`, `Reject:Rejected`, and `Checkpoint:Succeeded`.
   - Use missing or denied route context.
   - Assert `ExecutedSql=false`, route reason is preserved, and the safety summary says the route was rejected.

3. Terminal no-query evidence.
   - Build a summarize or end result with terminal steps and route context.
   - Assert `ExecutedSql=false`, terminal/checkpoint flags are correct, and the safety summary says terminal no-query.

4. Formatter stability and sanitization.
   - Build a pack containing unsafe newlines, delimiters, or context wrapper text in string fields.
   - Assert the formatted block keeps stable field order and removes unsafe content through `DataAgentContextFieldSanitizer`.

5. Readiness coverage.
   - Add a required readiness check named `DataAgentEvidencePackPresent`.
   - Assert `DataAgentReadinessTests` expects it.
   - Assert `tools/check-dataagent-readiness.ps1` has markers for the builder, formatter, stable block, and runtime readiness example.

## Readiness Gates

Add this required gate:

- `DataAgentEvidencePackPresent`

The gate should pass only when all of these are true:

- `DataAgentEvidencePack` exists.
- `DataAgentEvidencePackBuilder` exists.
- `DataAgentEvidencePackFormatter` emits `[data_agent_evidence_pack]`.
- `DataAgentReadiness` builds a pack from an accepted path.
- `DataAgentReadiness` builds a pack from a route-denied or terminal no-query path.
- readiness output includes the new check as required.

The current required count in `tools/check-dataagent-readiness.ps1` should increase by one when this gate is added.

## Expected File Changes

Expected implementation files:

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePack.cs`
  - define the immutable evidence model.

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs`
  - derive pack fields from orchestration result and optional audit inputs.

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs`
  - format a sanitized stable evidence block.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - build accepted, route-denied, and terminal evidence pack examples.
  - add `DataAgentEvidencePackPresent`.

- Modify `tools/check-dataagent-readiness.ps1`
  - add a required marker gate.
  - increment the expected required check count.

Expected test files:

- Create or modify `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs`
  - focused builder and formatter tests.

- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - assert the new readiness check is present.
  - assert the PowerShell script summary reflects the new required count.

No changes should be required in QChat runtime code for V2.4.

## Acceptance Criteria

V2.4 is complete when:

- `DataAgentEvidencePack` models one orchestration result in a stable, sanitized structure.
- `DataAgentEvidencePackBuilder` can build accepted, rejected, and terminal evidence without executing SQL or mutating sessions.
- `DataAgentEvidencePackFormatter` emits a deterministic `[data_agent_evidence_pack]` block.
- accepted query evidence includes route, trace, checkpoint, SQL execution, and audit facts when available.
- route-denied evidence includes rejected route reason and `executed_sql=false`.
- terminal evidence includes no-query status and checkpoint flags.
- readiness includes `DataAgentEvidencePackPresent` as a required gate.
- DataAgent focused tests pass.
- DataAgent readiness passes.
- QChat engineering map remains passing.
- full solution tests pass under the local .NET 9 SDK.
- no LangGraph, UI, live PostgreSQL requirement, or state-machine migration is introduced.

Required verification commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
git diff --check
```

## Interview Framing

V2.4 should be explained as the point where DataAgent becomes demonstrably inspectable, not just functional.

Strong interview framing:

```text
I added an Evidence Pack layer to my DataAgent so every NL2SQL analysis can be explained through a concrete chain: Tool Broker route decision, orchestrator node trace, state checkpoint, SQL/no-SQL execution evidence, and audit result. This made the system easier to debug and easier to present in interviews because safety, state, and execution are no longer hidden inside logs or tests. The Evidence Pack is observational only, so it does not weaken the Tool Broker, SQL safety layer, or DataAgent state machine.
```

This demonstrates Harness Engineering through readiness gates and audit checks, Loop Engineering through trace/checkpoint state progression, and Prompt Engineering through sanitized context boundaries.

## Future Work

V2.5 can expose Evidence Pack output through a dedicated diagnostic XML tool or append it to selected owner-only DataAgent responses.

V2.6 can add a local Markdown export command for interview demos and debugging transcripts.

V3 can map Evidence Pack fields into a LangGraph or multi-agent dashboard once the C# route, checkpoint, and audit contracts are stable.
