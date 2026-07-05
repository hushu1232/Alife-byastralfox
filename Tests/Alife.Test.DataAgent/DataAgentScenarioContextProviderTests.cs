using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentScenarioContextProviderTests
{
    const string EngineeringQuestion = "\u770b\u770b\u5de5\u7a0b\u95e8\u7981\u91cc\u6700\u8fd1\u5931\u8d25\u7684\u5fc5\u9700\u9879";

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
            Assert.That(
                context.Metrics.Select(metric => metric.Name),
                Is.EqualTo(new[] { "\u5931\u8d25", "\u5fc5\u9700" }));
        });
    }

    [Test]
    public void BuildReturnsNoMatchForUnrelatedQuestion()
    {
        DataAgentScenarioContextProvider provider = new(EngineeringPackPath());

        DataAgentScenarioContext context = provider.Build(
            DataAgentCatalog.CreateDefault(),
            "\u4eca\u5929\u684c\u5ba0\u5fc3\u60c5\u600e\u4e48\u6837");

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
