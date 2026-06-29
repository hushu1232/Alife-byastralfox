using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentToolBrokerAuditLogTests
{
    [Test]
    public void RecordAndReadAllPreservesRouteDecision()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.db");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentToolBrokerAuditLog log = new(databasePath);

        log.Record(new DataAgentToolBrokerAuditRecord(
            "session-1",
            "dataagent_analysis_continue",
            false,
            "tool_route_required",
            "route is required",
            DateTimeOffset.Parse("2026-06-29T00:00:00Z")));

        IReadOnlyList<DataAgentToolBrokerAuditRecord> records = log.ReadAll();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].SessionId, Is.EqualTo("session-1"));
            Assert.That(records[0].ToolName, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(records[0].Allowed, Is.False);
            Assert.That(records[0].ReasonCode, Is.EqualTo("tool_route_required"));
        });
    }
}