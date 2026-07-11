using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV216ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesDataQueryGraphOwnerDiagnosticsBridge()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v216-readiness.sqlite");
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);
        Dictionary<string, DataAgentReadinessCheck> byName = checks.ToDictionary(check => check.Name);

        Assert.Multiple(() =>
        {
            Assert.That(byName, Does.ContainKey("DataQueryGraphOwnerDiagnosticsPresent"));
            DataAgentReadinessCheck check = byName["DataQueryGraphOwnerDiagnosticsPresent"];
            Assert.That(check.Passed, Is.True, check.Detail);
            Assert.That(check.Detail, Does.Contain("handler_publisher=true"));
            Assert.That(check.Detail, Does.Contain("capability_provider=true"));
            Assert.That(check.Detail, Does.Contain("function_caller=true"));
            Assert.That(check.Detail, Does.Contain("disabled_diagnostics=true"));
            Assert.That(check.Detail, Does.Contain("no_langgraph_runtime=true"));
        });
    }

    [Test]
    public void StaticReadinessScriptIncludesV216OwnerDiagnosticsBridge()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("DataQueryGraphOwnerDiagnosticsPresent"));
            Assert.That(script, Does.Contain("RecordRecentDataAgentGraphDiagnostics"));
            Assert.That(script, Does.Contain("RecentDataAgentGraphDiagnostics"));
            Assert.That(script, Does.Contain("dataQueryGraphDiagnosticsPublisher"));
            Assert.That(script, Does.Contain("DataAgentDataQueryGraphTraceFormatter.Format"));
            Assert.That(script, Does.Contain("$expectedRequired = 116"));
        });
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

        return Directory.GetCurrentDirectory();
    }
}
