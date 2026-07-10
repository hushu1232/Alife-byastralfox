# DataAgent NL2SQL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Alife's native DataAgent NL2SQL core so QChat can answer evidence-backed project engineering questions through a safe QueryPlan-to-SQL pipeline.

**Architecture:** V1 is a local, deterministic, SQLite-backed DataAgent. It uses a QueryPlan intermediate representation, validates datasets/fields/operators, compiles only read-only SQL, executes against a fixture-backed engineering data mart, audits each query, and emits dynamic data context for QChat. PostgreSQL and Vue ChatBI are planned as V2/V3, not V1 hard dependencies.

**Tech Stack:** .NET 9, C#, Microsoft.Data.Sqlite, NUnit/xUnit-compatible test style already used by the repo, PowerShell readiness scripts, future Npgsql, future Vue 3 + TypeScript + Vite.

---

## File Structure

- Create: `sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`
  - DataAgent function module project.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
  - Orchestrates question, plan, SQL, execution, summary, audit, and context output.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs`
  - Defines V1 datasets, fields, field descriptions, allowed roles, and examples.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`
  - Defines the constrained QueryPlan model.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlanValidator.cs`
  - Validates datasets, fields, operators, limits, and values.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSqlCompiler.cs`
  - Compiles validated QueryPlans into parameterized SQLite SQL.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSqlSafetyValidator.cs`
  - Rejects destructive SQL and unsafe SQL shapes.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs`
  - Creates the local SQLite schema.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentFixtureImporter.cs`
  - Imports deterministic engineering fixture data.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryExecutor.cs`
  - Executes read-only queries against SQLite.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAuditLog.cs`
  - Records accepted and rejected query attempts.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`
  - Wraps results as dynamic Alife data context.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Exposes a core readiness check surface.
- Create: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
  - Test project for DataAgent.
- Create: `Tests/Alife.Test.DataAgent/*.cs`
  - Focused tests for catalog, QueryPlan, SQL safety, execution, audit, readiness, and context output.
- Create: `tools/check-dataagent-readiness.ps1`
  - Script-level readiness gate for DataAgent.
- Modify: `Alife.slnx`
  - Include the new module and test project.

---

### Task 1: Create DataAgent Test Project Skeleton

**Files:**
- Create: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
- Create: `Tests/Alife.Test.DataAgent/DataAgentCatalogTests.cs`
- Modify: `Alife.slnx`

- [ ] **Step 1: Create the test project file**

Create `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj` with project references matching local test conventions:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\sources\Alife.Function\Alife.Function.DataAgent\Alife.Function.DataAgent.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the first failing catalog test**

Create `Tests/Alife.Test.DataAgent/DataAgentCatalogTests.cs`:

```csharp
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentCatalogTests
{
    [Test]
    public void DefaultCatalogContainsEngineeringDatasets()
    {
        Alife.Function.DataAgent.DataAgentCatalog catalog =
            Alife.Function.DataAgent.DataAgentCatalog.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.HasDataset("engineering_gate"), Is.True);
            Assert.That(catalog.HasDataset("runtime_readiness_check"), Is.True);
            Assert.That(catalog.HasDataset("module_capability"), Is.True);
            Assert.That(catalog.HasDataset("test_run"), Is.True);
            Assert.That(catalog.HasDataset("document_index"), Is.True);
            Assert.That(catalog.HasDataset("query_audit"), Is.True);
        });
    }
}
```

- [ ] **Step 3: Add the project to `Alife.slnx`**

Add the test project and future DataAgent project to the solution in the same style as existing project entries.

- [ ] **Step 4: Run the test and verify it fails because the module does not exist**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected result:

```text
The referenced project does not exist
```

- [ ] **Step 5: Commit the failing skeleton**

Run:

```powershell
git add Tests/Alife.Test.DataAgent Alife.slnx
git commit -m "test: add DataAgent test skeleton"
```

---

### Task 2: Add DataAgent Module And Catalog

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs`

- [ ] **Step 1: Create the module project**

Create `sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the catalog model**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentCatalog
{
    readonly Dictionary<string, DataAgentDataset> datasets;

    DataAgentCatalog(IEnumerable<DataAgentDataset> datasets)
    {
        this.datasets = datasets.ToDictionary(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static DataAgentCatalog CreateDefault()
    {
        return new DataAgentCatalog(
        [
            DataAgentDataset.Create("engineering_gate", ["id", "name", "category", "required", "status", "evidence_path", "last_checked_at", "source"]),
            DataAgentDataset.Create("runtime_readiness_check", ["id", "capability", "account", "endpoint", "status", "required", "failure_reason", "last_checked_at", "evidence_path"]),
            DataAgentDataset.Create("module_capability", ["id", "module_name", "capability_name", "required", "status", "test_project", "evidence_path"]),
            DataAgentDataset.Create("test_run", ["id", "suite_name", "passed", "failed", "skipped", "total", "ran_at", "command"]),
            DataAgentDataset.Create("document_index", ["id", "path", "doc_type", "title", "summary", "tags", "updated_at"]),
            DataAgentDataset.Create("query_audit", ["id", "question", "dataset", "query_plan_json", "generated_sql", "validated", "rejected_reason", "row_count", "elapsed_ms", "created_at"])
        ]);
    }

    public bool HasDataset(string name) => datasets.ContainsKey(name);

    public bool HasField(string datasetName, string fieldName)
    {
        return datasets.TryGetValue(datasetName, out DataAgentDataset? dataset) &&
               dataset.Fields.Contains(fieldName);
    }

    public DataAgentDataset GetDataset(string name)
    {
        if (datasets.TryGetValue(name, out DataAgentDataset? dataset))
            return dataset;

        throw new InvalidOperationException($"Unknown DataAgent dataset '{name}'.");
    }
}

public sealed record DataAgentDataset(string Name, IReadOnlySet<string> Fields)
{
    public static DataAgentDataset Create(string name, IEnumerable<string> fields)
    {
        return new DataAgentDataset(
            name,
            fields.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 3: Run catalog tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 4: Commit catalog implementation**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent Alife.slnx
git commit -m "feat: add DataAgent catalog"
```

---

### Task 3: Add QueryPlan Model And Validator

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlanValidator.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentQueryPlanValidatorTests.cs`

- [ ] **Step 1: Add failing validator tests**

Create tests that prove:

```text
valid engineering_gate QueryPlan passes
unknown dataset is rejected
unknown field is rejected
missing limit is rejected
limit over 100 is rejected
unsupported operator is rejected
```

- [ ] **Step 2: Implement `DataAgentQueryPlan`**

Use immutable records:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentQueryPlan(
    string Dataset,
    string Intent,
    IReadOnlyList<string> Select,
    IReadOnlyList<DataAgentFilter> Filters,
    IReadOnlyList<DataAgentOrderBy> OrderBy,
    int Limit);

public sealed record DataAgentFilter(string Field, string Operator, object? Value);

public sealed record DataAgentOrderBy(string Field, string Direction);
```

- [ ] **Step 3: Implement `DataAgentQueryPlanValidator`**

Validation rules:

```text
dataset must exist
selected fields must exist
filter fields must exist
order fields must exist
operators allowed: =, !=, <>, >, >=, <, <=, contains
directions allowed: asc, desc
limit must be between 1 and 100
```

- [ ] **Step 4: Run validator tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentQueryPlanValidatorTests" -v:minimal
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 5: Commit QueryPlan validator**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent
git commit -m "feat: validate DataAgent query plans"
```

---

### Task 4: Add SQL Compiler And Safety Validator

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSqlCompiler.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSqlSafetyValidator.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentSqlCompilerTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentSqlSafetyValidatorTests.cs`

- [ ] **Step 1: Add compiler tests**

Tests must prove that a valid QueryPlan compiles to parameterized SQL:

```sql
SELECT name, status, evidence_path
FROM engineering_gate
WHERE required = @p0 AND status <> @p1
LIMIT 50
```

- [ ] **Step 2: Add safety tests**

Tests must prove these are rejected:

```sql
DELETE FROM engineering_gate
DROP TABLE engineering_gate
UPDATE engineering_gate SET status = 'passed'
SELECT * FROM engineering_gate; SELECT * FROM query_audit
PRAGMA table_info(engineering_gate)
ATTACH DATABASE 'x' AS y
```

Tests must prove this is accepted:

```sql
SELECT name FROM engineering_gate LIMIT 10
```

- [ ] **Step 3: Implement compiler**

Compiler requirements:

```text
quote identifiers from known catalog fields only
compile contains as LIKE @pN
compile != as <>
append LIMIT from QueryPlan
return SQL text plus parameter values
```

- [ ] **Step 4: Implement safety validator**

Safety requirements:

```text
reject semicolon multi-statements
reject destructive keywords
allow SELECT and WITH only
require LIMIT for SELECT row queries
return stable rejection reason strings
```

- [ ] **Step 5: Run SQL tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentSqlCompilerTests|DataAgentSqlSafetyValidatorTests" -v:minimal
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 6: Commit SQL compiler and safety validator**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent
git commit -m "feat: compile safe DataAgent SQL"
```

---

### Task 5: Add SQLite Schema, Fixture Import, Executor, And Audit

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentFixtureImporter.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryExecutor.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAuditLog.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentSqliteExecutionTests.cs`

- [ ] **Step 1: Add fixture execution tests**

Tests must prove:

```text
schema initializes in a temporary SQLite file
fixture import inserts engineering gates
valid read-only query returns expected rows
accepted query audit is recorded
rejected query audit is recorded
```

- [ ] **Step 2: Implement schema initializer**

Create tables from the V1 data model in the design doc.

- [ ] **Step 3: Implement fixture importer**

Seed deterministic rows:

```text
engineering_gate: Runtime readiness script, required=true, status=passed
engineering_gate: DataAgent readiness script, required=false, status=missing
runtime_readiness_check: MixuTts9881Reachable, required=true, status=missing
test_run: Alife.Test.QChat, passed=1168, failed=0, skipped=10
document_index: DataAgent NL2SQL design doc
```

- [ ] **Step 4: Implement executor**

Executor requirements:

```text
open SQLite connection
bind parameters
set command timeout
return rows as dictionaries
never execute SQL that failed safety validation
```

- [ ] **Step 5: Implement audit log**

Audit requirements:

```text
record question
record dataset
record query plan JSON
record generated SQL
record validated flag
record rejection reason
record row count
record elapsed milliseconds
```

- [ ] **Step 6: Run execution tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentSqliteExecutionTests" -v:minimal
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 7: Commit SQLite execution**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent
git commit -m "feat: execute DataAgent SQLite queries"
```

---

### Task 6: Add Service, Summary, And Context Output

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultSummarizer.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentServiceTests.cs`

- [ ] **Step 1: Add service tests for required V1 questions**

Tests must cover:

```text
当前还有哪些 required gate 没通过？
哪些 readiness check 与 QChat、视觉或 TTS 有关？
哪些测试证明 runtime readiness 是 required？
最近一次测试通过、失败和跳过的数量是多少？
哪些文档与 DataAgent/NL2SQL 计划有关？
```

For V1, use deterministic question-to-QueryPlan fixtures rather than live LLM calls.

- [ ] **Step 2: Implement service orchestration**

Service flow:

```text
question
 -> fixture QueryPlan resolver
 -> QueryPlan validator
 -> SQL compiler
 -> SQL safety validator
 -> executor
 -> summarizer
 -> context provider
 -> audit log
```

- [ ] **Step 3: Implement summarizer**

Summaries must include:

```text
answer
row_count
evidence paths when present
limitations when the result is fixture/local data
```

- [ ] **Step 4: Implement context provider**

Output format:

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

- [ ] **Step 5: Run service tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentServiceTests" -v:minimal
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 6: Commit service and context output**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent
git commit -m "feat: add DataAgent query service"
```

---

### Task 7: Add Readiness Script And Engineering Map Entry

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Create: `tools/check-dataagent-readiness.ps1`
- Create: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `tools/check-qchat-engineering-map.ps1` or create a future generalized engineering-map entry if the repo has one by then.

- [ ] **Step 1: Add readiness tests**

Tests must prove:

```text
schema initialization passes
fixture import passes
dangerous SQL rejection passes
fixture QueryPlan compiles
fixture query executes
context output is stable
```

- [ ] **Step 2: Implement readiness surface**

`DataAgentReadiness` returns stable check names and statuses:

```text
DataAgentModulePresent
SqliteSchemaInitializes
FixtureDataImports
QueryPlanFixturesPass
DangerousSqlRejected
ReadOnlyQueryExecutes
ContextContributionStable
```

- [ ] **Step 3: Create readiness script**

`tools/check-dataagent-readiness.ps1` default mode prints:

```text
DataAgent Readiness
[Core]
  PASS DataAgentModulePresent
  PASS SqliteSchemaInitializes
  PASS FixtureDataImports
[Safety]
  PASS DangerousSqlRejected
[Query]
  PASS QueryPlanFixturesPass
  PASS ReadOnlyQueryExecutes
[Context]
  PASS ContextContributionStable
[Summary]
  Summary: 7 required passed, 0 required missing
```

- [ ] **Step 4: Run readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1
```

Expected result:

```text
Summary: 7 required passed, 0 required missing
```

- [ ] **Step 5: Add engineering-map entry after readiness is stable**

Add DataAgent readiness as required only after all DataAgent tests pass.

- [ ] **Step 6: Commit readiness**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1
git commit -m "feat: add DataAgent readiness gate"
```

---

### Task 8: Full Verification

**Files:**
- Verify: `Alife.slnx`
- Verify: `tools/check-dataagent-readiness.ps1`
- Verify: `tools/check-qchat-engineering-map.ps1`

- [ ] **Step 1: Run DataAgent readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1
```

Expected:

```text
0 required missing
```

- [ ] **Step 2: Run engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-engineering-map.ps1
```

Expected:

```text
0 required missing
```

- [ ] **Step 3: Run full .NET 9 build**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build Alife.slnx --no-restore -v:minimal
```

Expected:

```text
Build succeeded.
0 Error(s)
```

- [ ] **Step 4: Run full .NET 9 tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
```

Expected:

```text
Failed: 0
```

- [ ] **Step 5: Run diff whitespace check**

Run:

```powershell
git diff --check
```

Expected: no output and exit code `0`.

- [ ] **Step 6: Commit final verification notes if docs changed**

Run:

```powershell
git status --short
```

Expected: no unexpected files. Commit only intentional documentation or test updates.

---

## V2 PostgreSQL Plan

V2 should start only after V1 required core passes. Add:

```text
Npgsql
IDataSourceConnector
ISqlDialect
PostgresDataSourceConnector
PostgresSchemaIntrospector
PostgresReadOnlyQueryExecutor
PostgresPermissionMapper
```

V2 starts as optional/live:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1 -Live -Postgres
```

Promote PostgreSQL to required only when:

```text
local/dev bootstrap is stable
read-only role setup is scripted
schema introspection is tested
permissions are tested
live failures produce stable diagnostics
```

## V3 ChatBI Console Outlook

V3 turns DataAgent into a visible product surface:

```text
Vue 3 + TypeScript + Vite
Pinia
Vue Router
Element Plus or Naive UI
ECharts
Monaco Editor
ASP.NET Core Minimal API
OpenAPI
```

V3 screens:

```text
Data sources
Data catalog
NL2SQL workbench
QueryPlan preview
SQL preview
Result table
Report preview
File/evidence viewer
Query audit
Permission rules
Version and publish history
```

V3 product value:

```text
demonstrates Vue middle/back-office work
demonstrates backend API integration
demonstrates BI/report preview
demonstrates permissions and audit
demonstrates LLM/Agent feature productization
```

V3 must not replace the V1 harness. The UI should consume the same DataAgent core that QChat uses.

---

## Self-Review

Spec coverage:

- SQLite V1 required backend is covered by Tasks 2, 5, and 7.
- QueryPlan-first NL2SQL is covered by Tasks 3, 4, and 6.
- SQL safety is covered by Task 4.
- Audit is covered by Task 5.
- Context contribution is covered by Task 6.
- Readiness and engineering map are covered by Task 7.
- V2 PostgreSQL and V3 Vue ChatBI are explicitly deferred.

Placeholder scan:

- No task relies on "implement later" as a completion condition.
- Deferred V2/V3 items are named as roadmap sections, not V1 requirements.
- Commands and expected outcomes are listed for each implementation phase.

Type consistency:

- `DataAgentCatalog`, `DataAgentQueryPlan`, validator, compiler, executor, service, context provider, and readiness names are consistent across tasks.
- Dataset names match the design document.

## 2026-07-10 Historical Reconciliation

This plan is historical V1/V2/V3 planning material. Any Vue or ChatBI wording
is a deferred-work note, not current scope or a future commitment. ChatBI
Console is not required and does not block V3 closure. The current roadmap
authority is `docs/dataagent/dataagent-roadmap-reconciliation.md`.

The canonical five current-readable questions remain in the earlier required
V1 question block; their wording is authoritative for this historical plan.

V3 closure is identity-bound at 111 static and 95 core checks. Static 114 and
dynamic 98 include the V3.28/V4.0/V4.1 identities that remain outside the V3
frozen set. This history does not authorize runtime startup, sidecar authority,
or the obsolete FOXD copy-based upload workflow.
