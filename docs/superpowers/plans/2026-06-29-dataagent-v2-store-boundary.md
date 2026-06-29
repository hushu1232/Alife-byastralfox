# DataAgent V2.0 Store Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a provider-neutral DataAgent store boundary that preserves SQLite as the default local provider and adds PostgreSQL behind an opt-in V2 provider.

**Architecture:** V2.0 moves query execution, schema initialization, fixture import, query audit, and Tool Broker audit persistence behind `IDataAgentStore`. `DataAgentService` keeps planner validation, SQL compilation, SQL safety, result explanation, and context formatting. SQLite remains backward-compatible through `SqliteDataAgentStore`; PostgreSQL is introduced through `PostgresDataAgentStore` with live tests gated by `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`.

**Tech Stack:** .NET 9, NUnit, Microsoft.Data.Sqlite, Npgsql, PowerShell readiness scripts, `Alife.Function.DataAgent`, `Alife.Function.FunctionCaller`, SQLite default storage, PostgreSQL opt-in storage.

---

## Scope Guard

Allowed in V2.0:

- `IDataAgentStore`
- `DataAgentAcceptedAuditInput`
- `DataAgentRejectedAuditInput`
- `SqliteDataAgentStore`
- `PostgresDataAgentStore`
- `DataAgentStoreFactory`
- DataAgentService constructor overloads that preserve current callers
- PostgreSQL live tests skipped without `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`
- readiness gates for the store boundary

Not allowed in V2.0:

- LangGraph runtime
- Python sidecar
- supervisor runtime
- multi-agent orchestration
- plugin marketplace
- new business query domains
- replacing SQLite as the default provider
- requiring live PostgreSQL for default tests or default readiness

## File Structure

- Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs`: store contract and audit input records.
- Create `sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs`: SQLite provider that delegates to current SQLite helpers.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentStoreFactory.cs`: provider selection from environment/configuration.
- Create `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs`: PostgreSQL provider behind the same contract.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`: use `IDataAgentStore` internally while preserving path constructors.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`: create the configured store and keep SQLite default behavior.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`: add runtime store-boundary readiness evidence.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`: add Npgsql package reference.
- Modify `tools/check-dataagent-readiness.ps1`: require V2 store boundary markers.
- Modify `tools/check-qchat-engineering-map.ps1`: require store boundary as Harness evidence without requiring live PostgreSQL.
- Test with new and existing tests under `Tests/Alife.Test.DataAgent`.

---

### Task 1: Store Contract And SQLite Provider

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs`

- [ ] **Step 1: Write failing SQLite store contract tests**

Create `Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentStoreContractTests
{
    [Test]
    public void SqliteStoreInitializesImportsFixturesAndExecutesReadOnlyQuery()
    {
        string databasePath = CreateDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);

        store.Initialize();
        store.ImportFixtures();

        DataAgentCompiledSql compiled = new(
            "SELECT path, title FROM document_index ORDER BY id LIMIT 10",
            []);
        DataAgentQueryResult result = store.Query(compiled);

        Assert.Multiple(() =>
        {
            Assert.That(store.ProviderName, Is.EqualTo("sqlite"));
            Assert.That(result.Rows, Is.Not.Empty);
            Assert.That(result.Rows[0].Keys, Does.Contain("path"));
            Assert.That(result.Rows[0].Keys, Does.Contain("title"));
        });
    }

    [Test]
    public void SqliteStoreRecordsAcceptedAndRejectedQueryAudit()
    {
        string databasePath = CreateDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();

        store.RecordAccepted(new DataAgentAcceptedAuditInput(
            "Which runtime readiness gate is required?",
            "engineering_gate",
            "{\"intent\":\"find_runtime_readiness_required_evidence\"}",
            "SELECT name FROM engineering_gate LIMIT 1",
            1,
            TimeSpan.FromMilliseconds(12)));

        store.RecordRejected(new DataAgentRejectedAuditInput(
            "Use unsafe operator.",
            "engineering_gate",
            "{\"intent\":\"unsafe\"}",
            "SELECT name FROM engineering_gate WHERE status LIKE 'pass%'",
            "unsupported_operator:starts_with",
            TimeSpan.Zero));

        IReadOnlyList<DataAgentAuditRecord> records = store.ReadQueryAudit();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0].Validated, Is.True);
            Assert.That(records[0].RowCount, Is.EqualTo(1));
            Assert.That(records[1].Validated, Is.False);
            Assert.That(records[1].RejectedReason, Is.EqualTo("unsupported_operator:starts_with"));
        });
    }

    [Test]
    public void SqliteStoreRecordsToolBrokerAudit()
    {
        string databasePath = CreateDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();

        store.RecordToolBrokerAudit(new DataAgentToolBrokerAuditRecord(
            "session-1",
            "dataagent_analysis_continue",
            false,
            "tool_route_required",
            "route is required",
            DateTimeOffset.Parse("2026-06-29T00:00:00Z")));

        IReadOnlyList<DataAgentToolBrokerAuditRecord> records = store.ReadToolBrokerAudit();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].SessionId, Is.EqualTo("session-1"));
            Assert.That(records[0].ToolName, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(records[0].Allowed, Is.False);
            Assert.That(records[0].ReasonCode, Is.EqualTo("tool_route_required"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-store-contract-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentStoreContractTests" -v:minimal
```

Expected: fail because `IDataAgentStore`, `SqliteDataAgentStore`, `DataAgentAcceptedAuditInput`, and `DataAgentRejectedAuditInput` do not exist.

- [ ] **Step 3: Add the store contract and audit input records**

Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentStore
{
    string ProviderName { get; }
    void Initialize();
    void ImportFixtures();
    DataAgentQueryResult Query(DataAgentCompiledSql compiledSql);
    void RecordAccepted(DataAgentAcceptedAuditInput input);
    void RecordRejected(DataAgentRejectedAuditInput input);
    IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit();
    void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record);
    IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit();
}

public sealed record DataAgentAcceptedAuditInput(
    string Question,
    string Dataset,
    string QueryPlanJson,
    string GeneratedSql,
    int RowCount,
    TimeSpan Elapsed);

public sealed record DataAgentRejectedAuditInput(
    string Question,
    string Dataset,
    string QueryPlanJson,
    string GeneratedSql,
    string RejectedReason,
    TimeSpan Elapsed);
```

- [ ] **Step 4: Add the SQLite provider**

Create `sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class SqliteDataAgentStore(string databasePath) : IDataAgentStore
{
    public string ProviderName => "sqlite";

    public void Initialize()
    {
        DataAgentSchemaInitializer.Initialize(databasePath);
    }

    public void ImportFixtures()
    {
        DataAgentFixtureImporter.Import(databasePath);
    }

    public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
    {
        return new DataAgentQueryExecutor(databasePath).Execute(compiledSql);
    }

    public void RecordAccepted(DataAgentAcceptedAuditInput input)
    {
        new DataAgentAuditLog(databasePath).RecordAccepted(
            input.Question,
            input.Dataset,
            input.QueryPlanJson,
            input.GeneratedSql,
            input.RowCount,
            input.Elapsed);
    }

    public void RecordRejected(DataAgentRejectedAuditInput input)
    {
        new DataAgentAuditLog(databasePath).RecordRejected(
            input.Question,
            input.Dataset,
            input.QueryPlanJson,
            input.GeneratedSql,
            input.RejectedReason,
            input.Elapsed);
    }

    public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit()
    {
        return new DataAgentAuditLog(databasePath).ReadAll();
    }

    public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
    {
        new DataAgentToolBrokerAuditLog(databasePath).Record(record);
    }

    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit()
    {
        return new DataAgentToolBrokerAuditLog(databasePath).ReadAll();
    }
}
```

- [ ] **Step 5: Run the store contract tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentStoreContractTests" -v:minimal
```

Expected: all `DataAgentStoreContractTests` pass.

- [ ] **Step 6: Commit the store contract and SQLite provider**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs
git commit -m "Add DataAgent store contract and SQLite provider"
```

---

### Task 2: DataAgentService Store Injection

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`

- [ ] **Step 1: Write failing service-injection tests**

Append these tests to `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`:

```csharp
[Test]
public void UsesInjectedStoreForAcceptedQueryAndAudit()
{
    RecordingStore store = new(new DataAgentQueryResult([
        new Dictionary<string, object?>
        {
            ["name"] = "Runtime readiness script",
            ["status"] = "passed",
            ["evidence_path"] = "tools/check-qchat-runtime-readiness.ps1"
        }
    ]));
    DataAgentService service = new(store, new FixedPlanner(new DataAgentQueryPlan(
        "engineering_gate",
        "find_runtime_readiness_required_evidence",
        ["name", "status", "evidence_path"],
        [new DataAgentFilter("required", "=", true)],
        [],
        20)));

    DataAgentAnswer answer = service.Answer("Which runtime readiness gate is required?");

    Assert.Multiple(() =>
    {
        Assert.That(answer.Validated, Is.True);
        Assert.That(store.Queries, Has.Count.EqualTo(1));
        Assert.That(store.AcceptedAudits, Has.Count.EqualTo(1));
        Assert.That(store.RejectedAudits, Is.Empty);
        Assert.That(store.AcceptedAudits[0].Dataset, Is.EqualTo("engineering_gate"));
    });
}

[Test]
public void UsesInjectedStoreForRejectedQueryAuditWithoutExecutingQuery()
{
    RecordingStore store = new(new DataAgentQueryResult([]));
    DataAgentService service = new(store, new FixedPlanner(new DataAgentQueryPlan(
        "engineering_gate",
        "unsafe",
        ["name"],
        [new DataAgentFilter("status", "starts_with", "pass")],
        [],
        20)));

    DataAgentAnswer answer = service.Answer("Use unsafe operator.");

    Assert.Multiple(() =>
    {
        Assert.That(answer.Validated, Is.False);
        Assert.That(store.Queries, Is.Empty);
        Assert.That(store.AcceptedAudits, Is.Empty);
        Assert.That(store.RejectedAudits, Has.Count.EqualTo(1));
        Assert.That(store.RejectedAudits[0].RejectedReason, Does.Contain("unsupported_operator:starts_with"));
    });
}

sealed class RecordingStore(DataAgentQueryResult queryResult) : IDataAgentStore
{
    public List<DataAgentCompiledSql> Queries { get; } = [];
    public List<DataAgentAcceptedAuditInput> AcceptedAudits { get; } = [];
    public List<DataAgentRejectedAuditInput> RejectedAudits { get; } = [];
    public List<DataAgentToolBrokerAuditRecord> ToolBrokerAudits { get; } = [];
    public string ProviderName => "recording";

    public void Initialize() { }
    public void ImportFixtures() { }

    public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
    {
        Queries.Add(compiledSql);
        return queryResult;
    }

    public void RecordAccepted(DataAgentAcceptedAuditInput input)
    {
        AcceptedAudits.Add(input);
    }

    public void RecordRejected(DataAgentRejectedAuditInput input)
    {
        RejectedAudits.Add(input);
    }

    public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit()
    {
        return [];
    }

    public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
    {
        ToolBrokerAudits.Add(record);
    }

    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit()
    {
        return ToolBrokerAudits;
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentServicePlannerInjectionTests" -v:minimal
```

Expected: fail because `DataAgentService` does not yet expose an `IDataAgentStore` constructor and still writes audit through `databasePath`.

- [ ] **Step 3: Update `DataAgentService` constructors and fields**

Modify the top of `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`:

```csharp
public sealed class DataAgentService
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    readonly DataAgentSqlSafetyValidator safetyValidator = new();
    readonly IDataAgentStore store;
    readonly IDataAgentQueryPlanner planner;

    public DataAgentService(string databasePath)
        : this(new SqliteDataAgentStore(databasePath), new DeterministicDataAgentQueryPlanner())
    {
    }

    public DataAgentService(string databasePath, IDataAgentQueryPlanner planner)
        : this(new SqliteDataAgentStore(databasePath), planner)
    {
    }

    public DataAgentService(IDataAgentStore store)
        : this(store, new DeterministicDataAgentQueryPlanner())
    {
    }

    public DataAgentService(IDataAgentStore store, IDataAgentQueryPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(planner);

        this.store = store;
        this.planner = planner;
    }
```

Remove the old `databasePath` field from `DataAgentService`.

- [ ] **Step 4: Update accepted query execution and audit**

In `Answer`, replace the direct SQLite query and accepted audit with store calls:

```csharp
Stopwatch stopwatch = Stopwatch.StartNew();
DataAgentQueryResult result = store.Query(compiled);
stopwatch.Stop();
```

Replace `new DataAgentAuditLog(databasePath).RecordAccepted(...)` with:

```csharp
store.RecordAccepted(new DataAgentAcceptedAuditInput(
    question,
    plan.Dataset,
    queryPlanJson,
    compiled.Sql,
    result.Rows.Count,
    stopwatch.Elapsed));
```

- [ ] **Step 5: Update rejected and clarification audit**

In `Reject`, replace `DataAgentAuditLog` with:

```csharp
store.RecordRejected(new DataAgentRejectedAuditInput(
    question,
    plan.Dataset,
    queryPlanJson,
    generatedSql,
    reason,
    TimeSpan.Zero));
```

In `Clarify`, replace `DataAgentAuditLog` with:

```csharp
store.RecordRejected(new DataAgentRejectedAuditInput(
    question,
    string.Empty,
    queryPlanJson,
    string.Empty,
    "needs_clarification",
    TimeSpan.Zero));
```

- [ ] **Step 6: Run service and DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentServicePlannerInjectionTests|FullyQualifiedName~DataAgentServiceTests|FullyQualifiedName~DataAgentToolHandlerTests|FullyQualifiedName~DataAgentCapabilityProviderTests" -v:minimal
```

Expected: all focused tests pass and existing `DataAgentService(string databasePath)` behavior remains intact.

- [ ] **Step 7: Commit service store injection**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs
git commit -m "Route DataAgent service through store boundary"
```

---

### Task 3: Store Provider Selection And Module Wiring

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentStoreFactory.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentStoreFactoryTests.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Write failing store factory tests**

Create `Tests/Alife.Test.DataAgent/DataAgentStoreFactoryTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentStoreFactoryTests
{
    [Test]
    public void CreateDefaultUsesSqliteStore()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.sqlite");

        IDataAgentStore store = DataAgentStoreFactory.Create(new DataAgentStoreOptions(
            ProviderName: string.Empty,
            SqlitePath: databasePath,
            PostgresConnectionString: string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(store, Is.TypeOf<SqliteDataAgentStore>());
            Assert.That(store.ProviderName, Is.EqualTo("sqlite"));
        });
    }

    [Test]
    public void CreateRejectsUnknownProvider()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentStoreFactory.Create(new DataAgentStoreOptions(
                ProviderName: "oracle",
                SqlitePath: "dataagent.sqlite",
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("Unsupported DataAgent store provider"));
    }

    [Test]
    public void CreateRejectsPostgresWithoutConnectionString()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentStoreFactory.Create(new DataAgentStoreOptions(
                ProviderName: "postgres",
                SqlitePath: string.Empty,
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("ALIFE_DATAAGENT_POSTGRES_CONNECTION"));
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentStoreFactoryTests" -v:minimal
```

Expected: fail because `DataAgentStoreFactory` and `DataAgentStoreOptions` do not exist.

- [ ] **Step 3: Add provider options and factory**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentStoreFactory.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentStoreOptions(
    string ProviderName,
    string SqlitePath,
    string PostgresConnectionString);

public static class DataAgentStoreFactory
{
    public static IDataAgentStore Create(DataAgentStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string provider = string.IsNullOrWhiteSpace(options.ProviderName)
            ? "sqlite"
            : options.ProviderName.Trim().ToLowerInvariant();

        return provider switch
        {
            "sqlite" => CreateSqlite(options.SqlitePath),
            "postgres" => CreatePostgres(options.PostgresConnectionString),
            _ => throw new InvalidOperationException($"Unsupported DataAgent store provider: {options.ProviderName}")
        };
    }

    public static DataAgentStoreOptions FromEnvironment(string defaultSqlitePath)
    {
        return new DataAgentStoreOptions(
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER") ?? string.Empty,
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH") ?? defaultSqlitePath,
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_CONNECTION") ?? string.Empty);
    }

    static IDataAgentStore CreateSqlite(string sqlitePath)
    {
        if (string.IsNullOrWhiteSpace(sqlitePath))
            throw new InvalidOperationException("ALIFE_DATAAGENT_SQLITE_PATH is required when DataAgent sqlite store is selected.");

        return new SqliteDataAgentStore(sqlitePath);
    }

    static IDataAgentStore CreatePostgres(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ALIFE_DATAAGENT_POSTGRES_CONNECTION is required when DataAgent postgres store is selected.");

        return new PostgresDataAgentStore(connectionString);
    }
}
```

This will not compile until `PostgresDataAgentStore` exists in Task 4. For this task, add a temporary minimal provider file in Step 4 and replace it with the real implementation in Task 4.

- [ ] **Step 4: Add the minimal PostgreSQL provider shell**

Create `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class PostgresDataAgentStore(string connectionString) : IDataAgentStore
{
    public string ProviderName => "postgres";

    public void Initialize() => throw NotImplemented();
    public void ImportFixtures() => throw NotImplemented();
    public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql) => throw NotImplemented();
    public void RecordAccepted(DataAgentAcceptedAuditInput input) => throw NotImplemented();
    public void RecordRejected(DataAgentRejectedAuditInput input) => throw NotImplemented();
    public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit() => throw NotImplemented();
    public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record) => throw NotImplemented();
    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit() => throw NotImplemented();

    static NotSupportedException NotImplemented()
    {
        return new NotSupportedException("PostgreSQL DataAgent store implementation is added in the V2 provider task.");
    }
}
```

- [ ] **Step 5: Wire DataAgentModuleService through the factory**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`, replace direct SQLite setup:

```csharp
string databasePath = Path.Combine(AppContext.BaseDirectory, "DataAgent", "dataagent.sqlite");
DataAgentSchemaInitializer.Initialize(databasePath);
DataAgentFixtureImporter.Import(databasePath);

DataAgentService service = new(databasePath);
```

with:

```csharp
string databasePath = Path.Combine(AppContext.BaseDirectory, "DataAgent", "dataagent.sqlite");
IDataAgentStore store = DataAgentStoreFactory.Create(DataAgentStoreFactory.FromEnvironment(databasePath));
store.Initialize();
store.ImportFixtures();

DataAgentService service = new(store);
```

Keep analysis-session storage unchanged:

```csharp
InMemoryDataAgentAnalysisSessionStore analysisSessionStore = new();
DataAgentAnalysisService analysisService = new(service, analysisSessionStore);
```

- [ ] **Step 6: Add module source marker assertions**

In `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`, add marker assertions to the existing module wiring test or add:

```csharp
[Test]
public void AwakeUsesConfiguredDataAgentStoreBoundary()
{
    string source = ReadModuleSource();

    Assert.Multiple(() =>
    {
        Assert.That(source, Does.Contain("IDataAgentStore"));
        Assert.That(source, Does.Contain("DataAgentStoreFactory.Create"));
        Assert.That(source, Does.Contain("store.Initialize()"));
        Assert.That(source, Does.Contain("store.ImportFixtures()"));
        Assert.That(source, Does.Contain("new(store)"));
    });
}
```

- [ ] **Step 7: Run focused tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentStoreFactoryTests|FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: focused tests pass.

- [ ] **Step 8: Commit provider selection and module wiring**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentStoreFactory.cs sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs Tests/Alife.Test.DataAgent/DataAgentStoreFactoryTests.cs Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs
git commit -m "Select DataAgent store provider through factory"
```

---

### Task 4: PostgreSQL Provider And Environment-Gated Live Tests

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentPostgresStoreTests.cs`

- [ ] **Step 1: Add the Npgsql package**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" add sources\Alife.Function\Alife.Function.DataAgent\Alife.Function.DataAgent.csproj package Npgsql
```

Expected: the DataAgent project contains a new `PackageReference Include="Npgsql"` entry. Keep the resolved version that the .NET CLI writes.

- [ ] **Step 2: Write environment-gated PostgreSQL tests**

Create `Tests/Alife.Test.DataAgent/DataAgentPostgresStoreTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentPostgresStoreTests
{
    [Test]
    public void LivePostgresStoreTestIsSkippedWithoutConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Pass("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set; live PostgreSQL test skipped.");

        Assert.That(connectionString, Is.Not.Empty);
    }

    [Test]
    public void LivePostgresStoreInitializesImportsFixturesAndExecutesReadOnlyQuery()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        IDataAgentStore store = new PostgresDataAgentStore(connectionString);
        store.Initialize();
        store.ImportFixtures();

        DataAgentQueryResult result = store.Query(new DataAgentCompiledSql(
            "SELECT path, title FROM document_index ORDER BY id LIMIT 10",
            []));

        Assert.Multiple(() =>
        {
            Assert.That(store.ProviderName, Is.EqualTo("postgres"));
            Assert.That(result.Rows, Is.Not.Empty);
            Assert.That(result.Rows[0].Keys, Does.Contain("path"));
            Assert.That(result.Rows[0].Keys, Does.Contain("title"));
        });
    }
}
```

- [ ] **Step 3: Run the PostgreSQL tests and verify default skip behavior**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentPostgresStoreTests" -v:minimal
```

Expected without `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`: one pass for the explicit skip-contract test and one ignored live integration test.

- [ ] **Step 4: Replace the PostgreSQL provider shell**

Replace `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs` with:

```csharp
using Npgsql;

namespace Alife.Function.DataAgent;

public sealed class PostgresDataAgentStore(string connectionString) : IDataAgentStore
{
    public string ProviderName => "postgres";

    public void Initialize()
    {
        using NpgsqlConnection connection = Open();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS engineering_gate (
                id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                name TEXT NOT NULL,
                category TEXT NOT NULL,
                required BOOLEAN NOT NULL,
                status TEXT NOT NULL,
                evidence_path TEXT NOT NULL,
                last_checked_at TEXT NOT NULL,
                source TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS runtime_readiness_check (
                id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                capability TEXT NOT NULL,
                account TEXT NOT NULL,
                endpoint TEXT NOT NULL,
                status TEXT NOT NULL,
                required BOOLEAN NOT NULL,
                failure_reason TEXT NOT NULL,
                last_checked_at TEXT NOT NULL,
                evidence_path TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS module_capability (
                id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                module_name TEXT NOT NULL,
                capability_name TEXT NOT NULL,
                required BOOLEAN NOT NULL,
                status TEXT NOT NULL,
                test_project TEXT NOT NULL,
                evidence_path TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS test_run (
                id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                suite_name TEXT NOT NULL,
                passed INTEGER NOT NULL,
                failed INTEGER NOT NULL,
                skipped INTEGER NOT NULL,
                total INTEGER NOT NULL,
                ran_at TEXT NOT NULL,
                command TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS document_index (
                id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                path TEXT NOT NULL,
                doc_type TEXT NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                tags TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS query_audit (
                id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                question TEXT NOT NULL,
                dataset TEXT NOT NULL,
                query_plan_json TEXT NOT NULL,
                generated_sql TEXT NOT NULL,
                validated BOOLEAN NOT NULL,
                rejected_reason TEXT NOT NULL,
                row_count INTEGER NOT NULL,
                elapsed_ms BIGINT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tool_broker_audit (
                id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                session_id TEXT NOT NULL,
                tool_name TEXT NOT NULL,
                allowed BOOLEAN NOT NULL,
                reason_code TEXT NOT NULL,
                reason TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void ImportFixtures()
    {
        using NpgsqlConnection connection = Open();
        Execute(connection, "DELETE FROM engineering_gate");
        Execute(connection, "DELETE FROM runtime_readiness_check");
        Execute(connection, "DELETE FROM module_capability");
        Execute(connection, "DELETE FROM test_run");
        Execute(connection, "DELETE FROM document_index");

        string timestamp = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero).ToString("O");
        Execute(connection, "INSERT INTO engineering_gate (name, category, required, status, evidence_path, last_checked_at, source) VALUES (@name, @category, @required, @status, @evidence_path, @last_checked_at, @source)",
            new NpgsqlParameter("@name", "Runtime readiness script"),
            new NpgsqlParameter("@category", "Harness"),
            new NpgsqlParameter("@required", true),
            new NpgsqlParameter("@status", "passed"),
            new NpgsqlParameter("@evidence_path", "tools/check-qchat-runtime-readiness.ps1"),
            new NpgsqlParameter("@last_checked_at", timestamp),
            new NpgsqlParameter("@source", "fixture"));
        Execute(connection, "INSERT INTO document_index (path, doc_type, title, summary, tags, updated_at) VALUES (@path, @doc_type, @title, @summary, @tags, @updated_at)",
            new NpgsqlParameter("@path", "docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md"),
            new NpgsqlParameter("@doc_type", "spec"),
            new NpgsqlParameter("@title", "DataAgent NL2SQL Design"),
            new NpgsqlParameter("@summary", "DataAgent provides governed NL2SQL analytics over local Alife engineering evidence."),
            new NpgsqlParameter("@tags", "dataagent,nl2sql,sqlite,postgres"),
            new NpgsqlParameter("@updated_at", timestamp));
    }

    public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
    {
        ArgumentNullException.ThrowIfNull(compiledSql);
        using NpgsqlConnection connection = Open();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = compiledSql.Sql;
        command.CommandTimeout = 5;
        foreach (DataAgentSqlParameter parameter in compiledSql.Parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);

        using NpgsqlDataReader reader = command.ExecuteReader();
        List<IReadOnlyDictionary<string, object?>> rows = [];
        while (reader.Read())
        {
            Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return new DataAgentQueryResult(rows);
    }

    public void RecordAccepted(DataAgentAcceptedAuditInput input)
    {
        InsertAudit(input.Question, input.Dataset, input.QueryPlanJson, input.GeneratedSql, true, string.Empty, input.RowCount, input.Elapsed);
    }

    public void RecordRejected(DataAgentRejectedAuditInput input)
    {
        InsertAudit(input.Question, input.Dataset, input.QueryPlanJson, input.GeneratedSql, false, input.RejectedReason, 0, input.Elapsed);
    }

    public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit()
    {
        using NpgsqlConnection connection = Open();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT question, dataset, query_plan_json, generated_sql, validated, rejected_reason, row_count, elapsed_ms, created_at FROM query_audit ORDER BY id ASC";
        using NpgsqlDataReader reader = command.ExecuteReader();
        List<DataAgentAuditRecord> records = [];
        while (reader.Read())
        {
            records.Add(new DataAgentAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetString(5),
                reader.GetInt32(6),
                TimeSpan.FromMilliseconds(reader.GetInt64(7)),
                DateTimeOffset.Parse(reader.GetString(8))));
        }

        return records;
    }

    public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
    {
        using NpgsqlConnection connection = Open();
        Execute(connection, "INSERT INTO tool_broker_audit (session_id, tool_name, allowed, reason_code, reason, created_at) VALUES (@session_id, @tool_name, @allowed, @reason_code, @reason, @created_at)",
            new NpgsqlParameter("@session_id", record.SessionId),
            new NpgsqlParameter("@tool_name", record.ToolName),
            new NpgsqlParameter("@allowed", record.Allowed),
            new NpgsqlParameter("@reason_code", record.ReasonCode),
            new NpgsqlParameter("@reason", record.Reason),
            new NpgsqlParameter("@created_at", record.CreatedAt.UtcDateTime.ToString("O")));
    }

    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit()
    {
        using NpgsqlConnection connection = Open();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT session_id, tool_name, allowed, reason_code, reason, created_at FROM tool_broker_audit ORDER BY id ASC";
        using NpgsqlDataReader reader = command.ExecuteReader();
        List<DataAgentToolBrokerAuditRecord> records = [];
        while (reader.Read())
        {
            records.Add(new DataAgentToolBrokerAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return records;
    }

    NpgsqlConnection Open()
    {
        NpgsqlConnection connection = new(connectionString);
        connection.Open();
        return connection;
    }

    void InsertAudit(string question, string dataset, string queryPlanJson, string generatedSql, bool validated, string rejectedReason, int rowCount, TimeSpan elapsed)
    {
        using NpgsqlConnection connection = Open();
        Execute(connection, "INSERT INTO query_audit (question, dataset, query_plan_json, generated_sql, validated, rejected_reason, row_count, elapsed_ms, created_at) VALUES (@question, @dataset, @query_plan_json, @generated_sql, @validated, @rejected_reason, @row_count, @elapsed_ms, @created_at)",
            new NpgsqlParameter("@question", question),
            new NpgsqlParameter("@dataset", dataset),
            new NpgsqlParameter("@query_plan_json", queryPlanJson),
            new NpgsqlParameter("@generated_sql", generatedSql),
            new NpgsqlParameter("@validated", validated),
            new NpgsqlParameter("@rejected_reason", rejectedReason),
            new NpgsqlParameter("@row_count", rowCount),
            new NpgsqlParameter("@elapsed_ms", checked((long)elapsed.TotalMilliseconds)),
            new NpgsqlParameter("@created_at", DateTimeOffset.UtcNow.ToString("O")));
    }

    static void Execute(NpgsqlConnection connection, string commandText, params NpgsqlParameter[] parameters)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (NpgsqlParameter parameter in parameters)
            command.Parameters.Add(parameter);
        command.ExecuteNonQuery();
    }
}
```

- [ ] **Step 5: Run PostgreSQL-focused and DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentPostgresStoreTests|FullyQualifiedName~DataAgentStoreFactoryTests|FullyQualifiedName~DataAgentStoreContractTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass. Without `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`, the live PostgreSQL test is ignored and the default suite still passes.

- [ ] **Step 6: Commit PostgreSQL provider**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs Tests/Alife.Test.DataAgent/DataAgentPostgresStoreTests.cs
git commit -m "Add DataAgent PostgreSQL store provider"
```

---

### Task 5: Readiness Gates For V2 Store Boundary

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV20ReadinessTests.cs`

- [ ] **Step 1: Write failing V2 readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV20ReadinessTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV20ReadinessTests
{
    static readonly string[] RequiredChecks =
    [
        "DataAgentStoreBoundaryPresent",
        "SqliteStoreCompatibilityPresent",
        "PostgresStoreProviderPresent",
        "PostgresLiveTestsEnvironmentGated",
        "DataAgentServiceUsesStoreBoundary"
    ];

    [Test]
    public void CoreReadinessIncludesV20StoreBoundaryChecks()
    {
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(NewDatabasePath());
        string[] names = checks.Select(check => check.Name).ToArray();

        foreach (string checkName in RequiredChecks)
            Assert.That(names, Does.Contain(checkName));
    }

    [Test]
    public void StaticReadinessScriptContainsV20StoreBoundaryMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
                Assert.That(script, Does.Contain(checkName));
            Assert.That(script, Does.Contain("IDataAgentStore"));
            Assert.That(script, Does.Contain("SqliteDataAgentStore"));
            Assert.That(script, Does.Contain("PostgresDataAgentStore"));
            Assert.That(script, Does.Contain("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION"));
        });
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v20-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }
}
```

- [ ] **Step 2: Run readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV20ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: fail because V2 store boundary readiness checks are not yet present.

- [ ] **Step 3: Add runtime readiness checks**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, add runtime checks that prove:

```csharp
checks.Add(typeof(IDataAgentStore).IsInterface &&
           typeof(SqliteDataAgentStore).GetInterface(nameof(IDataAgentStore)) is not null &&
           typeof(PostgresDataAgentStore).GetInterface(nameof(IDataAgentStore)) is not null
    ? Pass("DataAgentStoreBoundaryPresent", "DataAgent provider-neutral store boundary exists")
    : Fail("DataAgentStoreBoundaryPresent", "store boundary types missing"));

IDataAgentStore readinessStore = new SqliteDataAgentStore(databasePath);
checks.Add(string.Equals(readinessStore.ProviderName, "sqlite", StringComparison.Ordinal) &&
           readinessStore.Query(new DataAgentCompiledSql("SELECT path FROM document_index LIMIT 1", [])).Rows.Count >= 0
    ? Pass("SqliteStoreCompatibilityPresent", "SQLite store remains default-compatible")
    : Fail("SqliteStoreCompatibilityPresent", "SQLite store query failed"));

checks.Add(string.Equals(new PostgresDataAgentStore("Host=localhost;Database=alife_readiness;Username=alife;Password=alife").ProviderName, "postgres", StringComparison.Ordinal)
    ? Pass("PostgresStoreProviderPresent", "PostgreSQL store provider type exists")
    : Fail("PostgresStoreProviderPresent", "PostgreSQL provider missing"));

checks.Add(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION"))
    ? Pass("PostgresLiveTestsEnvironmentGated", "PostgreSQL live tests are environment-gated")
    : Pass("PostgresLiveTestsEnvironmentGated", "PostgreSQL live test connection is explicitly configured"));

DataAgentAnswer storeBoundaryAnswer = new DataAgentService(
    readinessStore,
    new FixedPlanner(new DataAgentQueryPlan(
        "engineering_gate",
        "store_boundary_readiness",
        ["name", "status", "evidence_path"],
        [new DataAgentFilter("required", "=", true)],
        [],
        20))).Answer("force store boundary readiness");
checks.Add(storeBoundaryAnswer.Validated &&
           storeBoundaryAnswer.Context.Contains("dataset=engineering_gate", StringComparison.Ordinal)
    ? Pass("DataAgentServiceUsesStoreBoundary", "DataAgentService accepted an injected IDataAgentStore")
    : Fail("DataAgentServiceUsesStoreBoundary", storeBoundaryAnswer.Context));
```

- [ ] **Step 4: Add script readiness checks**

In `tools/check-dataagent-readiness.ps1`, add required checks in a `Store` group:

```powershell
New-Check -Group "Store" -Name "DataAgentStoreBoundaryPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs" @("IDataAgentStore", "DataAgentAcceptedAuditInput", "DataAgentRejectedAuditInput")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs" @("IDataAgentStore", "store.Query", "store.RecordAccepted", "store.RecordRejected"))) -Detail "provider-neutral DataAgent store boundary markers"
New-Check -Group "Store" -Name "SqliteStoreCompatibilityPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs" @("SqliteDataAgentStore", "DataAgentSchemaInitializer.Initialize", "DataAgentFixtureImporter.Import", "DataAgentQueryExecutor", "DataAgentAuditLog", "DataAgentToolBrokerAuditLog")) -Detail "SQLite store compatibility markers"
New-Check -Group "Store" -Name "PostgresStoreProviderPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs" @("PostgresDataAgentStore", "NpgsqlConnection", "tool_broker_audit", "query_audit")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj" @("Npgsql"))) -Detail "PostgreSQL store provider markers"
New-Check -Group "Store" -Name "PostgresLiveTestsEnvironmentGated" -Passed (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentPostgresStoreTests.cs" @("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION", "Assert.Ignore", "LivePostgresStoreInitializesImportsFixturesAndExecutesReadOnlyQuery")) -Detail "PostgreSQL live tests environment gate"
New-Check -Group "Store" -Name "DataAgentServiceUsesStoreBoundary" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs" @("IDataAgentStore", "new SqliteDataAgentStore", "store.Query", "store.RecordAccepted", "store.RecordRejected")) -Detail "DataAgentService store boundary usage"
```

Add `"Store"` to the group output order:

```powershell
foreach ($group in @("Core", "Schema", "Safety", "Query", "Context", "Planner", "Tool", "ToolBroker", "Store", "Analysis"))
```

- [ ] **Step 5: Add QChat engineering map store boundary check**

In `tools/check-qchat-engineering-map.ps1`, add:

```powershell
Add-Check -Group "Harness" -Name "DataAgent store provider boundary" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataAgentStoreBoundaryPresent", "SqliteStoreCompatibilityPresent", "PostgresStoreProviderPresent", "PostgresLiveTestsEnvironmentGated", "DataAgentServiceUsesStoreBoundary")
```

- [ ] **Step 6: Update DataAgent readiness summary expectations**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update the expected core check count and script summary by adding five required checks to the current master totals:

```csharp
Assert.That(checks, Has.Count.EqualTo(31));
"  Summary: 45 required passed, 0 required missing"
```

Change them to:

```csharp
Assert.That(checks, Has.Count.EqualTo(36));
"  Summary: 50 required passed, 0 required missing"
```

Also assert the names contain:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentStoreBoundaryPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("SqliteStoreCompatibilityPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresStoreProviderPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresLiveTestsEnvironmentGated"));
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentServiceUsesStoreBoundary"));
```

- [ ] **Step 7: Run readiness tests and scripts**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV20ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected: tests pass. DataAgent readiness reports `50 required passed, 0 required missing`. QChat engineering map reports `43 required passed, 0 required missing, 0 optional present, 0 optional missing`.

- [ ] **Step 8: Commit readiness gates**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV20ReadinessTests.cs
git commit -m "Require DataAgent V2 store boundary readiness"
```

---

### Task 6: Full Verification And Push

**Files:**

- Verify all V2.0 store-boundary changes.
- Push branch to `alife-byastralfox`.

- [ ] **Step 1: Run DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass. PostgreSQL live test is ignored when `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is not set.

- [ ] **Step 2: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

Expected: all projects pass. Existing live tests remain skipped unless live dependencies are intentionally enabled.

- [ ] **Step 3: Run required readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected: both scripts exit with code 0 and report `0 required missing`.

- [ ] **Step 4: Confirm V2.0 scope boundaries**

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.DataAgent\*.cs -Pattern "LangGraph|Python sidecar|Supervisor"
Select-String -Path sources\Alife.Function\Alife.Function.QChat\*.cs -Pattern "PostgresDataAgentStore|Npgsql"
```

Expected: no matches. PostgreSQL code stays inside `Alife.Function.DataAgent` store provider files.

- [ ] **Step 5: Commit any final documentation updates**

If readiness counts or branch notes changed during implementation, update the plan or roadmap and commit:

```powershell
git status --short
git add docs/superpowers/plans/2026-06-29-dataagent-v2-store-boundary.md docs/superpowers/specs/2026-06-29-dataagent-v2-store-boundary-design.md
git commit -m "Document DataAgent V2 store boundary rollout"
```

When `git status --short` shows no documentation changes, skip this commit step.

- [ ] **Step 6: Push the V2.0 branch**

Run:

```powershell
git status --short --branch
git push alife-byastralfox dataagent-v2-store-boundary
git ls-remote alife-byastralfox refs/heads/dataagent-v2-store-boundary
```

Expected: remote `refs/heads/dataagent-v2-store-boundary` points to the latest local commit.

## Execution Notes

Recommended branch:

```text
dataagent-v2-store-boundary
```

Do not implement directly on `master`. Use an isolated worktree before executing this plan.

PostgreSQL live validation is optional by environment and must not block default local development:

```text
ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION
```

V2.0 is complete only when SQLite default compatibility, PostgreSQL provider availability, and required readiness gates all pass together.
