using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV30ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesGraphHandshakeBoundary()
    {
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(NewDatabasePath());
        DataAgentReadinessCheck check = checks.Single(item => item.Name == "GraphHandshakeBoundaryPresent");

        Assert.Multiple(() =>
        {
            Assert.That(check.Passed, Is.True, check.Detail);
            Assert.That(check.Detail, Does.Contain("default_enabled=false"));
            Assert.That(check.Detail, Does.Contain("validator=true"));
            Assert.That(check.Detail, Does.Contain("no_sql_authority=true"));
            Assert.That(check.Detail, Does.Contain("scoped_node_manifest=true"));
            Assert.That(check.Detail, Does.Contain("fallback=true"));
            Assert.That(check.Detail, Does.Contain("runtime_required=false"));
        });
    }

    [Test]
    public void StaticReadinessScriptContainsV30Markers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("GraphHandshakeBoundaryPresent"));
            Assert.That(script, Does.Contain("DataAgentGraphHandshakeCoordinator"));
            Assert.That(script, Does.Contain("DataAgentGraphHandshakeValidator"));
            Assert.That(script, Does.Contain("DataAgentGraphHandshakeManifestFactory"));
            Assert.That(script, Does.Contain("default_enabled=false"));
            Assert.That(script, Does.Contain("no_sql_authority=true"));
            Assert.That(script, Does.Contain("scoped_node_manifest=true"));
            Assert.That(script, Does.Contain("fallback=true"));
            Assert.That(script, Does.Contain("$expectedRequired = 86"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v30-readiness-tests");
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
