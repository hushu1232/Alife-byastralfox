# DataAgent V2.13 PostgreSQL Checkpoint Hardening

## Purpose

V2.13 productizes optional PostgreSQL persistence for DataAgent analysis checkpoints. The goal is to let long multi-turn DataAgent analysis sessions recover across process restarts without changing QueryPlan authority, SQL safety, Tool Broker routing, QChat ownership, or the V2.12 scenario-context prompt boundary.

## What PostgreSQL Persists

- `DataAgentAnalysisSession`
- `DataAgentAnalysisTurn`
- Session status
- Last dataset
- Last summary
- Pending clarification question
- Turn history used to derive `DataAgentOrchestrationCheckpoint`

The persisted state is the recovery source for `checkpoint_session_id`, `checkpoint_status`, `checkpoint_turn_count`, `checkpoint_can_continue`, `checkpoint_can_summarize`, and `checkpoint_terminal`.

## What PostgreSQL Does Not Change

- SQL is still generated only through QueryPlan compilation.
- SQL safety validation remains deterministic C# code.
- Query execution remains read-only and parameterized.
- Scenario context remains a hint only.
- Tool Broker route state remains required for DataAgent tools.
- QChat does not load or own DataAgent checkpoint providers.
- Progress diagnostics remain a runtime/owner observation stream, not the checkpoint authority.

## Runtime Configuration

Default checkpoint/session runtime remains in-memory:

```text
ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER=
```

Enable PostgreSQL checkpoint persistence:

```text
ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER=postgres
ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION=<connection string>
```

If `ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION` is not set, the factory falls back to:

```text
ALIFE_DATAAGENT_POSTGRES_CONNECTION=<connection string>
```

## Recovery Flow

1. `DataAgentModuleService.AwakeAsync(...)` creates the query/audit store through `DataAgentStoreFactory`.
2. It creates the analysis checkpoint/session store through `DataAgentAnalysisSessionStoreFactory`.
3. `DataAgentAnalysisService` and `DataAgentAnalysisOrchestrator` use the configured `IDataAgentAnalysisSessionStore`.
4. Accepted start/continue/summarize/end operations update the configured session store; route-denied or missing-session responses remain orchestration rejections rather than a new SQL authority or graph checkpoint.
5. `DataAgentOrchestrationContextProvider` emits checkpoint fields from the recovered session state.

## Non-Goals

- No LangGraph runtime.
- No StateGraph.
- No Python sidecar.
- No ORM or migration framework.
- No progress-stream persistence.
- No QChat main-loop refactor.
- No natural-language command auto-execution.
