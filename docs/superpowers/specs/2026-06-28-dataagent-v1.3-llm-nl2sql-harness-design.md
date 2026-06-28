# DataAgent v1.3 LLM NL2SQL Harness Design

## Goal

DataAgent v1.3 introduces a real LLM NL2SQL planning boundary without making external model availability part of required runtime correctness. The goal is to let DataAgent plan natural-language data questions through a schema-aware LLM contract while preserving the existing v1.2 safety chain:

```text
planner output -> envelope validation -> QueryPlan validation -> SQL compiler -> SQL safety validator -> SQLite executor -> audit/context
```

v1.3 is an LLM NL2SQL harness MVP. It proves the interfaces, prompt contract, parser, fallback path, clarification path, and result explanation path with deterministic tests. A live LLM can be enabled by configuration and checked through optional/live diagnostics, but required tests must remain stable without network access, API keys, or model nondeterminism.

## Current Baseline

DataAgent v1.2 provides the engineering base needed for LLM planning:

- `IDataAgentQueryPlanner` is an injectable planning boundary.
- `DeterministicDataAgentQueryPlanner` is the default planner and safety fallback.
- `DataAgentQueryPlanEnvelope` carries a plan and planner explanation.
- `DataAgentPlannerExplanation` records planner name, intent, dataset, confidence, signals, and reason.
- `DataAgentSchemaIntrospector` proves the default catalog matches initialized SQLite schema.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, sorting, and limits.
- `DataAgentSqlCompiler` compiles only validated QueryPlans.
- `DataAgentSqlSafetyValidator` rejects unsafe SQL.
- `DataAgentQueryExecutor` executes read-only SQLite queries.
- `DataAgentContextProvider` publishes accepted/rejected `data_agent_context` blocks with planner metadata.
- `DataAgentToolHandler` exposes `dataagent_query` as natural-language input only.
- `tools/check-dataagent-readiness.ps1` reports 15 required checks.
- `tools/check-qchat-engineering-map.ps1` remains green with DataAgent readiness delegated to the DataAgent script.

v1.3 should extend this baseline without allowing model output to bypass any of these layers.

## Non-Goals

v1.3 does not add PostgreSQL, arbitrary SQL execution, joins, aggregation, chart rendering, Vue ChatBI, report publishing, multi-turn state persistence, external data crawling, or QChat conversational integration. It also does not make live OpenAI or any other external model a required dependency.

LLM output must not be executable SQL. The model may only propose a structured plan or a clarification request. SQL remains generated internally by `DataAgentSqlCompiler`.

## Recommended Approach

Add a small LLM planner subsystem behind the existing planner interface:

```text
User question
 -> DataAgentQueryRequest
 -> DataAgentPlannerSelector
      -> Disabled/default: DeterministicDataAgentQueryPlanner
      -> Harness/live: LlmDataAgentQueryPlanner
 -> DataAgentQueryPlanEnvelope or clarification envelope
 -> DataAgentService validation and execution chain
 -> DataAgentContextProvider
 -> audit/readiness
```

The recommended mode is:

```text
LlmPlannerMode = Disabled | Harness | Live
Default = Disabled
```

`Disabled` preserves current required behavior. `Harness` uses a fake local planner client for deterministic tests. `Live` calls a real model adapter and is covered by optional/live diagnostics only.

## Components

Add these focused components:

```text
ILlmDataAgentPlannerClient
LlmDataAgentQueryPlanner
LlmDataAgentPlannerPromptFormatter
LlmDataAgentPlannerResponseParser
LlmDataAgentPlannerOptions
DataAgentPlannerSelector
DataAgentClarificationRequest
DataAgentResultExplainer
```

### ILlmDataAgentPlannerClient

`ILlmDataAgentPlannerClient` owns the model boundary. It accepts a formatted prompt and returns raw model text.

```csharp
public interface ILlmDataAgentPlannerClient
{
    string Complete(DataAgentLlmPlannerPrompt prompt);
}
```

Required tests use fake implementations. A live implementation may adapt the existing language-model stack or OpenAI configuration, but that adapter must not be required by the default test suite.

### LlmDataAgentPlannerPromptFormatter

`LlmDataAgentPlannerPromptFormatter` builds the schema-aware prompt from:

- `DataAgentQueryRequest`
- `DataAgentCatalog`
- `DataAgentSchemaSnapshot`

The prompt must:

- expose only approved catalog datasets and fields.
- include schema evidence from `DataAgentSchemaIntrospector`.
- tell the model not to output SQL.
- require JSON only, with no surrounding natural language.
- define `confidence` as `high`, `medium`, or `low`.
- define when to return a clarification instead of guessing.
- state that unknown datasets, unknown fields, unsupported operators, and unsafe requests must not be planned.

### LlmDataAgentPlannerResponseParser

`LlmDataAgentPlannerResponseParser` parses raw model text into a controlled result:

```csharp
public sealed record DataAgentLlmPlannerResult(
    DataAgentQueryPlanEnvelope? Envelope,
    DataAgentClarificationRequest? Clarification,
    bool UsedFallback,
    string RawModelOutput);
```

The parser is intentionally strict. It rejects invalid JSON, JSON with extra natural-language wrapping, unknown datasets, unknown fields, unsupported confidence values, empty signals, empty reasons, invalid operators, and limits outside the existing validator boundary.

### LlmDataAgentQueryPlanner

`LlmDataAgentQueryPlanner` implements `IDataAgentQueryPlanner`. It composes:

- prompt formatter
- LLM planner client
- response parser
- schema introspector
- fallback deterministic planner

Flow:

```text
Plan(request)
 -> inspect catalog/sqlite schema
 -> format prompt
 -> call ILlmDataAgentPlannerClient
 -> parse strict JSON
 -> return valid plan envelope
 -> return clarification envelope
 -> otherwise discard model output and fallback to DeterministicDataAgentQueryPlanner
```

Invalid model output must not execute and should not be copied into user-facing context except as a sanitized, short fallback reason.

### DataAgentPlannerSelector

`DataAgentPlannerSelector` chooses the planner from configuration:

```text
Disabled -> DeterministicDataAgentQueryPlanner
Harness  -> LlmDataAgentQueryPlanner with fake/client-injected harness
Live     -> LlmDataAgentQueryPlanner with live client
```

The selector keeps `DataAgentService` from becoming responsible for mode selection, model calls, and fallback policy.

### DataAgentResultExplainer

`DataAgentResultExplainer` produces a stable natural-language explanation from local deterministic inputs:

- question
- dataset
- SQL status
- row count
- result summary
- planner explanation

v1.3 should not require an LLM result explainer. A deterministic explanation is enough to make answers more useful and testable:

```text
This query matched document_index and returned 3 rows. The planner selected this dataset because the question included DataAgent, NL2SQL, and document signals. Results come from the local SQLite store and do not include live external data.
```

## QueryPlan And Clarification Contract

v1.2 envelopes always contain a plan. v1.3 should make clarification an explicit first-class result while preserving existing planner behavior.

Recommended shape:

```csharp
public sealed record DataAgentQueryPlanEnvelope(
    DataAgentQueryPlan? Plan,
    DataAgentPlannerExplanation Explanation,
    DataAgentClarificationRequest? Clarification);

public sealed record DataAgentClarificationRequest(
    string Question,
    IReadOnlyList<string> Options,
    string Reason);
```

Compatibility rules:

- deterministic planner returns `Plan != null` and `Clarification == null`.
- LLM planner returns `Plan != null` for valid plans.
- LLM planner returns `Plan == null` and `Clarification != null` when the question should not be guessed.
- `DataAgentService.Answer` handles clarification before SQL compilation and execution.
- clarification output is audited as a non-executed result with `sql_status=needs_clarification`.

Clarification context example:

```text
[data_agent_context]
question=How active has the project been recently?
dataset=
sql_status=needs_clarification
planner=LlmDataAgentQueryPlanner
planner_confidence=low
planner_reason=question has no time range or metric
planner_signals=ambiguous_time_range, ambiguous_metric
clarification_question=Do you want the last 7 days, last 30 days, or all history?
clarification_options=last 7 days, last 30 days, all history
[/data_agent_context]
```

## Strict JSON Contract

The LLM planner may return only two JSON types: `plan` or `clarification`.

Plan:

```json
{
  "type": "plan",
  "planner_name": "LlmDataAgentQueryPlanner",
  "intent": "find_dataagent_documents",
  "dataset": "document_index",
  "confidence": "medium",
  "signals": ["dataagent", "nl2sql", "document"],
  "reason": "question asks for DataAgent NL2SQL documentation",
  "select_fields": ["path", "title", "summary"],
  "filters": [
    {
      "field": "tags",
      "operator": "contains",
      "value": "dataagent"
    }
  ],
  "sorts": [],
  "limit": 20
}
```

Clarification:

```json
{
  "type": "clarification",
  "planner_name": "LlmDataAgentQueryPlanner",
  "intent": "clarify_ambiguous_query",
  "dataset": "",
  "confidence": "low",
  "signals": ["ambiguous_time_range", "ambiguous_metric"],
  "reason": "question does not specify metric or time range",
  "clarification_question": "Do you want the last 7 days, last 30 days, or all history?",
  "clarification_options": ["last 7 days", "last 30 days", "all history"]
}
```

Parser rules:

- JSON must be the whole response.
- `type` must be `plan` or `clarification`.
- `planner_name` must exist, but service-side code may normalize it to `LlmDataAgentQueryPlanner`.
- `confidence` must be `high`, `medium`, or `low`.
- `signals` must be a non-empty string array.
- `reason` must be non-empty and sanitized before context output.
- plan `dataset` must exist in `DataAgentCatalog`.
- plan selected fields, filter fields, and sort fields must exist in the approved dataset.
- plan operators must be supported by `DataAgentQueryPlanValidator`.
- plan limit must remain inside existing limits.
- clarification question must be non-empty.
- clarification options should contain 2 to 4 non-empty values.

Any invalid response is discarded for execution.

## Failure Strategy

v1.3 handles planner outcomes as:

```text
Valid plan
 -> enter existing validation, compile, SQL safety, execution, audit, context chain

Valid clarification
 -> do not compile or execute SQL
 -> return sql_status=needs_clarification
 -> audit as non-executed clarification

Invalid LLM output
 -> do not execute model output
 -> fallback to DeterministicDataAgentQueryPlanner
 -> context/audit include sanitized fallback signal
```

Fallback is preferred over hard failure for invalid model output because it preserves user-facing availability without weakening safety. The fallback path must never reuse model-selected SQL, datasets, fields, filters, or dangerous raw text.

## Readiness Upgrade

DataAgent readiness should add seven v1.3 required checks:

```text
LlmPlannerInterfacePresent
LlmPlannerPromptUsesSchemaSnapshot
LlmPlannerStrictJsonParser
LlmPlannerRejectsInvalidOutput
LlmPlannerFallbackPreservesSafety
ClarificationRequestSupported
NaturalLanguageResultExplanationPresent
```

Expected direction:

```text
15 required passed -> 22 required passed
0 required missing
```

`tools/check-qchat-engineering-map.ps1` should not increase its total required count for v1.3 unless QChat itself receives a new DataAgent-facing capability. QChat can continue to rely on the DataAgent readiness script and existing planner/tool integration marker.

Optional/live readiness can be added separately:

```text
tools/check-dataagent-live-llm-planner.ps1
```

Live diagnostics may verify:

- API key/config exists.
- selected live model is reachable.
- a fixture question produces a valid plan.
- a dangerous fixture question does not produce an executable plan.
- an ambiguous fixture question produces clarification.

Live diagnostics remain optional/manual until model access, latency, cost, and output stability are proven.

## Tests

Required tests must be deterministic and local. Add or extend tests in `Tests/Alife.Test.DataAgent`:

```text
LlmDataAgentPlannerPromptFormatterTests
LlmDataAgentPlannerResponseParserTests
LlmDataAgentQueryPlannerTests
DataAgentClarificationContextTests
DataAgentResultExplainerTests
DataAgentV13ReadinessTests
```

Coverage:

- prompt formatter includes schema snapshot and approved datasets.
- prompt formatter does not include unregistered tables.
- prompt formatter explicitly forbids SQL output.
- parser accepts a valid plan JSON.
- parser accepts a valid clarification JSON.
- parser rejects JSON wrapped with natural language.
- parser rejects unknown datasets.
- parser rejects unknown fields.
- parser rejects invalid operators.
- parser rejects invalid confidence.
- parser rejects empty signals or reason.
- LLM planner returns an envelope for fake valid plan output.
- LLM planner returns a clarification result for fake clarification output.
- LLM planner falls back for invalid fake output.
- clarification context does not compile or execute SQL.
- deterministic result explainer output is stable and sanitized.
- readiness includes all seven v1.3 required checks.

Regression tests should also prove deterministic planner behavior remains compatible.

## Acceptance Criteria

v1.3 is complete when:

- LLM planner mode is configurable and defaults to disabled.
- `ILlmDataAgentPlannerClient` exists and has fake harness coverage.
- `LlmDataAgentPlannerPromptFormatter` builds schema-aware prompts from catalog and schema snapshot.
- `LlmDataAgentPlannerResponseParser` strictly parses plan and clarification JSON.
- invalid model output cannot reach SQL compilation or execution.
- valid LLM plan output returns a `DataAgentQueryPlanEnvelope`.
- valid clarification output returns `sql_status=needs_clarification` without SQL execution.
- invalid LLM output falls back to `DeterministicDataAgentQueryPlanner`.
- deterministic result explanation appears in `DataAgentAnswer` or context.
- DataAgent readiness reports 22 required checks with 0 missing.
- QChat engineering map remains green.
- .NET 9 build, DataAgent tests, full solution tests, readiness scripts, and `git diff --check` pass.

## V2 Preparation Value

v1.3 prepares DataAgent V2 by adding the missing LLM planning boundary while preserving the v1/v1.1/v1.2 harness:

- schema-aware prompt construction.
- controlled LLM planner client abstraction.
- strict JSON parser.
- clarification as a first-class non-SQL result.
- deterministic fallback.
- deterministic result explanation.
- required harness tests and optional live diagnostics.

After v1.3, the natural next branches are:

```text
v1.4: aggregation, group-by, and metric semantic layer
v2.0: PostgreSQL connector, multi-turn ChatBI state, QChat integration, chart/report output
```

V2 should start only after v1.3 proves that LLM planning can be added without weakening QueryPlan validation, SQL safety, audit, readiness, or deterministic test stability.
