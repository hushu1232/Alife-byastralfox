# DataAgent V2.11 Scenario Context Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the V2.10 engineering scenario knowledge pack into the existing DataAgent NL2SQL planner preparation path as deterministic, catalog-safe context hints.

**Architecture:** V2.11 stays inside the current C# DataAgent pipeline: scenario pack -> deterministic context builder -> bounded LLM planner prompt hints -> owner diagnostics/readiness gates. QueryPlan validation, SQL compilation, SQL safety, read-only execution, audit, evidence, trace, and progress remain the authority. No LangGraph runtime, Python sidecar, PostgreSQL checkpoint productization, QChat main-loop refactor, or natural-language QChat command auto-execution is introduced.

**Tech Stack:** .NET 9, C# records/classes, NUnit, PowerShell readiness scripts, existing SQLite DataAgent harness.

---

## Working Context

Use the V2.11 worktree only:

```powershell
git -C D:\Alife\.worktrees\alife-v2.11-scenario-context-integration status --short --branch
```

Use the user-local .NET 9 SDK:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected branch:

```text
alife-v2.11-scenario-context-integration
```

Guardrails:

- Do not touch `D:\FOXD`, `D:\FOXD\alife-service`, or ASRRAL-FOX.
- Do not add LangGraph, StateGraph, a Python sidecar, or a new SQL execution path.
- Do not productize PostgreSQL checkpointing in V2.11.
- Do not let QChat directly depend on `DataAgentScenarioKnowledgePackProvider` or `DataAgentScenarioContextBuilder`.
- Do not add natural-language QChat command auto-execution.
- Use `git add -f` for ignored `docs/superpowers/*` and `docs/dataagent/*` files when needed.

---

## File Structure

Create:

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContext.cs`
  Immutable scenario context model with defensive read-only snapshots and stable reason-code constants.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextBuilder.cs`
  Deterministic term/metric resolver from scenario pack plus `DataAgentCatalog`; no model calls, no SQL, no file IO.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioDiagnosticsFormatter.cs`
  Owner-safe compact diagnostics text for already-built scenario context.

- `Tests/Alife.Test.DataAgent/DataAgentScenarioContextBuilderTests.cs`
  Builder behavior, catalog mismatch, metric matching, no-match, and defensive snapshot coverage.

- `Tests/Alife.Test.DataAgent/DataAgentScenarioDiagnosticsFormatterTests.cs`
  Formatter output, unavailable state, and redaction coverage.

- `Tests/Alife.Test.DataAgent/DataAgentV211ReadinessTests.cs`
  Focused V2.11 readiness guardrails not covered by the broad readiness smoke test.

Modify:

- `docs/dataagent/scenario-packs/engineering.zh-CN.json`
  Keep readable UTF-8 Chinese and align fields with `DataAgentCatalog`.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryRequest.cs`
  Add optional `DataAgentScenarioContext? ScenarioContext = null` without breaking existing four-argument call sites.

- `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs`
  Add overload accepting scenario context and emit bounded `Scenario context:` hints only when matched.

- `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs`
  Pass `request.ScenarioContext` into the prompt formatter.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  Add runtime gate `DataAgentScenarioContextIntegrated`.

- `Tests/Alife.Test.DataAgent/DataAgentScenarioKnowledgePackProviderTests.cs`
  Replace mojibake assertions with readable Chinese and add UTF-8/catalog-field protection.

- `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs`
  Add scenario-context prompt tests.

- `Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs`
  Add planner pass-through test for scenario context.

- `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  Update core/script counts and protect the V2.11 script gate.

- `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  Add the required engineering-map entry and prove QChat does not import scenario builder/provider types.

- `tools/check-dataagent-readiness.ps1`
  Add `DataAgentScenarioContextIntegrated`, update expected required count `79 -> 80`.

- `tools/check-qchat-engineering-map.ps1`
  Add `DataAgent scenario context diagnostics`, update expected required count `54 -> 55`.

---

### Task 1: UTF-8 Scenario Pack Normalization

**Files:**
- Modify: `docs/dataagent/scenario-packs/engineering.zh-CN.json`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentScenarioKnowledgePackProviderTests.cs`

- [ ] **Step 1: Write failing provider tests for readable Chinese and catalog-safe fields**

In `Tests/Alife.Test.DataAgent/DataAgentScenarioKnowledgePackProviderTests.cs`, add `using System.Text;` and replace the mojibake assertions with these readable terms. Add the two new tests shown below.

```csharp
using Alife.Function.DataAgent;
using System.Text;
using System.Text.Json;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentScenarioKnowledgePackProviderTests
{
    [Test]
    public void EngineeringPackLoadsControlledBusinessTerms()
    {
        DataAgentScenarioKnowledgePack pack = LoadEngineeringPack();

        Assert.Multiple(() =>
        {
            Assert.That(pack.Scenario, Is.EqualTo("engineering_readiness"));
            Assert.That(pack.Culture, Is.EqualTo("zh-CN"));
            Assert.That(pack.Terms.Select(term => term.Term), Does.Contain("工程门禁"));
            Assert.That(pack.Terms.Select(term => term.Term), Does.Contain("最近失败的测试"));
            Assert.That(pack.Terms.Select(term => term.Term), Does.Contain("缺失项"));
            Assert.That(pack.Terms.Select(term => term.Term), Does.Contain("文档证据"));
            Assert.That(pack.Metrics.Select(metric => metric.Name), Does.Contain("失败"));
            Assert.That(pack.Metrics.Select(metric => metric.Name), Does.Contain("必需"));
        });
    }

    [Test]
    public void EngineeringPackRoundTripsReadableUtf8Chinese()
    {
        string packPath = EngineeringPackPath();
        string json = File.ReadAllText(packPath, Encoding.UTF8);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("工程门禁"));
            Assert.That(json, Does.Contain("最近失败的测试"));
            Assert.That(json, Does.Contain("缺失项"));
            Assert.That(json, Does.Contain("文档证据"));
            Assert.That(json, Does.Contain("失败"));
            Assert.That(json, Does.Contain("必需"));
            Assert.That(json, Does.Not.Contain("宸ョ▼"));
            Assert.That(json, Does.Not.Contain("鏈€"));
            Assert.That(json, Does.Not.Contain("缂哄"));
            Assert.That(json, Does.Not.Contain("澶辫触"));
            Assert.That(json, Does.Not.Contain("蹇呴渶"));
        });
    }

    [Test]
    public void EngineeringPackFieldsExistInDefaultCatalog()
    {
        DataAgentScenarioKnowledgePack pack = LoadEngineeringPack();
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();

        foreach (DataAgentScenarioTerm term in pack.Terms)
        {
            Assert.That(catalog.HasDataset(term.Dataset), Is.True, $"{term.Term} dataset {term.Dataset}");

            foreach (string field in term.Fields)
            {
                Assert.That(catalog.HasField(term.Dataset, field), Is.True, $"{term.Term} field {term.Dataset}.{field}");
            }
        }
    }

    [Test]
    public void EngineeringPackNormalizesMetricValuesToScalarDotNetTypes()
    {
        DataAgentScenarioKnowledgePack pack = LoadEngineeringPack();

        DataAgentScenarioMetric failed = pack.Metrics.Single(metric => metric.Name == "失败");
        DataAgentScenarioMetric required = pack.Metrics.Single(metric => metric.Name == "必需");

        Assert.Multiple(() =>
        {
            Assert.That(failed.Value, Is.EqualTo("passed"));
            Assert.That(failed.Value, Is.TypeOf<string>());
            Assert.That(failed.Value, Is.Not.TypeOf<JsonElement>());
            Assert.That(required.Value, Is.EqualTo(true));
            Assert.That(required.Value, Is.TypeOf<bool>());
            Assert.That(required.Value, Is.Not.TypeOf<JsonElement>());
        });
    }

    [Test]
    public void ResolverMapsChineseUtteranceToCandidateDatasets()
    {
        DataAgentScenarioKnowledgePack pack = LoadEngineeringPack();

        IReadOnlyList<DataAgentScenarioTerm> terms =
            DataAgentScenarioKnowledgePackProvider.ResolveTerms(pack, "看看工程门禁里最近失败的必需项");

        Assert.Multiple(() =>
        {
            Assert.That(terms.Select(term => term.Dataset), Does.Contain("engineering_gate"));
            Assert.That(terms.Select(term => term.Dataset), Does.Contain("test_run"));
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("name"));
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("status"));
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("required"));
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("suite_name"));
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("failed"));
        });
    }

    [Test]
    public void PackValidationRejectsDuplicateTerms()
    {
        string directory = TestContext.CurrentContext.WorkDirectory;
        string path = Path.Combine(directory, "duplicate-scenario-pack.json");
        File.WriteAllText(path, """
        {
          "scenario": "duplicate",
          "culture": "zh-CN",
          "terms": [
            { "term": "工程门禁", "aliases": [], "dataset": "engineering_gate", "fields": ["name"] },
            { "term": "工程门禁", "aliases": [], "dataset": "engineering_gate", "fields": ["status"] }
          ],
          "metrics": []
        }
        """, Encoding.UTF8);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentScenarioKnowledgePackProvider.Load(path))!;

        Assert.That(exception.Message, Does.Contain("Duplicate scenario term"));
    }

    [TestCase(
        """{ "name": " ", "field": "status", "operator": "=", "value": "passed" }""",
        "name")]
    [TestCase(
        """{ "name": "失败", "field": " ", "operator": "=", "value": "passed" }""",
        "field")]
    [TestCase(
        """{ "name": "失败", "field": "status", "operator": " ", "value": "passed" }""",
        "operator")]
    [TestCase(
        """{ "name": "失败", "field": "status", "operator": "starts_with", "value": "passed" }""",
        "operator")]
    [TestCase(
        """{ "name": "失败", "field": "status", "operator": "=", "value": { "nested": true } }""",
        "value")]
    public void PackValidationRejectsInvalidMetrics(string metricJson, string expectedMessagePart)
    {
        string path = WriteScenarioPackWithMetric(metricJson);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentScenarioKnowledgePackProvider.Load(path))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("Scenario metric"));
            Assert.That(exception.Message, Does.Contain(expectedMessagePart).IgnoreCase);
        });
    }

    [Test]
    public void LoadedPackCollectionsAreDefensiveReadOnlySnapshots()
    {
        DataAgentScenarioKnowledgePack pack = LoadEngineeringPack();
        DataAgentScenarioTerm engineeringGate = pack.Terms.Single(term => term.Term == "工程门禁");

        AssertCannotPollute(
            pack.Terms,
            new DataAgentScenarioTerm("污染", [], "engineering_gate", ["name"]),
            new DataAgentScenarioTerm("新增", [], "engineering_gate", ["status"]));
        AssertCannotPollute(
            pack.Metrics,
            new DataAgentScenarioMetric("污染", "status", "=", "passed"),
            new DataAgentScenarioMetric("新增", "required", "=", true));
        AssertCannotPollute(engineeringGate.Aliases, "污染", "新增");
        AssertCannotPollute(engineeringGate.Fields, "polluted", "added");

        DataAgentScenarioKnowledgePack fresh = LoadEngineeringPack();
        DataAgentScenarioTerm freshEngineeringGate = fresh.Terms.Single(term => term.Term == "工程门禁");

        Assert.Multiple(() =>
        {
            Assert.That(fresh.Terms.Select(term => term.Term), Does.Not.Contain("污染"));
            Assert.That(fresh.Terms.Select(term => term.Term), Does.Not.Contain("新增"));
            Assert.That(fresh.Metrics.Select(metric => metric.Name), Does.Not.Contain("污染"));
            Assert.That(fresh.Metrics.Select(metric => metric.Name), Does.Not.Contain("新增"));
            Assert.That(freshEngineeringGate.Aliases, Does.Not.Contain("污染"));
            Assert.That(freshEngineeringGate.Aliases, Does.Not.Contain("新增"));
            Assert.That(freshEngineeringGate.Fields, Does.Not.Contain("polluted"));
            Assert.That(freshEngineeringGate.Fields, Does.Not.Contain("added"));
        });
    }

    [Test]
    public void ResolverReturnsEmptyForUnmatchedUtterance()
    {
        DataAgentScenarioKnowledgePack pack = LoadEngineeringPack();

        IReadOnlyList<DataAgentScenarioTerm> terms =
            DataAgentScenarioKnowledgePackProvider.ResolveTerms(pack, "今天天气怎么样");

        Assert.That(terms, Is.Empty);
    }

    static DataAgentScenarioKnowledgePack LoadEngineeringPack()
    {
        return DataAgentScenarioKnowledgePackProvider.Load(EngineeringPackPath());
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

    static string WriteScenarioPackWithMetric(string metricJson)
    {
        string path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}-scenario-pack.json");
        File.WriteAllText(path, $$"""
        {
          "scenario": "validation",
          "culture": "zh-CN",
          "terms": [
            { "term": "工程门禁", "aliases": [], "dataset": "engineering_gate", "fields": ["name"] }
          ],
          "metrics": [
            {{metricJson}}
          ]
        }
        """, Encoding.UTF8);

        return path;
    }

    static void AssertCannotPollute<T>(IReadOnlyList<T> values, T replacement, T addition)
    {
        Assert.That(values, Is.Not.TypeOf<T[]>());

        if (values is not IList<T> list)
        {
            return;
        }

        Assert.That(list.IsReadOnly, Is.True);

        if (list.Count > 0)
        {
            Assert.Throws<NotSupportedException>(() => { list[0] = replacement; });
        }

        Assert.Throws<NotSupportedException>(() => list.Add(addition));
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

- [ ] **Step 2: Run provider tests to verify the catalog-field test fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioKnowledgePackProviderTests" -v:minimal
```

Expected: FAIL with messages naming invalid current pack fields such as `test_run.name`, `test_run.status`, `test_run.failed_count`, `test_run.started_at`, or `runtime_readiness_check.name`; the resolver test can also fail until the `最近失败` alias is added.

- [ ] **Step 3: Replace the engineering pack with catalog-safe UTF-8 JSON**

Replace `docs/dataagent/scenario-packs/engineering.zh-CN.json` with:

```json
{
  "scenario": "engineering_readiness",
  "culture": "zh-CN",
  "terms": [
    {
      "term": "工程门禁",
      "aliases": ["门禁", "工程检查", "质量门禁"],
      "dataset": "engineering_gate",
      "fields": ["name", "status", "required", "evidence_path"]
    },
    {
      "term": "最近失败的测试",
      "aliases": ["失败测试", "测试失败", "最近失败", "最近测试失败"],
      "dataset": "test_run",
      "fields": ["suite_name", "failed", "total", "ran_at", "command"]
    },
    {
      "term": "缺失项",
      "aliases": ["缺口", "未完成项", "缺失检查"],
      "dataset": "runtime_readiness_check",
      "fields": ["capability", "status", "required", "failure_reason", "evidence_path"]
    },
    {
      "term": "文档证据",
      "aliases": ["证据文档", "设计文档", "计划文档"],
      "dataset": "document_index",
      "fields": ["path", "title", "tags"]
    }
  ],
  "metrics": [
    {
      "name": "失败",
      "field": "status",
      "operator": "!=",
      "value": "passed"
    },
    {
      "name": "必需",
      "field": "required",
      "operator": "=",
      "value": true
    }
  ]
}
```

- [ ] **Step 4: Run provider tests to verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioKnowledgePackProviderTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add -f docs/dataagent/scenario-packs/engineering.zh-CN.json
git add Tests/Alife.Test.DataAgent/DataAgentScenarioKnowledgePackProviderTests.cs
git commit -m "Normalize DataAgent scenario pack encoding"
```

Expected: commit succeeds.

---

### Task 2: Scenario Context Model and Builder

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContext.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextBuilder.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentScenarioContextBuilderTests.cs`

- [ ] **Step 1: Write failing builder tests**

Create `Tests/Alife.Test.DataAgent/DataAgentScenarioContextBuilderTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentScenarioContextBuilderTests
{
    [Test]
    public void BuildMapsEngineeringUtteranceToCatalogSafeHints()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            EngineeringPack(),
            "看看工程门禁里最近失败的必需项");

        Assert.Multiple(() =>
        {
            Assert.That(context.Scenario, Is.EqualTo("engineering_readiness"));
            Assert.That(context.Culture, Is.EqualTo("zh-CN"));
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.HasMatches, Is.True);
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(context.CandidateFields, Does.Contain("name"));
            Assert.That(context.CandidateFields, Does.Contain("status"));
            Assert.That(context.CandidateFields, Does.Contain("required"));
            Assert.That(context.CandidateFields, Does.Contain("suite_name"));
            Assert.That(context.CandidateFields, Does.Contain("failed"));
            Assert.That(context.Terms.Select(term => term.Term), Is.EqualTo(new[] { "工程门禁", "最近失败的测试" }));
            Assert.That(context.Terms.Single(term => term.Term == "工程门禁").MatchedText, Is.EqualTo("工程门禁"));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败", "必需" }));
            Assert.That(context.Metrics.Single(metric => metric.Name == "失败").Field, Is.EqualTo("status"));
            Assert.That(context.Metrics.Single(metric => metric.Name == "必需").Field, Is.EqualTo("required"));
        });
    }

    [Test]
    public void BuildMatchesAliasesAndPreservesMatchedText()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            EngineeringPack(),
            "质量门禁最近测试失败");

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.Terms.Select(term => term.Term), Is.EqualTo(new[] { "工程门禁", "最近失败的测试" }));
            Assert.That(context.Terms.Single(term => term.Term == "工程门禁").MatchedText, Is.EqualTo("质量门禁"));
            Assert.That(context.Terms.Single(term => term.Term == "最近失败的测试").MatchedText, Is.EqualTo("测试失败"));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败" }));
        });
    }

    [Test]
    public void BuildReturnsNoMatchForUnmatchedUtterance()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            EngineeringPack(),
            "今天天气怎么样");

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonNoMatch));
            Assert.That(context.HasMatches, Is.False);
            Assert.That(context.Terms, Is.Empty);
            Assert.That(context.Metrics, Is.Empty);
            Assert.That(context.CandidateDatasets, Is.Empty);
            Assert.That(context.CandidateFields, Is.Empty);
        });
    }

    [Test]
    public void BuildReturnsPackUnavailableWhenPackIsMissing()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            null,
            "看看工程门禁");

        Assert.Multiple(() =>
        {
            Assert.That(context.Scenario, Is.EqualTo("unavailable"));
            Assert.That(context.Culture, Is.EqualTo("und"));
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonPackUnavailable));
            Assert.That(context.HasMatches, Is.False);
        });
    }

    [Test]
    public void BuildDropsCatalogMismatchedDatasetsAndFields()
    {
        DataAgentScenarioKnowledgePack pack = new(
            "bad_pack",
            "zh-CN",
            [
                new DataAgentScenarioTerm("未知表", [], "unknown_dataset", ["name"]),
                new DataAgentScenarioTerm("工程门禁", [], "engineering_gate", ["unknown_field"])
            ],
            [
                new DataAgentScenarioMetric("未知指标", "unknown_metric_field", "=", true)
            ]);

        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            pack,
            "未知表 工程门禁 未知指标");

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonCatalogMismatch));
            Assert.That(context.HasMatches, Is.False);
            Assert.That(context.Terms, Is.Empty);
            Assert.That(context.Metrics, Is.Empty);
            Assert.That(context.CandidateDatasets, Is.Empty);
            Assert.That(context.CandidateFields, Is.Empty);
        });
    }

    [Test]
    public void ContextCollectionsAreDefensiveReadOnlySnapshots()
    {
        string[] fields = ["name", "status"];
        DataAgentScenarioTermMatch term = new("工程门禁", "engineering_gate", fields, "工程门禁");
        fields[0] = "polluted";

        DataAgentScenarioContext context = new(
            "engineering_readiness",
            "zh-CN",
            [term],
            [new DataAgentScenarioMetricMatch("失败", "status", "!=", "passed")],
            ["engineering_gate"],
            ["name", "status"],
            DataAgentScenarioContext.ReasonMatched);

        Assert.Multiple(() =>
        {
            Assert.That(term.Fields, Is.EqualTo(new[] { "name", "status" }));
            Assert.That(term.Fields, Is.Not.TypeOf<string[]>());
            Assert.That(context.Terms, Is.Not.TypeOf<DataAgentScenarioTermMatch[]>());
            Assert.That(context.Metrics, Is.Not.TypeOf<DataAgentScenarioMetricMatch[]>());
            Assert.That(context.CandidateDatasets, Is.Not.TypeOf<string[]>());
            Assert.That(context.CandidateFields, Is.Not.TypeOf<string[]>());
        });
    }

    static DataAgentScenarioKnowledgePack EngineeringPack()
    {
        return new DataAgentScenarioKnowledgePack(
            "engineering_readiness",
            "zh-CN",
            [
                new DataAgentScenarioTerm("工程门禁", ["门禁", "工程检查", "质量门禁"], "engineering_gate", ["name", "status", "required", "evidence_path"]),
                new DataAgentScenarioTerm("最近失败的测试", ["失败测试", "测试失败", "最近失败", "最近测试失败"], "test_run", ["suite_name", "failed", "total", "ran_at", "command"]),
                new DataAgentScenarioTerm("缺失项", ["缺口", "未完成项", "缺失检查"], "runtime_readiness_check", ["capability", "status", "required", "failure_reason", "evidence_path"]),
                new DataAgentScenarioTerm("文档证据", ["证据文档", "设计文档", "计划文档"], "document_index", ["path", "title", "tags"])
            ],
            [
                new DataAgentScenarioMetric("失败", "status", "!=", "passed"),
                new DataAgentScenarioMetric("必需", "required", "=", true)
            ]);
    }
}
```

- [ ] **Step 2: Run builder tests to verify they fail at compile time**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioContextBuilderTests" -v:minimal
```

Expected: FAIL with compile errors for missing `DataAgentScenarioContext`, `DataAgentScenarioTermMatch`, `DataAgentScenarioMetricMatch`, or `DataAgentScenarioContextBuilder`.

- [ ] **Step 3: Add the immutable scenario context model**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContext.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentScenarioContext
{
    public const string ReasonMatched = "scenario_context_matched";
    public const string ReasonNoMatch = "scenario_context_no_match";
    public const string ReasonCatalogMismatch = "scenario_context_catalog_mismatch";
    public const string ReasonPackUnavailable = "scenario_context_pack_unavailable";

    public DataAgentScenarioContext(
        string scenario,
        string culture,
        IEnumerable<DataAgentScenarioTermMatch>? terms,
        IEnumerable<DataAgentScenarioMetricMatch>? metrics,
        IEnumerable<string>? candidateDatasets,
        IEnumerable<string>? candidateFields,
        string reasonCode)
    {
        Scenario = string.IsNullOrWhiteSpace(scenario) ? "unknown" : scenario;
        Culture = string.IsNullOrWhiteSpace(culture) ? "und" : culture;
        Terms = Snapshot(terms);
        Metrics = Snapshot(metrics);
        CandidateDatasets = Snapshot(candidateDatasets);
        CandidateFields = Snapshot(candidateFields);
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? ReasonNoMatch : reasonCode;
    }

    public string Scenario { get; }

    public string Culture { get; }

    public IReadOnlyList<DataAgentScenarioTermMatch> Terms { get; }

    public IReadOnlyList<DataAgentScenarioMetricMatch> Metrics { get; }

    public IReadOnlyList<string> CandidateDatasets { get; }

    public IReadOnlyList<string> CandidateFields { get; }

    public string ReasonCode { get; }

    public bool HasMatches => Terms.Count > 0 || Metrics.Count > 0;

    static IReadOnlyList<T> Snapshot<T>(IEnumerable<T>? values)
    {
        return values is null
            ? Array.AsReadOnly(Array.Empty<T>())
            : Array.AsReadOnly(values.ToArray());
    }
}

public sealed record DataAgentScenarioTermMatch
{
    public DataAgentScenarioTermMatch(
        string term,
        string dataset,
        IEnumerable<string>? fields,
        string matchedText)
    {
        Term = term;
        Dataset = dataset;
        Fields = fields is null
            ? Array.AsReadOnly(Array.Empty<string>())
            : Array.AsReadOnly(fields.ToArray());
        MatchedText = matchedText;
    }

    public string Term { get; }

    public string Dataset { get; }

    public IReadOnlyList<string> Fields { get; }

    public string MatchedText { get; }
}

public sealed record DataAgentScenarioMetricMatch(
    string Name,
    string Field,
    string Operator,
    object? Value);
```

- [ ] **Step 4: Add the deterministic context builder**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextBuilder.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentScenarioContextBuilder
{
    public DataAgentScenarioContext Build(
        DataAgentCatalog catalog,
        DataAgentScenarioKnowledgePack? pack,
        string? utterance)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (pack is null)
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

        if (string.IsNullOrWhiteSpace(utterance))
        {
            return Empty(pack, DataAgentScenarioContext.ReasonNoMatch);
        }

        List<DataAgentScenarioTermMatch> terms = [];
        List<DataAgentScenarioMetricMatch> metrics = [];
        List<string> candidateDatasets = [];
        List<string> candidateFields = [];
        bool matchedAnyText = false;
        bool catalogMismatch = false;

        foreach (DataAgentScenarioTerm term in pack.Terms)
        {
            string? matchedText = FindMatchedText(term, utterance);
            if (matchedText is null)
                continue;

            matchedAnyText = true;

            if (catalog.HasDataset(term.Dataset) == false)
            {
                catalogMismatch = true;
                continue;
            }

            string[] validFields = term.Fields
                .Where(field => catalog.HasField(term.Dataset, field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (validFields.Length == 0)
            {
                catalogMismatch = true;
                continue;
            }

            terms.Add(new DataAgentScenarioTermMatch(term.Term, term.Dataset, validFields, matchedText));
            AddDistinct(candidateDatasets, term.Dataset);

            foreach (string field in validFields)
                AddDistinct(candidateFields, field);
        }

        foreach (DataAgentScenarioMetric metric in pack.Metrics)
        {
            if (Contains(utterance, metric.Name) == false)
                continue;

            matchedAnyText = true;

            if (MetricFieldIsKnown(catalog, candidateDatasets, metric.Field) == false)
            {
                catalogMismatch = true;
                continue;
            }

            metrics.Add(new DataAgentScenarioMetricMatch(
                metric.Name,
                metric.Field,
                metric.Operator,
                metric.Value));
        }

        string reasonCode = terms.Count > 0 || metrics.Count > 0
            ? DataAgentScenarioContext.ReasonMatched
            : matchedAnyText || catalogMismatch
                ? DataAgentScenarioContext.ReasonCatalogMismatch
                : DataAgentScenarioContext.ReasonNoMatch;

        return new DataAgentScenarioContext(
            pack.Scenario,
            pack.Culture,
            terms,
            metrics,
            candidateDatasets,
            candidateFields,
            reasonCode);
    }

    static DataAgentScenarioContext Empty(
        DataAgentScenarioKnowledgePack pack,
        string reasonCode)
    {
        return new DataAgentScenarioContext(
            pack.Scenario,
            pack.Culture,
            [],
            [],
            [],
            [],
            reasonCode);
    }

    static string? FindMatchedText(DataAgentScenarioTerm term, string utterance)
    {
        if (Contains(utterance, term.Term))
            return term.Term;

        return term.Aliases.FirstOrDefault(alias => Contains(utterance, alias));
    }

    static bool Contains(string utterance, string value)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               utterance.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    static bool MetricFieldIsKnown(
        DataAgentCatalog catalog,
        IReadOnlyList<string> candidateDatasets,
        string field)
    {
        if (candidateDatasets.Count > 0)
            return candidateDatasets.Any(dataset => catalog.HasField(dataset, field));

        return catalog.Datasets.Any(dataset => catalog.HasField(dataset.Name, field));
    }

    static void AddDistinct(List<string> values, string value)
    {
        if (values.Contains(value, StringComparer.OrdinalIgnoreCase) == false)
            values.Add(value);
    }
}
```

- [ ] **Step 5: Run builder tests to verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioContextBuilderTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 2**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContext.cs
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextBuilder.cs
git add Tests/Alife.Test.DataAgent/DataAgentScenarioContextBuilderTests.cs
git commit -m "Add DataAgent scenario context builder"
```

Expected: commit succeeds.

---

### Task 3: Planner Prompt Integration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryRequest.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs`
- Modify: `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs`

- [ ] **Step 1: Write failing prompt formatter tests**

In `Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs`, add:

```csharp
[Test]
public void FormatIncludesScenarioContextWhenMatched()
{
    string databasePath = CreateDatabasePath();
    DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
    DataAgentScenarioContext scenarioContext = new DataAgentScenarioContextBuilder().Build(
        catalog,
        EngineeringPack(),
        "看看工程门禁里最近失败的必需项");

    DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
        new DataAgentQueryRequest("看看工程门禁里最近失败的必需项", "developer", "zh-CN", false, scenarioContext),
        catalog,
        snapshot,
        scenarioContext);

    Assert.Multiple(() =>
    {
        Assert.That(prompt.Schema, Does.Contain("Scenario context:"));
        Assert.That(prompt.Schema, Does.Contain("scenario: engineering_readiness"));
        Assert.That(prompt.Schema, Does.Contain("reason_code: scenario_context_matched"));
        Assert.That(prompt.Schema, Does.Contain("candidate_datasets: engineering_gate, test_run"));
        Assert.That(prompt.Schema, Does.Contain("candidate_fields: name, status, required, evidence_path, suite_name, failed, total, ran_at, command"));
        Assert.That(prompt.Schema, Does.Contain("工程门禁 -> engineering_gate(name,status,required,evidence_path)"));
        Assert.That(prompt.Schema, Does.Contain("最近失败的测试 -> test_run(suite_name,failed,total,ran_at,command)"));
        Assert.That(prompt.Schema, Does.Contain("失败: status != passed"));
        Assert.That(prompt.Schema, Does.Contain("必需: required = true"));
        Assert.That(prompt.Schema, Does.Contain("Scenario context is a hint only"));
        Assert.That(prompt.Schema, Does.Contain("Do not output SQL"));
    });
}

[Test]
public void FormatOmitsScenarioContextWhenNoMatch()
{
    string databasePath = CreateDatabasePath();
    DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
    DataAgentScenarioContext scenarioContext = new DataAgentScenarioContext(
        "engineering_readiness",
        "zh-CN",
        [],
        [],
        [],
        [],
        DataAgentScenarioContext.ReasonNoMatch);

    DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
        new DataAgentQueryRequest("今天天气怎么样", "developer", "zh-CN", false, scenarioContext),
        catalog,
        snapshot,
        scenarioContext);

    Assert.That(prompt.Schema, Does.Not.Contain("Scenario context:"));
}

[Test]
public void FormatDoesNotEmitRawSqlInScenarioContext()
{
    string databasePath = CreateDatabasePath();
    DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
    DataAgentScenarioContext scenarioContext = new(
        "engineering_readiness",
        "zh-CN",
        [new DataAgentScenarioTermMatch("工程门禁", "engineering_gate", ["name", "status"], "工程门禁")],
        [],
        ["engineering_gate"],
        ["name", "status"],
        DataAgentScenarioContext.ReasonMatched);

    DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
        new DataAgentQueryRequest("工程门禁", "developer", "zh-CN", false, scenarioContext),
        catalog,
        snapshot,
        scenarioContext);

    string scenarioSection = prompt.Schema[prompt.Schema.IndexOf("Scenario context:", StringComparison.Ordinal)..];

    Assert.Multiple(() =>
    {
        Assert.That(scenarioSection, Does.Not.Contain("SELECT").IgnoreCase);
        Assert.That(scenarioSection, Does.Not.Contain("DELETE").IgnoreCase);
        Assert.That(scenarioSection, Does.Not.Contain("DROP").IgnoreCase);
        Assert.That(scenarioSection, Does.Not.Contain(";"));
    });
}

static DataAgentScenarioKnowledgePack EngineeringPack()
{
    return new DataAgentScenarioKnowledgePack(
        "engineering_readiness",
        "zh-CN",
        [
            new DataAgentScenarioTerm("工程门禁", ["门禁", "工程检查", "质量门禁"], "engineering_gate", ["name", "status", "required", "evidence_path"]),
            new DataAgentScenarioTerm("最近失败的测试", ["失败测试", "测试失败", "最近失败", "最近测试失败"], "test_run", ["suite_name", "failed", "total", "ran_at", "command"])
        ],
        [
            new DataAgentScenarioMetric("失败", "status", "!=", "passed"),
            new DataAgentScenarioMetric("必需", "required", "=", true)
        ]);
}
```

- [ ] **Step 2: Write failing LLM planner pass-through test**

In `Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs`, add the test:

```csharp
[Test]
public void PlanPassesScenarioContextToPromptFormatter()
{
    string databasePath = CreateDatabasePath();
    DataAgentScenarioContext scenarioContext = new(
        "engineering_readiness",
        "zh-CN",
        [new DataAgentScenarioTermMatch("工程门禁", "engineering_gate", ["name", "status", "required"], "工程门禁")],
        [new DataAgentScenarioMetricMatch("必需", "required", "=", true)],
        ["engineering_gate"],
        ["name", "status", "required"],
        DataAgentScenarioContext.ReasonMatched);
    DataAgentLlmPlannerPrompt? capturedPrompt = null;
    LlmDataAgentQueryPlanner planner = new(
        databasePath,
        new FakeLlmPlannerClient(ValidPlanJson, prompt => capturedPrompt = prompt),
        new DeterministicDataAgentQueryPlanner());

    planner.Plan(new DataAgentQueryRequest("看看工程门禁里的必需项", "developer", "zh-CN", false, scenarioContext));

    Assert.Multiple(() =>
    {
        Assert.That(capturedPrompt, Is.Not.Null);
        Assert.That(capturedPrompt!.Schema, Does.Contain("Scenario context:"));
        Assert.That(capturedPrompt.Schema, Does.Contain("工程门禁 -> engineering_gate(name,status,required)"));
        Assert.That(capturedPrompt.Schema, Does.Contain("必需: required = true"));
        Assert.That(capturedPrompt.System, Does.Contain("Do not output SQL"));
    });
}
```

Update the fake client at the bottom of that file:

```csharp
sealed class FakeLlmPlannerClient(string output, Action<DataAgentLlmPlannerPrompt>? onPrompt = null) : ILlmDataAgentPlannerClient
{
    public string Complete(DataAgentLlmPlannerPrompt prompt)
    {
        onPrompt?.Invoke(prompt);

        Assert.Multiple(() =>
        {
            Assert.That(prompt.System, Does.Contain("DataAgent LLM planner"));
            Assert.That(prompt.Schema, Does.Contain("document_index"));
            Assert.That(prompt.Contract, Does.Contain("\"planner_name\":\"LlmDataAgentQueryPlanner\""));
            Assert.That(prompt.User, Does.Contain("Role: developer"));
        });

        return output;
    }
}
```

- [ ] **Step 3: Run prompt and planner tests to verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~LlmDataAgentPlannerPromptFormatterTests|FullyQualifiedName~LlmDataAgentQueryPlannerTests" -v:minimal
```

Expected: FAIL with compile errors for the new formatter overload or missing `ScenarioContext` property.

- [ ] **Step 4: Add optional scenario context to query requests**

Replace `sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryRequest.cs` with:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentQueryRequest(
    string Question,
    string Role,
    string Locale,
    bool AllowLiveSources,
    DataAgentScenarioContext? ScenarioContext = null);
```

- [ ] **Step 5: Extend the prompt formatter**

In `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs`, add `using System.Globalization;` and update the formatter with these exact methods.

```csharp
public DataAgentLlmPlannerPrompt Format(
    DataAgentQueryRequest request,
    DataAgentCatalog catalog,
    DataAgentSchemaSnapshot schemaSnapshot)
{
    return Format(request, catalog, schemaSnapshot, null);
}

public DataAgentLlmPlannerPrompt Format(
    DataAgentQueryRequest request,
    DataAgentCatalog catalog,
    DataAgentSchemaSnapshot schemaSnapshot,
    DataAgentScenarioContext? scenarioContext)
{
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(catalog);
    ArgumentNullException.ThrowIfNull(schemaSnapshot);

    if (schemaSnapshot.CatalogMatchesDatabase == false)
        throw new InvalidOperationException("DataAgent LLM planner requires catalog and SQLite schema to match.");

    return new DataAgentLlmPlannerPrompt(
        BuildSystem(),
        BuildSchema(catalog, schemaSnapshot, scenarioContext),
        BuildContract(),
        BuildUser(request));
}

static string BuildSchema(
    DataAgentCatalog catalog,
    DataAgentSchemaSnapshot schemaSnapshot,
    DataAgentScenarioContext? scenarioContext)
{
    StringBuilder builder = new();
    builder.AppendLine("Approved schema:");

    foreach (DataAgentDatasetSchema datasetSchema in schemaSnapshot.Datasets
        .Where(dataset => catalog.HasDataset(dataset.Name) && dataset.ExistsInDatabase && dataset.FieldsMatch)
        .OrderBy(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase))
    {
        string[] fields = datasetSchema.DatabaseFields
            .Where(field => catalog.HasField(datasetSchema.Name, field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (fields.Length == 0)
            continue;

        builder.Append("- ");
        builder.Append(datasetSchema.Name);
        builder.Append(": ");
        builder.AppendJoin(", ", fields);
        builder.AppendLine();
    }

    AppendScenarioContext(builder, scenarioContext);

    return builder.ToString();
}

static void AppendScenarioContext(
    StringBuilder builder,
    DataAgentScenarioContext? scenarioContext)
{
    if (scenarioContext is null || scenarioContext.HasMatches == false)
        return;

    builder.AppendLine();
    builder.AppendLine("Scenario context:");
    builder.Append("- scenario: ");
    builder.AppendLine(OneLine(scenarioContext.Scenario));
    builder.Append("- reason_code: ");
    builder.AppendLine(OneLine(scenarioContext.ReasonCode));
    builder.Append("- candidate_datasets: ");
    builder.AppendLine(string.Join(", ", scenarioContext.CandidateDatasets.Select(OneLine)));
    builder.Append("- candidate_fields: ");
    builder.AppendLine(string.Join(", ", scenarioContext.CandidateFields.Select(OneLine)));
    builder.AppendLine("- matched_terms:");

    foreach (DataAgentScenarioTermMatch term in scenarioContext.Terms)
    {
        builder.Append("  - ");
        builder.Append(OneLine(term.Term));
        builder.Append(" -> ");
        builder.Append(OneLine(term.Dataset));
        builder.Append('(');
        builder.AppendJoin(",", term.Fields.Select(OneLine));
        builder.AppendLine(")");
    }

    if (scenarioContext.Metrics.Count > 0)
    {
        builder.AppendLine("- matched_metrics:");

        foreach (DataAgentScenarioMetricMatch metric in scenarioContext.Metrics)
        {
            builder.Append("  - ");
            builder.Append(OneLine(metric.Name));
            builder.Append(": ");
            builder.Append(OneLine(metric.Field));
            builder.Append(' ');
            builder.Append(OneLine(metric.Operator));
            builder.Append(' ');
            builder.AppendLine(FormatMetricValue(metric.Value));
        }
    }

    builder.AppendLine("- Scenario context is a hint only; use only approved schema fields and operators.");
    builder.AppendLine("- Do not output SQL.");
}

static string OneLine(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

static string FormatMetricValue(object? value)
{
    return value switch
    {
        null => "null",
        bool boolValue => boolValue.ToString().ToLowerInvariant(),
        IFormattable formattable => OneLine(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => OneLine(value.ToString())
    };
}
```

Remove the old three-argument `BuildSchema` method after adding the new version so there is only one schema builder used by `Format`.

- [ ] **Step 6: Pass scenario context through the LLM planner**

In `sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs`, replace:

```csharp
DataAgentLlmPlannerPrompt prompt = formatter.Format(request, catalog, schemaSnapshot);
```

with:

```csharp
DataAgentLlmPlannerPrompt prompt = formatter.Format(request, catalog, schemaSnapshot, request.ScenarioContext);
```

- [ ] **Step 7: Run prompt and planner tests to verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~LlmDataAgentPlannerPromptFormatterTests|FullyQualifiedName~LlmDataAgentQueryPlannerTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 8: Commit Task 3**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryRequest.cs
git add sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs
git add sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs
git add Tests/Alife.Test.DataAgent/LlmDataAgentPlannerPromptFormatterTests.cs
git add Tests/Alife.Test.DataAgent/LlmDataAgentQueryPlannerTests.cs
git commit -m "Integrate scenario context into DataAgent LLM planner prompts"
```

Expected: commit succeeds.

---

### Task 4: Scenario Diagnostics Formatter

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioDiagnosticsFormatter.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentScenarioDiagnosticsFormatterTests.cs`

- [ ] **Step 1: Write failing formatter tests**

Create `Tests/Alife.Test.DataAgent/DataAgentScenarioDiagnosticsFormatterTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentScenarioDiagnosticsFormatterTests
{
    [Test]
    public void FormatEmitsCompactMatchedDiagnostics()
    {
        DataAgentScenarioContext context = new(
            "engineering_readiness",
            "zh-CN",
            [
                new DataAgentScenarioTermMatch("工程门禁", "engineering_gate", ["name", "status", "required"], "工程门禁"),
                new DataAgentScenarioTermMatch("最近失败的测试", "test_run", ["suite_name", "failed", "ran_at"], "最近失败的测试")
            ],
            [
                new DataAgentScenarioMetricMatch("失败", "status", "!=", "passed"),
                new DataAgentScenarioMetricMatch("必需", "required", "=", true)
            ],
            ["engineering_gate", "test_run"],
            ["name", "status", "required", "suite_name", "failed", "ran_at"],
            DataAgentScenarioContext.ReasonMatched);

        string text = DataAgentScenarioDiagnosticsFormatter.Format(context);

        Assert.That(text, Is.EqualTo(string.Join(Environment.NewLine,
            "DataAgent scenario diagnostics",
            "scenario=engineering_readiness",
            "reason=scenario_context_matched",
            "datasets=engineering_gate,test_run",
            "fields=name,status,required,suite_name,failed,ran_at",
            "terms=工程门禁:engineering_gate;最近失败的测试:test_run",
            "metrics=失败:status!=passed;必需:required=true")));
    }

    [Test]
    public void FormatEmitsUnavailableWhenContextMissing()
    {
        string text = DataAgentScenarioDiagnosticsFormatter.Format(null);

        Assert.That(text, Is.EqualTo(string.Join(Environment.NewLine,
            "DataAgent scenario diagnostics",
            "state=unavailable",
            "reason=scenario_context_pack_unavailable")));
    }

    [Test]
    public void FormatOmitsRawSqlAndHiddenContext()
    {
        DataAgentScenarioContext context = new(
            "engineering_readiness",
            "zh-CN",
            [new DataAgentScenarioTermMatch("SELECT * FROM hidden_context", "engineering_gate", ["name"], "SELECT")],
            [],
            ["engineering_gate"],
            ["name"],
            DataAgentScenarioContext.ReasonMatched);

        string text = DataAgentScenarioDiagnosticsFormatter.Format(context);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("[redacted]"));
            Assert.That(text, Does.Not.Contain("SELECT").IgnoreCase);
            Assert.That(text, Does.Not.Contain("hidden_context").IgnoreCase);
            Assert.That(text, Does.Not.Contain("[tool_route_context]").IgnoreCase);
            Assert.That(text, Does.Not.Contain("[data_agent_evidence_pack]").IgnoreCase);
        });
    }
}
```

- [ ] **Step 2: Run diagnostics tests to verify they fail at compile time**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioDiagnosticsFormatterTests" -v:minimal
```

Expected: FAIL with compile errors for missing `DataAgentScenarioDiagnosticsFormatter`.

- [ ] **Step 3: Add the diagnostics formatter**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioDiagnosticsFormatter.cs`:

```csharp
using System.Globalization;

namespace Alife.Function.DataAgent;

public static class DataAgentScenarioDiagnosticsFormatter
{
    static readonly string[] UnsafeMarkers =
    [
        "hidden_context",
        "[tool_route_context]",
        "[data_agent_evidence_pack]",
        "Allowed XML tools"
    ];

    static readonly string[] SqlKeywords =
    [
        "SELECT",
        "DROP",
        "DELETE",
        "INSERT",
        "UPDATE",
        "ALTER",
        "ATTACH",
        "PRAGMA",
        "TABLE"
    ];

    public static string Format(DataAgentScenarioContext? context)
    {
        if (context is null)
        {
            return string.Join(Environment.NewLine,
                "DataAgent scenario diagnostics",
                "state=unavailable",
                $"reason={DataAgentScenarioContext.ReasonPackUnavailable}");
        }

        return string.Join(Environment.NewLine,
            "DataAgent scenario diagnostics",
            $"scenario={Safe(context.Scenario)}",
            $"reason={Safe(context.ReasonCode)}",
            $"datasets={string.Join(",", context.CandidateDatasets.Select(Safe))}",
            $"fields={string.Join(",", context.CandidateFields.Select(Safe))}",
            $"terms={FormatTerms(context.Terms)}",
            $"metrics={FormatMetrics(context.Metrics)}");
    }

    static string FormatTerms(IReadOnlyList<DataAgentScenarioTermMatch> terms)
    {
        return string.Join(";", terms.Select(term =>
            $"{Safe(term.Term)}:{Safe(term.Dataset)}"));
    }

    static string FormatMetrics(IReadOnlyList<DataAgentScenarioMetricMatch> metrics)
    {
        return string.Join(";", metrics.Select(metric =>
            $"{Safe(metric.Name)}:{Safe(metric.Field)}{Safe(metric.Operator)}{FormatValue(metric.Value)}"));
    }

    static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool boolValue => boolValue.ToString().ToLowerInvariant(),
            IFormattable formattable => Safe(formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => Safe(value.ToString())
        };
    }

    static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        foreach (string marker in UnsafeMarkers)
            sanitized = ReplaceInsensitive(sanitized, marker, "[redacted]");

        foreach (string keyword in SqlKeywords)
            sanitized = ReplaceKeyword(sanitized, keyword);

        return string.Join(' ', sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static string ReplaceInsensitive(string value, string marker, string replacement)
    {
        int index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            value = value[..index] + replacement + value[(index + marker.Length)..];
            index = value.IndexOf(marker, index + replacement.Length, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    static string ReplaceKeyword(string value, string keyword)
    {
        int start = 0;
        while (start < value.Length)
        {
            int index = value.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return value;

            int end = index + keyword.Length;
            if (IsBoundary(value, index - 1) && IsBoundary(value, end))
            {
                value = value[..index] + "[redacted]" + value[end..];
                start = index + "[redacted]".Length;
            }
            else
            {
                start = end;
            }
        }

        return value;
    }

    static bool IsBoundary(string value, int index)
    {
        return index < 0 ||
               index >= value.Length ||
               char.IsLetterOrDigit(value[index]) == false && value[index] != '_';
    }
}
```

- [ ] **Step 4: Run diagnostics tests to verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioDiagnosticsFormatterTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 5: Commit Task 4**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioDiagnosticsFormatter.cs
git add Tests/Alife.Test.DataAgent/DataAgentScenarioDiagnosticsFormatterTests.cs
git commit -m "Add DataAgent scenario diagnostics formatter"
```

Expected: commit succeeds.

---

### Task 5: Readiness and QChat Engineering Map Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV211ReadinessTests.cs`

- [ ] **Step 1: Write focused V2.11 readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV211ReadinessTests.cs`:

```csharp
using Alife.Function.DataAgent;
using System.Text;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV211ReadinessTests
{
    [Test]
    public void ScenarioContextNarrowsPlannerAttentionWithoutSqlAuthority()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            catalog,
            LoadEngineeringPack(),
            "看看工程门禁里最近失败的必需项");

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("看看工程门禁里最近失败的必需项", "developer", "zh-CN", false, context),
            catalog,
            snapshot,
            context);

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(context.CandidateFields, Does.Contain("required"));
            Assert.That(context.CandidateFields, Does.Contain("failed"));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败", "必需" }));
            Assert.That(prompt.Schema, Does.Contain("Scenario context:"));
            Assert.That(prompt.Schema, Does.Contain("Scenario context is a hint only"));
            Assert.That(prompt.System, Does.Contain("Do not output SQL"));
            Assert.That(typeof(DataAgentQueryPlanValidator).IsClass, Is.True);
            Assert.That(typeof(DataAgentSqlCompiler).IsClass, Is.True);
            Assert.That(typeof(DataAgentSqlSafetyValidator).IsClass, Is.True);
            Assert.That(typeof(DataAgentQueryExecutor).IsClass, Is.True);
        });
    }

    [Test]
    public void ScenarioDiagnosticsAreDataAgentOwnedAndOwnerSafe()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            LoadEngineeringPack(),
            "看看工程门禁里最近失败的必需项");

        string diagnostics = DataAgentScenarioDiagnosticsFormatter.Format(context);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Does.Contain("DataAgent scenario diagnostics"));
            Assert.That(diagnostics, Does.Contain("reason=scenario_context_matched"));
            Assert.That(diagnostics, Does.Contain("datasets=engineering_gate,test_run"));
            Assert.That(diagnostics, Does.Contain("metrics=失败:status!=passed;必需:required=true"));
            Assert.That(diagnostics, Does.Not.Contain("SELECT").IgnoreCase);
            Assert.That(diagnostics, Does.Not.Contain("[tool_route_context]").IgnoreCase);
            Assert.That(diagnostics, Does.Not.Contain("[data_agent_evidence_pack]").IgnoreCase);
            Assert.That(diagnostics, Does.Not.Contain("hidden_context").IgnoreCase);
        });
    }

    [Test]
    public void ScenarioPackFileRemainsReadableUtf8()
    {
        string json = File.ReadAllText(EngineeringPackPath(), Encoding.UTF8);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("工程门禁"));
            Assert.That(json, Does.Contain("最近失败的测试"));
            Assert.That(json, Does.Contain("缺失项"));
            Assert.That(json, Does.Contain("文档证据"));
            Assert.That(json, Does.Contain("失败"));
            Assert.That(json, Does.Contain("必需"));
            Assert.That(json, Does.Not.Contain("宸ョ▼"));
            Assert.That(json, Does.Not.Contain("澶辫触"));
        });
    }

    static DataAgentScenarioKnowledgePack LoadEngineeringPack()
    {
        return DataAgentScenarioKnowledgePackProvider.Load(EngineeringPackPath());
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

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v211-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
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

- [ ] **Step 2: Update broad DataAgent readiness tests before implementation**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`:

Replace:

```csharp
Assert.That(checks, Has.Count.EqualTo(65));
```

with:

```csharp
Assert.That(checks, Has.Count.EqualTo(66));
```

Add near the existing V2.10 governance assertions:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentScenarioContextIntegrated"));
DataAgentReadinessCheck scenarioContextCheck = checks.Single(check => check.Name == "DataAgentScenarioContextIntegrated");
Assert.That(scenarioContextCheck.Detail, Does.Contain("scenario_context=true"));
Assert.That(scenarioContextCheck.Detail, Does.Contain("prompt_hint=true"));
Assert.That(scenarioContextCheck.Detail, Does.Contain("owner_diag=true"));
Assert.That(scenarioContextCheck.Detail, Does.Contain("sql_boundary=true"));
```

Replace script summary:

```csharp
"  Summary: 79 required passed, 0 required missing"
```

with:

```csharp
"  Summary: 80 required passed, 0 required missing"
```

Add to the readiness script output assertions:

```csharp
Assert.That(result.StandardOutput, Does.Contain("DataAgentScenarioContextIntegrated"));
```

Replace:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 79"));
```

with:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 80"));
```

In the same file, update the QChat engineering-map assertions that live in `DataAgentReadinessTests`:

Replace:

```csharp
"Summary: 54 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

with:

```csharp
"Summary: 55 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

Replace:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 54"));
```

with:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 55"));
```

Add this test to protect the static script markers:

```csharp
[Test]
public void ReadinessScriptProtectsV211ScenarioContextContract()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "DataAgentScenarioContextIntegrated");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentScenarioContext.cs"));
        Assert.That(declaration, Does.Contain("DataAgentScenarioContextBuilder.cs"));
        Assert.That(declaration, Does.Contain("DataAgentScenarioDiagnosticsFormatter.cs"));
        Assert.That(declaration, Does.Contain("LlmDataAgentPlannerPromptFormatter.cs"));
        Assert.That(declaration, Does.Contain("Scenario context:"));
        Assert.That(declaration, Does.Contain("Do not output SQL"));
        Assert.That(declaration, Does.Contain("DataAgentScenarioContextBuilderTests"));
        Assert.That(declaration, Does.Contain("DataAgentScenarioDiagnosticsFormatterTests"));
        Assert.That(declaration, Does.Contain("DataAgentV211ReadinessTests"));
    });
}
```

- [ ] **Step 3: Update QChat engineering-map tests before implementation**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add the new required check name to `RequiredV2Checks`:

```csharp
"DataAgent scenario context diagnostics"
```

Add this test:

```csharp
[Test]
public void QChatDoesNotDirectlyImportDataAgentScenarioContextBuilder()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string qchatDirectory = Path.Combine(repoRoot, "sources", "Alife.Function", "Alife.Function.QChat");
    string[] qchatFiles = Directory.GetFiles(qchatDirectory, "*.cs", SearchOption.AllDirectories);
    string combinedText = string.Join(Environment.NewLine, qchatFiles.Select(File.ReadAllText));

    Assert.Multiple(() =>
    {
        Assert.That(combinedText, Does.Not.Contain("DataAgentScenarioKnowledgePackProvider"));
        Assert.That(combinedText, Does.Not.Contain("DataAgentScenarioContextBuilder"));
    });
}
```

- [ ] **Step 4: Run readiness and QChat tests to verify the new gates fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV211ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: FAIL until `DataAgentReadiness.cs` and `tools/check-dataagent-readiness.ps1` expose the V2.11 gate.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: FAIL until `tools/check-qchat-engineering-map.ps1` declares the new required check and expected count.

- [ ] **Step 5: Add the runtime readiness gate**

At the top of `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, add:

```csharp
using System.Text;
```

Replace the current scenario-pack block after `DataAgentProgressStreamingPresent` with:

```csharp
string repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
string scenarioPackPath = Path.Combine(repoRoot, "docs", "dataagent", "scenario-packs", "engineering.zh-CN.json");
string scenarioPackText = File.ReadAllText(scenarioPackPath, Encoding.UTF8);
DataAgentScenarioKnowledgePack pack = DataAgentScenarioKnowledgePackProvider.Load(scenarioPackPath);
IReadOnlyList<DataAgentScenarioTerm> resolvedTerms =
    DataAgentScenarioKnowledgePackProvider.ResolveTerms(pack, "看看工程门禁里最近失败的必需项");
bool scenarioPackReadable =
    scenarioPackText.Contains("工程门禁", StringComparison.Ordinal) &&
    scenarioPackText.Contains("最近失败的测试", StringComparison.Ordinal) &&
    scenarioPackText.Contains("缺失项", StringComparison.Ordinal) &&
    scenarioPackText.Contains("文档证据", StringComparison.Ordinal) &&
    scenarioPackText.Contains("失败", StringComparison.Ordinal) &&
    scenarioPackText.Contains("必需", StringComparison.Ordinal) &&
    scenarioPackText.Contains("宸ョ▼", StringComparison.Ordinal) == false &&
    scenarioPackText.Contains("澶辫触", StringComparison.Ordinal) == false;
bool scenarioPackReady =
    scenarioPackReadable &&
    string.Equals(pack.Scenario, "engineering_readiness", StringComparison.Ordinal) &&
    resolvedTerms.Any(term =>
        string.Equals(term.Dataset, "engineering_gate", StringComparison.Ordinal) &&
        term.Fields.Contains("status", StringComparer.Ordinal)) &&
    resolvedTerms.Any(term =>
        string.Equals(term.Dataset, "test_run", StringComparison.Ordinal) &&
        term.Fields.Contains("failed", StringComparer.Ordinal));
checks.Add(scenarioPackReady
    ? Pass("DataAgentScenarioKnowledgePackPresent", "scenario=engineering_readiness;dataset=engineering_gate;field=status;utf8=readable")
    : Fail("DataAgentScenarioKnowledgePackPresent", $"path={scenarioPackPath};terms={string.Join(",", resolvedTerms.Select(term => term.Dataset))};utf8={LowerBool(scenarioPackReadable)}"));

DataAgentCatalog scenarioCatalog = DataAgentCatalog.CreateDefault();
DataAgentScenarioContext scenarioContext = new DataAgentScenarioContextBuilder().Build(
    scenarioCatalog,
    pack,
    "看看工程门禁里最近失败的必需项");
DataAgentLlmPlannerPrompt scenarioPrompt = new LlmDataAgentPlannerPromptFormatter().Format(
    new DataAgentQueryRequest("看看工程门禁里最近失败的必需项", "developer", "zh-CN", false, scenarioContext),
    scenarioCatalog,
    schemaSnapshot,
    scenarioContext);
string scenarioDiagnostics = DataAgentScenarioDiagnosticsFormatter.Format(scenarioContext);
bool scenarioContextReady =
    scenarioContext.ReasonCode == DataAgentScenarioContext.ReasonMatched &&
    scenarioContext.CandidateDatasets.SequenceEqual(new[] { "engineering_gate", "test_run" }) &&
    scenarioContext.CandidateFields.Contains("required", StringComparer.OrdinalIgnoreCase) &&
    scenarioContext.CandidateFields.Contains("failed", StringComparer.OrdinalIgnoreCase) &&
    scenarioContext.Metrics.Select(metric => metric.Name).SequenceEqual(new[] { "失败", "必需" }) &&
    scenarioPrompt.Schema.Contains("Scenario context:", StringComparison.Ordinal) &&
    scenarioPrompt.Schema.Contains("Scenario context is a hint only", StringComparison.Ordinal) &&
    scenarioPrompt.System.Contains("Do not output SQL", StringComparison.Ordinal) &&
    scenarioDiagnostics.Contains("DataAgent scenario diagnostics", StringComparison.Ordinal) &&
    scenarioDiagnostics.Contains("reason=scenario_context_matched", StringComparison.Ordinal) &&
    scenarioDiagnostics.Contains("metrics=失败:status!=passed;必需:required=true", StringComparison.Ordinal) &&
    scenarioDiagnostics.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
    typeof(DataAgentQueryPlanValidator).IsClass &&
    typeof(DataAgentSqlCompiler).IsClass &&
    typeof(DataAgentSqlSafetyValidator).IsClass &&
    typeof(DataAgentQueryExecutor).IsClass;
checks.Add(scenarioContextReady
    ? Pass("DataAgentScenarioContextIntegrated", "scenario_context=true;prompt_hint=true;owner_diag=true;sql_boundary=true")
    : Fail("DataAgentScenarioContextIntegrated", $"reason={scenarioContext.ReasonCode};datasets={string.Join(",", scenarioContext.CandidateDatasets)};prompt_hint={LowerBool(scenarioPrompt.Schema.Contains("Scenario context:", StringComparison.Ordinal))};owner_diag={LowerBool(scenarioDiagnostics.Contains("DataAgent scenario diagnostics", StringComparison.Ordinal))}"));
```

- [ ] **Step 6: Add static readiness script gate**

In `tools/check-dataagent-readiness.ps1`, after `DataAgentScenarioKnowledgePackPresent`, add:

```powershell
    New-Check -Group "Governance" -Name "DataAgentScenarioContextIntegrated" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContext.cs" @("DataAgentScenarioContext", "scenario_context_matched", "scenario_context_no_match", "scenario_context_catalog_mismatch", "scenario_context_pack_unavailable")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextBuilder.cs" @("DataAgentScenarioContextBuilder", "DataAgentCatalog", "MetricFieldIsKnown")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs" @("Scenario context:", "Scenario context is a hint only", "Do not output SQL")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs" @("request.ScenarioContext")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioDiagnosticsFormatter.cs" @("DataAgent scenario diagnostics", "scenario_context_pack_unavailable")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentScenarioContextIntegrated", "scenario_context=true", "prompt_hint=true", "owner_diag=true", "sql_boundary=true")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentScenarioContextBuilderTests.cs" @("DataAgentScenarioContextBuilderTests", "BuildMapsEngineeringUtteranceToCatalogSafeHints")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentScenarioDiagnosticsFormatterTests.cs" @("DataAgentScenarioDiagnosticsFormatterTests", "FormatEmitsCompactMatchedDiagnostics")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentV211ReadinessTests.cs" @("DataAgentV211ReadinessTests", "ScenarioContextNarrowsPlannerAttentionWithoutSqlAuthority"))) -Detail "V2.11 scenario context builder, prompt hints, diagnostics, and readiness markers"
```

Replace:

```powershell
$expectedRequired = 79
```

with:

```powershell
$expectedRequired = 80
```

- [ ] **Step 7: Add QChat engineering-map gate without direct QChat scenario imports**

In `tools/check-qchat-engineering-map.ps1`, after `DataAgent progress diagnostics`, add:

```powershell
Add-Check -Group "Harness" -Name "DataAgent scenario context diagnostics" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataAgentScenarioContextIntegrated", "DataAgentScenarioDiagnosticsFormatter", "DataAgent scenario diagnostics", "scenario_context_matched")
```

Replace:

```powershell
$expectedRequired = 54
```

with:

```powershell
$expectedRequired = 55
```

- [ ] **Step 8: Run focused readiness tests and scripts to verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioKnowledgePackProviderTests|FullyQualifiedName~DataAgentScenarioContextBuilderTests|FullyQualifiedName~DataAgentScenarioDiagnosticsFormatterTests|FullyQualifiedName~DataAgentV211ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: PASS.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected summary:

```text
  Summary: 80 required passed, 0 required missing
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected summary:

```text
Summary: 55 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 9: Commit Task 5**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs
git add tools/check-dataagent-readiness.ps1
git add tools/check-qchat-engineering-map.ps1
git add Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git add Tests/Alife.Test.DataAgent/DataAgentV211ReadinessTests.cs
git add Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Gate DataAgent scenario context readiness"
```

Expected: commit succeeds.

---

### Task 6: Final Verification and Scope Audit

**Files:**
- Verify: all changed files
- Commit: final integration if verification edits were required

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentScenarioKnowledgePackProviderTests|FullyQualifiedName~DataAgentScenarioContextBuilderTests|FullyQualifiedName~LlmDataAgentPlannerPromptFormatterTests|FullyQualifiedName~LlmDataAgentQueryPlannerTests|FullyQualifiedName~DataAgentScenarioDiagnosticsFormatterTests|FullyQualifiedName~DataAgentV211ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 2: Run focused QChat engineering-map tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: PASS.

- [ ] **Step 3: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
  Summary: 80 required passed, 0 required missing
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 55 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Verify V2.11 did not introduce forbidden runtime shapes**

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.DataAgent\*.cs -Pattern "LangGraph|Python sidecar|StateGraph"
```

Expected: no matches.

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.QChat\*.cs -Pattern "DataAgentScenarioKnowledgePackProvider|DataAgentScenarioContextBuilder|DataAgentToolScopePolicy"
```

Expected: no matches.

- [ ] **Step 5: Verify formatting and repository state**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors.

Run:

```powershell
git status --short --branch
```

Expected: only intentional files are modified or clean after commits. The local warning about `C:\Users\hu shu/.config/git/ignore` can appear and does not block this task.

- [ ] **Step 6: Run full solution verification**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
```

Expected: restore succeeds.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected: PASS.

- [ ] **Step 7: Commit final verification edits if any were made**

If Step 1 through Step 6 required small corrections, commit only those corrections:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent
git add Tests/Alife.Test.DataAgent
git add Tests/Alife.Test.QChat
git add tools/check-dataagent-readiness.ps1
git add tools/check-qchat-engineering-map.ps1
git add -f docs/dataagent/scenario-packs/engineering.zh-CN.json
git commit -m "Finalize DataAgent V2.11 scenario context integration"
```

Expected: commit succeeds when there are corrections. When no files changed after Task 5, skip this commit.

---

## Acceptance Criteria

- `engineering.zh-CN.json` stores readable UTF-8 Chinese and only catalog-valid term fields.
- `DataAgentScenarioContextBuilder` deterministically maps business terms and metrics before the LLM planner sees the question.
- `LlmDataAgentPlannerPromptFormatter` emits compact scenario hints only for matched context and still tells the model not to output SQL.
- `LlmDataAgentQueryPlanner` passes `request.ScenarioContext` into the formatter without changing fallback safety behavior.
- `DataAgentScenarioDiagnosticsFormatter` emits bounded owner-safe text and redacts SQL/hidden context/tool-route/evidence-pack markers.
- `DataAgentReadiness.CheckCore` includes `DataAgentScenarioContextIntegrated`.
- DataAgent readiness script reports `80 required passed, 0 required missing`.
- QChat engineering map reports `55 required passed, 0 required missing, 0 optional present, 0 optional missing`.
- QChat source files do not import `DataAgentScenarioKnowledgePackProvider` or `DataAgentScenarioContextBuilder`.
- No LangGraph, StateGraph, Python sidecar, PostgreSQL checkpoint productization, new SQL execution path, or QChat main-loop change is added.
