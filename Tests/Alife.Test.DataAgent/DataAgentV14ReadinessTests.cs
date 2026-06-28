using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV14ReadinessTests
{
    static readonly string[] RequiredChecks =
    [
        "AnalysisSessionServicePresent",
        "AnalysisSessionStorePresent",
        "AnalysisSessionStateMachineTransitions",
        "AnalysisFollowUpInterpreterPresent",
        "AnalysisSessionContextProviderPresent",
        "AnalysisSummaryWindowPresent",
        "AnalysisSessionHasNoSqliteBinding"
    ];

    [Test]
    public void CoreReadinessIncludesAllV14AnalysisSessionChecks()
    {
        string databasePath = NewDatabasePath();

        IReadOnlyDictionary<string, DataAgentReadinessCheck> checks = DataAgentReadiness
            .CheckCore(databasePath)
            .ToDictionary(check => check.Name, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
            {
                Assert.That(checks, Does.ContainKey(checkName), checkName);
                Assert.That(checks[checkName].Passed, Is.True, $"{checkName}:{checks[checkName].Detail}");
            }
        });
    }

    [Test]
    public void StaticReadinessScriptContainsAllV14AnalysisSessionMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
                Assert.That(script, Does.Contain(checkName), checkName);

            Assert.That(script, Does.Contain("DataAgentAnalysisService.cs"));
            Assert.That(script, Does.Contain("InMemoryDataAgentAnalysisSessionStore.cs"));
            Assert.That(script, Does.Contain("DataAgentFollowUpInterpreter.cs"));
            Assert.That(script, Does.Contain("DataAgentAnalysisContextProvider.cs"));
            Assert.That(script, Does.Contain("DataAgentAnalysisSummarizer.cs"));
            Assert.That(script, Does.Contain("Test-FileOmitsMarker"));
            Assert.That(script, Does.Contain("SqliteConnection"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v14-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }
}
