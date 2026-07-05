# DataAgent V2.12 Runtime Scenario Context Activation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Activate V2.11 scenario context in the real DataAgent runtime path so ordinary DataAgent/QChat tool queries get deterministic, catalog-safe business-term hints before the LLM planner sees the request.

**Architecture:** V2.12 stays inside the existing C# DataAgent pipeline. A DataAgent-owned scenario context provider loads the existing engineering scenario pack, builds a `DataAgentScenarioContext`, and attaches it to `DataAgentQueryRequest` inside `DataAgentService`; QChat remains a consumer of DataAgent tools and diagnostics, not a scenario-pack owner. QueryPlan validation, SQL compilation, SQL safety validation, read-only execution, audit, evidence, trace, and progress remain authoritative.

**Tech Stack:** .NET 9, C# records/classes, NUnit, PowerShell readiness scripts, existing SQLite DataAgent harness.

---

## Working Context

Use an Alife worktree only. If V2.11 has not been merged into `master`, branch V2.12 from the current V2.11 head:

```powershell
git -C D:\Alife\.worktrees\alife-v2.11-scenario-context-integration status --short --branch
git -C D:\Alife\.worktrees\alife-v2.11-scenario-context-integration rev-parse HEAD
```

Recommended V2.12 branch/worktree after V2.11 is merged:

```powershell
git -C D:\Alife worktree add D:\Alife\.worktrees\alife-v2.12-runtime-scenario-context-activation -b alife-v2.12-runtime-scenario-context-activation master
```

Use the user-local .NET 9 SDK:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Guardrails:

- Do not touch `D:\FOXD`, `D:\FOXD\alife-service`, or ASRRAL-FOX.
- Do not add LangGraph, StateGraph, a Python sidecar, or a new SQL execution path.
- Do not productize PostgreSQL checkpointing in V2.12.
- Do not refactor the QChat main loop.
- Do not add natural-language QChat command auto-execution.
- Do not let QChat directly depend on `DataAgentScenarioKnowledgePackProvider`, `DataAgentScenarioContextBuilder`, or `DataAgentToolScopePolicy`.
- Do not let scenario context become SQL authority. It is prompt guidance only.
- Use `git add -f` for ignored `docs/superpowers/*` files when committing the plan.

---

## File Structure

Create:

- `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentScenarioContextProvider.cs`  
  Small DataAgent-owned boundary for building scenario context for a query.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextProvider.cs`  
  Runtime provider that locates the engineering scenario pack, loads it through `DataAgentScenarioKnowledgePackProvider`, and builds context through `DataAgentScenarioContextBuilder`.

- `Tests/Alife.Test.DataAgent/DataAgentScenarioContextProviderTests.cs`  
  Provider tests for matched context, no-match context, missing pack fallback, and strict DataAgent-only behavior.

- `Tests/Alife.Test.DataAgent/DataAgentRuntimeScenarioContextActivationTests.cs`  
  Service/planner integration tests proving actual `DataAgentService.Answer(...)` requests carry runtime scenario context into the planner and LLM prompt.

- `docs/dataagent/dataagent-v2.12-runtime-scenario-context.md`  
  Short design note explaining the runtime activation boundary, why QChat does not own scenario packs, and why this is not LangGraph productization.

Modify:

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`  
  Build scenario context before planner execution and attach it to `DataAgentQueryRequest`.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`  
  Add runtime readiness check `DataAgentRuntimeScenarioContextActivationPresent`.

- `tools/check-dataagent-readiness.ps1`  
  Add static gate for runtime activation markers and update required count `80 -> 81`.

- `tools/check-qchat-engineering-map.ps1`  
  Add a required QChat map check for DataAgent runtime scenario activation while retaining the direct QChat source omit scan; update required count `55 -> 56`.

- `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`  
  Update counts and protect the V2.12 readiness script contract.

- `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`  
  Add the V2.12 required check and protect that QChat still does not import scenario provider/builder/scope types.

---

## Task 1: DataAgent Scenario Context Provider

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentScenarioContextProvider.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextProvider.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentScenarioContextProviderTests.cs`

- [ ] **Step 1: Write failing provider tests**

Create `Tests/Alife.Test.DataAgent/DataAgentScenarioContextProviderTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentScenarioContextProviderTests
{
    const string EngineeringQuestion = "看看工程门禁里最近失败的必需项";

    [Test]
    public void BuildMapsEngineeringQuestionToMatchedRuntimeContext()
    {
        DataAgentScenarioContextProvider provider = new(EngineeringPackPath());

        DataAgentScenarioContext context = provider.Build(
            DataAgentCatalog.CreateDefault(),
            EngineeringQuestion);

        Assert.Multiple(() =>
        {
            Assert.That(context.Scenario, Is.EqualTo("engineering_readiness"));
            Assert.That(context.Culture, Is.EqualTo("zh-CN"));
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(context.CandidateFields, Does.Contain("required"));
            Assert.That(context.CandidateFields, Does.Contain("failed"));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败", "必需" }));
        });
    }

    [Test]
    public void BuildReturnsNoMatchForUnrelatedQuestion()
    {
        DataAgentScenarioContextProvider provider = new(EngineeringPackPath());

        DataAgentScenarioContext context = provider.Build(
            DataAgentCatalog.CreateDefault(),
            "今天桌宠心情怎么样");

        Assert.Multiple(() =>
        {
            Assert.That(context.Scenario, Is.EqualTo("engineering_readiness"));
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonNoMatch));
            Assert.That(context.CandidateDatasets, Is.Empty);
            Assert.That(context.CandidateFields, Is.Empty);
        });
    }

    [Test]
    public void BuildReturnsPackUnavailableWhenPackFileIsMissing()
    {
        string missingPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}-missing-engineering-pack.json");
        DataAgentScenarioContextProvider provider = new(missingPath);

        DataAgentScenarioContext context = provider.Build(
            DataAgentCatalog.CreateDefault(),
            EngineeringQuestion);

        Assert.Multiple(() =>
        {
            Assert.That(context.Scenario, Is.EqualTo("unavailable"));
            Assert.That(context.Culture, Is.EqualTo("und"));
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonPackUnavailable));
            Assert.That(context.HasMatches, Is.False);
        });
    }

    [Test]
    public void BuildRejectsNullCatalog()
    {
        DataAgentScenarioContextProvider provider = new(EngineeringPackPath());

        Assert.Throws<ArgumentNullException>(() => provider.Build(null!, EngineeringQuestion));
    }

    static string EngineeringPackPath()
    {
        return Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run provider tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioContextProviderTests" -v:minimal
```

Expected: FAIL because `DataAgentScenarioContextProvider` and `IDataAgentScenarioContextProvider` do not exist.

- [ ] **Step 3: Add the provider interface**

Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentScenarioContextProvider.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentScenarioContextProvider
{
    DataAgentScenarioContext Build(DataAgentCatalog catalog, string question);
}
```

- [ ] **Step 4: Add the runtime provider implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextProvider.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentScenarioContextProvider : IDataAgentScenarioContextProvider
{
    readonly string scenarioPackPath;
    readonly DataAgentScenarioContextBuilder builder;

    public DataAgentScenarioContextProvider(
        string scenarioPackPath,
        DataAgentScenarioContextBuilder? builder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioPackPath);

        this.scenarioPackPath = scenarioPackPath;
        this.builder = builder ?? new DataAgentScenarioContextBuilder();
    }

    public static DataAgentScenarioContextProvider CreateDefault()
    {
        string repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return new DataAgentScenarioContextProvider(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json"));
    }

    public DataAgentScenarioContext Build(DataAgentCatalog catalog, string question)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (File.Exists(scenarioPackPath) == false)
        {
            return new DataAgentScenarioContext(
                "unavailable",
                "und",
                [],
                [],
                [],
                [],
                DataAgentScenarioContext.ReasonPackUnavailable);
        }

        DataAgentScenarioKnowledgePack pack = DataAgentScenarioKnowledgePackProvider.Load(scenarioPackPath);
        return builder.Build(catalog, pack, question);
    }

    static string FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
```

- [ ] **Step 5: Run provider tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioContextProviderTests" -v:minimal
```

Expected: PASS, `4` tests passed.

- [ ] **Step 6: Commit Task 1**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentScenarioContextProvider.cs
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextProvider.cs
git add Tests/Alife.Test.DataAgent/DataAgentScenarioContextProviderTests.cs
git commit -m "Add DataAgent runtime scenario context provider"
```

Expected: commit succeeds.

---

## Task 2: Runtime Activation in DataAgentService

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentRuntimeScenarioContextActivationTests.cs`

- [ ] **Step 1: Write failing service runtime activation tests**

Create `Tests/Alife.Test.DataAgent/DataAgentRuntimeScenarioContextActivationTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentRuntimeScenarioContextActivationTests
{
    const string EngineeringQuestion = "看看工程门禁里最近失败的必需项";

    [Test]
    public void AnswerBuildsScenarioContextBeforeCallingPlanner()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        RecordingPlanner planner = new(new DataAgentQueryPlan(
            "engineering_gate",
            "runtime_scenario_activation",
            ["name", "status", "required"],
            [new DataAgentFilter("required", "=", true)],
            [],
            20));
        DataAgentService service = new(
            databasePath,
            planner,
            new DataAgentScenarioContextProvider(EngineeringPackPath()));

        DataAgentAnswer answer = service.Answer(EngineeringQuestion);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            Assert.That(planner.Requests, Has.Count.EqualTo(1));
            DataAgentScenarioContext? context = planner.Requests.Single().ScenarioContext;
            Assert.That(context, Is.Not.Null);
            Assert.That(context!.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(context.CandidateFields, Does.Contain("required"));
            Assert.That(context.CandidateFields, Does.Contain("failed"));
        });
    }

    [Test]
    public void AnswerContinuesWhenScenarioPackIsUnavailable()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        RecordingPlanner planner = new(new DataAgentQueryPlan(
            "document_index",
            "runtime_scenario_pack_unavailable",
            ["path", "title"],
            [],
            [],
            20));
        string missingPack = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}-missing-pack.json");
        DataAgentService service = new(
            databasePath,
            planner,
            new DataAgentScenarioContextProvider(missingPack));

        DataAgentAnswer answer = service.Answer("Which documents describe DataAgent NL2SQL?");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            DataAgentScenarioContext? context = planner.Requests.Single().ScenarioContext;
            Assert.That(context, Is.Not.Null);
            Assert.That(context!.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonPackUnavailable));
            Assert.That(context.HasMatches, Is.False);
        });
    }

    [Test]
    public void LlmPlannerReceivesRuntimeScenarioContextFromService()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        DataAgentLlmPlannerPrompt? capturedPrompt = null;
        LlmDataAgentQueryPlanner planner = new(
            databasePath,
            new CapturingInvalidLlmClient(prompt => capturedPrompt = prompt),
            new DeterministicDataAgentQueryPlanner());
        DataAgentService service = new(
            databasePath,
            planner,
            new DataAgentScenarioContextProvider(EngineeringPackPath()));

        DataAgentAnswer answer = service.Answer(EngineeringQuestion);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            Assert.That(answer.PlannerExplanation.Signals, Does.Contain("llm_invalid_output_fallback"));
            Assert.That(capturedPrompt, Is.Not.Null);
            Assert.That(capturedPrompt!.Schema, Does.Contain("Scenario context:"));
            Assert.That(capturedPrompt.Schema, Does.Contain("engineering_gate"));
            Assert.That(capturedPrompt.Schema, Does.Contain("test_run"));
            Assert.That(capturedPrompt.System, Does.Contain("Do not output SQL"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v212-runtime-scenario-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string EngineeringPackPath()
    {
        return Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    sealed class RecordingPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
    {
        public List<DataAgentQueryRequest> Requests { get; } = [];

        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            Requests.Add(request);
            return new DataAgentQueryPlanEnvelope(
                plan,
                new DataAgentPlannerExplanation(
                    nameof(RecordingPlanner),
                    plan.Intent,
                    plan.Dataset,
                    "medium",
                    ["runtime_scenario_context"],
                    "recorded runtime scenario context"));
        }
    }

    sealed class CapturingInvalidLlmClient(Action<DataAgentLlmPlannerPrompt> capturePrompt) : ILlmDataAgentPlannerClient
    {
        public string Complete(DataAgentLlmPlannerPrompt prompt)
        {
            capturePrompt(prompt);
            return """
                {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_operator","dataset":"engineering_gate","confidence":"medium","signals":["scenario"],"reason":"bad operator","select_fields":["name"],"filters":[{"field":"status","operator":"starts_with","value":"fail"}],"sorts":[],"limit":20}
                """;
        }
    }
}
```

- [ ] **Step 2: Run service activation tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentRuntimeScenarioContextActivationTests" -v:minimal
```

Expected: FAIL because `DataAgentService` has no constructor accepting `IDataAgentScenarioContextProvider`, and runtime requests do not attach scenario context.

- [ ] **Step 3: Extend DataAgentService constructors**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`, replace the field block and constructors at the top with:

```csharp
public sealed class DataAgentService
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    readonly DataAgentSqlSafetyValidator safetyValidator = new();
    readonly IDataAgentStore store;
    readonly IDataAgentQueryPlanner planner;
    readonly IDataAgentScenarioContextProvider scenarioContextProvider;

    public DataAgentService(string databasePath)
        : this(new SqliteDataAgentStore(databasePath), new DeterministicDataAgentQueryPlanner())
    {
    }

    public DataAgentService(string databasePath, IDataAgentQueryPlanner planner)
        : this(new SqliteDataAgentStore(databasePath), planner)
    {
    }

    public DataAgentService(
        string databasePath,
        IDataAgentQueryPlanner planner,
        IDataAgentScenarioContextProvider scenarioContextProvider)
        : this(new SqliteDataAgentStore(databasePath), planner, scenarioContextProvider)
    {
    }

    public DataAgentService(IDataAgentStore store)
        : this(store, new DeterministicDataAgentQueryPlanner())
    {
    }

    public DataAgentService(IDataAgentStore store, IDataAgentQueryPlanner planner)
        : this(store, planner, DataAgentScenarioContextProvider.CreateDefault())
    {
    }

    public DataAgentService(
        IDataAgentStore store,
        IDataAgentQueryPlanner planner,
        IDataAgentScenarioContextProvider scenarioContextProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(scenarioContextProvider);

        this.store = store;
        this.planner = planner;
        this.scenarioContextProvider = scenarioContextProvider;
    }
```

- [ ] **Step 4: Attach runtime scenario context to the planner request**

In `DataAgentService.Answer(...)`, replace:

```csharp
DataAgentQueryPlanEnvelope envelope = ValidateEnvelope(planner.Plan(new DataAgentQueryRequest(question, "developer", "zh-CN", false)));
```

with:

```csharp
DataAgentScenarioContext scenarioContext = scenarioContextProvider.Build(catalog, question);
DataAgentQueryPlanEnvelope envelope = ValidateEnvelope(planner.Plan(new DataAgentQueryRequest(
    question,
    "developer",
    "zh-CN",
    false,
    scenarioContext)));
```

- [ ] **Step 5: Run service activation tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentRuntimeScenarioContextActivationTests" -v:minimal
```

Expected: PASS, `3` tests passed.

- [ ] **Step 6: Run existing service/planner tests for regression coverage**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentServiceTests|FullyQualifiedName~DataAgentServicePlannerInjectionTests|FullyQualifiedName~LlmDataAgentQueryPlannerTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 7: Commit Task 2**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs
git add Tests/Alife.Test.DataAgent/DataAgentRuntimeScenarioContextActivationTests.cs
git commit -m "Activate DataAgent scenario context at runtime"
```

Expected: commit succeeds.

---

## Task 3: Runtime Readiness and QChat Engineering Map Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Update readiness tests before implementation**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`:

Replace:

```csharp
Assert.That(checks, Has.Count.EqualTo(66));
```

with:

```csharp
Assert.That(checks, Has.Count.EqualTo(67));
```

Add near the existing V2.11 scenario assertions:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentRuntimeScenarioContextActivationPresent"));
DataAgentReadinessCheck runtimeScenarioCheck = checks.Single(check => check.Name == "DataAgentRuntimeScenarioContextActivationPresent");
Assert.That(runtimeScenarioCheck.Detail, Does.Contain("service_context=true"));
Assert.That(runtimeScenarioCheck.Detail, Does.Contain("llm_prompt=true"));
Assert.That(runtimeScenarioCheck.Detail, Does.Contain("qchat_boundary=true"));
Assert.That(runtimeScenarioCheck.Detail, Does.Contain("sql_boundary=true"));
```

Replace script summary:

```csharp
"  Summary: 80 required passed, 0 required missing"
```

with:

```csharp
"  Summary: 81 required passed, 0 required missing"
```

Add script output assertion:

```csharp
Assert.That(result.StandardOutput, Does.Contain("DataAgentRuntimeScenarioContextActivationPresent"));
```

Replace:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 80"));
```

with:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 81"));
```

Add a contract test:

```csharp
[Test]
public void ReadinessScriptProtectsV212RuntimeScenarioContextContract()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "DataAgentRuntimeScenarioContextActivationPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("IDataAgentScenarioContextProvider.cs"));
        Assert.That(declaration, Does.Contain("DataAgentScenarioContextProvider.cs"));
        Assert.That(declaration, Does.Contain("DataAgentService.cs"));
        Assert.That(declaration, Does.Contain("scenarioContextProvider.Build"));
        Assert.That(declaration, Does.Contain("request.ScenarioContext"));
        Assert.That(declaration, Does.Contain("DataAgentRuntimeScenarioContextActivationTests"));
        Assert.That(declaration, Does.Contain("service_context=true"));
        Assert.That(declaration, Does.Contain("llm_prompt=true"));
        Assert.That(declaration, Does.Contain("qchat_boundary=true"));
        Assert.That(declaration, Does.Contain("sql_boundary=true"));
    });
}
```

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add to `RequiredV2Checks`:

```csharp
"DataAgent runtime scenario context activation"
```

Add to the existing scenario map contract test:

```csharp
string activationDeclaration = FindAddCheckDeclaration(script, "DataAgent runtime scenario context activation");

Assert.That(activationDeclaration, Does.Contain("DataAgentRuntimeScenarioContextActivationPresent"));
Assert.That(activationDeclaration, Does.Contain("DataAgentRuntimeScenarioContextActivationTests"));
Assert.That(activationDeclaration, Does.Contain("DataAgentScenarioKnowledgePackProvider"));
Assert.That(activationDeclaration, Does.Contain("DataAgentScenarioContextBuilder"));
Assert.That(activationDeclaration, Does.Contain("DataAgentToolScopePolicy"));
```

- [ ] **Step 2: Run readiness and QChat tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: FAIL until runtime readiness and script gates are added.

- [ ] **Step 3: Add DataAgent runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after `DataAgentScenarioContextIntegrated`, add:

```csharp
RecordingPlanner runtimeScenarioPlanner = new(new DataAgentQueryPlan(
    "engineering_gate",
    "runtime_scenario_context_activation",
    ["name", "status", "required"],
    [new DataAgentFilter("required", "=", true)],
    [],
    20));
DataAgentService runtimeScenarioService = new(
    databasePath,
    runtimeScenarioPlanner,
    new DataAgentScenarioContextProvider(scenarioPackPath));
DataAgentAnswer runtimeScenarioAnswer = runtimeScenarioService.Answer("看看工程门禁里最近失败的必需项");
DataAgentScenarioContext? runtimeScenarioContext = runtimeScenarioPlanner.Requests.SingleOrDefault()?.ScenarioContext;
DataAgentLlmPlannerPrompt? runtimeScenarioPrompt = null;
DataAgentService runtimeLlmScenarioService = new(
    databasePath,
    new LlmDataAgentQueryPlanner(
        databasePath,
        new CapturingFixedLlmClient(
            """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_operator","dataset":"engineering_gate","confidence":"medium","signals":["scenario"],"reason":"bad operator","select_fields":["name"],"filters":[{"field":"status","operator":"starts_with","value":"fail"}],"sorts":[],"limit":20}
            """,
            prompt => runtimeScenarioPrompt = prompt),
        new DeterministicDataAgentQueryPlanner()),
    new DataAgentScenarioContextProvider(scenarioPackPath));
DataAgentAnswer runtimeLlmScenarioAnswer = runtimeLlmScenarioService.Answer("看看工程门禁里最近失败的必需项");
bool runtimeServiceContextReady =
    runtimeScenarioAnswer.Validated &&
    runtimeScenarioContext is not null &&
    runtimeScenarioContext.ReasonCode == DataAgentScenarioContext.ReasonMatched &&
    runtimeScenarioContext.CandidateDatasets.SequenceEqual(["engineering_gate", "test_run"], StringComparer.Ordinal);
bool runtimeLlmPromptReady =
    runtimeLlmScenarioAnswer.Validated &&
    runtimeLlmScenarioAnswer.PlannerExplanation.Signals.Contains("llm_invalid_output_fallback", StringComparer.OrdinalIgnoreCase) &&
    runtimeScenarioPrompt?.Schema.Contains("Scenario context:", StringComparison.Ordinal) == true;
bool runtimeQChatBoundaryReady =
    typeof(DataAgentModuleService).Assembly.GetReferencedAssemblies().Any(assemblyName =>
        string.Equals(assemblyName.Name, "Alife.Function.QChat", StringComparison.Ordinal)) == false;
bool runtimeSqlBoundaryReady =
    typeof(DataAgentQueryPlanValidator).IsClass &&
    typeof(DataAgentSqlCompiler).IsClass &&
    typeof(DataAgentSqlSafetyValidator).IsClass &&
    typeof(DataAgentQueryExecutor).IsClass;
bool runtimeActivationReady =
    runtimeServiceContextReady &&
    runtimeLlmPromptReady &&
    runtimeQChatBoundaryReady &&
    runtimeSqlBoundaryReady;
checks.Add(runtimeActivationReady
    ? Pass("DataAgentRuntimeScenarioContextActivationPresent", "service_context=true;llm_prompt=true;qchat_boundary=true;sql_boundary=true")
    : Fail("DataAgentRuntimeScenarioContextActivationPresent", $"service_context={LowerBool(runtimeServiceContextReady)};llm_prompt={LowerBool(runtimeLlmPromptReady)};qchat_boundary={LowerBool(runtimeQChatBoundaryReady)};sql_boundary={LowerBool(runtimeSqlBoundaryReady)}"));
```

Add this nested planner near existing readiness helper classes:

```csharp
sealed class RecordingPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
{
    public List<DataAgentQueryRequest> Requests { get; } = [];

    public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
    {
        Requests.Add(request);
        return new DataAgentQueryPlanEnvelope(
            plan,
            new DataAgentPlannerExplanation(
                nameof(RecordingPlanner),
                plan.Intent,
                plan.Dataset,
                "medium",
                ["runtime_scenario_context"],
                "recorded runtime scenario context"));
    }
}
```

If a `CapturingFixedLlmClient` helper does not exist in `DataAgentReadiness.cs`, add:

```csharp
sealed class CapturingFixedLlmClient(string raw, Action<DataAgentLlmPlannerPrompt> capturePrompt) : ILlmDataAgentPlannerClient
{
    public string Complete(DataAgentLlmPlannerPrompt prompt)
    {
        capturePrompt(prompt);
        return raw;
    }
}
```

- [ ] **Step 4: Add static DataAgent readiness script gate**

In `tools/check-dataagent-readiness.ps1`, after `DataAgentScenarioContextIntegrated`, add:

```powershell
    New-Check -Group "Governance" -Name "DataAgentRuntimeScenarioContextActivationPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/IDataAgentScenarioContextProvider.cs" @("IDataAgentScenarioContextProvider", "Build")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextProvider.cs" @("DataAgentScenarioContextProvider", "CreateDefault", "DataAgentScenarioKnowledgePackProvider.Load", "DataAgentScenarioContextBuilder")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs" @("IDataAgentScenarioContextProvider", "scenarioContextProvider.Build", "new DataAgentQueryRequest(", "scenarioContext")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs" @("request.ScenarioContext", "formatter.Format")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentRuntimeScenarioContextActivationPresent", "service_context=true", "llm_prompt=true", "qchat_boundary=true", "sql_boundary=true")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentScenarioContextProviderTests.cs" @("DataAgentScenarioContextProviderTests", "BuildMapsEngineeringQuestionToMatchedRuntimeContext", "BuildReturnsPackUnavailableWhenPackFileIsMissing")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentRuntimeScenarioContextActivationTests.cs" @("DataAgentRuntimeScenarioContextActivationTests", "AnswerBuildsScenarioContextBeforeCallingPlanner", "LlmPlannerReceivesRuntimeScenarioContextFromService"))) -Detail "V2.12 DataAgent runtime scenario context activation markers"
```

Replace:

```powershell
$expectedRequired = 80
```

with:

```powershell
$expectedRequired = 81
```

- [ ] **Step 5: Add QChat engineering map gate**

In `tools/check-qchat-engineering-map.ps1`, after `DataAgent scenario context diagnostics`, add:

```powershell
Add-Check -Group "Harness" -Name "DataAgent runtime scenario context activation" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataAgentRuntimeScenarioContextActivationPresent", "DataAgentRuntimeScenarioContextActivationTests", "DataAgentScenarioContextProvider", "service_context=true", "llm_prompt=true") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitPatterns @("DataAgentScenarioKnowledgePackProvider", "DataAgentScenarioContextBuilder", "DataAgentToolScopePolicy")
```

Replace:

```powershell
$expectedRequired = 55
```

with:

```powershell
$expectedRequired = 56
```

- [ ] **Step 6: Run readiness tests and scripts**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentScenarioContextProviderTests|FullyQualifiedName~DataAgentRuntimeScenarioContextActivationTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent tests: PASS
QChat tests: PASS
DataAgent readiness: Summary: 81 required passed, 0 required missing
QChat engineering map: Summary: 56 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 7: Commit Task 3**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs
git add tools/check-dataagent-readiness.ps1
git add tools/check-qchat-engineering-map.ps1
git add Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git add Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Gate DataAgent runtime scenario activation"
```

Expected: commit succeeds.

---

## Task 4: Runtime Boundary Documentation

**Files:**
- Create: `docs/dataagent/dataagent-v2.12-runtime-scenario-context.md`

- [ ] **Step 1: Create the V2.12 runtime boundary document**

Create `docs/dataagent/dataagent-v2.12-runtime-scenario-context.md`:

```markdown
# DataAgent V2.12 Runtime Scenario Context

## Purpose

V2.12 activates the V2.11 engineering scenario context in the normal DataAgent runtime path. The goal is to reduce LLM planner ambiguity before query planning without moving SQL authority into the model.

## Runtime Flow

1. `DataAgentService.Answer(...)` receives a natural-language question.
2. `DataAgentScenarioContextProvider` loads the engineering scenario pack owned by DataAgent.
3. `DataAgentScenarioContextBuilder` maps business terms such as "工程门禁", "最近失败", and "必需" to catalog-safe datasets, fields, and metrics.
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
```

- [ ] **Step 2: Commit Task 4**

Run:

```powershell
git add -f docs/dataagent/dataagent-v2.12-runtime-scenario-context.md
git commit -m "Document DataAgent runtime scenario context boundary"
```

Expected: commit succeeds.

---

## Task 5: Final Verification and Scope Audit

**Files:**
- Verify: all changed files
- Commit: final corrections if any are needed

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioKnowledgePackProviderTests|FullyQualifiedName~DataAgentScenarioContextBuilderTests|FullyQualifiedName~DataAgentScenarioContextProviderTests|FullyQualifiedName~DataAgentRuntimeScenarioContextActivationTests|FullyQualifiedName~LlmDataAgentPlannerPromptFormatterTests|FullyQualifiedName~LlmDataAgentQueryPlannerTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 2: Run focused QChat boundary tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: PASS.

- [ ] **Step 3: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent readiness: Summary: 81 required passed, 0 required missing
QChat engineering map: Summary: 56 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Verify forbidden runtime shapes were not introduced**

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.DataAgent\*.cs -Pattern "LangGraph|Python sidecar|StateGraph"
Select-String -Path sources\Alife.Function\Alife.Function.QChat\*.cs -Pattern "DataAgentScenarioKnowledgePackProvider|DataAgentScenarioContextBuilder|DataAgentToolScopePolicy"
```

Expected: no matches.

- [ ] **Step 5: Verify formatting and repository state**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: no whitespace errors; only intentional files are modified or clean after commits. The local warning about `C:\Users\hu shu/.config/git/ignore` can appear and does not block this task.

- [ ] **Step 6: Run solution verification**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal -m:1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected: restore succeeds, build succeeds with `0 errors`, and tests pass. Live tests may remain skipped by existing environment gates.

- [ ] **Step 7: Commit final verification corrections if any were made**

If Step 1 through Step 6 required small corrections, commit only those corrections:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent
git add Tests/Alife.Test.DataAgent
git add Tests/Alife.Test.QChat
git add tools/check-dataagent-readiness.ps1
git add tools/check-qchat-engineering-map.ps1
git add -f docs/dataagent/dataagent-v2.12-runtime-scenario-context.md
git commit -m "Finalize DataAgent V2.12 runtime scenario activation"
```

Expected: commit succeeds when there are corrections. When no files changed after Task 4, skip this commit.

---

## Acceptance Criteria

- DataAgent runtime has an `IDataAgentScenarioContextProvider` boundary.
- `DataAgentScenarioContextProvider` loads the existing engineering scenario pack and returns pack-unavailable context when the pack is missing.
- `DataAgentService.Answer(...)` attaches `DataAgentScenarioContext` to `DataAgentQueryRequest` before calling the planner.
- `LlmDataAgentQueryPlanner` receives runtime scenario context through the existing `request.ScenarioContext` path.
- Scenario context remains a prompt hint only; validation, compilation, safety, and read-only execution remain deterministic.
- QChat source files do not reference `DataAgentScenarioKnowledgePackProvider`, `DataAgentScenarioContextBuilder`, or `DataAgentToolScopePolicy`.
- DataAgent readiness reports `81 required passed, 0 required missing`.
- QChat engineering map reports `56 required passed, 0 required missing, 0 optional present, 0 optional missing`.
- No LangGraph, StateGraph, Python sidecar, PostgreSQL checkpoint productization, new SQL execution path, QChat main-loop refactor, or natural-language QChat command auto-execution is added.

