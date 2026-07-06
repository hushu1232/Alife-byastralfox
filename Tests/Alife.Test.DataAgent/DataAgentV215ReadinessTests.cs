using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV215ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesDataQueryGraphPilotRuntimeGate()
    {
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(NewDatabasePath());

        Dictionary<string, DataAgentReadinessCheck> byName = checks.ToDictionary(check => check.Name);

        Assert.Multiple(() =>
        {
            Assert.That(byName, Does.ContainKey("DataQueryGraphPilotPresent"));
            DataAgentReadinessCheck check = byName["DataQueryGraphPilotPresent"];
            Assert.That(check.Passed, Is.True, check.Detail);
            Assert.That(check.Detail, Does.Contain("default_enabled=false"));
            Assert.That(check.Detail, Does.Contain("dry_run=true"));
            Assert.That(check.Detail, Does.Contain("no_langgraph_runtime=true"));
            Assert.That(check.Detail, Does.Contain("node_scope=true"));
            Assert.That(check.Detail, Does.Contain("no_sql_authority=true"));
            Assert.That(check.Detail, Does.Contain("fallback=true"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v215-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }
}
