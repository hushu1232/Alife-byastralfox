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
            LoadEngineeringPack(),
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
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败", "必需" }));
        });
    }

    [Test]
    public void BuildMatchesAliasesAndPreservesMatchedText()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            LoadEngineeringPack(),
            "质量门禁最近测试失败");

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.HasMatches, Is.True);
            Assert.That(context.Terms.Select(term => term.Term), Is.EqualTo(new[] { "工程门禁", "最近失败的测试" }));
            Assert.That(context.Terms.Select(term => term.MatchedText), Is.EqualTo(new[] { "质量门禁", "最近测试失败" }));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败" }));
        });
    }

    [Test]
    public void BuildReturnsNoMatchForUnmatchedUtterance()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            LoadEngineeringPack(),
            "今天随便聊聊");

        Assert.Multiple(() =>
        {
            Assert.That(context.Scenario, Is.EqualTo("engineering_readiness"));
            Assert.That(context.Culture, Is.EqualTo("zh-CN"));
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
            "工程门禁");

        Assert.Multiple(() =>
        {
            Assert.That(context.Scenario, Is.EqualTo("unavailable"));
            Assert.That(context.Culture, Is.EqualTo("und"));
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonPackUnavailable));
            Assert.That(context.HasMatches, Is.False);
            Assert.That(context.Terms, Is.Empty);
            Assert.That(context.Metrics, Is.Empty);
            Assert.That(context.CandidateDatasets, Is.Empty);
            Assert.That(context.CandidateFields, Is.Empty);
        });
    }

    [Test]
    public void BuildDropsCatalogMismatchedDatasetsAndFields()
    {
        DataAgentScenarioKnowledgePack pack = new(
            "catalog_mismatch",
            "zh-CN",
            [
                new DataAgentScenarioTerm("有效门禁", [], "engineering_gate", ["name", "missing_field", "status"]),
                new DataAgentScenarioTerm("坏数据集", [], "missing_dataset", ["name"]),
                new DataAgentScenarioTerm("坏字段", [], "test_run", ["missing_field"])
            ],
            [
                new DataAgentScenarioMetric("失败", "status", "!=", "passed"),
                new DataAgentScenarioMetric("坏指标", "missing_field", "=", true)
            ]);

        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            pack,
            "有效门禁 坏数据集 坏字段 失败 坏指标");
        DataAgentScenarioContext mismatch = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            pack,
            "坏数据集 坏字段 坏指标");

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.HasMatches, Is.True);
            Assert.That(context.Terms.Select(term => term.Term), Is.EqualTo(new[] { "有效门禁" }));
            Assert.That(context.Terms.Single().Fields, Is.EqualTo(new[] { "name", "status" }));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate" }));
            Assert.That(context.CandidateFields, Is.EqualTo(new[] { "name", "status" }));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败" }));
            Assert.That(mismatch.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonCatalogMismatch));
            Assert.That(mismatch.HasMatches, Is.False);
        });
    }

    [Test]
    public void BuildReturnsCatalogMismatchWhenInvalidTermMatchesWithValidMetric()
    {
        DataAgentScenarioKnowledgePack pack = new(
            "catalog_mismatch_metric",
            "zh-CN",
            [new DataAgentScenarioTerm("坏数据集", [], "missing_dataset", ["name"])],
            [new DataAgentScenarioMetric("失败", "status", "!=", "passed")]);

        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            pack,
            "坏数据集 失败");

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
    public void BuildDedupesCandidateHintsOrdinalIgnoreCaseByFirstAppearance()
    {
        DataAgentScenarioKnowledgePack pack = new(
            "dedupe_hints",
            "zh-CN",
            [
                new DataAgentScenarioTerm("第一门禁", [], "engineering_gate", ["name", "STATUS", "required"]),
                new DataAgentScenarioTerm("第二门禁", [], "ENGINEERING_GATE", ["Name", "status", "evidence_path"]),
                new DataAgentScenarioTerm("测试运行", [], "test_run", ["FAILED", "failed", "total"])
            ],
            []);

        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            pack,
            "第一门禁 第二门禁 测试运行");

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(
                context.CandidateFields,
                Is.EqualTo(new[] { "name", "STATUS", "required", "evidence_path", "FAILED", "total" }));
        });
    }

    [Test]
    public void BuildKeepsMetricWhenOnlyMetricMatchesKnownCatalogField()
    {
        DataAgentScenarioKnowledgePack pack = new(
            "metric_only",
            "zh-CN",
            [],
            [new DataAgentScenarioMetric("失败", "status", "!=", "passed")]);

        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            pack,
            "看看失败情况");

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.HasMatches, Is.True);
            Assert.That(context.CandidateDatasets, Is.Empty);
            Assert.That(context.CandidateFields, Is.Empty);
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败" }));
        });
    }

    [Test]
    public void ContextCollectionsAreDefensiveReadOnlySnapshots()
    {
        string[] termFields = ["name", "status"];
        List<DataAgentScenarioTermMatch> terms =
        [
            new DataAgentScenarioTermMatch("工程门禁", "engineering_gate", termFields, "工程门禁")
        ];
        List<DataAgentScenarioMetricMatch> metrics =
        [
            new DataAgentScenarioMetricMatch("失败", "status", "!=", "passed")
        ];
        string[] candidateDatasets = ["engineering_gate"];
        string[] candidateFields = ["name", "status"];

        DataAgentScenarioContext context = new(
            "engineering_readiness",
            "zh-CN",
            terms,
            metrics,
            candidateDatasets,
            candidateFields,
            DataAgentScenarioContext.ReasonMatched);

        termFields[0] = "polluted";
        terms[0] = new DataAgentScenarioTermMatch("污染", "engineering_gate", ["polluted"], "污染");
        terms.Add(new DataAgentScenarioTermMatch("新增", "engineering_gate", ["name"], "新增"));
        metrics[0] = new DataAgentScenarioMetricMatch("污染", "status", "=", "failed");
        metrics.Add(new DataAgentScenarioMetricMatch("新增", "required", "=", true));
        candidateDatasets[0] = "polluted";
        candidateFields[0] = "polluted";
        candidateFields[1] = "added";

        Assert.Multiple(() =>
        {
            Assert.That(context.Terms.Select(term => term.Term), Is.EqualTo(new[] { "工程门禁" }));
            Assert.That(context.Terms.Single().Fields, Is.EqualTo(new[] { "name", "status" }));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败" }));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate" }));
            Assert.That(context.CandidateFields, Is.EqualTo(new[] { "name", "status" }));

            AssertCannotPollute(
                context.Terms,
                new DataAgentScenarioTermMatch("替换", "engineering_gate", ["name"], "替换"),
                new DataAgentScenarioTermMatch("追加", "engineering_gate", ["name"], "追加"));
            AssertCannotPollute(
                context.Metrics,
                new DataAgentScenarioMetricMatch("替换", "status", "=", "passed"),
                new DataAgentScenarioMetricMatch("追加", "required", "=", true));
            AssertCannotPollute(context.CandidateDatasets, "替换", "追加");
            AssertCannotPollute(context.CandidateFields, "替换", "追加");
            AssertCannotPollute(context.Terms.Single().Fields, "替换", "追加");
        });
    }

    static DataAgentScenarioKnowledgePack LoadEngineeringPack()
    {
        return DataAgentScenarioKnowledgePackProvider.Load(GetEngineeringPackPath());
    }

    static string GetEngineeringPackPath()
    {
        return Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json");
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
