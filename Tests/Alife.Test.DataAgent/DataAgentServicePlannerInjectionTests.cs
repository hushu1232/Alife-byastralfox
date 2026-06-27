using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentServicePlannerInjectionTests
{
    [Test]
    public void UsesInjectedPlanner()
    {
        string databasePath = CreateDatabasePath();
        DataAgentService service = new(databasePath, new FixedPlanner(new DataAgentQueryPlan(
            "document_index",
            "forced_document_lookup",
            ["path", "title", "summary"],
            [new DataAgentFilter("tags", "contains", "dataagent")],
            [],
            20)));

        DataAgentAnswer answer = service.Answer("This question would normally use the fallback plan.");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            Assert.That(answer.Dataset, Is.EqualTo("document_index"));
            Assert.That(answer.Summary, Does.Contain("DataAgent NL2SQL Design"));
        });
    }

    [Test]
    public void DefaultConstructorPreservesExistingBehavior()
    {
        DataAgentService service = new(CreateDatabasePath());

        DataAgentAnswer answer = service.Answer("Which runtime readiness gate is required?");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            Assert.That(answer.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(answer.Summary, Does.Contain("Runtime readiness script"));
        });
    }

    [Test]
    public void InvalidInjectedPlannerOutputIsRejectedAndAudited()
    {
        string databasePath = CreateDatabasePath();
        DataAgentService service = new(databasePath, new FixedPlanner(new DataAgentQueryPlan(
            "engineering_gate",
            "unsafe",
            ["name"],
            [new DataAgentFilter("status", "starts_with", "pass")],
            [],
            50)));

        DataAgentAnswer answer = service.Answer("Use unsafe operator.");
        DataAgentAuditRecord audit = new DataAgentAuditLog(databasePath).ReadAll().Single();

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.False);
            Assert.That(answer.RejectedReason, Does.Contain("unsupported_operator:starts_with"));
            Assert.That(answer.Context, Does.Contain("sql_status=rejected"));
            Assert.That(audit.Validated, Is.False);
            Assert.That(audit.Dataset, Is.EqualTo("engineering_gate"));
            Assert.That(audit.RejectedReason, Does.Contain("unsupported_operator:starts_with"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-service-planner-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }

    sealed class FixedPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlan Plan(DataAgentQueryRequest request) => plan;
    }
}
