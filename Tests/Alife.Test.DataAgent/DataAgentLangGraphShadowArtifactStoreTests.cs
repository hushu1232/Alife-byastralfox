using Alife.Function.DataAgent;
using Microsoft.Data.Sqlite;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentLangGraphShadowArtifactStoreTests
{
    [Test]
    public void WritePersistsSafeAcceptedAndRejectedMetadata()
    {
        string databasePath = CreateDatabasePath();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
        DataAgentSchemaInitializer.Initialize(databasePath);
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);

        DataAgentLangGraphShadowArtifactWriteResult accepted = store.RecordLangGraphShadowArtifact(CreateArtifact(
            "artifact-accepted", "session-1", "replay-1", DataAgentLangGraphShadowArtifactOutcome.Accepted,
            "accepted", "advisory matched", now), now);
        DataAgentLangGraphShadowArtifactWriteResult rejected = store.RecordLangGraphShadowArtifact(CreateArtifact(
            "artifact-rejected", "session-1", "replay-1", DataAgentLangGraphShadowArtifactOutcome.GateRejected,
            "diff_gate_rejected", "diff gate rejected", now.AddMinutes(1)), now);

        DataAgentLangGraphShadowArtifactAggregate aggregate = store.ReadLangGraphShadowArtifactAggregate(now);

        Assert.Multiple(() =>
        {
            Assert.That(accepted.Written, Is.True);
            Assert.That(rejected.Written, Is.True);
            Assert.That(aggregate.Total, Is.EqualTo(2));
            Assert.That(aggregate.Accepted, Is.EqualTo(1));
            Assert.That(aggregate.GateRejected, Is.EqualTo(1));
            Assert.That(aggregate.LatestReasonCode, Is.EqualTo("diff_gate_rejected"));
        });
    }

    [TestCase("SELECT id FROM users")]
    [TestCase("Bearer example-token")]
    [TestCase("password=hunter2")]
    [TestCase("connection_string=Data Source=local")]
    [TestCase("hidden_context")]
    public void WriteDoesNotPersistUnsafeMetadata(string unsafeValue)
    {
        string databasePath = CreateDatabasePath();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentLangGraphShadowArtifactStore store = new(databasePath);

        DataAgentLangGraphShadowArtifact artifact = CreateArtifact(
            "artifact-safe", "session-safe", "replay-safe", DataAgentLangGraphShadowArtifactOutcome.ProtocolRejected,
            "protocol_rejected", "safe summary", now);
        artifact = unsafeValue switch
        {
            "SELECT id FROM users" => artifact with { ArtifactId = unsafeValue },
            "Bearer example-token" => artifact with { ReasonCode = unsafeValue },
            _ => artifact with { Summary = unsafeValue }
        };

        DataAgentLangGraphShadowArtifactWriteResult result = store.Write(artifact, now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Written, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("unsafe_artifact_metadata"));
            Assert.That(ReadStoredRowCount(databasePath), Is.Zero);
            Assert.That(ReadStoredMetadata(databasePath), Does.Not.Contain(unsafeValue));
        });
    }

    [Test]
    public void WriteRemovesExpiredArtifactsAndKeepsNewestTwentyPerScope()
    {
        string databasePath = CreateDatabasePath();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentLangGraphShadowArtifactStore store = new(databasePath);

        DataAgentLangGraphShadowArtifact expired = CreateArtifact(
            "expired", "session-1", "replay-1", DataAgentLangGraphShadowArtifactOutcome.Timeout,
            "timed_out", "expired", now.AddDays(-1), now);
        store.Write(expired, now);
        for (int index = 0; index < 21; index++)
        {
            store.Write(CreateArtifact(
                $"artifact-{index:D2}", "session-1", "replay-1", DataAgentLangGraphShadowArtifactOutcome.Accepted,
                $"accepted-{index:D2}", "safe", now.AddMinutes(index)), now);
        }

        DataAgentLangGraphShadowArtifactAggregate aggregate = store.ReadAggregate(now);

        Assert.Multiple(() =>
        {
            Assert.That(aggregate.Total, Is.EqualTo(20));
            Assert.That(aggregate.Accepted, Is.EqualTo(20));
            Assert.That(aggregate.PerScopeLimit, Is.EqualTo(20));
            Assert.That(ReadArtifactIds(databasePath), Does.Not.Contain("expired"));
            Assert.That(ReadArtifactIds(databasePath), Does.Not.Contain("artifact-00"));
            Assert.That(ReadArtifactIds(databasePath), Does.Contain("artifact-20"));
        });
    }

    [Test]
    public void ReadAggregateReturnsCountsRetentionAndNoSummary()
    {
        string databasePath = CreateDatabasePath();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentLangGraphShadowArtifactStore store = new(databasePath);

        store.Write(CreateArtifact("a", "session-1", "replay-1", DataAgentLangGraphShadowArtifactOutcome.Accepted, "accepted", "summary-a", now), now);
        store.Write(CreateArtifact("b", "session-2", "replay-2", DataAgentLangGraphShadowArtifactOutcome.ProtocolRejected, "protocol", "summary-b", now.AddMinutes(1)), now);
        store.Write(CreateArtifact("c", "session-3", "replay-3", DataAgentLangGraphShadowArtifactOutcome.Timeout, "timeout", "summary-c", now.AddMinutes(2)), now);
        store.Write(CreateArtifact("d", "session-4", "replay-4", DataAgentLangGraphShadowArtifactOutcome.Fallback, "fallback", "summary-d", now.AddMinutes(3)), now);

        DataAgentLangGraphShadowArtifactAggregate aggregate = store.ReadAggregate(now);

        Assert.Multiple(() =>
        {
            Assert.That(aggregate.Total, Is.EqualTo(4));
            Assert.That(aggregate.Accepted, Is.EqualTo(1));
            Assert.That(aggregate.ProtocolRejected, Is.EqualTo(1));
            Assert.That(aggregate.Timeout, Is.EqualTo(1));
            Assert.That(aggregate.Fallback, Is.EqualTo(1));
            Assert.That(aggregate.LatestReasonCode, Is.EqualTo("fallback"));
            Assert.That(aggregate.OldestCreatedAt, Is.EqualTo(now));
            Assert.That(aggregate.NewestCreatedAt, Is.EqualTo(now.AddMinutes(3)));
            Assert.That(aggregate.RetentionDays, Is.EqualTo(90));
            Assert.That(typeof(DataAgentLangGraphShadowArtifactAggregate).GetProperties().Select(property => property.Name), Does.Not.Contain("Summary"));
        });
    }

    [Test]
    public void WriteReturnsBoundedFailureForMissingPathWithoutChangingArtifactDecision()
    {
        string missingDatabasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"), "missing", "artifact.db");
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-14T00:00:00Z");
        DataAgentLangGraphShadowArtifact artifact = CreateArtifact(
            "artifact-1", "session-1", "replay-1", DataAgentLangGraphShadowArtifactOutcome.GateRejected,
            "diff_gate_rejected", "safe", now);
        DataAgentLangGraphShadowArtifactStore store = new(missingDatabasePath);

        DataAgentLangGraphShadowArtifactWriteResult result = store.Write(artifact, now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Written, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("artifact_write_failed"));
            Assert.That(artifact.Outcome, Is.EqualTo(DataAgentLangGraphShadowArtifactOutcome.GateRejected));
            Assert.That(artifact.FallbackRequired, Is.False);
        });
    }

    static DataAgentLangGraphShadowArtifact CreateArtifact(
        string artifactId,
        string sessionId,
        string replayId,
        DataAgentLangGraphShadowArtifactOutcome outcome,
        string reasonCode,
        string summary,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null)
    {
        return new DataAgentLangGraphShadowArtifact(
            artifactId,
            sessionId,
            replayId,
            outcome,
            reasonCode,
            summary,
            42,
            outcome == DataAgentLangGraphShadowArtifactOutcome.Accepted,
            false,
            createdAt,
            expiresAt ?? createdAt.AddDays(90));
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "langgraph-shadow-artifact-store-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static int ReadStoredRowCount(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM langgraph_shadow_artifact";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    static IReadOnlyList<string> ReadArtifactIds(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT artifact_id FROM langgraph_shadow_artifact ORDER BY artifact_id";
        using SqliteDataReader reader = command.ExecuteReader();
        List<string> artifactIds = [];
        while (reader.Read())
            artifactIds.Add(reader.GetString(0));
        return artifactIds;
    }

    static string ReadStoredMetadata(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(group_concat(artifact_id || reason_code || summary, '|'), '') FROM langgraph_shadow_artifact";
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }
}
