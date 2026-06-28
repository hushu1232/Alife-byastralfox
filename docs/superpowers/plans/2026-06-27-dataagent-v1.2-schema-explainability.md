# DataAgent v1.2 Schema Explainability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add DataAgent schema introspection and explainable planner metadata while preserving the v1.1 NL2SQL safety chain and XML tool runtime path.

**Architecture:** Keep `DataAgentCatalog` as the approved schema source, add a SQLite-backed introspector that proves the initialized database matches it, and change planner output from a bare `DataAgentQueryPlan` to a `DataAgentQueryPlanEnvelope` containing the plan plus explanation metadata. `DataAgentService` still owns validation, SQL compilation, SQL safety, execution, audit, and context publishing.

**Tech Stack:** .NET 9, C#, NUnit, Microsoft.Data.Sqlite, existing Alife FunctionCaller XML tool registration, PowerShell readiness scripts.

---

## File Map

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaIntrospector.cs`
  - Builds `DataAgentSchemaSnapshot` from `DataAgentCatalog` and SQLite `PRAGMA table_info`.
- Create: `Tests/Alife.Test.DataAgent/DataAgentSchemaIntrospectorTests.cs`
  - Proves schema snapshot coverage and mismatch detection.
- Create: `Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs`
  - Proves planner metadata appears in accepted/rejected context and is sanitized.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`
  - Adds `DataAgentQueryPlanEnvelope` and `DataAgentPlannerExplanation`.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs`
  - Changes return type to `DataAgentQueryPlanEnvelope`.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DeterministicDataAgentQueryPlanner.cs`
  - Returns envelopes and stable explanation metadata.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
  - Uses envelope, stores planner explanation in `DataAgentAnswer`, and passes it to context builders.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`
  - Adds planner metadata fields to accepted and rejected context blocks.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds `SchemaSnapshotAvailable`, `CatalogMatchesSqliteSchema`, and `PlannerExplanationInContext` checks.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Adds static required markers for v1.2.
- Modify existing tests:
  - `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentServiceTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentToolHandlerTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentV11ReadinessTests.cs`

---

### Task 1: Schema Introspection

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaIntrospector.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentSchemaIntrospectorTests.cs`

- [ ] **Step 1: Write failing schema introspector tests**

Create `Tests/Alife.Test.DataAgent/DataAgentSchemaIntrospectorTests.cs`:

```csharp
using Alife.Function.DataAgent;
using Microsoft.Data.Sqlite;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentSchemaIntrospectorTests
{
    [Test]
    public void SnapshotIncludesEveryDefaultCatalogDataset()
    {
        string databasePath = CreateDatabasePath();

        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(
            DataAgentCatalog.CreateDefault(),
            databasePath).Inspect();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("engineering_gate"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("runtime_readiness_check"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("module_capability"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("test_run"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("document_index"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("query_audit"));
        });
    }

    [Test]
    public void InitializedSqliteSchemaMatchesDefaultCatalog()
    {
        string databasePath = CreateDatabasePath();

        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(
            DataAgentCatalog.CreateDefault(),
            databasePath).Inspect();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CatalogMatchesDatabase, Is.True);
            Assert.That(snapshot.Datasets, Has.All.Matches<DataAgentDatasetSchema>(dataset => dataset.ExistsInDatabase));
            Assert.That(snapshot.Datasets, Has.All.Matches<DataAgentDatasetSchema>(dataset => dataset.FieldsMatch));
        });
    }

    [Test]
    public void MissingCatalogTableIsReportedAsMismatch()
    {
        string databasePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "dataagent-schema-introspector-tests",
            $"{Guid.NewGuid():N}.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE engineering_gate (id INTEGER PRIMARY KEY, name TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(
            DataAgentCatalog.CreateDefault(),
            databasePath).Inspect();

        DataAgentDatasetSchema engineeringGate = snapshot.Datasets.Single(dataset => dataset.Name == "engineering_gate");
        DataAgentDatasetSchema documentIndex = snapshot.Datasets.Single(dataset => dataset.Name == "document_index");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CatalogMatchesDatabase, Is.False);
            Assert.That(engineeringGate.ExistsInDatabase, Is.True);
            Assert.That(engineeringGate.FieldsMatch, Is.False);
            Assert.That(documentIndex.ExistsInDatabase, Is.False);
            Assert.That(documentIndex.FieldsMatch, Is.False);
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-schema-introspector-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        return databasePath;
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentSchemaIntrospectorTests -v:minimal
```

Expected: FAIL at compile time because `DataAgentSchemaSnapshot`, `DataAgentDatasetSchema`, and `DataAgentSchemaIntrospector` do not exist.

- [ ] **Step 3: Implement schema introspector**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaIntrospector.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed class DataAgentSchemaIntrospector(DataAgentCatalog catalog, string databasePath)
{
    public DataAgentSchemaSnapshot Inspect()
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        List<DataAgentDatasetSchema> datasets = [];

        foreach (DataAgentDataset dataset in catalog.Datasets)
        {
            IReadOnlyList<string> databaseFields = ReadDatabaseFields(connection, dataset.Name);
            bool exists = databaseFields.Count > 0;
            bool fieldsMatch = exists && dataset.Fields.SetEquals(databaseFields);
            datasets.Add(new DataAgentDatasetSchema(
                dataset.Name,
                dataset.Fields.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                databaseFields,
                exists,
                fieldsMatch));
        }

        return new DataAgentSchemaSnapshot(
            datasets,
            datasets.All(dataset => dataset.ExistsInDatabase && dataset.FieldsMatch));
    }

    static IReadOnlyList<string> ReadDatabaseFields(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";

        using SqliteDataReader reader = command.ExecuteReader();
        List<string> fields = [];
        while (reader.Read())
            fields.Add(reader.GetString(reader.GetOrdinal("name")));

        return fields.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

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

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs` to expose datasets:

```csharp
public IReadOnlyList<DataAgentDataset> Datasets => datasets.Values
    .OrderBy(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase)
    .ToArray();
```

- [ ] **Step 4: Run tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentSchemaIntrospectorTests -v:minimal
```

Expected: PASS for all `DataAgentSchemaIntrospectorTests`.

- [ ] **Step 5: Commit schema introspection**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaIntrospector.cs Tests/Alife.Test.DataAgent/DataAgentSchemaIntrospectorTests.cs
git commit -m "feat: add DataAgent schema introspection"
```

---

### Task 2: Planner Envelope And Explanation

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DeterministicDataAgentQueryPlanner.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs`

- [ ] **Step 1: Write failing planner explanation tests**

Modify `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs` so planner tests use envelopes. Add these tests:

```csharp
[Test]
public void RuntimeReadinessRequiredQuestionIncludesHighConfidenceExplanation()
{
    DeterministicDataAgentQueryPlanner planner = new();

    DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
        "Which runtime readiness gate is required?",
        "developer",
        "en-US",
        false));

    Assert.Multiple(() =>
    {
        Assert.That(envelope.Plan.Dataset, Is.EqualTo("engineering_gate"));
        Assert.That(envelope.Plan.Intent, Is.EqualTo("find_runtime_readiness_required_evidence"));
        Assert.That(envelope.Explanation.PlannerName, Is.EqualTo(nameof(DeterministicDataAgentQueryPlanner)));
        Assert.That(envelope.Explanation.Dataset, Is.EqualTo(envelope.Plan.Dataset));
        Assert.That(envelope.Explanation.Intent, Is.EqualTo(envelope.Plan.Intent));
        Assert.That(envelope.Explanation.Confidence, Is.EqualTo("high"));
        Assert.That(envelope.Explanation.Signals, Does.Contain("runtime"));
        Assert.That(envelope.Explanation.Signals, Does.Contain("readiness"));
        Assert.That(envelope.Explanation.Signals, Does.Contain("required"));
        Assert.That(envelope.Explanation.Reason, Does.Contain("runtime readiness"));
    });
}

[Test]
public void UnknownProjectStateFallbackIncludesLowConfidenceExplanation()
{
    DeterministicDataAgentQueryPlanner planner = new();

    DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
        "What project state still needs attention?",
        "developer",
        "en-US",
        false));

    Assert.Multiple(() =>
    {
        Assert.That(envelope.Plan.Dataset, Is.EqualTo("engineering_gate"));
        Assert.That(envelope.Plan.Intent, Is.EqualTo("find_missing_required_gates"));
        Assert.That(envelope.Explanation.Confidence, Is.EqualTo("low"));
        Assert.That(envelope.Explanation.Signals, Does.Contain("fallback"));
        Assert.That(envelope.Explanation.Reason, Does.Contain("fallback"));
    });
}
```

Update existing assertions from `plan.Dataset` to `envelope.Plan.Dataset`.

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentPlannerTests -v:minimal
```

Expected: FAIL at compile time because `DataAgentQueryPlanEnvelope` and `DataAgentPlannerExplanation` do not exist and `IDataAgentQueryPlanner.Plan` still returns `DataAgentQueryPlan`.

- [ ] **Step 3: Add envelope records and update planner interface**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`:

```csharp
public sealed record DataAgentQueryPlanEnvelope(
    DataAgentQueryPlan Plan,
    DataAgentPlannerExplanation Explanation);

public sealed record DataAgentPlannerExplanation(
    string PlannerName,
    string Intent,
    string Dataset,
    string Confidence,
    IReadOnlyList<string> Signals,
    string Reason);
```

Modify `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs`:

```csharp
public interface IDataAgentQueryPlanner
{
    DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request);
}
```

- [ ] **Step 4: Update deterministic planner**

Change `DeterministicDataAgentQueryPlanner.Plan` to return `DataAgentQueryPlanEnvelope`. Add a private helper:

```csharp
static DataAgentQueryPlanEnvelope Envelope(
    DataAgentQueryPlan plan,
    string confidence,
    IReadOnlyList<string> signals,
    string reason)
{
    return new DataAgentQueryPlanEnvelope(
        plan,
        new DataAgentPlannerExplanation(
            nameof(DeterministicDataAgentQueryPlanner),
            plan.Intent,
            plan.Dataset,
            confidence,
            signals,
            reason));
}
```

Wrap each existing return:

```csharp
return Envelope(
    new DataAgentQueryPlan(
        "engineering_gate",
        "find_runtime_readiness_required_evidence",
        ["name", "status", "evidence_path"],
        [new DataAgentFilter("name", "contains", "Runtime readiness")],
        [],
        10),
    "high",
    ["runtime", "readiness", "required"],
    "question mentions runtime readiness required evidence");
```

Use these explanation values:

```text
find_qchat_tts_readiness: high, signals ["readiness", "tts", "vision"], reason "question mentions QChat TTS or vision readiness"
find_runtime_readiness_required_evidence: high, signals ["runtime", "readiness", "required"], reason "question mentions runtime readiness required evidence"
latest_test_run_summary: high, signals ["test", "result"], reason "question asks for latest test results"
find_dataagent_documents: high, signals ["dataagent", "nl2sql", "document"], reason "question asks for DataAgent or NL2SQL documentation"
find_missing_required_gates: low, signals ["fallback"], reason "fallback to missing required engineering gates"
```

- [ ] **Step 5: Run planner tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentPlannerTests -v:minimal
```

Expected: PASS for `DataAgentPlannerTests`.

- [ ] **Step 6: Commit planner envelope**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs sources/Alife.Function/Alife.Function.DataAgent/DeterministicDataAgentQueryPlanner.cs Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs
git commit -m "feat: explain DataAgent planner decisions"
```

---

### Task 3: Service, Context, And Tool Integration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentServiceTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentToolHandlerTests.cs`

- [ ] **Step 1: Write failing context provider tests**

Create `Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentContextProviderTests
{
    [Test]
    public void AcceptedContextIncludesPlannerMetadata()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent", "document"],
            "question asks for DataAgent documentation");

        string context = DataAgentContextProvider.Build(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            1,
            "DataAgent NL2SQL Design",
            new DataAgentQueryResult([
                new Dictionary<string, object?> { ["path"] = "docs/a.md", ["title"] = "A" }
            ]),
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("planner=DeterministicDataAgentQueryPlanner"));
            Assert.That(context, Does.Contain("planner_confidence=high"));
            Assert.That(context, Does.Contain("planner_reason=question asks for DataAgent documentation"));
            Assert.That(context, Does.Contain("planner_signals=dataagent, document"));
        });
    }

    [Test]
    public void RejectedContextIncludesPlannerMetadataAndReason()
    {
        DataAgentPlannerExplanation explanation = new(
            "FixedPlanner",
            "unsafe",
            "engineering_gate",
            "low",
            ["injected-test"],
            "test planner returned invalid operator");

        string context = DataAgentContextProvider.BuildRejected(
            "unsafe planner output",
            "engineering_gate",
            "unsupported_operator:starts_with",
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("sql_status=rejected"));
            Assert.That(context, Does.Contain("planner=FixedPlanner"));
            Assert.That(context, Does.Contain("planner_confidence=low"));
            Assert.That(context, Does.Contain("planner_reason=test planner returned invalid operator"));
            Assert.That(context, Does.Contain("planner_signals=injected-test"));
            Assert.That(context, Does.Contain("rejected_reason=unsupported_operator:starts_with"));
        });
    }

    [Test]
    public void PlannerMetadataIsSanitized()
    {
        DataAgentPlannerExplanation explanation = new(
            "FixedPlanner",
            "unsafe",
            "engineering_gate",
            "low",
            ["line\r\nbreak"],
            "reason\r\nwith newline");

        string context = DataAgentContextProvider.BuildRejected(
            "unsafe planner output",
            "engineering_gate",
            "unsupported_operator:starts_with",
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("planner_reason=reason  with newline"));
            Assert.That(context, Does.Contain("planner_signals=line  break"));
            Assert.That(context, Does.Not.Contain("reason\r\nwith"));
            Assert.That(context, Does.Not.Contain("line\r\nbreak"));
        });
    }
}
```

- [ ] **Step 2: Update service/tool tests for envelope helpers**

In `DataAgentServicePlannerInjectionTests`, change `FixedPlanner` to:

```csharp
sealed class FixedPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
{
    public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
    {
        return new DataAgentQueryPlanEnvelope(
            plan,
            new DataAgentPlannerExplanation(
                nameof(FixedPlanner),
                plan.Intent,
                plan.Dataset,
                "low",
                ["injected-test"],
                "test planner returned fixed query plan"));
    }
}
```

Add an assertion to `UsesInjectedPlanner`:

```csharp
Assert.That(answer.PlannerExplanation.PlannerName, Is.EqualTo("FixedPlanner"));
```

Add an assertion to `InvalidInjectedPlannerOutputIsRejectedAndAudited`:

```csharp
Assert.That(answer.Context, Does.Contain("planner=FixedPlanner"));
```

In `DataAgentToolHandlerTests`, add to `QueryPublishesContextWhenRuntimeCallbackIsProvided`:

```csharp
Assert.That(published.Single(), Does.Contain("planner_confidence="));
```

- [ ] **Step 3: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentContextProviderTests|DataAgentServicePlannerInjectionTests|DataAgentToolHandlerTests" -v:minimal
```

Expected: FAIL because `DataAgentContextProvider.Build` and `BuildRejected` do not accept planner explanations, `DataAgentAnswer` has no planner explanation property, and service still expects bare plans in some call sites.

- [ ] **Step 4: Update DataAgentService**

Modify `DataAgentService.Answer`:

```csharp
DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(question, "developer", "zh-CN", false));
DataAgentQueryPlan plan = envelope.Plan;
DataAgentPlannerExplanation explanation = envelope.Explanation;
string queryPlanJson = JsonSerializer.Serialize(plan);
```

Pass explanation to context:

```csharp
string context = DataAgentContextProvider.Build(question, plan.Dataset, compiled.Sql, result.Rows.Count, summary, result, explanation);
```

Change `Reject` signature and call sites:

```csharp
DataAgentAnswer Reject(
    string question,
    DataAgentQueryPlan plan,
    DataAgentPlannerExplanation explanation,
    string queryPlanJson,
    string reason,
    string generatedSql)
```

Use:

```csharp
string context = DataAgentContextProvider.BuildRejected(question, plan.Dataset, reason, explanation);
return new DataAgentAnswer(plan.Dataset, generatedSql, 0, summary, context, trueOrFalseValue, reason, explanation);
```

Extend `DataAgentAnswer`:

```csharp
public sealed record DataAgentAnswer(
    string Dataset,
    string Sql,
    int RowCount,
    string Summary,
    string Context,
    bool Validated,
    string RejectedReason,
    DataAgentPlannerExplanation PlannerExplanation);
```

- [ ] **Step 5: Update DataAgentContextProvider**

Change `Build` signature:

```csharp
public static string Build(
    string question,
    string dataset,
    string sql,
    int rowCount,
    string summary,
    DataAgentQueryResult result,
    DataAgentPlannerExplanation explanation)
```

Add after `sql_status=validated`:

```csharp
AppendPlannerMetadata(builder, explanation);
```

Change `BuildRejected` signature:

```csharp
public static string BuildRejected(
    string question,
    string dataset,
    string reason,
    DataAgentPlannerExplanation explanation)
```

Add after `sql_status=rejected`:

```csharp
AppendPlannerMetadata(builder, explanation);
```

Add helper:

```csharp
static void AppendPlannerMetadata(StringBuilder builder, DataAgentPlannerExplanation explanation)
{
    builder.AppendLine($"planner={Sanitize(explanation.PlannerName)}");
    builder.AppendLine($"planner_confidence={Sanitize(explanation.Confidence)}");
    builder.AppendLine($"planner_reason={Sanitize(explanation.Reason)}");
    builder.AppendLine($"planner_signals={Sanitize(string.Join(", ", explanation.Signals))}");
}
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentContextProviderTests|DataAgentServicePlannerInjectionTests|DataAgentToolHandlerTests" -v:minimal
```

Expected: PASS for the filtered tests.

- [ ] **Step 7: Commit service/context integration**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs Tests/Alife.Test.DataAgent/DataAgentServiceTests.cs Tests/Alife.Test.DataAgent/DataAgentToolHandlerTests.cs
git commit -m "feat: include DataAgent planner metadata in context"
```

---

### Task 4: Readiness Gate Upgrade

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV11ReadinessTests.cs`
- Create or modify: `Tests/Alife.Test.DataAgent/DataAgentV12ReadinessTests.cs`

- [ ] **Step 1: Write failing readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV12ReadinessTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV12ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesSchemaAndPlannerExplanationChecks()
    {
        string databasePath = CreateDatabasePath();

        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(checks.Single(check => check.Name == "SchemaSnapshotAvailable").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "CatalogMatchesSqliteSchema").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "PlannerExplanationInContext").Passed, Is.True);
        });
    }

    [Test]
    public void StaticReadinessScriptDeclaresV12Markers()
    {
        string script = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("SchemaSnapshotAvailable"));
            Assert.That(script, Does.Contain("CatalogMatchesSqliteSchema"));
            Assert.That(script, Does.Contain("PlannerExplanationInContext"));
            Assert.That(script, Does.Contain("DataAgentSchemaIntrospector"));
            Assert.That(script, Does.Contain("planner_confidence"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v12-readiness-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }
}
```

- [ ] **Step 2: Run readiness tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentV12ReadinessTests -v:minimal
```

Expected: FAIL because the three v1.2 readiness checks do not exist yet.

- [ ] **Step 3: Update runtime readiness**

In `DataAgentReadiness.CheckCore`, after fixture import, add:

```csharp
DataAgentSchemaSnapshot schemaSnapshot = new DataAgentSchemaIntrospector(
    DataAgentCatalog.CreateDefault(),
    databasePath).Inspect();

checks.Add(schemaSnapshot.Datasets.Count > 0
    ? Pass("SchemaSnapshotAvailable", $"{schemaSnapshot.Datasets.Count} datasets")
    : Fail("SchemaSnapshotAvailable", "schema snapshot is empty"));

checks.Add(schemaSnapshot.CatalogMatchesDatabase
    ? Pass("CatalogMatchesSqliteSchema", "catalog fields match sqlite schema")
    : Fail("CatalogMatchesSqliteSchema", "catalog fields do not match sqlite schema"));
```

After `answer` is created, add:

```csharp
checks.Add(answer.Context.Contains("planner_confidence=", StringComparison.Ordinal) &&
           answer.Context.Contains("planner_reason=", StringComparison.Ordinal) &&
           answer.PlannerExplanation.Signals.Count > 0
    ? Pass("PlannerExplanationInContext", answer.PlannerExplanation.Confidence)
    : Fail("PlannerExplanationInContext", answer.Context));
```

Update the nested `FixedPlanner` to return `DataAgentQueryPlanEnvelope` with `DataAgentPlannerExplanation`.

- [ ] **Step 4: Update static readiness script**

Modify `tools/check-dataagent-readiness.ps1`:

Add checks:

```powershell
New-Check -Group "Schema" -Name "SchemaSnapshotAvailable" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaIntrospector.cs" @("DataAgentSchemaSnapshot", "Inspect", "PRAGMA table_info")) -Detail "schema introspection markers"
New-Check -Group "Schema" -Name "CatalogMatchesSqliteSchema" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("CatalogMatchesSqliteSchema", "CatalogMatchesDatabase")) -Detail "catalog/sqlite schema match markers"
New-Check -Group "Planner" -Name "PlannerExplanationInContext" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs" @("planner_confidence", "planner_reason", "planner_signals")) -Detail "planner explanation context markers"
```

Update group output list:

```powershell
foreach ($group in @("Core", "Schema", "Safety", "Query", "Context", "Planner", "Tool")) {
```

- [ ] **Step 5: Run readiness tests and scripts to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentV12ReadinessTests|DataAgentV11ReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgentV12ReadinessTests: PASS
DataAgent readiness: Summary: 15 required passed, 0 required missing
QChat engineering map: Summary: 33 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 6: Commit readiness upgrade**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 Tests/Alife.Test.DataAgent/DataAgentV11ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV12ReadinessTests.cs
git commit -m "feat: require DataAgent v1.2 schema explainability"
```

---

### Task 5: Final Verification And Upload

**Files:**
- No production files unless earlier verification exposes a defect.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass, with 0 failed.

- [ ] **Step 2: Run solution build**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build Alife.slnx --no-restore -v:minimal
```

Expected: build succeeds with 0 errors. Existing CS0067 warnings in QChat test fakes may remain.

- [ ] **Step 3: Run full solution tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
```

Expected: 0 failed. Live/manual tests may remain skipped.

- [ ] **Step 4: Run readiness gates**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
git diff --check
```

Expected:

```text
DataAgent readiness: 15 required passed, 0 required missing
QChat engineering map: 33 required passed, 0 required missing
git diff --check: exit 0
```

- [ ] **Step 5: Commit any verification fixes**

If verification required fixes, commit only related files:

```powershell
git status --short --branch --untracked-files=all
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaIntrospector.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs sources/Alife.Function/Alife.Function.DataAgent/DeterministicDataAgentQueryPlanner.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentSchemaIntrospectorTests.cs Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs Tests/Alife.Test.DataAgent/DataAgentServiceTests.cs Tests/Alife.Test.DataAgent/DataAgentToolHandlerTests.cs Tests/Alife.Test.DataAgent/DataAgentV11ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV12ReadinessTests.cs tools/check-dataagent-readiness.ps1
git commit -m "fix: stabilize DataAgent v1.2 verification"
```

If no fixes are needed, skip this step.

- [ ] **Step 6: Upload to GitHub snapshot remote**

Use the established direct `Alife-byastralfox` upload path, not `origin`:

```powershell
git remote -v
git ls-remote alife-byastralfox refs/heads/master
```

Create an isolated upload worktree from current `alife-byastralfox/master`, sync tracked files from `D:\Alife`, commit with:

```text
Update Alife service snapshot
```

Push:

```powershell
git push alife-byastralfox HEAD:master
git ls-remote alife-byastralfox refs/heads/master
```

Expected: remote `refs/heads/master` points to the upload commit hash.

---

## Self-Review Checklist

- Spec coverage:
  - Schema introspection is covered by Task 1.
  - Planner explanation and envelope are covered by Task 2.
  - Context and runtime tool preservation are covered by Task 3.
  - Required readiness gates are covered by Task 4.
  - Final build/test/readiness/upload are covered by Task 5.
- Placeholder scan:
  - No task uses unresolved markers or copy-by-reference instructions.
  - Each code-modifying task includes concrete test and implementation snippets.
- Type consistency:
  - `IDataAgentQueryPlanner.Plan` returns `DataAgentQueryPlanEnvelope`.
  - `DataAgentQueryPlanEnvelope` contains `Plan` and `Explanation`.
  - `DataAgentAnswer` exposes `PlannerExplanation`.
  - Context provider signatures include `DataAgentPlannerExplanation`.
