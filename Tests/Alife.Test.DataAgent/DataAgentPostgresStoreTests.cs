using Alife.Function.DataAgent;
using Npgsql;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentPostgresStoreTests
{
    [Test]
    public void LivePostgresStoreTestIsSkippedWithoutConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Pass("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set; live PostgreSQL test skipped.");

        Assert.That(connectionString, Is.Not.Empty);
    }

    [Test]
    public void LivePostgresStoreInitializesImportsFixturesAndExecutesReadOnlyQuery()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        IDataAgentStore store = new PostgresDataAgentStore(connectionString);
        store.Initialize();
        store.ImportFixtures();

        DataAgentQueryResult result = store.Query(new DataAgentCompiledSql(
            "SELECT path, title FROM document_index ORDER BY id LIMIT 10",
            []));
        DataAgentQueryResult readinessResult = store.Query(new DataAgentCompiledSql(
            "SELECT capability, account, endpoint, status, required, failure_reason FROM runtime_readiness_check WHERE capability = @p0 AND evidence_path = @p1",
            [
                new DataAgentSqlParameter("@p0", "MixuTts9881Reachable"),
                new DataAgentSqlParameter("@p1", "tools/check-qchat-runtime-readiness.ps1")
            ]));
        DataAgentQueryResult testRunResult = store.Query(new DataAgentCompiledSql(
            "SELECT suite_name, passed, failed, skipped, total FROM test_run WHERE suite_name = @p0 AND command = @p1",
            [
                new DataAgentSqlParameter("@p0", "Alife.Test.QChat"),
                new DataAgentSqlParameter("@p1", "dotnet test Alife.slnx --no-restore --no-build -v:minimal")
            ]));
        DataAgentQueryResult parameterizedResult = store.Query(new DataAgentCompiledSql(
            "SELECT capability FROM runtime_readiness_check WHERE capability LIKE @p0 ORDER BY id LIMIT 10",
            [new DataAgentSqlParameter("@p0", "%Tts%")]));
        DataAgentQueryResult typedBooleanResult = store.Query(new DataAgentCompiledSql(
            "SELECT capability FROM runtime_readiness_check WHERE capability = @p0 AND required = @p1",
            [
                new DataAgentSqlParameter("@p0", "MixuTts9881Reachable"),
                new DataAgentSqlParameter("@p1", true)
            ]));
        DataAgentQueryResult typedIntegerResult = store.Query(new DataAgentCompiledSql(
            "SELECT suite_name FROM test_run WHERE suite_name = @p0 AND passed = @p1 AND failed = @p2",
            [
                new DataAgentSqlParameter("@p0", "Alife.Test.QChat"),
                new DataAgentSqlParameter("@p1", 1168),
                new DataAgentSqlParameter("@p2", 0)
            ]));

        string unique = Guid.NewGuid().ToString("N");
        string acceptedQuestion = $"live-postgres-accepted-{unique}";
        string rejectedQuestion = $"live-postgres-rejected-{unique}";
        string sessionId = $"live-postgres-session-{unique}";

        DeleteLiveAuditRows(connectionString, acceptedQuestion, rejectedQuestion, sessionId);
        try
        {
            store.RecordAccepted(new DataAgentAcceptedAuditInput(
                acceptedQuestion,
                "document_index",
                """{"dataset":"document_index"}""",
                "SELECT path FROM document_index LIMIT 1",
                1,
                TimeSpan.FromMilliseconds(12)));
            store.RecordRejected(new DataAgentRejectedAuditInput(
                rejectedQuestion,
                "document_index",
                """{"dataset":"document_index"}""",
                "DROP TABLE document_index",
                "dangerous_sql",
                TimeSpan.FromMilliseconds(3)));
            store.RecordToolBrokerAudit(new DataAgentToolBrokerAuditRecord(
                sessionId,
                "dataagent_analysis_continue",
                false,
                "tool_route_required",
                "route is required",
                DateTimeOffset.Parse("2026-06-29T00:00:00Z")));

            IReadOnlyList<DataAgentAuditRecord> queryAudit = store.ReadQueryAudit();
            IReadOnlyList<DataAgentToolBrokerAuditRecord> toolBrokerAudit = store.ReadToolBrokerAudit();

            Assert.Multiple(() =>
            {
                Assert.That(store.ProviderName, Is.EqualTo("postgres"));
                Assert.That(result.Rows, Is.Not.Empty);
                Assert.That(result.Rows[0].Keys, Does.Contain("path"));
                Assert.That(result.Rows[0].Keys, Does.Contain("title"));
                Assert.That(readinessResult.Rows, Has.Count.EqualTo(1));
                Assert.That(readinessResult.Rows[0]["capability"], Is.EqualTo("MixuTts9881Reachable"));
                Assert.That(readinessResult.Rows[0]["account"], Is.EqualTo("mixu"));
                Assert.That(readinessResult.Rows[0]["endpoint"], Is.EqualTo("127.0.0.1:9881"));
                Assert.That(readinessResult.Rows[0]["status"], Is.EqualTo("missing"));
                Assert.That(readinessResult.Rows[0]["required"], Is.EqualTo(true));
                Assert.That(readinessResult.Rows[0]["failure_reason"], Is.EqualTo("mixu_tts_endpoint_unreachable"));
                Assert.That(testRunResult.Rows, Has.Count.EqualTo(1));
                Assert.That(testRunResult.Rows[0]["suite_name"], Is.EqualTo("Alife.Test.QChat"));
                Assert.That(testRunResult.Rows[0]["passed"], Is.EqualTo(1168));
                Assert.That(testRunResult.Rows[0]["failed"], Is.EqualTo(0));
                Assert.That(testRunResult.Rows[0]["skipped"], Is.EqualTo(10));
                Assert.That(testRunResult.Rows[0]["total"], Is.EqualTo(1178));
                Assert.That(
                    parameterizedResult.Rows.Select(row => row["capability"]),
                    Does.Contain("MixuTts9881Reachable"));
                Assert.That(typedBooleanResult.Rows, Has.Count.EqualTo(1));
                Assert.That(typedIntegerResult.Rows, Has.Count.EqualTo(1));
                Assert.That(
                    queryAudit,
                    Has.Some.Matches<DataAgentAuditRecord>(record =>
                        record.Question == acceptedQuestion &&
                        record.Validated &&
                        record.RowCount == 1));
                Assert.That(
                    queryAudit,
                    Has.Some.Matches<DataAgentAuditRecord>(record =>
                        record.Question == rejectedQuestion &&
                        record.Validated == false &&
                        record.RejectedReason == "dangerous_sql"));
                Assert.That(
                    toolBrokerAudit,
                    Has.Some.Matches<DataAgentToolBrokerAuditRecord>(record =>
                        record.SessionId == sessionId &&
                        record.ToolName == "dataagent_analysis_continue" &&
                        record.Allowed == false &&
                        record.ReasonCode == "tool_route_required"));
            });
        }
        finally
        {
            DeleteLiveAuditRows(connectionString, acceptedQuestion, rejectedQuestion, sessionId);
        }
    }

    [Test]
    public void ImportFixturesUsesScopedFixtureDeletes()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "PostgresDataAgentStore.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DELETE FROM engineering_gate WHERE source = @source"));
            Assert.That(source, Does.Contain("DELETE FROM runtime_readiness_check WHERE capability = @capability AND evidence_path = @evidence_path"));
            Assert.That(source, Does.Contain("DELETE FROM test_run WHERE suite_name = @suite_name AND command = @command"));
            Assert.That(source, Does.Contain("DELETE FROM document_index WHERE path = @path"));
            Assert.That(source, Does.Not.Contain("\"DELETE FROM engineering_gate\""));
            Assert.That(source, Does.Not.Contain("\"DELETE FROM runtime_readiness_check\""));
            Assert.That(source, Does.Not.Contain("\"DELETE FROM test_run\""));
            Assert.That(source, Does.Not.Contain("\"DELETE FROM document_index\""));
            Assert.That(source, Does.Not.Contain("DELETE FROM module_capability"));
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

    static void DeleteLiveAuditRows(
        string connectionString,
        string acceptedQuestion,
        string rejectedQuestion,
        string sessionId)
    {
        using NpgsqlConnection connection = new(connectionString);
        connection.Open();

        Execute(
            connection,
            "DELETE FROM query_audit WHERE question = @accepted_question OR question = @rejected_question",
            new NpgsqlParameter("accepted_question", acceptedQuestion),
            new NpgsqlParameter("rejected_question", rejectedQuestion));
        Execute(
            connection,
            "DELETE FROM tool_broker_audit WHERE session_id = @session_id",
            new NpgsqlParameter("session_id", sessionId));
    }

    static void Execute(NpgsqlConnection connection, string commandText, params NpgsqlParameter[] parameters)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = commandText;

        foreach (NpgsqlParameter parameter in parameters)
            command.Parameters.Add(parameter);

        command.ExecuteNonQuery();
    }
}
