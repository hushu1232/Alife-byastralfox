# DataAgent V2.1 Orchestrator Design

## Goal

DataAgent V2.1 introduces a native C# orchestration layer for governed NL2SQL analysis. The goal is to turn the existing one-shot query and analysis-session capabilities into an explicit, inspectable workflow with node boundaries, state transitions, checkpoints, and readiness evidence.

V2.1 is not a LangGraph release. It prepares the project for later LangGraph or multi-agent linked execution by defining stable orchestration concepts inside the current C# system first.

## Current Baseline

DataAgent already has the major pieces needed for an orchestration layer:

- `DataAgentService` owns planner invocation, query-plan validation, SQL compilation, SQL safety validation, query execution through `IDataAgentStore`, result explanation, context generation, and query audit.
- `IDataAgentStore` separates DataAgent from SQLite/PostgreSQL persistence details.
- `SqliteDataAgentStore` preserves the local default behavior.
- `PostgresDataAgentStore` provides the V2 PostgreSQL provider path behind the same contract.
- `IDataAgentQueryPlanner` allows deterministic and LLM planner strategies behind one interface.
- `DataAgentAnalysisService` owns multi-turn analysis sessions and session statuses.
- `DataAgentAnalysisSessionStatus` already models `Active`, `AwaitingClarification`, `ReadyToSummarize`, `Summarized`, `Ended`, and `Rejected`.
- `DataAgentCapabilityProvider` and Tool Broker wiring expose DataAgent query and analysis tools through governed routes.
- `tools/check-dataagent-readiness.ps1` and `tools/check-qchat-engineering-map.ps1` verify DataAgent, Tool Broker, store-boundary, analysis-session, loop, prompt, and harness markers.

The weakness is that the end-to-end analysis path is still mostly implicit. A caller can observe a final `DataAgentAnswer` or `DataAgentAnalysisResponse`, but there is no first-class orchestration result that says which nodes ran, which node queried the store, why a branch ended, what checkpoint was produced, or why a terminal action did not execute SQL.

## Non-Goals

V2.1 does not introduce LangGraph, a Python sidecar, a distributed supervisor, streaming front-end progress, chart rendering, report publishing, external data ingestion, or new business datasets.

V2.1 does not replace `DataAgentService` or `DataAgentAnalysisService`. It coordinates them and makes their workflow visible.

V2.1 does not require live PostgreSQL for the default test suite. PostgreSQL remains opt-in and environment-gated.

V2.1 does not relax Tool Broker, SQL safety, planner validation, prompt-leak prevention, or analysis-session terminal-node constraints.

V2.1 does not change QChat runtime behavior. QChat can later consume orchestration context, but this release should keep the implementation inside DataAgent.

## Design Decision

Use a conservative native orchestrator:

```text
DataAgentAnalysisOrchestrator
  -> route gate
  -> schema context
  -> planner/query path through DataAgentService
  -> analysis session path through DataAgentAnalysisService
  -> explicit steps
  -> checkpoint/result
```

The orchestrator should not duplicate SQL compiler or store-provider logic. It should record and enforce workflow boundaries around existing services.

The first implementation should be deterministic and test-first. It should not depend on a live LLM or live PostgreSQL.

## Core Types

V2.1 should add a small set of orchestration models:

```csharp
public enum DataAgentOrchestrationNodeKind
{
    RouteGate,
    SchemaContext,
    Plan,
    Validate,
    Execute,
    Explain,
    Clarification,
    Summarize,
    End,
    Reject,
    Checkpoint
}
```

```csharp
public enum DataAgentOrchestrationStepStatus
{
    Succeeded,
    Skipped,
    Rejected,
    Failed
}
```

```csharp
public sealed record DataAgentOrchestrationStep(
    DataAgentOrchestrationNodeKind Node,
    DataAgentOrchestrationStepStatus Status,
    string Reason,
    bool ExecutedSql);
```

```csharp
public sealed record DataAgentOrchestrationCheckpoint(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    string LastDataset,
    int TurnCount,
    bool CanContinue,
    bool CanSummarize,
    bool Terminal);
```

```csharp
public sealed record DataAgentOrchestrationRequest(
    string CallerId,
    string Input,
    string? SessionId,
    bool RouteAllowsQuery);
```

```csharp
public sealed record DataAgentOrchestrationResult(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    IReadOnlyList<DataAgentOrchestrationStep> Steps,
    DataAgentOrchestrationCheckpoint Checkpoint,
    DataAgentAnalysisResponse Response);
```

These types are intentionally small. They make node execution visible without exposing internal prompt text, connection strings, or raw Tool Broker state.

## Orchestrator Contract

The main contract should be:

```csharp
public interface IDataAgentAnalysisOrchestrator
{
    DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request);
}
```

`Start` creates a new analysis session and runs one query-producing turn when route policy allows it.

`Continue` uses the supplied `SessionId` and delegates follow-up intent handling to `DataAgentAnalysisService`. It must preserve the existing behavior where summarize and end intents are terminal actions that do not execute SQL.

The request includes `RouteAllowsQuery` so the first V2.1 implementation can be tested without booting QChat or requiring a live Tool Broker route. Later versions can replace the boolean with a richer route decision object.

## Node Responsibilities

### RouteGate

The route gate decides whether a query-producing turn is allowed. If `RouteAllowsQuery` is false, the orchestrator must fail closed before calling `DataAgentService.Answer` or `DataAgentAnalysisService.Start`.

Terminal actions such as summarize and end should not require query permission because they do not execute SQL. They should still require a valid session.

### SchemaContext

The schema-context node records that the orchestrator is operating against the DataAgent catalog and store boundary. V2.1 does not need to expose a full schema snapshot in the result. It only needs enough evidence for tests and readiness to show that planning is schema-governed rather than prompt-only.

### Plan

The plan node represents planner use through `DataAgentService`. The orchestrator should not call planner implementations directly in V2.1. This preserves the existing validation pipeline.

### Validate

The validate node represents query-plan validation and SQL safety validation. In V2.1 this node is inferred from `DataAgentAnswer.Validated` and `RejectedReason`, because `DataAgentService` already owns the detailed validation logic.

### Execute

The execute node is the only query path that may set `ExecutedSql` to true. It is only successful when `DataAgentAnswer.Validated` is true and the analysis response accepted the query-producing turn.

### Explain

The explain node records result explanation and context generation. It does not query the store. It exists so callers can show that the output is not raw rows alone.

### Clarification

The clarification node is terminal for a turn but not terminal for the whole session. It should set `ExecutedSql` to false and move the checkpoint into an awaiting-clarification shape.

### Summarize

The summarize node must not call `DataAgentService.Answer` and must not execute SQL. It summarizes existing turns through `DataAgentAnalysisService`.

### End

The end node must not call `DataAgentService.Answer` and must not execute SQL. It closes the session and produces a terminal checkpoint.

### Reject

The reject node captures fail-closed behavior, invalid sessions, ended sessions, unsafe planner output, route denial, or other non-query failures. Rejected results should still produce a checkpoint when a session exists.

### Checkpoint

The checkpoint node creates a compact resumable state summary. The checkpoint is not a durable store in V2.1. It is a structured result object derived from `IDataAgentAnalysisSessionStore`.

## State Model

The orchestrator should preserve the existing analysis statuses:

```text
Active
AwaitingClarification
ReadyToSummarize
Summarized
Ended
Rejected
```

V2.1 should not invent a separate session state machine. Instead, it should make each transition observable through orchestration steps and checkpoints.

Expected transitions:

- new query accepted: `Active` or `ReadyToSummarize`
- planner asks clarification: `AwaitingClarification`
- invalid planner output or unsafe SQL: `Rejected`
- summarize intent: `Summarized`
- end intent: `Ended`
- route denied before a session exists: `Rejected` result with no SQL execution
- route denied for query-producing continue: no SQL execution and a rejected turn/result

## Data Flow

Accepted start flow:

```text
DataAgentOrchestrationRequest
 -> RouteGate
 -> SchemaContext
 -> DataAgentAnalysisService.Start
 -> DataAgentService.Answer
 -> Plan
 -> Validate
 -> Execute
 -> Explain
 -> Checkpoint
 -> DataAgentOrchestrationResult
```

Clarification flow:

```text
request
 -> RouteGate
 -> SchemaContext
 -> DataAgentAnalysisService.Start or Continue
 -> DataAgentService.Answer
 -> Plan
 -> Validate
 -> Clarification
 -> Checkpoint
```

Summary flow:

```text
continue request with summarize intent
 -> RouteGate
 -> DataAgentAnalysisService.Continue
 -> Summarize
 -> Checkpoint
```

End flow:

```text
continue request with end intent
 -> RouteGate
 -> DataAgentAnalysisService.Continue
 -> End
 -> Checkpoint
```

Denied query flow:

```text
request where RouteAllowsQuery is false
 -> RouteGate rejected
 -> Reject
 -> Checkpoint if session exists
```

## Safety Invariants

V2.1 must preserve these invariants:

- query-producing nodes fail closed when route policy denies DataAgent query use;
- summarize and end nodes do not execute SQL;
- clarification nodes do not execute SQL;
- the orchestrator never sends raw SQL directly to a store provider;
- the orchestrator never bypasses `DataAgentService` validation;
- `IDataAgentStore` remains the only persistence/query boundary;
- checkpoints do not include connection strings, API keys, Authorization headers, Bearer tokens, or raw Tool Broker manuals;
- invalid session IDs do not create hidden fallback sessions;
- ended sessions cannot be resumed into query execution.

## Error Handling

V2.1 should prefer structured rejected results over thrown exceptions for normal workflow denials:

- route denied;
- missing session;
- ended session;
- summarization without enough turns;
- planner clarification;
- unsafe or invalid query plan.

Constructor errors should still throw for invalid dependencies:

- null `DataAgentAnalysisService`;
- null session store if the orchestrator needs one directly;
- null clock or other injected dependency only when no default exists.

## Testing Strategy

V2.1 should be implemented with TDD. The first tests should cover observable behavior instead of implementation details:

- starts an accepted analysis and records route, schema, plan, validate, execute, explain, and checkpoint steps;
- route denial before start does not execute SQL;
- route denial for a query-producing continue does not execute SQL;
- planner clarification produces a clarification step and no execute step;
- summarize intent produces a summarize step and no execute step;
- end intent produces an end step and a terminal checkpoint;
- unsafe planner output produces reject and no execute step;
- checkpoint contains session id, status, turn count, last dataset, and continuation flags;
- readiness script marks the orchestrator as required.

Tests should use fake planners and the existing SQLite fixture path helpers. No live PostgreSQL or live LLM should be required.

Required verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

## Readiness Gates

V2.1 should add required readiness markers for:

- `DataAgentOrchestratorPresent`
- `OrchestratorNodeBoundaryPresent`
- `OrchestratorCheckpointPresent`
- `OrchestratorRouteGateFailClosed`
- `OrchestratorTerminalNodesDoNotQuery`
- `OrchestratorStateMachineTransitions`

These gates should live in `tools/check-dataagent-readiness.ps1`. The QChat engineering map should not duplicate detailed orchestrator checks, but it may keep relying on DataAgent readiness as the DataAgent harness authority.

## Interview Value

V2.1 lets the project be described as more than NL2SQL:

```text
I built a governed DataAgent orchestration layer for natural-language analytics.
The system does not let an LLM directly touch the database. It splits the flow
into route gating, schema context, planning, validation, read-only execution,
explanation, and checkpointed session state. Terminal actions such as summarize
and end cannot query the database, and every important boundary is covered by
readiness harness checks.
```

This makes the project easier to position for roles that ask for NL2SQL, agentic workflow design, data governance, prompt engineering, and test harness engineering.

## Rollout

1. Add orchestration model tests.
2. Add orchestration model types.
3. Add the orchestrator contract and implementation.
4. Route accepted start and continue flows through `DataAgentAnalysisService`.
5. Add route-denial handling and checkpoint generation.
6. Add terminal-node tests for summarize and end.
7. Add clarification and rejected-flow tests.
8. Add readiness markers and tests.
9. Run DataAgent tests, readiness scripts, and full solution tests.

## Acceptance Criteria

V2.1 is ready to merge when:

- `IDataAgentAnalysisOrchestrator` exists;
- `DataAgentAnalysisOrchestrator` produces ordered node steps;
- accepted query-producing analysis includes an execute step;
- route-denied query-producing analysis has no execute step;
- clarification, summarize, end, and reject branches do not execute SQL;
- checkpoints include session status and continuation flags;
- readiness reports `0 required missing`;
- QChat engineering map reports `0 required missing`;
- full solution tests pass under the local .NET 9 SDK;
- no LangGraph, Python sidecar, live PostgreSQL requirement, or QChat runtime behavior change is introduced.

## Future Work

V2.2 can introduce a richer route decision object instead of the initial `RouteAllowsQuery` boolean.

V2.3 can persist analysis sessions and checkpoints behind the DataAgent store boundary.

V2.5 can add progress streaming and front-end analysis timeline views.

V3 can map the stable C# orchestration nodes into LangGraph or another multi-agent runtime without rewriting DataAgent safety or store boundaries.
