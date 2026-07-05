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
        string packPath = Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json");

        DataAgentScenarioKnowledgePack pack =
            DataAgentScenarioKnowledgePackProvider.Load(packPath);

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
        string json = File.ReadAllText(GetEngineeringPackPath(), Encoding.UTF8);

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
            Assert.That(json, Does.Not.Contain("蹇呯渶"));
            Assert.That(json, Does.Not.Contain("蹇呴渶"));
        });
    }

    [Test]
    public void EngineeringPackFieldsExistInDefaultCatalog()
    {
        DataAgentScenarioKnowledgePack pack = LoadEngineeringPack();
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();

        Assert.Multiple(() =>
        {
            foreach (DataAgentScenarioTerm term in pack.Terms)
            {
                Assert.That(catalog.HasDataset(term.Dataset), Is.True, $"Dataset '{term.Dataset}' should exist.");

                foreach (string field in term.Fields)
                {
                    Assert.That(
                        catalog.HasField(term.Dataset, field),
                        Is.True,
                        $"Field '{term.Dataset}.{field}' should exist.");
                }
            }
        });
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
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("name"));
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("status"));
            Assert.That(terms.SelectMany(term => term.Fields), Does.Contain("required"));
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
        """);

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
        """);

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
