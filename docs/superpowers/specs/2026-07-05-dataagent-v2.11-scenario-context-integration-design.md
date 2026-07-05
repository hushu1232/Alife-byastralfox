# DataAgent V2.11 Scenario Context Integration Design

## Decision

V2.11 should wire the V2.10 DataAgent scenario knowledge pack into the planner-preparation path and owner diagnostics. It should not introduce LangGraph runtime behavior, a Python sidecar, PostgreSQL checkpointing, a new SQL execution path, or natural-language QChat command execution.

The goal is to make business terms deterministic before the model sees them. A question such as "工程门禁里最近失败的必需项" should first resolve into controlled DataAgent hints such as `engineering_gate`, `test_run`, `runtime_readiness_check`, `status`, and `required`. The LLM planner may use those hints to generate a QueryPlan, but QueryPlan validation, SQL compilation, SQL safety, read-only execution, evidence, audit, progress, and trace boundaries remain the authority.

## Goals

- Make the existing `engineering.zh-CN.json` scenario pack readable, UTF-8 safe, and protected by round-trip tests.
- Add a compact `DataAgentScenarioContext` model that represents matched terms, matched metrics, candidate datasets, candidate fields, and a stable reason code.
- Add a deterministic `DataAgentScenarioContextBuilder` that resolves a user utterance against a scenario pack and the DataAgent catalog.
- Inject scenario context into `LlmDataAgentPlannerPromptFormatter` as bounded hints, not as SQL or authority.
- Add owner-only DataAgent scenario diagnostics text so a maintainer can inspect which business terms were recognized.
- Add readiness and QChat engineering-map gates proving V2.11 is wired without letting QChat depend directly on DataAgent internals.

## Non-Goals

- Do not add LangGraph, StateGraph, or Python sidecar code.
- Do not add PostgreSQL checkpoint or audit productization in V2.11.
- Do not change QChat's main reply loop.
- Do not let natural-language QChat messages trigger diagnostics commands automatically.
- Do not let scenario context execute SQL, call XML tools, authorize tool use, or bypass Tool Broker.
- Do not let scenario metrics bypass `DataAgentQueryPlanValidator`.
- Do not make Memory, Vision, Speech, DeskPet, Emotion, or presentation adapters into agents.

## Current Baseline

V2.10 already introduced:

- `DataAgentScenarioKnowledgePackProvider`
- `docs/dataagent/scenario-packs/engineering.zh-CN.json`
- `DataAgentToolScopePolicy`
- `DataAgentScenarioKnowledgePackPresent`
- `DataAgentNodeToolScopePolicyPresent`
- `DataAgentSafetyCapabilitiesRemainDeterministic`

The missing piece is usage. The pack is validated and gated, but it is not yet part of the planner prompt or a diagnostics surface. V2.11 should therefore consume the pack at the scenario-knowledge boundary and leave the rest of the pipeline intact.

## Encoding Invariant

The scenario pack must store readable `zh-CN` text. The following terms should be visible in the JSON file, test source, prompt output, and diagnostics output:

- `工程门禁`
- `最近失败的测试`
- `缺失项`
- `文档证据`
- `失败`
- `必需`

Tests should read the file as UTF-8 and assert those exact strings. This protects the project from mojibake regressions that make the feature hard to demo or maintain.

## Core Model

Add a small model surface inside `Alife.Function.DataAgent`:

```csharp
public sealed record DataAgentScenarioContext(
    string Scenario,
    string Culture,
    IReadOnlyList<DataAgentScenarioTermMatch> Terms,
    IReadOnlyList<DataAgentScenarioMetricMatch> Metrics,
    IReadOnlyList<string> CandidateDatasets,
    IReadOnlyList<string> CandidateFields,
    string ReasonCode)
{
    public bool HasMatches => Terms.Count > 0 || Metrics.Count > 0;
}

public sealed record DataAgentScenarioTermMatch(
    string Term,
    string Dataset,
    IReadOnlyList<string> Fields,
    string MatchedText);

public sealed record DataAgentScenarioMetricMatch(
    string Name,
    string Field,
    string Operator,
    object? Value);
```

Reason codes should be stable and small:

```text
scenario_context_matched
scenario_context_no_match
scenario_context_catalog_mismatch
scenario_context_pack_unavailable
```

The model must use defensive read-only snapshots. It should not expose mutable arrays or lists.

## Scenario Context Builder

Add `DataAgentScenarioContextBuilder`.

Inputs:

- `DataAgentCatalog`
- `DataAgentScenarioKnowledgePack`
- user utterance

Responsibilities:

- Match scenario terms by term and alias, using the existing provider semantics.
- Match metrics only when the utterance contains the metric name.
- Keep only datasets that exist in `DataAgentCatalog`.
- Keep only fields that exist in the matched dataset.
- Deduplicate datasets and fields with ordinal-ignore-case comparison.
- Preserve stable output order: term order from the pack, metric order from the pack, dataset and field order by first appearance.
- Return `scenario_context_no_match` for blank or unmatched utterances.
- Return `scenario_context_catalog_mismatch` when the pack matches text but all matched datasets or fields are invalid for the catalog.

The builder is deterministic. It must not call the model, run SQL, inspect SQLite, call QChat, or read arbitrary files.

## Planner Prompt Integration

Extend `LlmDataAgentPlannerPromptFormatter` with an overload:

```csharp
public DataAgentLlmPlannerPrompt Format(
    DataAgentQueryRequest request,
    DataAgentCatalog catalog,
    DataAgentSchemaSnapshot schemaSnapshot,
    DataAgentScenarioContext? scenarioContext)
```

The existing overload should remain and delegate with `null` so current tests and callers keep working.

When scenario context has matches, add a `Scenario context:` section after the approved schema and before the output contract. The section should be compact and bounded:

```text
Scenario context:
- scenario: engineering_readiness
- reason_code: scenario_context_matched
- candidate_datasets: engineering_gate, test_run, runtime_readiness_check
- candidate_fields: name, status, required, evidence_path, failed_count, started_at
- matched_terms:
  - 工程门禁 -> engineering_gate(name,status,required,evidence_path)
  - 最近失败的测试 -> test_run(name,status,failed_count,started_at)
  - 缺失项 -> runtime_readiness_check(name,status,required,evidence_path)
- matched_metrics:
  - 失败: status != passed
  - 必需: required = true
```

The prompt must state that scenario context is a hint and that the planner still must use only approved schema fields and operators. It must also repeat that the model must not output SQL.

When there is no match, the prompt can omit the section or include a one-line `reason_code=scenario_context_no_match`. The recommended behavior is to omit the section to avoid distracting the planner.

## Planner Consumption Boundary

V2.11 should avoid a large service refactor. The preferred implementation is:

- Add `DataAgentScenarioContext` to `DataAgentQueryRequest` as an optional property, or add a wrapper request type if the existing record shape makes that cleaner.
- Update `LlmDataAgentQueryPlanner` to pass the optional context into `LlmDataAgentPlannerPromptFormatter`.
- Keep `DeterministicDataAgentQueryPlanner` behavior unchanged unless it can consume the context with a small, tested improvement.
- Keep `DataAgentService` constructors backward compatible.

If a scenario pack is not configured, DataAgent should behave exactly as it does today. Scenario context is an enhancement, not a required runtime dependency for all callers.

## Owner Diagnostics

Add `DataAgentScenarioDiagnosticsFormatter`.

The output should be owner-only, bounded, and safe:

```text
DataAgent scenario diagnostics
scenario=engineering_readiness
reason=scenario_context_matched
datasets=engineering_gate,test_run,runtime_readiness_check
fields=name,status,required,evidence_path,failed_count,started_at
metrics=失败:status!=passed;必需:required=true
```

Rules:

- Do not include raw SQL.
- Do not include hidden prompts or Tool Broker manuals.
- Do not include arbitrary user text beyond sanitized matched terms.
- Keep output deterministic for tests.
- Expose the formatter through DataAgent-owned code first.

QChat should not directly reference `DataAgentScenarioKnowledgePackProvider` or `DataAgentScenarioContextBuilder`. If QChat receives scenario diagnostics later, it should receive already formatted text through the existing FunctionCaller/DataAgent diagnostics bridge pattern.

## Readiness Gates

Add one DataAgent readiness gate:

```text
DataAgentScenarioContextIntegrated
```

The gate should verify:

- The engineering pack contains readable UTF-8 Chinese terms.
- The scenario context builder maps `看看工程门禁里最近失败的必需项` to controlled datasets and fields.
- The LLM planner prompt includes scenario context hints.
- The prompt still contains `Do not output SQL`.
- The validator/compiler/safety/execute deterministic boundaries remain unchanged.

Add one QChat engineering-map gate:

```text
DataAgent scenario context diagnostics
```

This should be a static harness check that DataAgent exposes scenario diagnostics without requiring QChat to import scenario-provider implementation types.

Expected count changes should be explicit in tests and scripts:

- DataAgent readiness: `79 -> 80`
- QChat engineering map: `54 -> 55`

## Testing Strategy

Focused tests should cover:

- UTF-8 scenario pack readability.
- Scenario context builder matched term behavior.
- Scenario context builder matched metric behavior.
- Unmatched utterance returns `scenario_context_no_match`.
- Catalog mismatch drops unsafe or unknown dataset/field hints.
- Prompt formatter emits scenario context for matched terms.
- Prompt formatter omits or minimizes scenario context for no-match utterances.
- Prompt formatter never emits SQL in the scenario context section.
- Diagnostics formatter emits compact owner-safe text.
- Readiness script and QChat engineering map include the new gates.

No test should require live PostgreSQL, live QChat, a model endpoint, LangGraph, or a Python sidecar.

## Safety Invariants

- Scenario context narrows planner attention but does not authorize a query.
- QueryPlan validation remains the first authority over dataset, field, operator, sort, and limit.
- SQL compiler and SQL safety validator remain deterministic and model-free.
- Read-only execution remains the only execution path.
- Diagnostics remain read-only and owner-only.
- Unknown scenario terms fail closed as no-match hints, not as inferred datasets.
- Scenario pack errors fail the new readiness gate but should not make non-DataAgent runtime startup depend on external services.

## Future Work

V2.12 can productize PostgreSQL-backed audit and checkpoint storage. V2.13 can add a disabled-by-default workflow orchestrator contract for a future LangGraph sidecar. V2.14 can pilot a DataQueryGraph where the `scenario_knowledge` node is deterministic and the planner node receives only the scoped capabilities defined by `DataAgentToolScopePolicy`.

V2.11 should stop before those steps. Its success criterion is narrower: business terms become controlled context before QueryPlan generation, and that behavior is testable, observable, and reversible.
