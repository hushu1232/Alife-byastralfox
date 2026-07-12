using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV210ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesV210GovernanceChecks()
    {
        string databasePath = NewDatabasePath();

        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentScenarioKnowledgePackPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentNodeToolScopePolicyPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentSafetyCapabilitiesRemainDeterministic"));
            Assert.That(checks.Single(check => check.Name == "DataAgentScenarioKnowledgePackPresent").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "DataAgentNodeToolScopePolicyPresent").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "DataAgentSafetyCapabilitiesRemainDeterministic").Passed, Is.True);
        });
    }

    [Test]
    public void StaticReadinessScriptContainsV210Markers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string dataAgentScript = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string qchatScript = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(dataAgentScript, Does.Contain("V2.10"));
            Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 119"));
            Assert.That(dataAgentScript, Does.Contain("DataAgentScenarioKnowledgePackPresent"));
            Assert.That(dataAgentScript, Does.Contain("DataAgentNodeToolScopePolicyPresent"));
            Assert.That(dataAgentScript, Does.Contain("DataAgentSafetyCapabilitiesRemainDeterministic"));
            Assert.That(qchatScript, Does.Contain("V2.10"));
            Assert.That(qchatScript, Does.Contain("$expectedRequired = 63"));
            Assert.That(qchatScript, Does.Contain("Alife capability governance catalog"));
            Assert.That(qchatScript, Does.Contain("DataAgent node tool scope policy"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v210-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
