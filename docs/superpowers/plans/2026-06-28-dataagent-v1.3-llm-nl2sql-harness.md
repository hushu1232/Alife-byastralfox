# DataAgent v1.3 LLM NL2SQL Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic, testable LLM NL2SQL harness for DataAgent without making live model access part of required correctness.

**Architecture:** Add a small LLM planner subsystem behind `IDataAgentQueryPlanner`. LLM output is strict JSON that becomes either a `DataAgentQueryPlanEnvelope` or a clarification request; invalid model output is discarded and falls back to `DeterministicDataAgentQueryPlanner`. `DataAgentService` keeps ownership of validation, SQL compilation, SQL safety, execution, audit, and context.

**Tech Stack:** .NET 9, C#, NUnit, `System.Text.Json`, Microsoft.Data.Sqlite, existing DataAgent QueryPlan/Safety/Audit chain, PowerShell readiness scripts.

---

## File Map

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`
  - Make `DataAgentQueryPlanEnvelope.Plan` nullable and add `DataAgentClarificationRequest`.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`
  - Add clarification context and optional result explanation line.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
  - Validate clarification envelopes, return `needs_clarification` before SQL compile, and add deterministic result explanations.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultExplainer.cs`
  - Stable natural-language explanation from deterministic inputs.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs`
  - Schema-aware strict JSON prompt builder and `DataAgentLlmPlannerPrompt`.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerResponseParser.cs`
  - Strict JSON parser and `DataAgentLlmPlannerResult`.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/ILlmDataAgentPlannerClient.cs`
  - Model boundary abstraction.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs`
  - `IDataAgentQueryPlanner` implementation using prompt/client/parser/fallback.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentPlannerSelector.cs`
  - Chooses deterministic, harness, or live planner by options.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add v1.3 runtime readiness checks.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Add v1.3 static readiness checks.
- Create tests:
  - `Tests/Alife.Test.DataAgent/DataAgentClarificationContextTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentResultExplainerTests.cs`
  - `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs`
  - `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerResponseParserTests.cs`
  - `Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentV13ReadinessTests.cs`
- Modify existing tests:
  - `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

---

### Task 1: Clarification Contract And Context

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentClarificationContextTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs`

- [ ] **Step 1: Write failing clarification tests**

Create `Tests/Alife.Test.DataAgent/DataAgentClarificationContextTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentClarificationContextTests
{
    [Test]
    public void BuildClarificationContextIncludesPlannerAndOptions()
    {
        DataAgentPlannerExplanation explanation = new(
            "LlmDataAgentQueryPlanner",
            "clarify_ambiguous_query",
            string.Empty,
            "low",
            ["ambiguous_time_range", "ambiguous_metric"],
            "question has no time range or metric");
        DataAgentClarificationRequest clarification = new(
            "Do you want the last 7 days, last 30 days, or all history?",
            ["last 7 days", "last 30 days", "all history"],
            "question does not specify metric or time range");

        string context = DataAgentContextProvider.BuildClarification(
            "How active has the project been recently?",
            clarification,
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("sql_status=needs_clarification"));
            Assert.That(context, Does.Contain("planner=LlmDataAgentQueryPlanner"));
            Assert.That(context, Does.Contain("planner_confidence=low"));
            Assert.That(context, Does.Contain("planner_signals=ambiguous_time_range, ambiguous_metric"));
            Assert.That(context, Does.Contain("clarification_question=Do you want the last 7 days, last 30 days, or all history?"));
            Assert.That(context, Does.Contain("clarification_options=last 7 days, last 30 days, all history"));
        });
    }

    [Test]
    public void ServiceReturnsClarificationWithoutSqlExecution()
    {
        string databasePath = CreateDatabasePath();
        DataAgentService service = new(databasePath, new ClarifyingPlanner());

        DataAgentAnswer answer = service.Answer("How active has the project been recently?");
        IReadOnlyList<DataAgentAuditRecord> audit = new DataAgentAuditLog(databasePath).ReadAll();

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.False);
            Assert.That(answer.Dataset, Is.Empty);
            Assert.That(answer.Sql, Is.Empty);
            Assert.That(answer.RowCount, Is.EqualTo(0));
            Assert.That(answer.RejectedReason, Is.EqualTo("needs_clarification"));
            Assert.That(answer.Context, Does.Contain("sql_status=needs_clarification"));
            Assert.That(answer.Context, Does.Contain("clarification_question="));
            Assert.That(audit, Has.Count.EqualTo(1));
            Assert.That(audit[0].Validated, Is.False);
            Assert.That(audit[0].GeneratedSql, Is.Empty);
            Assert.That(audit[0].RejectedReason, Is.EqualTo("needs_clarification"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-clarification-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }

    sealed class ClarifyingPlanner : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            return new DataAgentQueryPlanEnvelope(
                null,
                new DataAgentPlannerExplanation(
                    nameof(LlmDataAgentQueryPlanner),
                    "clarify_ambiguous_query",
                    string.Empty,
                    "low",
                    ["ambiguous_time_range", "ambiguous_metric"],
                    "question has no time range or metric"),
                new DataAgentClarificationRequest(
                    "Do you want the last 7 days, last 30 days, or all history?",
                    ["last 7 days", "last 30 days", "all history"],
                    "question does not specify metric or time range"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentClarificationContextTests -v:minimal
```

Expected: compile fails because `DataAgentClarificationRequest`, nullable envelope constructor, `BuildClarification`, and `LlmDataAgentQueryPlanner` do not exist.

- [ ] **Step 3: Add clarification contract**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentQueryPlan(
    string Dataset,
    string Intent,
    IReadOnlyList<string> Select,
    IReadOnlyList<DataAgentFilter> Filters,
    IReadOnlyList<DataAgentOrderBy> OrderBy,
    int Limit);

public sealed record DataAgentQueryPlanEnvelope(
    DataAgentQueryPlan? Plan,
    DataAgentPlannerExplanation Explanation,
    DataAgentClarificationRequest? Clarification = null);

public sealed record DataAgentPlannerExplanation(
    string PlannerName,
    string Intent,
    string Dataset,
    string Confidence,
    IReadOnlyList<string> Signals,
    string Reason);

public sealed record DataAgentClarificationRequest(
    string Question,
    IReadOnlyList<string> Options,
    string Reason);

public sealed record DataAgentFilter(string Field, string Operator, object? Value);

public sealed record DataAgentOrderBy(string Field, string Direction);
```

- [ ] **Step 4: Update deterministic-plan tests for nullable plan**

In `Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs`, update helper methods and local plan access:

```csharp
static DataAgentQueryPlan RequirePlan(DataAgentQueryPlanEnvelope envelope)
{
    Assert.That(envelope.Plan, Is.Not.Null);
    return envelope.Plan!;
}
```

Replace direct `envelope.Plan.Dataset` assertions with:

```csharp
DataAgentQueryPlan plan = RequirePlan(envelope);
Assert.That(plan.Dataset, Is.EqualTo("runtime_readiness_check"));
```

Update `AssertExplanation`:

```csharp
static void AssertExplanation(
    DataAgentQueryPlanEnvelope envelope,
    string confidence,
    string[] signals,
    string reason)
{
    DataAgentQueryPlan plan = RequirePlan(envelope);
    Assert.Multiple(() =>
    {
        Assert.That(envelope.Explanation.PlannerName, Is.EqualTo(nameof(DeterministicDataAgentQueryPlanner)));
        Assert.That(envelope.Explanation.Dataset, Is.EqualTo(plan.Dataset));
        Assert.That(envelope.Explanation.Intent, Is.EqualTo(plan.Intent));
        Assert.That(envelope.Explanation.Confidence, Is.EqualTo(confidence));
        Assert.That(envelope.Explanation.Signals, Is.EqualTo(signals));
        Assert.That(envelope.Explanation.Reason, Is.EqualTo(reason));
    });
}
```

In `DataAgentServicePlannerInjectionTests.cs`, no behavior change is needed for fixed planners because the third envelope constructor argument defaults to `null`.

- [ ] **Step 5: Add clarification context builder**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`:

```csharp
public static string BuildClarification(
    string question,
    DataAgentClarificationRequest clarification,
    DataAgentPlannerExplanation explanation)
{
    ArgumentNullException.ThrowIfNull(clarification);

    StringBuilder builder = new();
    builder.AppendLine("[data_agent_context]");
    builder.AppendLine($"question={Sanitize(question)}");
    builder.AppendLine("dataset=");
    builder.AppendLine("sql_status=needs_clarification");
    AppendPlannerMetadata(builder, explanation);
    builder.AppendLine($"clarification_question={Sanitize(clarification.Question)}");
    builder.AppendLine($"clarification_options={Sanitize(string.Join(", ", clarification.Options))}");
    builder.AppendLine($"clarification_reason={Sanitize(clarification.Reason)}");
    builder.AppendLine("[/data_agent_context]");
    return builder.ToString().Trim();
}
```

- [ ] **Step 6: Update service validation and clarification handling**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`.

At the start of `Answer`, after envelope validation:

```csharp
DataAgentQueryPlanEnvelope envelope = ValidateEnvelope(planner.Plan(new DataAgentQueryRequest(question, "developer", "zh-CN", false)));
DataAgentPlannerExplanation explanation = envelope.Explanation;

if (envelope.Clarification is not null)
    return Clarify(question, envelope.Clarification, explanation);

DataAgentQueryPlan plan = envelope.Plan!;
string queryPlanJson = JsonSerializer.Serialize(plan);
```

Add:

```csharp
DataAgentAnswer Clarify(
    string question,
    DataAgentClarificationRequest clarification,
    DataAgentPlannerExplanation explanation)
{
    string queryPlanJson = JsonSerializer.Serialize(clarification);
    new DataAgentAuditLog(databasePath).RecordRejected(
        question,
        string.Empty,
        queryPlanJson,
        string.Empty,
        "needs_clarification",
        TimeSpan.Zero);

    string summary = $"DataAgent needs clarification: {clarification.Question}";
    string context = DataAgentContextProvider.BuildClarification(question, clarification, explanation);
    return new DataAgentAnswer(string.Empty, string.Empty, 0, summary, context, false, "needs_clarification", explanation);
}
```

Replace `ValidateEnvelope` with:

```csharp
static DataAgentQueryPlanEnvelope ValidateEnvelope(DataAgentQueryPlanEnvelope envelope)
{
    ArgumentNullException.ThrowIfNull(envelope);
    ArgumentNullException.ThrowIfNull(envelope.Explanation);

    DataAgentPlannerExplanation explanation = envelope.Explanation;
    ArgumentException.ThrowIfNullOrWhiteSpace(explanation.PlannerName);
    ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Intent);
    ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Confidence);
    ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Reason);
    ArgumentNullException.ThrowIfNull(explanation.Signals);

    foreach (string signal in explanation.Signals)
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);

    bool hasPlan = envelope.Plan is not null;
    bool hasClarification = envelope.Clarification is not null;
    if (hasPlan == hasClarification)
        throw new ArgumentException("Planner envelope must contain exactly one plan or clarification.", nameof(envelope));

    if (hasPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Dataset);
        if (string.Equals(explanation.Dataset, envelope.Plan!.Dataset, StringComparison.Ordinal) == false)
            throw new ArgumentException("Planner explanation dataset must match the query plan dataset.", nameof(envelope));

        if (string.Equals(explanation.Intent, envelope.Plan.Intent, StringComparison.Ordinal) == false)
            throw new ArgumentException("Planner explanation intent must match the query plan intent.", nameof(envelope));
    }
    else
    {
        DataAgentClarificationRequest clarification = envelope.Clarification!;
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.Question);
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.Reason);
        ArgumentNullException.ThrowIfNull(clarification.Options);
        if (clarification.Options.Count is < 2 or > 4)
            throw new ArgumentException("Clarification must include 2 to 4 options.", nameof(envelope));

        foreach (string option in clarification.Options)
            ArgumentException.ThrowIfNullOrWhiteSpace(option);
    }

    return envelope;
}
```

- [ ] **Step 7: Add a temporary planner-name constant if needed**

If `LlmDataAgentQueryPlanner` does not exist yet and tests need `nameof(LlmDataAgentQueryPlanner)`, create a minimal file `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class LlmDataAgentQueryPlanner
{
}
```

This file will be replaced with the real implementation in Task 5.

- [ ] **Step 8: Run tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentClarificationContextTests|DataAgentPlannerTests|DataAgentServicePlannerInjectionTests" -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit clarification contract**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlan.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs Tests/Alife.Test.DataAgent/DataAgentClarificationContextTests.cs Tests/Alife.Test.DataAgent/DataAgentPlannerTests.cs Tests/Alife.Test.DataAgent/DataAgentServicePlannerInjectionTests.cs
git commit -m "feat: add DataAgent clarification contract"
```

---

### Task 2: Deterministic Result Explanation

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultExplainer.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentResultExplainerTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs`

- [ ] **Step 1: Write failing result explainer tests**

Create `Tests/Alife.Test.DataAgent/DataAgentResultExplainerTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentResultExplainerTests
{
    [Test]
    public void ExplainAcceptedResultIncludesDatasetRowsSignalsAndSourceBoundary()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent", "nl2sql", "document"],
            "question asks for DataAgent or NL2SQL documentation");

        string result = DataAgentResultExplainer.ExplainAccepted(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            3,
            "DataAgent NL2SQL Design",
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("document_index"));
            Assert.That(result, Does.Contain("3 rows"));
            Assert.That(result, Does.Contain("dataagent, nl2sql, document"));
            Assert.That(result, Does.Contain("local SQLite"));
            Assert.That(result, Does.Not.Contain("\r"));
            Assert.That(result, Does.Not.Contain("\n"));
        });
    }

    [Test]
    public void ContextIncludesResultExplanationWhenProvided()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent"],
            "question asks for DataAgent documentation");

        string context = DataAgentContextProvider.Build(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            1,
            "DataAgent NL2SQL Design",
            new DataAgentQueryResult([
                new Dictionary<string, object?> { ["path"] = "docs/a.md" }
            ]),
            explanation,
            "This query matched document_index and returned 1 row.");

        Assert.That(context, Does.Contain("result_explanation=This query matched document_index and returned 1 row."));
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentResultExplainerTests -v:minimal
```

Expected: compile fails because `DataAgentResultExplainer` and the extra context argument do not exist.

- [ ] **Step 3: Implement result explainer**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultExplainer.cs`:

```csharp
namespace Alife.Function.DataAgent;

public static class DataAgentResultExplainer
{
    public static string ExplainAccepted(
        string question,
        string dataset,
        int rowCount,
        string summary,
        DataAgentPlannerExplanation explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(explanation);

        string rowWord = rowCount == 1 ? "row" : "rows";
        string signals = string.Join(", ", explanation.Signals);
        return Sanitize(
            $"This query matched {dataset} and returned {rowCount} {rowWord}. " +
            $"The planner selected this dataset because it observed these signals: {signals}. " +
            "Results come from the local SQLite store and do not include live external data.");
    }

    public static string ExplainClarification(DataAgentClarificationRequest clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        return Sanitize($"DataAgent needs clarification before it can run a SQL query: {clarification.Question}");
    }

    static string Sanitize(string value)
    {
        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
```

- [ ] **Step 4: Add optional result explanation to context**

Modify `DataAgentContextProvider.Build` signature:

```csharp
public static string Build(
    string question,
    string dataset,
    string sql,
    int rowCount,
    string summary,
    DataAgentQueryResult result,
    DataAgentPlannerExplanation explanation,
    string resultExplanation = "")
```

Add after the `summary=` line:

```csharp
if (string.IsNullOrWhiteSpace(resultExplanation) == false)
    builder.AppendLine($"result_explanation={Sanitize(resultExplanation)}");
```

- [ ] **Step 5: Use explainer in service**

In `DataAgentService.Answer`, after `summary`:

```csharp
string resultExplanation = DataAgentResultExplainer.ExplainAccepted(question, plan.Dataset, result.Rows.Count, summary, explanation);
string context = DataAgentContextProvider.Build(question, plan.Dataset, compiled.Sql, result.Rows.Count, summary, result, explanation, resultExplanation);
```

In `Clarify`, replace the summary line with:

```csharp
string summary = DataAgentResultExplainer.ExplainClarification(clarification);
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentResultExplainerTests|DataAgentContextProviderTests|DataAgentClarificationContextTests" -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit result explainer**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultExplainer.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs Tests/Alife.Test.DataAgent/DataAgentResultExplainerTests.cs Tests/Alife.Test.DataAgent/DataAgentContextProviderTests.cs
git commit -m "feat: explain DataAgent query results"
```

---

### Task 3: LLM Planner Prompt Formatter

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs`
- Create: `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs`

- [ ] **Step 1: Write failing prompt formatter tests**

Create `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class LlmDataAgentPlannerPromptFormatterTests
{
    [Test]
    public void FormatIncludesApprovedSchemaAndJsonContract()
    {
        string databasePath = CreateDatabasePath();
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
            catalog,
            snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(prompt.System, Does.Contain("Do not output SQL"));
            Assert.That(prompt.System, Does.Contain("JSON"));
            Assert.That(prompt.System, Does.Contain("type"));
            Assert.That(prompt.Schema, Does.Contain("document_index"));
            Assert.That(prompt.Schema, Does.Contain("path"));
            Assert.That(prompt.Schema, Does.Contain("summary"));
            Assert.That(prompt.Schema, Does.Not.Contain("sqlite_master"));
            Assert.That(prompt.User, Does.Contain("Which documents describe DataAgent NL2SQL?"));
        });
    }

    [Test]
    public void FormatRejectsMismatchedSchemaSnapshot()
    {
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = new(
            [new DataAgentDatasetSchema("document_index", ["path"], ["path"], true, false)],
            false);

        Assert.Throws<InvalidOperationException>(() => new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
            catalog,
            snapshot));
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-llm-prompt-tests");
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
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter LlmDataAgentPlannerPromptFormatterTests -v:minimal
```

Expected: compile fails because `DataAgentLlmPlannerPrompt` and `LlmDataAgentPlannerPromptFormatter` do not exist.

- [ ] **Step 3: Implement prompt formatter**

Create `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs`:

```csharp
using System.Text;

namespace Alife.Function.DataAgent;

public sealed class LlmDataAgentPlannerPromptFormatter
{
    public DataAgentLlmPlannerPrompt Format(
        DataAgentQueryRequest request,
        DataAgentCatalog catalog,
        DataAgentSchemaSnapshot schemaSnapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(schemaSnapshot);

        if (schemaSnapshot.CatalogMatchesDatabase == false)
            throw new InvalidOperationException("DataAgent LLM planner requires catalog and SQLite schema to match.");

        string system = """
            You are a DataAgent NL2SQL planner.
            Do not output SQL.
            Output one JSON object only, with no markdown and no surrounding natural language.
            Return type=plan for a safe QueryPlan.
            Return type=clarification when the user question is ambiguous.
            confidence must be high, medium, or low.
            Operators allowed for filters are =, !=, <>, >, >=, <, <=, contains.
            Never invent datasets or fields outside the approved schema.
            """;

        StringBuilder schema = new();
        foreach (DataAgentDatasetSchema dataset in schemaSnapshot.Datasets.OrderBy(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (dataset.ExistsInDatabase == false || dataset.FieldsMatch == false)
                continue;

            schema.Append(dataset.Name);
            schema.Append(": ");
            schema.AppendLine(string.Join(", ", dataset.CatalogFields.Order(StringComparer.OrdinalIgnoreCase)));
        }

        string contract = """
            Plan JSON:
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"find_dataagent_documents","dataset":"document_index","confidence":"medium","signals":["dataagent"],"reason":"question asks for DataAgent documentation","select_fields":["path","title"],"filters":[{"field":"tags","operator":"contains","value":"dataagent"}],"sorts":[],"limit":20}

            Clarification JSON:
            {"type":"clarification","planner_name":"LlmDataAgentQueryPlanner","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous_time_range"],"reason":"question does not specify time range","clarification_question":"Do you want the last 7 days, last 30 days, or all history?","clarification_options":["last 7 days","last 30 days","all history"]}
            """;

        string user = $"Question: {request.Question}\nRole: {request.Role}\nLocale: {request.Locale}\nAllowLiveSources: {request.AllowLiveSources}";
        return new DataAgentLlmPlannerPrompt(system.Trim(), schema.ToString().Trim(), contract.Trim(), user);
    }
}

public sealed record DataAgentLlmPlannerPrompt(
    string System,
    string Schema,
    string Contract,
    string User);
```

- [ ] **Step 4: Run tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter LlmDataAgentPlannerPromptFormatterTests -v:minimal
```

Expected: prompt formatter tests pass.

- [ ] **Step 5: Commit prompt formatter**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs
git commit -m "feat: add DataAgent LLM planner prompt formatter"
```

---

### Task 4: Strict LLM Planner JSON Parser

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerResponseParser.cs`
- Create: `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerResponseParserTests.cs`

- [ ] **Step 1: Write failing parser tests**

Create `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerResponseParserTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class LlmDataAgentPlannerResponseParserTests
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();

    [Test]
    public void ParseValidPlanReturnsEnvelope()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"find_dataagent_documents","dataset":"document_index","confidence":"medium","signals":["dataagent","document"],"reason":"question asks for DataAgent documentation","select_fields":["path","title","summary"],"filters":[{"field":"tags","operator":"contains","value":"dataagent"}],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Envelope, Is.Not.Null);
            Assert.That(result.Envelope!.Plan, Is.Not.Null);
            Assert.That(result.Envelope.Plan!.Dataset, Is.EqualTo("document_index"));
            Assert.That(result.Envelope.Explanation.Confidence, Is.EqualTo("medium"));
            Assert.That(result.RejectedReason, Is.Empty);
        });
    }

    [Test]
    public void ParseValidClarificationReturnsClarificationEnvelope()
    {
        string json = """
            {"type":"clarification","planner_name":"LlmDataAgentQueryPlanner","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous_time_range"],"reason":"question does not specify time range","clarification_question":"Do you want the last 7 days, last 30 days, or all history?","clarification_options":["last 7 days","last 30 days","all history"]}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Envelope, Is.Not.Null);
            Assert.That(result.Envelope!.Plan, Is.Null);
            Assert.That(result.Envelope.Clarification, Is.Not.Null);
            Assert.That(result.Envelope.Clarification!.Options, Has.Count.EqualTo(3));
        });
    }

    [TestCase("preface {\"type\":\"plan\"}")]
    [TestCase("{\"type\":\"plan\"} trailing")]
    public void ParseRejectsJsonWrappedWithNaturalLanguage(string raw)
    {
        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(raw);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.RejectedReason, Does.Contain("json_must_be_single_object"));
        });
    }

    [Test]
    public void ParseRejectsUnknownDataset()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"unsafe","dataset":"sqlite_master","confidence":"medium","signals":["unsafe"],"reason":"bad dataset","select_fields":["name"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.That(result.RejectedReason, Does.Contain("unknown_dataset:sqlite_master"));
    }

    [Test]
    public void ParseRejectsUnknownField()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_field","dataset":"document_index","confidence":"medium","signals":["bad"],"reason":"bad field","select_fields":["path","secret"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.That(result.RejectedReason, Does.Contain("unknown_select_field:document_index.secret"));
    }

    [Test]
    public void ParseRejectsInvalidOperator()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_operator","dataset":"document_index","confidence":"medium","signals":["bad"],"reason":"bad operator","select_fields":["path"],"filters":[{"field":"tags","operator":"starts_with","value":"dataagent"}],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.That(result.RejectedReason, Does.Contain("unsupported_operator:starts_with"));
    }

    [Test]
    public void ParseRejectsInvalidConfidence()
    {
        string json = """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_confidence","dataset":"document_index","confidence":"certain","signals":["bad"],"reason":"bad confidence","select_fields":["path"],"filters":[],"sorts":[],"limit":20}
            """;

        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(json);

        Assert.That(result.RejectedReason, Does.Contain("invalid_confidence:certain"));
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter LlmDataAgentPlannerResponseParserTests -v:minimal
```

Expected: compile fails because parser/result types do not exist.

- [ ] **Step 3: Implement parser result type and parser**

Create `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerResponseParser.cs`:

```csharp
using System.Text.Json;

namespace Alife.Function.DataAgent;

public sealed class LlmDataAgentPlannerResponseParser(DataAgentCatalog catalog)
{
    static readonly HashSet<string> AllowedConfidence = new(StringComparer.OrdinalIgnoreCase)
    {
        "high",
        "medium",
        "low"
    };

    public DataAgentLlmPlannerResult Parse(string rawModelOutput)
    {
        if (string.IsNullOrWhiteSpace(rawModelOutput))
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, "empty_model_output");

        string trimmed = rawModelOutput.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) == false ||
            trimmed.EndsWith("}", StringComparison.Ordinal) == false)
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, "json_must_be_single_object");

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            JsonElement root = document.RootElement;
            string type = RequiredString(root, "type");
            return type switch
            {
                "plan" => ParsePlan(root, rawModelOutput),
                "clarification" => ParseClarification(root, rawModelOutput),
                _ => DataAgentLlmPlannerResult.Invalid(rawModelOutput, $"unsupported_type:{type}")
            };
        }
        catch (JsonException ex)
        {
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, $"invalid_json:{ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, ex.Message);
        }
    }

    DataAgentLlmPlannerResult ParsePlan(JsonElement root, string raw)
    {
        string intent = RequiredString(root, "intent");
        string dataset = RequiredString(root, "dataset");
        string confidence = RequiredConfidence(root);
        string[] signals = RequiredStringArray(root, "signals");
        string reason = RequiredString(root, "reason");
        string[] select = RequiredStringArray(root, "select_fields");
        IReadOnlyList<DataAgentFilter> filters = ReadFilters(root);
        IReadOnlyList<DataAgentOrderBy> sorts = ReadSorts(root);
        int limit = RequiredInt(root, "limit");

        DataAgentQueryPlan plan = new(dataset, intent, select, filters, sorts, limit);
        DataAgentValidationResult validation = new DataAgentQueryPlanValidator(catalog).Validate(plan);
        if (validation.IsValid == false)
            return DataAgentLlmPlannerResult.Invalid(raw, string.Join(";", validation.Errors));

        DataAgentQueryPlanEnvelope envelope = new(
            plan,
            new DataAgentPlannerExplanation(
                nameof(LlmDataAgentQueryPlanner),
                intent,
                dataset,
                confidence,
                signals,
                reason));
        return DataAgentLlmPlannerResult.Valid(raw, envelope);
    }

    DataAgentLlmPlannerResult ParseClarification(JsonElement root, string raw)
    {
        string intent = RequiredString(root, "intent");
        string confidence = RequiredConfidence(root);
        string[] signals = RequiredStringArray(root, "signals");
        string reason = RequiredString(root, "reason");
        string question = RequiredString(root, "clarification_question");
        string[] options = RequiredStringArray(root, "clarification_options");
        if (options.Length is < 2 or > 4)
            throw new ArgumentException("invalid_clarification_option_count");

        DataAgentClarificationRequest clarification = new(question, options, reason);
        DataAgentQueryPlanEnvelope envelope = new(
            null,
            new DataAgentPlannerExplanation(
                nameof(LlmDataAgentQueryPlanner),
                intent,
                string.Empty,
                confidence,
                signals,
                reason),
            clarification);
        return DataAgentLlmPlannerResult.Valid(raw, envelope);
    }

    static IReadOnlyList<DataAgentFilter> ReadFilters(JsonElement root)
    {
        if (root.TryGetProperty("filters", out JsonElement filtersElement) == false)
            return [];

        List<DataAgentFilter> filters = [];
        foreach (JsonElement filter in filtersElement.EnumerateArray())
        {
            filters.Add(new DataAgentFilter(
                RequiredString(filter, "field"),
                RequiredString(filter, "operator"),
                ReadScalar(filter.GetProperty("value"))));
        }

        return filters;
    }

    static IReadOnlyList<DataAgentOrderBy> ReadSorts(JsonElement root)
    {
        if (root.TryGetProperty("sorts", out JsonElement sortsElement) == false)
            return [];

        List<DataAgentOrderBy> sorts = [];
        foreach (JsonElement sort in sortsElement.EnumerateArray())
            sorts.Add(new DataAgentOrderBy(RequiredString(sort, "field"), RequiredString(sort, "direction")));

        return sorts;
    }

    static object? ReadScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long longValue) => longValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new ArgumentException("unsupported_scalar_value")
        };
    }

    static string RequiredConfidence(JsonElement root)
    {
        string confidence = RequiredString(root, "confidence");
        if (AllowedConfidence.Contains(confidence) == false)
            throw new ArgumentException($"invalid_confidence:{confidence}");

        return confidence.ToLowerInvariant();
    }

    static string RequiredString(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out JsonElement value) == false ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"missing_or_empty:{property}");

        return value.GetString()!;
    }

    static int RequiredInt(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out JsonElement value) == false || value.TryGetInt32(out int result) == false)
            throw new ArgumentException($"missing_or_invalid_int:{property}");

        return result;
    }

    static string[] RequiredStringArray(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out JsonElement value) == false || value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"missing_or_invalid_array:{property}");

        string[] values = value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .Where(item => string.IsNullOrWhiteSpace(item) == false)
            .Select(item => item!)
            .ToArray();

        if (values.Length == 0)
            throw new ArgumentException($"empty_array:{property}");

        return values;
    }
}

public sealed record DataAgentLlmPlannerResult(
    bool IsValid,
    DataAgentQueryPlanEnvelope? Envelope,
    string RawModelOutput,
    string RejectedReason)
{
    public static DataAgentLlmPlannerResult Valid(string rawModelOutput, DataAgentQueryPlanEnvelope envelope)
    {
        return new DataAgentLlmPlannerResult(true, envelope, rawModelOutput, string.Empty);
    }

    public static DataAgentLlmPlannerResult Invalid(string rawModelOutput, string rejectedReason)
    {
        return new DataAgentLlmPlannerResult(false, null, rawModelOutput, rejectedReason);
    }
}
```

- [ ] **Step 4: Run tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter LlmDataAgentPlannerResponseParserTests -v:minimal
```

Expected: parser tests pass.

- [ ] **Step 5: Commit strict parser**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerResponseParser.cs Tests/Alife.Test.DataAgent/LlmDataAgentPlannerResponseParserTests.cs
git commit -m "feat: parse DataAgent LLM planner JSON"
```

---

### Task 5: LLM Planner And Planner Selector

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/ILlmDataAgentPlannerClient.cs`
- Replace/Modify: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentPlannerSelector.cs`
- Create: `Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs`

- [ ] **Step 1: Write failing LLM planner tests**

Create `Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class LlmDataAgentQueryPlannerTests
{
    [Test]
    public void PlanReturnsEnvelopeForValidFakeClientOutput()
    {
        string databasePath = CreateDatabasePath();
        LlmDataAgentQueryPlanner planner = CreatePlanner(databasePath, """
            {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"find_dataagent_documents","dataset":"document_index","confidence":"medium","signals":["dataagent","document"],"reason":"question asks for DataAgent documentation","select_fields":["path","title","summary"],"filters":[{"field":"tags","operator":"contains","value":"dataagent"}],"sorts":[],"limit":20}
            """);

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "developer",
            "en-US",
            false));

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan, Is.Not.Null);
            Assert.That(envelope.Plan!.Dataset, Is.EqualTo("document_index"));
            Assert.That(envelope.Explanation.PlannerName, Is.EqualTo(nameof(LlmDataAgentQueryPlanner)));
            Assert.That(envelope.Explanation.Confidence, Is.EqualTo("medium"));
        });
    }

    [Test]
    public void PlanReturnsClarificationForClarificationOutput()
    {
        string databasePath = CreateDatabasePath();
        LlmDataAgentQueryPlanner planner = CreatePlanner(databasePath, """
            {"type":"clarification","planner_name":"LlmDataAgentQueryPlanner","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous_time_range"],"reason":"question does not specify time range","clarification_question":"Do you want the last 7 days, last 30 days, or all history?","clarification_options":["last 7 days","last 30 days","all history"]}
            """);

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "How active has the project been recently?",
            "developer",
            "en-US",
            false));

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan, Is.Null);
            Assert.That(envelope.Clarification, Is.Not.Null);
            Assert.That(envelope.Explanation.Intent, Is.EqualTo("clarify_ambiguous_query"));
        });
    }

    [Test]
    public void PlanFallsBackForInvalidOutputAndKeepsSafetySignal()
    {
        string databasePath = CreateDatabasePath();
        LlmDataAgentQueryPlanner planner = CreatePlanner(databasePath, "Here is SQL: DROP TABLE engineering_gate;");

        DataAgentQueryPlanEnvelope envelope = planner.Plan(new DataAgentQueryRequest(
            "Which documents describe DataAgent NL2SQL?",
            "developer",
            "en-US",
            false));

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Plan, Is.Not.Null);
            Assert.That(envelope.Plan!.Dataset, Is.EqualTo("document_index"));
            Assert.That(envelope.Explanation.PlannerName, Is.EqualTo(nameof(LlmDataAgentQueryPlanner)));
            Assert.That(envelope.Explanation.Signals, Does.Contain("llm_invalid_output_fallback"));
            Assert.That(envelope.Explanation.Reason, Does.Contain("deterministic fallback"));
            Assert.That(envelope.Explanation.Reason, Does.Not.Contain("DROP TABLE"));
        });
    }

    [Test]
    public void SelectorDefaultsToDeterministicPlanner()
    {
        IDataAgentQueryPlanner planner = DataAgentPlannerSelector.Create(
            new LlmDataAgentPlannerOptions(),
            CreateDatabasePath(),
            new FixedClient("{}"));

        Assert.That(planner, Is.TypeOf<DeterministicDataAgentQueryPlanner>());
    }

    [Test]
    public void SelectorCreatesHarnessPlannerWhenEnabled()
    {
        IDataAgentQueryPlanner planner = DataAgentPlannerSelector.Create(
            new LlmDataAgentPlannerOptions { Mode = LlmDataAgentPlannerMode.Harness },
            CreateDatabasePath(),
            new FixedClient("{}"));

        Assert.That(planner, Is.TypeOf<LlmDataAgentQueryPlanner>());
    }

    static LlmDataAgentQueryPlanner CreatePlanner(string databasePath, string raw)
    {
        return new LlmDataAgentQueryPlanner(
            databasePath,
            new FixedClient(raw),
            new DeterministicDataAgentQueryPlanner());
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-llm-planner-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }

    sealed class FixedClient(string raw) : ILlmDataAgentPlannerClient
    {
        public string Complete(DataAgentLlmPlannerPrompt prompt) => raw;
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter LlmDataAgentQueryPlannerTests -v:minimal
```

Expected: compile fails because client/options/selector are missing or temporary `LlmDataAgentQueryPlanner` does not implement `IDataAgentQueryPlanner`.

- [ ] **Step 3: Add client interface**

Create `sources/Alife.Function/Alife.Function.DataAgent/ILlmDataAgentPlannerClient.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface ILlmDataAgentPlannerClient
{
    string Complete(DataAgentLlmPlannerPrompt prompt);
}
```

- [ ] **Step 4: Implement LLM planner**

Replace `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class LlmDataAgentQueryPlanner : IDataAgentQueryPlanner
{
    readonly string databasePath;
    readonly ILlmDataAgentPlannerClient client;
    readonly IDataAgentQueryPlanner fallbackPlanner;
    readonly DataAgentCatalog catalog;
    readonly LlmDataAgentPlannerPromptFormatter formatter;
    readonly LlmDataAgentPlannerResponseParser parser;

    public LlmDataAgentQueryPlanner(
        string databasePath,
        ILlmDataAgentPlannerClient client,
        IDataAgentQueryPlanner fallbackPlanner)
        : this(
            databasePath,
            client,
            fallbackPlanner,
            DataAgentCatalog.CreateDefault(),
            new LlmDataAgentPlannerPromptFormatter())
    {
    }

    public LlmDataAgentQueryPlanner(
        string databasePath,
        ILlmDataAgentPlannerClient client,
        IDataAgentQueryPlanner fallbackPlanner,
        DataAgentCatalog catalog,
        LlmDataAgentPlannerPromptFormatter formatter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fallbackPlanner);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(formatter);

        this.databasePath = databasePath;
        this.client = client;
        this.fallbackPlanner = fallbackPlanner;
        this.catalog = catalog;
        this.formatter = formatter;
        parser = new LlmDataAgentPlannerResponseParser(catalog);
    }

    public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
    {
        DataAgentSchemaSnapshot schemaSnapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
        DataAgentLlmPlannerPrompt prompt = formatter.Format(request, catalog, schemaSnapshot);
        string raw = client.Complete(prompt);
        DataAgentLlmPlannerResult parsed = parser.Parse(raw);

        if (parsed.IsValid && parsed.Envelope is not null)
            return parsed.Envelope;

        DataAgentQueryPlanEnvelope fallback = fallbackPlanner.Plan(request);
        DataAgentQueryPlan plan = fallback.Plan ?? throw new InvalidOperationException("Fallback planner must return a query plan.");
        string sanitizedReason = SanitizeFallbackReason(parsed.RejectedReason);
        return new DataAgentQueryPlanEnvelope(
            plan,
            new DataAgentPlannerExplanation(
                nameof(LlmDataAgentQueryPlanner),
                plan.Intent,
                plan.Dataset,
                "low",
                fallback.Explanation.Signals.Concat(["llm_invalid_output_fallback"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                $"LLM planner output was invalid; deterministic fallback used: {sanitizedReason}"));
    }

    static string SanitizeFallbackReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "invalid_model_output";

        string sanitized = reason
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        string[] dangerous = ["DROP", "DELETE", "INSERT", "UPDATE", "ALTER", "ATTACH", "PRAGMA", "TABLE"];
        foreach (string token in dangerous)
            sanitized = sanitized.Replace(token, "[redacted]", StringComparison.OrdinalIgnoreCase);

        return sanitized.Length <= 120 ? sanitized : sanitized[..120];
    }
}
```

- [ ] **Step 5: Add planner selector and options**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentPlannerSelector.cs`:

```csharp
namespace Alife.Function.DataAgent;

public static class DataAgentPlannerSelector
{
    public static IDataAgentQueryPlanner Create(
        LlmDataAgentPlannerOptions options,
        string databasePath,
        ILlmDataAgentPlannerClient? llmClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return options.Mode switch
        {
            LlmDataAgentPlannerMode.Disabled => new DeterministicDataAgentQueryPlanner(),
            LlmDataAgentPlannerMode.Harness => new LlmDataAgentQueryPlanner(
                databasePath,
                llmClient ?? throw new InvalidOperationException("Harness mode requires an LLM planner client."),
                new DeterministicDataAgentQueryPlanner()),
            LlmDataAgentPlannerMode.Live => new LlmDataAgentQueryPlanner(
                databasePath,
                llmClient ?? throw new InvalidOperationException("Live mode requires an LLM planner client."),
                new DeterministicDataAgentQueryPlanner()),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unsupported LLM planner mode.")
        };
    }
}

public sealed class LlmDataAgentPlannerOptions
{
    public LlmDataAgentPlannerMode Mode { get; init; } = LlmDataAgentPlannerMode.Disabled;
}

public enum LlmDataAgentPlannerMode
{
    Disabled,
    Harness,
    Live
}
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "LlmDataAgentQueryPlannerTests|LlmDataAgentPlannerPromptFormatterTests|LlmDataAgentPlannerResponseParserTests" -v:minimal
```

Expected: selected tests pass.

- [ ] **Step 7: Commit LLM planner**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/ILlmDataAgentPlannerClient.cs sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentPlannerSelector.cs Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs
git commit -m "feat: add DataAgent LLM planner harness"
```

---

### Task 6: v1.3 Readiness Upgrade

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV13ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Write failing v1.3 readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV13ReadinessTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV13ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesV13LlmHarnessChecks()
    {
        string databasePath = CreateDatabasePath();

        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(checks.Single(check => check.Name == "LlmPlannerInterfacePresent").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "LlmPlannerPromptUsesSchemaSnapshot").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "LlmPlannerStrictJsonParser").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "LlmPlannerRejectsInvalidOutput").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "LlmPlannerFallbackPreservesSafety").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "ClarificationRequestSupported").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "NaturalLanguageResultExplanationPresent").Passed, Is.True);
        });
    }

    [Test]
    public void StaticReadinessScriptDeclaresV13Markers()
    {
        string script = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("LlmPlannerInterfacePresent"));
            Assert.That(script, Does.Contain("LlmPlannerPromptUsesSchemaSnapshot"));
            Assert.That(script, Does.Contain("LlmPlannerStrictJsonParser"));
            Assert.That(script, Does.Contain("LlmPlannerRejectsInvalidOutput"));
            Assert.That(script, Does.Contain("LlmPlannerFallbackPreservesSafety"));
            Assert.That(script, Does.Contain("ClarificationRequestSupported"));
            Assert.That(script, Does.Contain("NaturalLanguageResultExplanationPresent"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v13-readiness-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }
}
```

Update existing readiness count assertions in `DataAgentReadinessTests.cs` from 15 to 22 if that file asserts the total required count.

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentV13ReadinessTests|DataAgentReadinessTests" -v:minimal
```

Expected: fails because v1.3 checks are missing.

- [ ] **Step 3: Add runtime readiness checks**

In `DataAgentReadiness.CheckCore`, after existing planner/tool checks, add:

```csharp
checks.Add(typeof(ILlmDataAgentPlannerClient).IsInterface
    ? Pass("LlmPlannerInterfacePresent", nameof(ILlmDataAgentPlannerClient))
    : Fail("LlmPlannerInterfacePresent", "missing LLM planner client interface"));

DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
    new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
    DataAgentCatalog.CreateDefault(),
    schemaSnapshot);
checks.Add(prompt.Schema.Contains("document_index", StringComparison.Ordinal) &&
           prompt.System.Contains("Do not output SQL", StringComparison.Ordinal)
    ? Pass("LlmPlannerPromptUsesSchemaSnapshot", "schema-aware no-SQL prompt")
    : Fail("LlmPlannerPromptUsesSchemaSnapshot", prompt.Schema));

DataAgentLlmPlannerResult parsedPlan = new LlmDataAgentPlannerResponseParser(DataAgentCatalog.CreateDefault()).Parse(
    "{\"type\":\"plan\",\"planner_name\":\"LlmDataAgentQueryPlanner\",\"intent\":\"find_dataagent_documents\",\"dataset\":\"document_index\",\"confidence\":\"medium\",\"signals\":[\"dataagent\"],\"reason\":\"question asks for DataAgent documentation\",\"select_fields\":[\"path\",\"title\"],\"filters\":[{\"field\":\"tags\",\"operator\":\"contains\",\"value\":\"dataagent\"}],\"sorts\":[],\"limit\":20}");
checks.Add(parsedPlan.IsValid && parsedPlan.Envelope?.Plan?.Dataset == "document_index"
    ? Pass("LlmPlannerStrictJsonParser", parsedPlan.Envelope.Plan.Dataset)
    : Fail("LlmPlannerStrictJsonParser", parsedPlan.RejectedReason));

DataAgentLlmPlannerResult invalidParsedPlan = new LlmDataAgentPlannerResponseParser(DataAgentCatalog.CreateDefault()).Parse(
    "{\"type\":\"plan\",\"planner_name\":\"LlmDataAgentQueryPlanner\",\"intent\":\"bad\",\"dataset\":\"document_index\",\"confidence\":\"medium\",\"signals\":[\"bad\"],\"reason\":\"bad operator\",\"select_fields\":[\"path\"],\"filters\":[{\"field\":\"tags\",\"operator\":\"starts_with\",\"value\":\"dataagent\"}],\"sorts\":[],\"limit\":20}");
checks.Add(invalidParsedPlan.IsValid == false &&
           invalidParsedPlan.RejectedReason.Contains("unsupported_operator:starts_with", StringComparison.Ordinal)
    ? Pass("LlmPlannerRejectsInvalidOutput", invalidParsedPlan.RejectedReason)
    : Fail("LlmPlannerRejectsInvalidOutput", invalidParsedPlan.RejectedReason));

DataAgentQueryPlanEnvelope fallbackEnvelope = new LlmDataAgentQueryPlanner(
    databasePath,
    new FixedLlmClient("not json"),
    new DeterministicDataAgentQueryPlanner()).Plan(new DataAgentQueryRequest(
        "Which documents describe DataAgent NL2SQL?",
        "developer",
        "en-US",
        false));
checks.Add(fallbackEnvelope.Plan?.Dataset == "document_index" &&
           fallbackEnvelope.Explanation.Signals.Contains("llm_invalid_output_fallback")
    ? Pass("LlmPlannerFallbackPreservesSafety", fallbackEnvelope.Explanation.Reason)
    : Fail("LlmPlannerFallbackPreservesSafety", fallbackEnvelope.Explanation.Reason));

DataAgentQueryPlanEnvelope clarificationEnvelope = new LlmDataAgentQueryPlanner(
    databasePath,
    new FixedLlmClient("{\"type\":\"clarification\",\"planner_name\":\"LlmDataAgentQueryPlanner\",\"intent\":\"clarify_ambiguous_query\",\"dataset\":\"\",\"confidence\":\"low\",\"signals\":[\"ambiguous_time_range\"],\"reason\":\"question does not specify time range\",\"clarification_question\":\"Do you want the last 7 days, last 30 days, or all history?\",\"clarification_options\":[\"last 7 days\",\"last 30 days\",\"all history\"]}"),
    new DeterministicDataAgentQueryPlanner()).Plan(new DataAgentQueryRequest(
        "How active has the project been recently?",
        "developer",
        "en-US",
        false));
checks.Add(clarificationEnvelope.Plan is null && clarificationEnvelope.Clarification is not null
    ? Pass("ClarificationRequestSupported", clarificationEnvelope.Clarification.Question)
    : Fail("ClarificationRequestSupported", "clarification envelope missing"));

checks.Add(answer.Context.Contains("result_explanation=", StringComparison.Ordinal)
    ? Pass("NaturalLanguageResultExplanationPresent", "result explanation context field")
    : Fail("NaturalLanguageResultExplanationPresent", answer.Context));
```

Add nested fixed client:

```csharp
sealed class FixedLlmClient(string raw) : ILlmDataAgentPlannerClient
{
    public string Complete(DataAgentLlmPlannerPrompt prompt) => raw;
}
```

- [ ] **Step 4: Update static readiness script**

Modify `tools/check-dataagent-readiness.ps1` and add checks under the Planner/Context groups:

```powershell
New-Check -Group "Planner" -Name "LlmPlannerInterfacePresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/ILlmDataAgentPlannerClient.cs" @("ILlmDataAgentPlannerClient", "Complete")) -Detail "LLM planner client interface markers"
New-Check -Group "Planner" -Name "LlmPlannerPromptUsesSchemaSnapshot" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs" @("DataAgentSchemaSnapshot", "Do not output SQL", "document_index")) -Detail "schema-aware LLM prompt markers"
New-Check -Group "Planner" -Name "LlmPlannerStrictJsonParser" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerResponseParser.cs" @("JsonDocument", "json_must_be_single_object", "DataAgentQueryPlanValidator")) -Detail "strict JSON parser markers"
New-Check -Group "Planner" -Name "LlmPlannerRejectsInvalidOutput" -Passed (Test-FileMarker "Tests/Alife.Test.DataAgent/LlmDataAgentPlannerResponseParserTests.cs" @("ParseRejectsInvalidOperator", "unsupported_operator:starts_with")) -Detail "invalid LLM output rejection tests"
New-Check -Group "Planner" -Name "LlmPlannerFallbackPreservesSafety" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs" @("llm_invalid_output_fallback", "DeterministicDataAgentQueryPlanner", "SanitizeFallbackReason")) -Detail "LLM fallback safety markers"
New-Check -Group "Context" -Name "ClarificationRequestSupported" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs" @("BuildClarification", "needs_clarification", "clarification_options")) -Detail "clarification context markers"
New-Check -Group "Context" -Name "NaturalLanguageResultExplanationPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultExplainer.cs" @("ExplainAccepted", "local SQLite")) -Detail "result explanation markers"
```

- [ ] **Step 5: Run readiness tests and scripts to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentV13ReadinessTests|DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgentV13ReadinessTests: PASS
DataAgent readiness: Summary: 22 required passed, 0 required missing
QChat engineering map: Summary: 33 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 6: Commit readiness upgrade**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 Tests/Alife.Test.DataAgent/DataAgentV13ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "feat: require DataAgent v1.3 LLM harness readiness"
```

---

### Task 7: Final Verification And Upload

**Files:**
- No production files unless verification exposes a defect.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass with 0 failed.

- [ ] **Step 2: Run solution build**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build Alife.slnx --no-restore -v:minimal
```

Expected: build succeeds with 0 errors. Existing QChat fake-runtime `CS0067` warnings may remain.

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
DataAgent readiness: 22 required passed, 0 required missing
QChat engineering map: 33 required passed, 0 required missing, 0 optional present, 0 optional missing
git diff --check: exit 0
```

- [ ] **Step 5: Commit verification fixes if needed**

If verification required fixes, commit only related files:

```powershell
git status --short --branch --untracked-files=all
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent tools/check-dataagent-readiness.ps1
git commit -m "fix: stabilize DataAgent v1.3 LLM harness"
```

If no fixes are needed, skip this step.

- [ ] **Step 6: Upload to GitHub snapshot remote**

Use the default project upload target, not `origin`:

```powershell
git remote -v
git ls-remote alife-byastralfox refs/heads/master
```

Create an isolated upload worktree from current `alife-byastralfox/master`, sync only tracked files from `D:\Alife`, commit with:

```text
Update Alife service snapshot
```

Push and verify:

```powershell
git push alife-byastralfox HEAD:master
git ls-remote alife-byastralfox refs/heads/master
```

Expected: remote `refs/heads/master` points to the new upload commit hash.

---

## Self-Review Checklist

- Spec coverage:
  - LLM planner interface: Task 5.
  - Prompt formatter with schema snapshot: Task 3.
  - Strict JSON parser: Task 4.
  - Clarification result: Task 1 and Task 5.
  - Invalid output fallback: Task 5.
  - Natural-language result explanation: Task 2.
  - Readiness 15 -> 22: Task 6.
  - Full verification and upload: Task 7.
- Red-flag scan:
  - The plan does not contain unresolved work markers.
  - Each code-modifying task includes concrete code and exact commands.
- Type consistency:
  - `DataAgentQueryPlanEnvelope` has `DataAgentQueryPlan? Plan`, `DataAgentPlannerExplanation Explanation`, and optional `DataAgentClarificationRequest? Clarification`.
  - `LlmDataAgentQueryPlanner` implements `IDataAgentQueryPlanner`.
  - `ILlmDataAgentPlannerClient.Complete` accepts `DataAgentLlmPlannerPrompt`.
  - Readiness expected count is 22 required checks.
