using Alife.Function.DataAgent;

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
            Assert.That(pack.Metrics.Select(metric => metric.Name), Does.Contain("失败"));
            Assert.That(pack.Metrics.Select(metric => metric.Name), Does.Contain("必需"));
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
        string packPath = Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json");
        return DataAgentScenarioKnowledgePackProvider.Load(packPath);
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
