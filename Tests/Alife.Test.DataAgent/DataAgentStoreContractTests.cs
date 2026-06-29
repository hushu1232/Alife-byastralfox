using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentStoreContractTests
{
    [Test]
    public void SqliteStoreRejectsInvalidDatabasePath()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new SqliteDataAgentStore(null!));
            Assert.Throws<ArgumentException>(() => new SqliteDataAgentStore(string.Empty));
            Assert.Throws<ArgumentException>(() => new SqliteDataAgentStore("   "));
        });
    }

    [Test]
    public void SqliteStoreInitializesImportsFixturesAndExecutesReadOnlyQuery()
    {
        string databasePath = CreateDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);

        store.Initialize();
        store.ImportFixtures();

        DataAgentCompiledSql compiled = new(
            "SELECT path, title FROM document_index ORDER BY id LIMIT 10",
            []);
        DataAgentQueryResult result = store.Query(compiled);

        Assert.Multiple(() =>
        {
            Assert.That(store.ProviderName, Is.EqualTo("sqlite"));
            Assert.That(result.Rows, Is.Not.Empty);
            Assert.That(result.Rows[0].Keys, Does.Contain("path"));
            Assert.That(result.Rows[0].Keys, Does.Contain("title"));
        });
    }

    [Test]
    public void SqliteStoreRecordsAcceptedAndRejectedQueryAudit()
    {
        string databasePath = CreateDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();

        store.RecordAccepted(new DataAgentAcceptedAuditInput(
            "Which runtime readiness gate is required?",
            "engineering_gate",
            "{\"intent\":\"find_runtime_readiness_required_evidence\"}",
            "SELECT name FROM engineering_gate LIMIT 1",
            1,
            TimeSpan.FromMilliseconds(12)));

        store.RecordRejected(new DataAgentRejectedAuditInput(
            "Use unsafe operator.",
            "engineering_gate",
            "{\"intent\":\"unsafe\"}",
            "SELECT name FROM engineering_gate WHERE status LIKE 'pass%'",
            "unsupported_operator:starts_with",
            TimeSpan.Zero));

        IReadOnlyList<DataAgentAuditRecord> records = store.ReadQueryAudit();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0].Validated, Is.True);
            Assert.That(records[0].RowCount, Is.EqualTo(1));
            Assert.That(records[1].Validated, Is.False);
            Assert.That(records[1].RejectedReason, Is.EqualTo("unsupported_operator:starts_with"));
        });
    }

    [Test]
    public void SqliteStoreRecordsToolBrokerAudit()
    {
        string databasePath = CreateDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();

        store.RecordToolBrokerAudit(new DataAgentToolBrokerAuditRecord(
            "session-1",
            "dataagent_analysis_continue",
            false,
            "tool_route_required",
            "route is required",
            DateTimeOffset.Parse("2026-06-29T00:00:00Z")));

        IReadOnlyList<DataAgentToolBrokerAuditRecord> records = store.ReadToolBrokerAudit();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].SessionId, Is.EqualTo("session-1"));
            Assert.That(records[0].ToolName, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(records[0].Allowed, Is.False);
            Assert.That(records[0].ReasonCode, Is.EqualTo("tool_route_required"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-store-contract-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }
}
