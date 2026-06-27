# DataAgent NL2SQL Design

## Goal

Build `Alife.Function.DataAgent` as Alife's native data analysis and NL2SQL module. The first version must make Alife able to answer evidence-backed questions about its own engineering state, runtime readiness, tests, documents, and required gates through a controlled natural-language-to-SQL pipeline.

The module is not a standalone NL2SQL demo. It is an Alife-native data agent that contributes auditable context to QChat and strengthens HarnessEngineering, LoopEngineering, and PromptEngineering.

## Job-Fit Purpose

This module maps directly to the target ChatBI role requirements:

- ChatBI report center: DataAgent produces query results, summaries, reports, and audit records.
- Context management and scenario knowledge packages: DataAgent maintains data catalogs and schema snapshots.
- Backend API and integration: later phases expose DataAgent through a small API for a Vue console.
- SQL and data modeling: the module owns datasets, schema snapshots, query plans, SQL compilation, and safety checks.
- Permission validation: query execution is restricted by dataset, table, field, role, and read-only SQL policy.
- LLM/Agent/RAG product delivery: DataAgent turns LLM-generated query intent into validated data context and evidence-backed responses.

The project value is real: Alife gains a way to inspect its own engineering state instead of relying on memory or manual file searches.

## Recommended Scope

Use a phased strategy:

1. **V1 Required Core:** local SQLite-backed engineering data mart, QueryPlan-first NL2SQL, SQL safety validation, read-only execution, result summarization, QChat context contribution, and readiness/harness tests.
2. **V2 Data Source Expansion:** PostgreSQL read-only connector, schema introspection, dialect isolation, role/field permission mapping, and optional live readiness.
3. **V3 ChatBI Console:** Vue 3 + TypeScript console with data catalog, NL2SQL workbench, SQL preview, query results, report preview, file/evidence viewer, query audit, and publish workflow.

## Why SQLite First

SQLite is the required V1 backend because it keeps the core DataAgent loop local, deterministic, and testable:

- No external database service is required to run Alife or the test harness.
- Fixture databases can be created quickly during tests.
- Readiness checks can validate DataAgent without Docker, ports, users, passwords, or network services.
- Engineering data such as gates, readiness output, test results, document indexes, and query audit records is small and local.
- The module can become a required gate earlier because its default checks are not blocked by external infrastructure.

PostgreSQL is still part of the roadmap, but it should be introduced as a V2 connector rather than a V1 hard dependency.

## DataAgent Positioning

DataAgent is responsible for:

```text
natural language question
 -> semantic settle gate
 -> dataset selection
 -> schema/context snapshot
 -> QueryPlan generation
 -> QueryPlan validation
 -> SQL compilation
 -> SQL safety validation
 -> read-only execution
 -> result summarization
 -> ContextContribution
 -> query audit
```

DataAgent is not responsible for:

- Editing user files.
- Deleting or mutating data.
- Uploading data.
- Reading arbitrary databases without registration.
- Injecting raw query results into stable persona prompts.
- Replacing the Memory module.
- Replacing QChat state-machine behavior.
- Building the full Vue console in V1.

## V1 Data Model

V1 should start with an Alife engineering data mart:

```text
engineering_gate
runtime_readiness_check
module_capability
test_run
document_index
query_audit
```

### `engineering_gate`

Tracks required and optional engineering-map gates.

```text
id
name
category
required
status
evidence_path
last_checked_at
source
```

### `runtime_readiness_check`

Tracks runtime readiness status for capabilities such as QChat, vision, TTS, and future DataAgent live connectors.

```text
id
capability
account
endpoint
status
required
failure_reason
last_checked_at
evidence_path
```

### `module_capability`

Tracks which module owns which capability and whether the capability is required.

```text
id
module_name
capability_name
required
status
test_project
evidence_path
```

### `test_run`

Tracks summarized test execution results.

```text
id
suite_name
passed
failed
skipped
total
ran_at
command
```

### `document_index`

Tracks specs, plans, runbooks, and other project documents.

```text
id
path
doc_type
title
summary
tags
updated_at
```

### `query_audit`

Tracks DataAgent query attempts.

```text
id
question
dataset
query_plan_json
generated_sql
validated
rejected_reason
row_count
elapsed_ms
created_at
```

## QueryPlan-First NL2SQL

V1 must not allow an LLM to produce arbitrary SQL and execute it directly. The model should produce a constrained QueryPlan JSON shape:

```json
{
  "dataset": "engineering_gate",
  "intent": "find_missing_required_gates",
  "select": ["name", "status", "evidence_path"],
  "filters": [
    { "field": "required", "op": "=", "value": true },
    { "field": "status", "op": "!=", "value": "passed" }
  ],
  "orderBy": [],
  "limit": 50
}
```

Code then validates and compiles the QueryPlan into SQL:

```sql
SELECT name, status, evidence_path
FROM engineering_gate
WHERE required = 1 AND status <> 'passed'
LIMIT 50;
```

This keeps NL2SQL testable and explainable.

## SQL Safety Policy

V1 only supports read-only queries:

Allowed:

```sql
SELECT
WITH ... SELECT
```

Rejected:

```sql
INSERT
UPDATE
DELETE
DROP
ALTER
CREATE
ATTACH
DETACH
PRAGMA
VACUUM
REINDEX
```

Additional required controls:

- Reject multi-statement SQL.
- Require a limit when returning row data.
- Enforce a max limit, initially 100.
- Enforce a whitelist of datasets, tables, and fields.
- Apply a command timeout.
- Audit every accepted and rejected query.

## Core Components

```text
DataAgentService
DataAgentCatalog
DataAgentSchemaInitializer
DataAgentFixtureImporter
DataAgentQueryPlan
DataAgentQueryPlanValidator
DataAgentSqlCompiler
DataAgentSqlSafetyValidator
DataAgentQueryExecutor
DataAgentResultSummarizer
DataAgentContextProvider
DataAgentAuditLog
DataAgentReadiness
```

### `DataAgentService`

The orchestrator used by QChat/tool calling. It receives a user question and returns a structured answer with evidence.

### `DataAgentCatalog`

Owns dataset definitions, allowed fields, field descriptions, and examples.

### `DataAgentSchemaInitializer`

Creates the local SQLite schema.

### `DataAgentFixtureImporter`

Loads deterministic V1 data from project scripts, engineering-map output, readiness output, or test fixtures.

### `DataAgentQueryPlan`

Represents a constrained query plan. It is the main boundary between LLM output and SQL generation.

### `DataAgentQueryPlanValidator`

Rejects unknown datasets, unknown fields, unsupported operators, missing limits, or unsafe values.

### `DataAgentSqlCompiler`

Compiles a validated QueryPlan into parameterized SQL.

### `DataAgentSqlSafetyValidator`

Performs a final SQL text safety check before execution.

### `DataAgentQueryExecutor`

Executes read-only SQL against SQLite in V1.

### `DataAgentResultSummarizer`

Turns tabular results into evidence-backed summaries.

### `DataAgentContextProvider`

Wraps results for QChat as dynamic, untrusted data context:

```text
[data_agent_context]
question=...
dataset=...
sql_status=validated
row_count=...
summary=...
evidence=...
[/data_agent_context]
```

### `DataAgentAuditLog`

Records accepted and rejected query attempts.

### `DataAgentReadiness`

Provides a readiness surface for tests and `tools/check-dataagent-readiness.ps1`.

## Alife Integration

V1 should integrate with existing Alife patterns:

- Create `sources/Alife.Function/Alife.Function.DataAgent`.
- Create `Tests/Alife.Test.DataAgent`.
- Register DataAgent as a function module only after core service tests pass.
- Use QChat/tool-call integration to allow natural-language project-state questions.
- Put DataAgent output into dynamic context, not stable persona prompt.
- Add readiness checks to the engineering map after the module is stable.

## Required V1 Questions

The first harness should prove at least these questions:

```text
当前还有哪些 required gate 没通过？
哪些 readiness check 和 QChat 视图/TTS 有关？
哪些测试证明 runtime readiness 是 required？
最近一次测试通过、失败、跳过数量是多少？
哪些文档和 DataAgent/NL2SQL 计划有关？
```

Each question should have a fixture-backed expected QueryPlan and expected result shape.

## V2 PostgreSQL Connector

V2 adds PostgreSQL as an external data source connector:

```text
Npgsql
PostgresDataSourceConnector
PostgresSchemaIntrospector
PostgresReadOnlyQueryExecutor
PostgresDialect
PostgresPermissionMapper
```

PostgreSQL should be optional/live at first. The required V1 SQLite harness must remain stable even when PostgreSQL is not installed.

## V3 ChatBI Console

V3 turns DataAgent into a visible ChatBI product surface:

```text
Vue 3
TypeScript
Vite
Pinia
Vue Router
Element Plus or Naive UI
ECharts
Monaco Editor
ASP.NET Core Minimal API
```

V3 screens:

- Data sources.
- Data catalog.
- NL2SQL workbench.
- QueryPlan and SQL preview.
- Query result table.
- Report preview.
- File/evidence viewer.
- Query audit.
- Permission rules.
- Version/publish history.

V3 query state flow:

```text
DraftQuestion
 -> Planned
 -> SqlGenerated
 -> Validated
 -> Executed
 -> Summarized
 -> ReportDrafted
 -> Published
```

Rejected flow:

```text
DraftQuestion
 -> Planned
 -> SqlGenerated
 -> Rejected
```

## Readiness Strategy

V1 adds:

```text
tools/check-dataagent-readiness.ps1
```

Default mode should validate:

- DataAgent module files exist.
- SQLite schema initializes.
- Fixture data imports.
- Fixed QueryPlan cases compile.
- Dangerous SQL is rejected.
- Read-only SQL executes.
- Context contribution formatting is stable.

Later live mode can validate PostgreSQL connectivity:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1 -Live -Strict
```

## Success Criteria

V1 is done when:

- `Alife.Function.DataAgent` exists.
- `Tests/Alife.Test.DataAgent` proves QueryPlan, SQL safety, execution, audit, and context output.
- A local SQLite fixture data store can answer the required V1 questions.
- Dangerous SQL is rejected with stable reasons.
- DataAgent output is wrapped as dynamic context.
- A readiness script verifies the core chain.
- Engineering map can promote DataAgent readiness after it proves stable.

## Explicit Deferred Work

These are not V1 requirements:

- Full Vue console.
- PostgreSQL as required backend.
- Arbitrary SQL editor.
- Automatic report publishing.
- Production BI deployment.
- Long-term personal memory.
- Automatic file mutation.
- Internet data crawling.
