# DataAgent V2.12 Runtime Scenario Context

## Purpose

V2.12 activates the V2.11 engineering scenario context in the normal DataAgent runtime path. The goal is to reduce LLM planner ambiguity before query planning without moving SQL authority into the model.

## Runtime Flow

1. `DataAgentService.Answer(...)` receives a natural-language question.
2. `DataAgentScenarioContextProvider` loads the engineering scenario pack owned by DataAgent.
3. `DataAgentScenarioContextBuilder` maps business terms to catalog-safe datasets, fields, and metrics.
4. `DataAgentService` attaches the context to `DataAgentQueryRequest`.
5. `LlmDataAgentQueryPlanner` includes the scenario context as bounded prompt hints.
6. QueryPlan validation, SQL compilation, SQL safety validation, and read-only execution remain deterministic authority.

## Boundaries

- QChat does not load scenario packs.
- QChat does not call `DataAgentScenarioContextBuilder`.
- QChat does not own DataAgent node tool scope policy.
- Scenario context is a hint only and cannot authorize fields, operators, SQL text, or tool execution.
- DataAgent readiness and QChat engineering-map scripts guard these boundaries.

## Non-Goals

- No LangGraph runtime.
- No StateGraph.
- No Python sidecar.
- No PostgreSQL checkpoint productization.
- No new SQL execution path.
- No QChat main-loop refactor.
- No natural-language QChat command auto-execution.
