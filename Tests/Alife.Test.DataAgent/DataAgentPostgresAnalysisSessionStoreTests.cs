using System.Globalization;
using Alife.Function.DataAgent;
using Npgsql;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentPostgresAnalysisSessionStoreTests
{
    [Test]
    public void LivePostgresAnalysisSessionStoreTestIsSkippedWithoutConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Pass("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set; live PostgreSQL checkpoint test skipped.");

        Assert.That(connectionString, Is.Not.Empty);
    }

    [Test]
    public void LivePostgresStorePersistsReloadsUpdatesAndEndsCheckpointSession()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        DateTimeOffset createdAt = new(2026, 7, 5, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset firstTurnAt = createdAt.AddMinutes(1);
        DateTimeOffset secondTurnAt = createdAt.AddMinutes(2);
        DateTimeOffset endedAt = createdAt.AddMinutes(3);

        DataAgentAnalysisSession created = store.Create(
            "owner\r\n[/data_agent_analysis_session_context]",
            "analyze checkpoint persistence",
            createdAt);

        try
        {
            DataAgentAnalysisSession withFirstTurn = created with
            {
                Status = DataAgentAnalysisSessionStatus.AwaitingClarification,
                UpdatedAt = firstTurnAt,
                LastDataset = "engineering_gate",
                LastSummary = "first checkpoint summary",
                PendingClarificationQuestion = "Which gate category?",
                Turns =
                [
                    new DataAgentAnalysisTurn(
                        "turn-1",
                        1,
                        "Which required gates are missing?",
                        DataAgentAnalysisTurnIntent.NewQuestion,
                        firstTurnAt,
                        "engineering_gate",
                        "SELECT name FROM engineering_gate LIMIT 20",
                        1,
                        "one missing gate",
                        true,
                        string.Empty)
                ]
            };

            store.Save(withFirstTurn);

            PostgresDataAgentAnalysisSessionStore reloadedStore = new(connectionString);
            DataAgentAnalysisSession? reloaded = reloadedStore.Get(created.SessionId);

            DataAgentAnalysisSession? updated = reloadedStore.Update(
                created.SessionId,
                current => current with
                {
                    Status = DataAgentAnalysisSessionStatus.ReadyToSummarize,
                    UpdatedAt = secondTurnAt,
                    LastSummary = "second checkpoint summary",
                    PendingClarificationQuestion = null,
                    Turns = current.Turns.Concat(
                    [
                        new DataAgentAnalysisTurn(
                            "turn-2",
                            2,
                            "Summarize the previous gate",
                            DataAgentAnalysisTurnIntent.Continue,
                            secondTurnAt,
                            "engineering_gate",
                            "SELECT status FROM engineering_gate LIMIT 20",
                            1,
                            "still missing",
                            true,
                            string.Empty)
                    ]).ToArray()
                });

            bool ended = reloadedStore.End(created.SessionId, endedAt);
            DataAgentAnalysisSession? final = reloadedStore.Get(created.SessionId);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded!.SessionId, Is.EqualTo(created.SessionId));
                Assert.That(reloaded.CallerId, Is.EqualTo("owner (/data_agent_analysis_session_context)"));
                Assert.That(reloaded.Goal, Is.EqualTo("analyze checkpoint persistence"));
                Assert.That(reloaded.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
                Assert.That(reloaded.Turns, Has.Count.EqualTo(1));
                Assert.That(reloaded.Turns[0].TurnId, Is.EqualTo("turn-1"));
                Assert.That(reloaded.Turns[0].Validated, Is.True);
                Assert.That(updated, Is.Not.Null);
                Assert.That(updated!.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.ReadyToSummarize));
                Assert.That(updated.Turns, Has.Count.EqualTo(2));
                Assert.That(ended, Is.True);
                Assert.That(final, Is.Not.Null);
                Assert.That(final!.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
                Assert.That(final.UpdatedAt, Is.EqualTo(endedAt));
                Assert.That(final.Turns, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            DeleteSessionRows(connectionString, created.SessionId);
        }
    }

    [Test]
    public void LivePostgresStoreDoesNotReopenEndedSessionFromOldSnapshot()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        DateTimeOffset createdAt = new(2026, 7, 5, 11, 0, 0, TimeSpan.Zero);
        DateTimeOffset endedAt = createdAt.AddMinutes(2);
        DateTimeOffset staleAt = createdAt.AddMinutes(3);
        DataAgentAnalysisSession session = store.Create("owner", "protect terminal checkpoint", createdAt);

        try
        {
            DataAgentAnalysisSession activeSnapshot = session with
            {
                Status = DataAgentAnalysisSessionStatus.ReadyToSummarize,
                UpdatedAt = staleAt,
                LastDataset = "document_index",
                LastSummary = "stale active summary"
            };

            Assert.That(store.End(session.SessionId, endedAt), Is.True);
            DataAgentAnalysisSession saved = store.Save(activeSnapshot);
            DataAgentAnalysisSession? loaded = store.Get(session.SessionId);

            Assert.Multiple(() =>
            {
                Assert.That(saved.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
                Assert.That(loaded.UpdatedAt, Is.EqualTo(endedAt));
                Assert.That(loaded.LastSummary, Is.Null);
            });
        }
        finally
        {
            DeleteSessionRows(connectionString, session.SessionId);
        }
    }

    [Test]
    public void LivePostgresUpdateOnEndedSessionDoesNotInvokeCallback()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        DateTimeOffset createdAt = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset endedAt = createdAt.AddMinutes(1);
        DataAgentAnalysisSession session = store.Create("owner", "skip stale update callback", createdAt);

        try
        {
            Assert.That(store.End(session.SessionId, endedAt), Is.True);
            bool callbackInvoked = false;

            DataAgentAnalysisSession? updated = store.Update(
                session.SessionId,
                current =>
                {
                    callbackInvoked = true;
                    return current with
                    {
                        Status = DataAgentAnalysisSessionStatus.ReadyToSummarize,
                        UpdatedAt = endedAt.AddMinutes(1),
                        LastSummary = "should not be saved"
                    };
                });

            Assert.Multiple(() =>
            {
                Assert.That(callbackInvoked, Is.False);
                Assert.That(updated, Is.Not.Null);
                Assert.That(updated!.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
                Assert.That(updated.UpdatedAt, Is.EqualTo(endedAt));
                Assert.That(updated.LastSummary, Is.Null);
            });
        }
        finally
        {
            DeleteSessionRows(connectionString, session.SessionId);
        }
    }

    [Test]
    public void LivePostgresUpdateThrowsWhenCallbackChangesSessionId()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        DataAgentAnalysisSession session = store.Create(
            "owner",
            "reject changed checkpoint session id",
            new DateTimeOffset(2026, 7, 5, 13, 0, 0, TimeSpan.Zero));

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                store.Update(session.SessionId, current => current with { SessionId = "changed-session-id" }))!;

            DataAgentAnalysisSession? loaded = store.Get(session.SessionId);
            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Does.Contain("Session update cannot change the session id."));
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.SessionId, Is.EqualTo(session.SessionId));
                Assert.That(loaded.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            });
        }
        finally
        {
            DeleteSessionRows(connectionString, session.SessionId);
        }
    }

    [Test]
    public void LivePostgresEndReturnsFalseForBlankAndMissingIds()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        DateTimeOffset now = new(2026, 7, 5, 14, 0, 0, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(store.End(string.Empty, now), Is.False);
            Assert.That(store.End("   ", now), Is.False);
            Assert.That(store.End($"missing-{Guid.NewGuid():N}", now), Is.False);
        });
    }

    [Test]
    public void LivePostgresGetAndSaveReturnReadOnlyTurnCopies()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        DateTimeOffset createdAt = new(2026, 7, 5, 15, 0, 0, TimeSpan.Zero);
        DateTimeOffset turnAt = createdAt.AddMinutes(1);
        DataAgentAnalysisTurn turn = new(
            "turn-readonly",
            1,
            "Which checkpoint rows were saved?",
            DataAgentAnalysisTurnIntent.NewQuestion,
            turnAt,
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            1,
            "one row",
            true,
            string.Empty);
        DataAgentAnalysisSession session = store.Create("owner", "return immutable checkpoint turns", createdAt);

        try
        {
            DataAgentAnalysisTurn[] mutableTurns = [turn];
            DataAgentAnalysisSession saved = store.Save(session with { UpdatedAt = turnAt, Turns = mutableTurns });
            mutableTurns[0] = turn with { TurnId = "mutated-after-save" };
            DataAgentAnalysisSession? loaded = store.Get(session.SessionId);

            Assert.Multiple(() =>
            {
                Assert.That(saved.Turns, Is.Not.SameAs(mutableTurns));
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.Turns, Is.Not.SameAs(saved.Turns));
                Assert.That(saved.Turns[0].TurnId, Is.EqualTo("turn-readonly"));
                Assert.That(loaded.Turns[0].TurnId, Is.EqualTo("turn-readonly"));
                AssertReadOnlyTurns(saved.Turns);
                AssertReadOnlyTurns(loaded.Turns);
            });
        }
        finally
        {
            DeleteSessionRows(connectionString, session.SessionId);
        }
    }

    [Test]
    public void LivePostgresGetReturnsCoherentSnapshotDuringConcurrentTurnReplacement()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        string applicationName = $"DataAgentAtomicGetTest-{Guid.NewGuid():N}";
        string getConnectionString = BuildConnectionStringWithApplicationName(connectionString, applicationName);
        PostgresDataAgentAnalysisSessionStore store = new(getConnectionString);
        store.Initialize();
        DateTimeOffset createdAt = new(2026, 7, 5, 17, 0, 0, TimeSpan.Zero);
        DateTimeOffset oldTurnAt = createdAt.AddMinutes(1);
        DateTimeOffset newTurnAt = createdAt.AddMinutes(2);
        DataAgentAnalysisSession session = store.Create(
            "owner",
            "read coherent checkpoint while writer replaces turns",
            createdAt);

        try
        {
            store.Save(session with
            {
                Status = DataAgentAnalysisSessionStatus.AwaitingClarification,
                UpdatedAt = oldTurnAt,
                LastDataset = "document_index",
                LastSummary = "old summary",
                Turns =
                [
                    new DataAgentAnalysisTurn(
                        "old-turn",
                        1,
                        "old question",
                        DataAgentAnalysisTurnIntent.NewQuestion,
                        oldTurnAt,
                        "document_index",
                        "SELECT path FROM document_index LIMIT 20",
                        1,
                        "old turn summary",
                        true,
                        string.Empty)
                ]
            });

            using NpgsqlConnection writerConnection = new(connectionString);
            writerConnection.Open();
            using NpgsqlTransaction writerTransaction = writerConnection.BeginTransaction();
            bool writerCompleted = false;

            try
            {
                Execute(
                    writerConnection,
                    writerTransaction,
                    "LOCK TABLE dataagent_analysis_turn IN ACCESS EXCLUSIVE MODE");
                Execute(
                    writerConnection,
                    writerTransaction,
                    """
                    UPDATE dataagent_analysis_session
                    SET status = @status,
                        updated_at = @updated_at,
                        last_dataset = @last_dataset,
                        last_summary = @last_summary,
                        pending_clarification_question = @pending_clarification_question
                    WHERE session_id = @session_id
                    """,
                    new NpgsqlParameter("status", (int)DataAgentAnalysisSessionStatus.ReadyToSummarize),
                    new NpgsqlParameter("updated_at", newTurnAt.ToString("O", CultureInfo.InvariantCulture)),
                    new NpgsqlParameter("last_dataset", "engineering_gate"),
                    new NpgsqlParameter("last_summary", "new summary"),
                    new NpgsqlParameter("pending_clarification_question", DBNull.Value),
                    new NpgsqlParameter("session_id", session.SessionId));
                Execute(
                    writerConnection,
                    writerTransaction,
                    "DELETE FROM dataagent_analysis_turn WHERE session_id = @session_id",
                    new NpgsqlParameter("session_id", session.SessionId));
                Execute(
                    writerConnection,
                    writerTransaction,
                    """
                    INSERT INTO dataagent_analysis_turn (
                        session_id,
                        turn_index,
                        turn_id,
                        question,
                        intent,
                        created_at,
                        dataset,
                        sql,
                        row_count,
                        summary,
                        validated,
                        rejected_reason)
                    VALUES (
                        @session_id,
                        @turn_index,
                        @turn_id,
                        @question,
                        @intent,
                        @created_at,
                        @dataset,
                        @sql,
                        @row_count,
                        @summary,
                        @validated,
                        @rejected_reason)
                    """,
                    new NpgsqlParameter("session_id", session.SessionId),
                    new NpgsqlParameter("turn_index", 1),
                    new NpgsqlParameter("turn_id", "new-turn"),
                    new NpgsqlParameter("question", "new question"),
                    new NpgsqlParameter("intent", (int)DataAgentAnalysisTurnIntent.Continue),
                    new NpgsqlParameter("created_at", newTurnAt.ToString("O", CultureInfo.InvariantCulture)),
                    new NpgsqlParameter("dataset", "engineering_gate"),
                    new NpgsqlParameter("sql", "SELECT name FROM engineering_gate LIMIT 20"),
                    new NpgsqlParameter("row_count", 1),
                    new NpgsqlParameter("summary", "new turn summary"),
                    new NpgsqlParameter("validated", true),
                    new NpgsqlParameter("rejected_reason", string.Empty));

                Task<DataAgentAnalysisSession?> getTask = Task.Run(() => store.Get(session.SessionId));

                if (WaitForWaitingTurnTableLock(connectionString, applicationName, TimeSpan.FromSeconds(5)) == false)
                {
                    writerTransaction.Rollback();
                    writerCompleted = true;
                    Assert.Fail(
                        "Timed out waiting for store.Get to block on AccessShareLock for dataagent_analysis_turn; " +
                        "the test did not force the session-read/turn-read interleaving.");
                }

                writerTransaction.Commit();
                writerCompleted = true;

                DataAgentAnalysisSession? duringWrite = AwaitTaskWithTimeout(getTask, TimeSpan.FromSeconds(5));
                AssertCoherentSnapshot(
                    duringWrite,
                    DataAgentAnalysisSessionStatus.AwaitingClarification,
                    oldTurnAt,
                    "document_index",
                    "old summary",
                    "old-turn",
                    "old turn summary");

                DataAgentAnalysisSession? final = store.Get(session.SessionId);
                AssertCoherentSnapshot(
                    final,
                    DataAgentAnalysisSessionStatus.ReadyToSummarize,
                    newTurnAt,
                    "engineering_gate",
                    "new summary",
                    "new-turn",
                    "new turn summary");
            }
            finally
            {
                if (writerCompleted == false)
                    writerTransaction.Rollback();
            }
        }
        finally
        {
            DeleteSessionRows(connectionString, session.SessionId);
        }
    }

    [Test]
    public void SourceUsesTransactionsAndRowLockForUpdates()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "PostgresDataAgentAnalysisSessionStore.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("BeginTransaction"));
            Assert.That(source, Does.Contain("FOR UPDATE"));
            Assert.That(source, Does.Contain("dataagent_analysis_session"));
            Assert.That(source, Does.Contain("dataagent_analysis_turn"));
            Assert.That(source, Does.Contain("Save(DataAgentAnalysisSession session)"));
            Assert.That(source, Does.Contain("Update("));
            Assert.That(source, Does.Contain("End("));
        });
    }

    [Test]
    public void SourceUsesRepeatableReadTransactionForAtomicGet()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "PostgresDataAgentAnalysisSessionStore.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("BeginTransaction(IsolationLevel.RepeatableRead)"));
            Assert.That(source, Does.Contain("LoadSession(connection, transaction, sessionId, forUpdate: false)"));
        });
    }

    [Test]
    public void SourceUsesInvariantTimestampFormattingForPersistedValues()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "PostgresDataAgentAnalysisSessionStore.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(".ToString(\"O\", CultureInfo.InvariantCulture)"));
            Assert.That(source, Does.Not.Contain(".ToString(\"O\")"));
        });
    }

    static void AssertReadOnlyTurns(IReadOnlyList<DataAgentAnalysisTurn> turns)
    {
        Assert.That(turns, Is.AssignableTo<ICollection<DataAgentAnalysisTurn>>());
        ICollection<DataAgentAnalysisTurn> collection = (ICollection<DataAgentAnalysisTurn>)turns;
        Assert.That(collection.IsReadOnly, Is.True);
        Assert.Throws<NotSupportedException>(() => collection.Add(new DataAgentAnalysisTurn(
            "turn-add",
            99,
            "attempt mutation",
            DataAgentAnalysisTurnIntent.Continue,
            new DateTimeOffset(2026, 7, 5, 16, 0, 0, TimeSpan.Zero),
            "document_index",
            "SELECT 1",
            1,
            "mutation",
            true,
            string.Empty)));
    }

    static void AssertCoherentSnapshot(
        DataAgentAnalysisSession? session,
        DataAgentAnalysisSessionStatus status,
        DateTimeOffset updatedAt,
        string dataset,
        string summary,
        string turnId,
        string turnSummary)
    {
        Assert.Multiple(() =>
        {
            Assert.That(session, Is.Not.Null);
            Assert.That(session!.Status, Is.EqualTo(status));
            Assert.That(session.UpdatedAt, Is.EqualTo(updatedAt));
            Assert.That(session.LastDataset, Is.EqualTo(dataset));
            Assert.That(session.LastSummary, Is.EqualTo(summary));
            Assert.That(session.Turns, Has.Count.EqualTo(1));
            Assert.That(session.Turns[0].TurnId, Is.EqualTo(turnId));
            Assert.That(session.Turns[0].Dataset, Is.EqualTo(dataset));
            Assert.That(session.Turns[0].Summary, Is.EqualTo(turnSummary));
        });
    }

    static string BuildConnectionStringWithApplicationName(string connectionString, string applicationName)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString)
        {
            ApplicationName = applicationName
        };

        return builder.ConnectionString;
    }

    static bool WaitForWaitingTurnTableLock(
        string connectionString,
        string applicationName,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (HasWaitingTurnTableLock(connectionString, applicationName))
                return true;

            Thread.Sleep(50);
        }

        return false;
    }

    static bool HasWaitingTurnTableLock(string connectionString, string applicationName)
    {
        using NpgsqlConnection connection = new(connectionString);
        connection.Open();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_locks l
                JOIN pg_class c ON c.oid = l.relation
                JOIN pg_stat_activity a ON a.pid = l.pid
                WHERE c.relname = 'dataagent_analysis_turn'
                  AND l.mode = 'AccessShareLock'
                  AND l.granted = false
                  AND a.application_name = @application_name
            )
            """;
        command.Parameters.Add(new NpgsqlParameter("application_name", applicationName));
        return (bool)command.ExecuteScalar()!;
    }

    static T? AwaitTaskWithTimeout<T>(Task<T?> task, TimeSpan timeout)
    {
        Assert.That(task.Wait(timeout), Is.True, "Timed out waiting for store.Get after committing writer transaction.");
        return task.Result;
    }

    static void DeleteSessionRows(string connectionString, string sessionId)
    {
        using NpgsqlConnection connection = new(connectionString);
        connection.Open();

        Execute(
            connection,
            "DELETE FROM dataagent_analysis_turn WHERE session_id = @session_id",
            new NpgsqlParameter("session_id", sessionId));
        Execute(
            connection,
            "DELETE FROM dataagent_analysis_session WHERE session_id = @session_id",
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

    static void Execute(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText,
        params NpgsqlParameter[] parameters)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;

        foreach (NpgsqlParameter parameter in parameters)
            command.Parameters.Add(parameter);

        command.ExecuteNonQuery();
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "sources")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
