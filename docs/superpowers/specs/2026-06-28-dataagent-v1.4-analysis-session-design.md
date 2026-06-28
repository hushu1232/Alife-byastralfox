# DataAgent v1.4 Analysis Session Design

## Goal

DataAgent v1.4 adds a real multi-turn analysis session layer around the existing governed NL2SQL chain. The goal is to let DataAgent keep an analysis task alive across follow-up questions such as:

```text
继续
只看失败的
刚才那组结果里哪个最关键
总结这次分析
```

v1.4 must preserve the v1.3 safety boundary:

```text
planner output -> envelope validation -> QueryPlan validation -> SQL compiler -> SQL safety validator -> SQLite executor -> audit/context
```

The new session layer may remember prior turns and produce deterministic analysis context, but it must not allow any user message, LLM output, or previous answer to bypass the existing single-turn DataAgent execution chain.

## Current Baseline

DataAgent v1.3 already provides:

- `DataAgentService.Answer(string question)` as the single-turn execution boundary.
- `IDataAgentQueryPlanner` with deterministic and LLM-backed planner options.
- strict planner envelope validation.
- clarification support through `DataAgentClarificationRequest`.
- strict LLM JSON parsing and model-output fallback.
- `DataAgentQueryPlanValidator`, `DataAgentSqlCompiler`, and `DataAgentSqlSafetyValidator`.
- accepted/rejected/clarification context output through `DataAgentContextProvider`.
- accepted/rejected query audit.
- required readiness checks for the LLM NL2SQL harness.

The current state machine is single-turn. `DataAgentService.Answer` can return a validated answer, rejected answer, or clarification answer, but DataAgent does not yet know whether several questions belong to the same analysis task.

QChat has its own bot/persona state machine. DataAgent v1.4 does not merge with it. QChat may call DataAgent and summarize DataAgent context, but DataAgent owns analysis state and query safety.

## Non-Goals

v1.4 does not add PostgreSQL persistence, SQLite session persistence, arbitrary SQL input, chart rendering, report publishing, a ChatBI UI, cross-dataset joins, aggregation, external crawling, or direct QChat persona-state mutation.

SQLite remains the local v1.x evidence/query store. It should not become the long-term Analysis Session persistence layer. The V2 target for persistent sessions is PostgreSQL.

v1.4 also does not require an LLM result summarizer. Session summaries must be deterministic and derived from already validated `DataAgentAnswer` values.

## Recommended Approach

Add an outer Analysis Session service around the existing single-turn service:

```text
QChat or caller
 -> DataAgentAnalysisService
      -> session state and follow-up interpretation
      -> DataAgentAnalysisContextProvider
      -> DataAgentService.Answer(...)
           -> existing v1.3 NL2SQL safety chain
      -> append DataAgentAnalysisTurn
      -> update session status
      -> deterministic session summary when needed
```

This keeps the responsibilities separate:

```text
QChat state machine
  bot persona, relationship, tone, reply strategy

DataAgent Analysis Session state machine
  multi-turn analysis task, follow-up context, clarification wait, summary window

DataAgent single-turn execution state chain
  planner, validation, SQL compilation, SQL safety, execution, audit
```

The recommended v1.4 store is in-memory only:

```text
IDataAgentAnalysisSessionStore
  -> InMemoryDataAgentAnalysisSessionStore
```

The interface should be designed so V2 can add:

```text
IDataAgentAnalysisSessionStore
  -> PostgreSqlDataAgentAnalysisSessionStore
```

without changing the analysis service contract.

## Components

Add these focused components:

```text
DataAgentAnalysisSession
DataAgentAnalysisTurn
DataAgentAnalysisSessionStatus
DataAgentAnalysisTurnIntent
IDataAgentAnalysisSessionStore
InMemoryDataAgentAnalysisSessionStore
DataAgentAnalysisService
DataAgentAnalysisContextProvider
DataAgentAnalysisSummarizer
DataAgentFollowUpInterpreter
```

### DataAgentAnalysisSession

`DataAgentAnalysisSession` is the aggregate root for one analysis task.

Recommended fields:

```csharp
public sealed record DataAgentAnalysisSession(
    string SessionId,
    string CallerId,
    string Goal,
    DataAgentAnalysisSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LastDataset,
    string? LastSummary,
    string? PendingClarificationQuestion,
    IReadOnlyList<DataAgentAnalysisTurn> Turns);
```

The session stores bounded, sanitized analysis metadata. It must not store unbounded raw model output.

`CallerId` is account or caller isolation metadata, for example `local`, `xiayu`, or `mixu`. It must not import or mutate QChat persona state.

### DataAgentAnalysisTurn

`DataAgentAnalysisTurn` records one user input and one DataAgent result snapshot.

Recommended fields:

```csharp
public sealed record DataAgentAnalysisTurn(
    string TurnId,
    int Index,
    string Question,
    DataAgentAnalysisTurnIntent Intent,
    DateTimeOffset CreatedAt,
    string Dataset,
    string Sql,
    int RowCount,
    string Summary,
    bool Validated,
    string RejectedReason);
```

The turn should keep enough information to support follow-up context and deterministic summaries, while avoiding a full copy of large result rows.

### DataAgentAnalysisSessionStatus

v1.4 should make session state explicit:

```csharp
public enum DataAgentAnalysisSessionStatus
{
    Active,
    AwaitingClarification,
    ReadyToSummarize,
    Summarized,
    Ended
}
```

State changes must be owned by `DataAgentAnalysisService`, not by the LLM planner, context provider, tool handler, or QChat persona state machine.

### DataAgentAnalysisTurnIntent

The follow-up interpreter classifies simple session intent deterministically:

```csharp
public enum DataAgentAnalysisTurnIntent
{
    NewQuestion,
    Continue,
    RefinePrevious,
    AnswerClarification,
    Summarize,
    End
}
```

This does not need to be perfect semantic understanding in v1.4. The goal is a testable baseline for common Chinese follow-up forms and safe fallback to `NewQuestion`.

### IDataAgentAnalysisSessionStore

The store isolates storage from session logic:

```csharp
public interface IDataAgentAnalysisSessionStore
{
    DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now);
    DataAgentAnalysisSession? Get(string sessionId);
    DataAgentAnalysisSession Save(DataAgentAnalysisSession session);
    bool End(string sessionId, DateTimeOffset now);
}
```

v1.4 implements this with an in-memory store. The interface should avoid SQLite-specific assumptions such as file paths, `SqliteConnection`, or SQLite row shapes.

For V2 PostgreSQL, the same interface can be implemented with session and turn tables.

### DataAgentAnalysisService

`DataAgentAnalysisService` is the new high-level entry point.

Recommended methods:

```csharp
public DataAgentAnalysisResponse Start(string goalOrQuestion);
public DataAgentAnalysisResponse Start(string callerId, string goalOrQuestion);
public DataAgentAnalysisResponse Continue(string sessionId, string question);
public DataAgentAnalysisResponse Summarize(string sessionId);
public DataAgentAnalysisResponse End(string sessionId);
```

Responsibilities:

- create and retrieve sessions.
- classify turn intent.
- compose follow-up question text for `DataAgentService.Answer`.
- call the existing single-turn DataAgent service.
- append a bounded turn snapshot.
- update session status.
- generate analysis session context.
- generate deterministic summaries when requested or when the summary window is reached.

### DataAgentAnalysisContextProvider

`DataAgentAnalysisContextProvider` emits explicit session context:

```text
[data_agent_analysis_session_context]
session_id=...
caller_id=...
goal=...
status=active
turn_count=3
last_dataset=test_run
last_row_count=12
last_summary=...
pending_clarification=false
pending_summary=false
[/data_agent_analysis_session_context]
```

This is explicit injection, not hidden memory. It must sanitize delimiters, control characters, and long values with `DataAgentContextFieldSanitizer`.

### DataAgentAnalysisSummarizer

The session summarizer is deterministic. It reads session turns and produces:

- analysis goal.
- number of turns.
- datasets touched.
- validated query count.
- rejected or clarification count.
- latest result summary.
- notable unfinished clarification, if any.

It must not invent conclusions that were not present in prior validated `DataAgentAnswer` summaries.

### DataAgentFollowUpInterpreter

`DataAgentFollowUpInterpreter` classifies common follow-up inputs with deterministic rules.

Examples:

```text
继续 -> Continue
接着看 -> Continue
只看失败的 -> RefinePrevious
换成 DataAgent 相关 -> RefinePrevious
总结一下 -> Summarize
结束 -> End
```

If the input is ambiguous or unsupported, default to `NewQuestion`.

## State Machine

The v1.4 Analysis Session state machine is explicit and separate from both QChat persona state and the single-turn query execution chain.

Recommended transitions:

```text
Start(question)
  -> Active

Active + validated answer
  -> Active

Active + needs_clarification
  -> AwaitingClarification

AwaitingClarification + answer clarification
  -> Active

Active + summary window reached
  -> ReadyToSummarize

Active/ReadyToSummarize + summarize intent
  -> Summarized

Active/Summarized + end intent
  -> Ended

Ended + continue
  -> rejected analysis response: session_ended
```

Single-turn answer state is still represented by `DataAgentAnswer.Validated` and `DataAgentAnswer.RejectedReason`. Session status must be derived from those stable outputs, not directly from model text.

## Summary Window

v1.4 should add a bounded semantic summary window:

- if the user explicitly asks for summary, summarize immediately.
- if the user says continue/refine, keep the session active.
- after 3 validated turns, mark the session `ReadyToSummarize`.
- when `ReadyToSummarize`, the next non-refinement turn may include a compact session summary context.
- if several turns produce no validated result, keep the state active or awaiting clarification rather than inventing a conclusion.

The summary window is deterministic. It is a state policy, not an LLM decision.

## Data Flow

Start flow:

```text
Start(goalOrQuestion)
 -> create session
 -> classify first input as NewQuestion
 -> call DataAgentService.Answer(goalOrQuestion)
 -> append turn
 -> update session status from DataAgentAnswer
 -> return DataAgentAnalysisResponse
```

Continue flow:

```text
Continue(sessionId, question)
 -> load session
 -> reject if missing or ended
 -> classify follow-up intent
 -> if summarize/end, handle without SQL execution
 -> compose bounded follow-up context
 -> call DataAgentService.Answer(composedQuestion)
 -> append turn
 -> update session status
 -> return DataAgentAnalysisResponse
```

Summarize flow:

```text
Summarize(sessionId)
 -> load session
 -> deterministic summary from turns
 -> set Summarized
 -> return context + summary
```

## Response Contract

Add a response type separate from `DataAgentAnswer`:

```csharp
public sealed record DataAgentAnalysisResponse(
    string SessionId,
    DataAgentAnalysisSessionStatus Status,
    DataAgentAnalysisTurnIntent Intent,
    DataAgentAnswer? Answer,
    string Summary,
    string Context,
    bool Accepted,
    string RejectedReason);
```

`Answer` may be null for summary-only or ended responses. `Accepted=false` is used for missing sessions, ended sessions, invalid input, or other session-layer rejections.

## Safety And Error Handling

v1.4 must keep these boundaries:

- The LLM planner never owns session state.
- QChat never bypasses DataAgent validation.
- Analysis Session never accepts raw SQL.
- Follow-up context is transformed into a natural-language question, then sent through the normal `DataAgentService.Answer` chain.
- Previous SQL text is context evidence only; it must not be re-executed directly.
- Session context and summary fields are sanitized before output.
- Missing session returns a stable rejection reason such as `analysis_session_not_found`.
- Ended session returns `analysis_session_ended`.
- Clarification state is driven by `DataAgentAnswer.RejectedReason == "needs_clarification"`.

## QChat Integration Boundary

v1.4 can expose the analysis session service through a DataAgent tool handler later, but the design keeps QChat integration shallow:

```text
QChat decides whether to call DataAgent
DataAgent owns analysis state, caller isolation, and query safety
QChat summarizes returned context in the bot voice
```

DataAgent session state must not mutate `XiaYuSelfStateMachine` fields such as mood, energy, vigilance, or relationships.

For future multi-account support, XiaYu and Mixu can call the same DataAgent Analysis Session API with separate `CallerId` and `SessionId` values. The DataAgent state remains account-neutral; `CallerId` is isolation metadata, not persona state.

## PostgreSQL V2 Boundary

V2 should add PostgreSQL persistence for analysis sessions after v1.4 validates the semantics.

Anticipated PostgreSQL model:

```text
dataagent_analysis_sessions
  session_id
  caller_id
  goal
  status
  created_at
  updated_at
  last_dataset
  last_summary
  pending_clarification_question

dataagent_analysis_turns
  turn_id
  session_id
  turn_index
  question
  intent
  created_at
  dataset
  sql
  row_count
  summary
  validated
  rejected_reason
```

v1.4 should not implement this schema, but its store interface and data records should make the migration direct.

## Readiness And Testing

Required tests for v1.4:

- starting a session creates `Active` state and appends the first turn.
- validated single-turn answer keeps session `Active`.
- clarification answer moves session to `AwaitingClarification`.
- explicit summary request moves session to `Summarized`.
- explicit end request moves session to `Ended`.
- continuing an ended session is rejected with `analysis_session_ended`.
- missing session is rejected with `analysis_session_not_found`.
- follow-up phrases such as `继续`, `只看失败的`, and `总结一下` classify deterministically.
- session context includes bounded sanitized fields and neutralizes context delimiters.
- summary output is deterministic and derived from turn snapshots.
- session store does not expose SQLite-specific dependencies.
- DataAgent readiness script includes v1.4 required markers.

Recommended readiness additions:

```text
dataagent_v14_analysis_session_service
dataagent_v14_analysis_session_store
dataagent_v14_state_machine_transitions
dataagent_v14_follow_up_interpreter
dataagent_v14_analysis_context_provider
dataagent_v14_summary_window
dataagent_v14_no_sqlite_session_binding
```

## Acceptance Criteria

v1.4 is complete when:

- DataAgent can start and continue an in-memory analysis session.
- session state is explicit and deterministic.
- common follow-up intents are recognized.
- summary and end flows work without SQL execution when appropriate.
- all query-producing turns still pass through `DataAgentService.Answer`.
- session context is explicit, sanitized, and test-covered.
- required readiness reports the new v1.4 checks.
- no PostgreSQL or SQLite session persistence is implemented in v1.4.
- the implementation leaves a clear V2 path for PostgreSQL session persistence.

