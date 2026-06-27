# DataAgent v1.1 Planner Tool Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make DataAgent v1.1 a runtime/tool-callable Alife capability by adding a pluggable QueryPlanner, a DataAgent XML tool handler, a module service, and required readiness checks.

**Architecture:** Keep SQLite and deterministic fixtures as the required harness. Move planning out of `DataAgentService` into `IDataAgentQueryPlanner`, inject the planner into the service, expose only natural-language query input through `DataAgentToolHandler`, and register the handler from `DataAgentModuleService` using the existing `XmlFunctionCaller` pattern.

**Tech Stack:** .NET 9, C#, NUnit, Microsoft.Data.Sqlite, existing Alife Framework module lifecycle, existing FunctionCaller XML handler system, PowerShell readiness scripts.

---

## File Structure

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryRequest.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DeterministicDataAgentQueryPlanner.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolHandler.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Create: `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentToolHandlerTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV11ReadinessTests.cs`

---

### Task 1: Planner Interface And Deterministic Planner

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryRequest.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DeterministicDataAgentQueryPlanner.cs`

- [ ] **Step 1: Write failing planner tests**

Create `DataAgentPlannerTests` that asserts:

```text
QChat TTS/vision readiness question -> runtime_readiness_check / find_qchat_tts_readiness
runtime readiness required question -> engineering_gate / find_runtime_readiness_required_evidence
test result question -> test_run / latest_test_run_summary
DataAgent document question -> document_index / find_dataagent_documents
unknown project-state question -> engineering_gate / find_missing_required_gates
role/locale/live flags do not change deterministic planner output
```

- [ ] **Step 2: Run the planner tests and verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentPlannerTests" -v:minimal
```

Expected: compile failure because `IDataAgentQueryPlanner`, `DataAgentQueryRequest`, and `DeterministicDataAgentQueryPlanner` do not exist.

- [ ] **Step 3: Implement planner interface and deterministic planner**

Use:

```csharp
public sealed record DataAgentQueryRequest(string Question, string Role, string Locale, bool AllowLiveSources);

public interface IDataAgentQueryPlanner
{
    DataAgentQueryPlan Plan(DataAgentQueryRequest request);
}
```

Move the current `DataAgentService.ResolvePlan(question)` rules into `DeterministicDataAgentQueryPlanner.Plan(request)`.

- [ ] **Step 4: Run planner tests and verify GREEN**

Run the same filtered test command. Expected: all planner tests pass.

---

### Task 2: Service Planner Injection And Safety Invariant

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`

- [ ] **Step 1: Write failing service injection tests**

Tests must prove:

```text
DataAgentService uses injected planner
default constructor preserves v1 behavior
invalid injected planner output is rejected
rejected planner output writes audit row
rejected context includes sql_status=rejected
```

Use an unsafe test planner returning:

```csharp
new DataAgentQueryPlan(
    "engineering_gate",
    "unsafe",
    ["name"],
    [new DataAgentFilter("status", "starts_with", "pass")],
    [],
    50);
```

- [ ] **Step 2: Run tests and verify RED**

Expected: constructor overload and planner injection behavior missing.

- [ ] **Step 3: Modify `DataAgentService`**

Add constructors:

```csharp
public DataAgentService(string databasePath)
    : this(databasePath, new DeterministicDataAgentQueryPlanner())
{
}

public DataAgentService(string databasePath, IDataAgentQueryPlanner planner)
{
    this.databasePath = databasePath;
    this.planner = planner;
}
```

Change `Answer(question)` to build:

```csharp
new DataAgentQueryRequest(question, "developer", "zh-CN", false)
```

and call `planner.Plan(request)`.

- [ ] **Step 4: Run tests and verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentServicePlannerInjectionTests|DataAgentServiceTests" -v:minimal
```

Expected: all pass.

---

### Task 3: DataAgent Tool Handler

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentToolHandlerTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolHandler.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`

- [ ] **Step 1: Write failing tool handler tests**

Tests must prove:

```text
DataAgentToolHandler.Query(question) returns [data_agent_context]
returned context includes dataset and evidence when available
handler method has XmlFunctionAttribute name dataagent_query
handler output is not raw SQL-only
```

- [ ] **Step 2: Run tests and verify RED**

Expected: `DataAgentToolHandler` missing.

- [ ] **Step 3: Add FunctionCaller project reference**

Modify `Alife.Function.DataAgent.csproj`:

```xml
<ProjectReference Include="..\Alife.Function.FunctionCaller\Alife.Function.FunctionCaller.csproj" />
<ProjectReference Include="..\..\Alife\Alife.Framework\Alife.Framework.csproj" />
```

- [ ] **Step 4: Implement `DataAgentToolHandler`**

The handler should expose:

```csharp
[XmlFunction(FunctionMode.OneShot, name: "dataagent_query")]
public string Query(string question)
{
    return service.Answer(question).Context;
}
```

- [ ] **Step 5: Run tests and verify GREEN**

Expected: tool handler tests pass.

---

### Task 4: DataAgent Module Service Runtime Registration

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`

- [ ] **Step 1: Write failing module service tests**

Tests must prove:

```text
DataAgentModuleService has ModuleAttribute
AwakeAsync source registers DataAgentToolHandler through XmlHandler
AwakeAsync source calls RegisterHandlerWithoutDocument or RegisterHandler
AwakeAsync source injects prompt text containing dataagent_query
module prompt warns that output is dynamic data context
```

- [ ] **Step 2: Run tests and verify RED**

Expected: `DataAgentModuleService` missing.

- [ ] **Step 3: Implement module service**

Implement:

```csharp
[Module("DataAgent", "...", defaultCategory: "astralfox-alife/数据分析")]
public sealed class DataAgentModuleService(XmlFunctionCaller functionService)
    : InteractiveModule<DataAgentModuleService>
```

In `AwakeAsync`:

```csharp
await base.AwakeAsync(context);
string databasePath = Path.Combine(AppContext.BaseDirectory, "DataAgent", "dataagent.sqlite");
DataAgentSchemaInitializer.Initialize(databasePath);
DataAgentFixtureImporter.Import(databasePath);
DataAgentService service = new(databasePath);
XmlHandler xmlHandler = new(new DataAgentToolHandler(service));
functionService.RegisterHandlerWithoutDocument(xmlHandler);
Prompt($"... dataagent_query ... dynamic data context ... {xmlHandler.FunctionDocument()}");
```

- [ ] **Step 4: Run tests and verify GREEN**

Expected: module service tests pass.

---

### Task 5: Readiness And Engineering Map Required Gate

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentV11ReadinessTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`

- [ ] **Step 1: Write failing v1.1 readiness tests**

Tests must prove:

```text
DataAgentReadiness.CheckCore includes PlannerInterfacePresent
DataAgentReadiness.CheckCore includes DeterministicPlannerPassesFixtures
DataAgentReadiness.CheckCore includes ServiceUsesInjectedPlanner
DataAgentReadiness.CheckCore includes UnsafePlannerOutputRejected
DataAgentReadiness.CheckCore includes ToolHandlerReturnsDataAgentContext
check-dataagent-readiness.ps1 prints these markers
engineering map declares DataAgent planner/tool integration as required
```

- [ ] **Step 2: Run tests and verify RED**

Expected: readiness markers missing.

- [ ] **Step 3: Extend readiness**

Add the five v1.1 checks to `DataAgentReadiness.CheckCore`.

- [ ] **Step 4: Extend PowerShell readiness script**

Add marker checks for:

```text
IDataAgentQueryPlanner
DeterministicDataAgentQueryPlanner
DataAgentToolHandler
DataAgentModuleService
dataagent_query
```

- [ ] **Step 5: Extend engineering map**

Add required check:

```powershell
Add-Check -Group "Harness" -Name "DataAgent planner/tool integration" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("PlannerInterfacePresent", "ToolHandlerReturnsDataAgentContext", "dataagent_query")
```

- [ ] **Step 6: Run focused tests and scripts**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent tests: 0 failed
DataAgent readiness: 12 required passed, 0 required missing
Engineering map: 33 required passed, 0 required missing
```

---

### Task 6: Full Verification, Merge, Upload

**Files:**
- Verify all changed files.

- [ ] **Step 1: Run build**

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build Alife.slnx --no-restore -v:minimal
```

Expected: 0 errors.

- [ ] **Step 2: Run full tests**

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
```

Expected: 0 failed.

- [ ] **Step 3: Run diff check**

```powershell
git diff --check
```

Expected: no output.

- [ ] **Step 4: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 docs/superpowers/plans/2026-06-27-dataagent-v1.1-planner-tool-integration.md
git commit -m "feat: add DataAgent v1.1 planner tool integration"
```

- [ ] **Step 5: Merge to master and upload**

Merge the feature branch back to `master`, run verification on `master`, and upload the snapshot to:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

---

## Self-Review

Spec coverage:

- Planner abstraction is covered by Tasks 1 and 2.
- Runtime/tool registration is covered by Tasks 3 and 4.
- Safety invariant is covered by Task 2.
- Readiness and engineering map are covered by Task 5.
- V2 remains deferred until v1.1 passes.

Placeholder scan:

- No deferred implementation placeholders are used as v1.1 requirements.
- V2 is explicitly marked as future work by the design spec, not part of this plan.

Type consistency:

- `IDataAgentQueryPlanner`, `DataAgentQueryRequest`, `DeterministicDataAgentQueryPlanner`, `DataAgentToolHandler`, and `DataAgentModuleService` are used consistently across tasks.
