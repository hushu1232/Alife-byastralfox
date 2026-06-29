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
