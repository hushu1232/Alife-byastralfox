# DataAgent v1.2 Schema Explainability Design

## Goal

DataAgent v1.2 upgrades the v1.1 planner/tool capability with a stable schema contract and explainable planner metadata. The goal is to make DataAgent safer to extend toward LLM-assisted planning by proving what tables and fields exist, why a planner chose a dataset, and how confident the planner is before any generated query reaches SQL validation.

## Current Baseline

DataAgent v1.1 already provides the required runtime path:

- `DataAgentService` accepts an injectable `IDataAgentQueryPlanner`.
- `DeterministicDataAgentQueryPlanner` is the default planner.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles only validated QueryPlans.
- `DataAgentSqlSafetyValidator` rejects unsafe SQL.
- `DataAgentQueryExecutor` executes read-only SQLite queries.
- `DataAgentAuditLog` records accepted and rejected queries.
- `DataAgentToolHandler` exposes XML tool `dataagent_query`.
- `DataAgentModuleService` registers the XML tool and publishes results through `Poke`.
- `tools/check-dataagent-readiness.ps1` reports 12 required checks.
- `tools/check-qchat-engineering-map.ps1` includes DataAgent planner/tool integration as required.

v1.2 should preserve this path and add observability around it.

## Non-Goals

v1.2 does not introduce PostgreSQL, arbitrary SQL input, live external crawlers, vector search, Vue ChatBI UI, report publishing, or an LLM planner. It also does not rename the XML tool or change the existing `data_agent_context` wrapper. Those larger features depend on having a reliable schema and explanation contract first.

## Recommended Approach

Use a small metadata layer around the existing QueryPlan flow:

```text
User question
 -> DataAgentQueryRequest
 -> IDataAgentQueryPlanner
 -> DataAgentQueryPlanEnvelope
      - QueryPlan
      - PlannerExplanation
 -> QueryPlan validator
 -> SQL compiler
 -> SQL safety validator
 -> SQLite executor
 -> result summarizer
 -> data_agent_context with planner metadata
 -> XML tool publishes context through Poke
```

The planner still returns deterministic QueryPlans, but now it also reports why it made the choice. The service still owns validation, compilation, safety, execution, audit, and context building.

## Schema Introspection

Add a `DataAgentSchemaIntrospector` that compares the static catalog against the initialized SQLite schema.

The introspector should produce:

```csharp
public sealed record DataAgentSchemaSnapshot(
    IReadOnlyList<DataAgentDatasetSchema> Datasets,
    bool CatalogMatchesDatabase);

public sealed record DataAgentDatasetSchema(
    string Name,
    IReadOnlyList<string> CatalogFields,
    IReadOnlyList<string> DatabaseFields,
    bool ExistsInDatabase,
    bool FieldsMatch);
```

The source of truth remains `DataAgentCatalog.CreateDefault()`. SQLite introspection exists to prove the runtime store matches the catalog, not to allow dynamic unapproved datasets. If a table exists in SQLite but not in the catalog, v1.2 may ignore it for planning; if a catalog dataset is missing or has mismatched fields, readiness should fail.

The first implementation should inspect SQLite through `PRAGMA table_info('<table>')` using table names from the catalog. It should not query arbitrary table names supplied by users or model output.

## Planner Explanation Contract

Add an explicit explanation record:

```csharp
public sealed record DataAgentPlannerExplanation(
    string PlannerName,
    string Intent,
    string Dataset,
    string Confidence,
    IReadOnlyList<string> Signals,
    string Reason);
```

Allowed confidence values:

```text
high
medium
low
```

The deterministic planner should use stable, testable signals:

- Questions mentioning required gates, missing gates, or readiness evidence map to `engineering_gate`.
- Questions mentioning TTS, voice, vision, runtime, or account capability map to `runtime_readiness_check` unless the wording asks for required evidence.
- Questions mentioning tests, passed, failed, skipped, or suite map to `test_run`.
- Questions mentioning documents, docs, plan, design, DataAgent, NL2SQL, or SQL analysis map to `document_index`.
- Unknown project-state questions fall back to missing required engineering gates with `low` confidence.

The explanation must be audit-friendly and concise. It should not include raw user secrets, full prompt text, API keys, or large documents.

## QueryPlan Envelope

Add:

```csharp
public sealed record DataAgentQueryPlanEnvelope(
    DataAgentQueryPlan Plan,
    DataAgentPlannerExplanation Explanation);
```

Update `IDataAgentQueryPlanner`:

```csharp
DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request);
```

This is a controlled breaking change inside the DataAgent project. Existing test helper planners should be updated to return an envelope. `DataAgentService` should extract the plan from the envelope, validate it as before, and pass the explanation to context and audit-friendly response data.

## Answer Metadata

Extend `DataAgentAnswer` with planner metadata:

```csharp
DataAgentPlannerExplanation PlannerExplanation
```

Rejected answers should still include the planner explanation when available. If planner construction itself fails, the service may throw as it does today for invalid dependencies; v1.2 does not add exception swallowing around planner bugs.

## Context Output

Extend accepted context with:

```text
planner=DeterministicDataAgentQueryPlanner
planner_confidence=high
planner_reason=question mentions runtime readiness required evidence
planner_signals=runtime, readiness, required
```

Rejected context should include the same planner fields before the rejection reason:

```text
[data_agent_context]
question=...
dataset=engineering_gate
sql_status=rejected
planner=FixedPlanner
planner_confidence=low
planner_reason=test planner returned invalid operator
planner_signals=injected-test
rejected_reason=unsupported_operator:starts_with
[/data_agent_context]
```

`DataAgentContextProvider` remains responsible for sanitizing line breaks. It should sanitize planner reason and signal text the same way it sanitizes question, SQL, summary, and rejection reason.

## Readiness Upgrade

Extend DataAgent readiness with three required checks:

```text
SchemaSnapshotAvailable
CatalogMatchesSqliteSchema
PlannerExplanationInContext
```

Expected readiness direction:

```text
12 required passed -> 15 required passed
0 required missing
```

The QChat engineering map does not need a new category if it continues to delegate DataAgent details to `tools/check-dataagent-readiness.ps1`. It should remain green with all required checks present.

## Safety Invariants

v1.2 must preserve all v1.1 safety properties:

- A planner still cannot provide raw SQL.
- All planner output still goes through `DataAgentQueryPlanValidator`.
- All compiled SQL still goes through `DataAgentSqlSafetyValidator`.
- Rejected planner output is still audited.
- XML tool input remains natural-language `question` only.
- `dataagent_query` still returns and publishes a `data_agent_context` block.
- Schema introspection cannot authorize unknown datasets.

## Tests

Add focused tests in `Tests/Alife.Test.DataAgent`:

- `DataAgentSchemaIntrospectorTests`
  - snapshot includes every default catalog dataset.
  - initialized SQLite schema matches the catalog.
  - missing or mismatched table fields are reported as not matching.

- `DataAgentPlannerExplanationTests`
  - deterministic planner returns an envelope.
  - runtime readiness required-evidence question has dataset `engineering_gate`, confidence `high`, and meaningful signals.
  - unknown fallback question has `low` confidence and a fallback reason.

- `DataAgentContextProviderTests`
  - accepted context includes planner name, confidence, reason, and signals.
  - rejected context includes planner metadata and rejection reason.
  - planner fields are sanitized for line breaks.

- Update existing service/tool/readiness tests:
  - injected planners return `DataAgentQueryPlanEnvelope`.
  - `DataAgentAnswer` exposes `PlannerExplanation`.
  - tool output includes planner metadata.
  - readiness includes the three v1.2 required checks.

## Acceptance Criteria

v1.2 is complete when:

- `DataAgentSchemaIntrospector` can produce a schema snapshot from the default catalog and SQLite database.
- schema mismatch is detectable without executing user-provided SQL.
- `IDataAgentQueryPlanner` returns a `DataAgentQueryPlanEnvelope`.
- deterministic planner explanations are stable and covered by tests.
- `DataAgentService` preserves validation, SQL safety, execution, audit, and rejection behavior.
- `DataAgentAnswer` exposes planner explanation metadata.
- accepted and rejected `data_agent_context` blocks include planner metadata.
- DataAgent readiness reports schema and explanation checks as required.
- QChat engineering map remains green.
- .NET 9 build, DataAgent tests, full solution tests, and readiness scripts pass.

## V2 Preparation Value

This design prepares V2 without overbuilding it. A future LLM planner can use the schema snapshot as its allowed schema prompt and must return the same `DataAgentQueryPlanEnvelope`. A future PostgreSQL connector can implement its own schema introspection while still proving catalog alignment. A future ChatBI UI can show planner reason, confidence, SQL status, evidence paths, and audit trail without changing the core service contract.
