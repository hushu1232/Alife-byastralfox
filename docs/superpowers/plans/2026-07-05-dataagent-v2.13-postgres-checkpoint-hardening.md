# DataAgent V2.13 PostgreSQL Checkpoint Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Productize optional PostgreSQL-backed DataAgent analysis checkpoint persistence so long DataAgent analysis sessions can recover across process restarts without changing SQL authority, QChat ownership, or the existing C# safety pipeline.

**Architecture:** V2.13 keeps the current C# DataAgent pipeline as the system of record. The existing `IDataAgentStore` remains the provider-neutral query/audit store, while a new PostgreSQL implementation of the existing `IDataAgentAnalysisSessionStore` persists analysis sessions and turns that already drive `DataAgentOrchestrationCheckpoint`. `DataAgentModuleService` selects the checkpoint/session store through a small environment-backed factory, defaulting to in-memory unless PostgreSQL checkpointing is explicitly configured.

**Tech Stack:** .NET 9, C#, Npgsql, NUnit, existing DataAgent orchestration/session/checkpoint models, existing PowerShell readiness scripts.

---

## Working Context

Use the V2.13 worktree:

```powershell
cd D:\Alife\.worktrees\alife-v2.13-postgres-checkpoint-hardening
git status --short --branch
```

The worktree starts from V2.12 head:

```text
f8f267977d924938eedb2e70e7b59daaabc8c8ad
Finalize DataAgent V2.12 runtime scenario activation
```

Use the user-local .NET 9 SDK:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Baseline already passed before this plan was written:

```text
tools/check-dataagent-readiness.ps1: 81 required passed, 0 required missing
tools/check-qchat-engineering-map.ps1: 56 required passed, 0 required missing, 0 optional present, 0 optional missing
```

Guardrails:

- Do not touch `D:\FOXD`, `D:\FOXD\alife-service`, or ASRRAL-FOX.
- Do not introduce LangGraph, StateGraph, a Python sidecar, or a new SQL execution path.
- Do not refactor the QChat main loop.
- Do not add natural-language QChat command auto-execution.
- Do not make PostgreSQL the default. SQLite query storage and in-memory checkpoint storage remain default-compatible.
- Do not persist progress streaming in V2.13. Progress remains an owner diagnostics stream, not the recovery authority.
- Do not let QChat reference `PostgresDataAgentAnalysisSessionStore` or `DataAgentAnalysisSessionStoreFactory` directly.
- Checkpoint authority remains derived from `DataAgentAnalysisSession` state and `DataAgentOrchestrationCheckpoint`, not a separate graph runtime.
- Use `git add -f` for ignored `docs/superpowers/*` files when committing plans.

## Current State

V2.12 already has:

- PostgreSQL query/audit provider: `PostgresDataAgentStore`.
- Environment-gated PostgreSQL live tests: `DataAgentPostgresStoreTests`.
- Provider-neutral query/audit store boundary: `IDataAgentStore`.
- Analysis checkpoint model: `DataAgentOrchestrationCheckpoint`.
- Analysis session boundary: `IDataAgentAnalysisSessionStore`.
- In-memory analysis session implementation: `InMemoryDataAgentAnalysisSessionStore`.
- Runtime progress stream and owner diagnostics.
- Scenario context activated at runtime before planner execution.

V2.13 fills the missing product gap:

- Analysis sessions and checkpoint state currently disappear on process restart because `DataAgentModuleService` constructs `InMemoryDataAgentAnalysisSessionStore`.
- PostgreSQL is implemented for query/audit storage, but not for recoverable analysis sessions.
- Checkpoint readiness exists, but it does not prove a configured runtime can persist and reload checkpoint state through PostgreSQL.

## Scope Lock

V2.13 must do:

- Add `PostgresDataAgentAnalysisSessionStore` implementing the existing `IDataAgentAnalysisSessionStore`.
- Store `DataAgentAnalysisSession` and `DataAgentAnalysisTurn` in PostgreSQL tables owned by DataAgent.
- Preserve current in-memory behavior and snapshot semantics.
- Preserve `SaveAfterEndWithOldActiveSnapshotDoesNotReopenSession` and `UpdateAfterEndCannotReopenSession` behavior in PostgreSQL.
- Add a small `DataAgentAnalysisSessionStoreFactory` with environment-backed provider selection.
- Wire `DataAgentModuleService` through that factory.
- Add live PostgreSQL tests behind `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`.
- Add static readiness and QChat engineering-map gates.
- Document how PostgreSQL checkpointing is enabled and what it does not change.

V2.13 must not do:

- No LangGraph runtime contract.
- No sidecar process.
- No new `StateGraph`.
- No new model-driven SQL execution path.
- No ORM or migration framework.
- No persistence of progress event stream.
- No changes to Tool Broker route semantics.
- No changes to QChat prompt or owner command behavior.

---

## File Structure

Create:

- `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentAnalysisSessionStore.cs`
  - PostgreSQL implementation of `IDataAgentAnalysisSessionStore`.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSessionStoreFactory.cs`
  - Environment-backed factory for `memory` and `postgres` analysis checkpoint/session stores.

- `Tests/Alife.Test.DataAgent/DataAgentPostgresAnalysisSessionStoreTests.cs`
  - Environment-gated live PostgreSQL persistence and recovery tests.

- `Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreFactoryTests.cs`
  - Unit tests for default memory provider, explicit PostgreSQL provider, env fallback, and missing connection validation.

- `docs/dataagent/dataagent-v2.13-postgres-checkpoint-hardening.md`
  - Runtime design note for PostgreSQL checkpoint productization.

Modify:

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Replace direct in-memory session-store construction with `DataAgentAnalysisSessionStoreFactory`.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add runtime/static readiness check `PostgresCheckpointPersistencePresent`.

- `tools/check-dataagent-readiness.ps1`
  - Add static gate and update required count `81 -> 82`.

- `tools/check-qchat-engineering-map.ps1`
  - Add required QChat map check and update required count `56 -> 57`.

- `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Update core readiness count `67 -> 68`, script summary, and V2.13 contract assertions.

- `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
  - Update readiness script count assertions to the new total.

- `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`
  - Assert runtime module uses the configured analysis session store factory.

- `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Require the new V2.13 engineering-map gate and guard QChat from direct checkpoint provider imports.

- `docs/superpowers/plans/2026-07-03-alife-v2.10-capability-agent-orchestration.md`
  - Correct the later-work sequence to reflect V2.11/V2.12 reality and V2.13/V2.14/V2.15 planning.

---

### Task 1: PostgreSQL Analysis Session Store

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentAnalysisSessionStore.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentPostgresAnalysisSessionStoreTests.cs`

- [ ] **Step 1: Write failing PostgreSQL session-store tests**

Create `Tests/Alife.Test.DataAgent/DataAgentPostgresAnalysisSessionStoreTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the new tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentPostgresAnalysisSessionStoreTests" -v:minimal
```

Expected:

```text
FAIL
PostgresDataAgentAnalysisSessionStore does not exist
```

When `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is not set, only the source-contract test should run after the class exists; live tests remain skipped/ignored.

- [ ] **Step 3: Add PostgreSQL analysis session store**

Create `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentAnalysisSessionStore.cs`:

```csharp
using System.Data;
using Npgsql;

namespace Alife.Function.DataAgent;

public sealed class PostgresDataAgentAnalysisSessionStore : IDataAgentAnalysisSessionStore
{
    readonly string connectionString;

    public PostgresDataAgentAnalysisSessionStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        this.connectionString = connectionString;
    }

    public void Initialize()
    {
        using NpgsqlConnection connection = Open();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS dataagent_analysis_session (
                session_id TEXT PRIMARY KEY,
                caller_id TEXT NOT NULL,
                goal TEXT NOT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_dataset TEXT NULL,
                last_summary TEXT NULL,
                pending_clarification_question TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS dataagent_analysis_turn (
                turn_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL REFERENCES dataagent_analysis_session(session_id) ON DELETE CASCADE,
                turn_index INTEGER NOT NULL,
                question TEXT NOT NULL,
                intent INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                dataset TEXT NOT NULL,
                sql TEXT NOT NULL,
                row_count INTEGER NOT NULL,
                summary TEXT NOT NULL,
                validated BOOLEAN NOT NULL,
                rejected_reason TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_dataagent_analysis_turn_session_index
            ON dataagent_analysis_turn(session_id, turn_index);
            """;
        command.ExecuteNonQuery();
    }

    public DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(goal);

        string safeCallerId = DataAgentContextFieldSanitizer.Sanitize(callerId ?? string.Empty, 80);
        if (string.IsNullOrWhiteSpace(safeCallerId))
            safeCallerId = "local";

        string safeGoal = DataAgentContextFieldSanitizer.Sanitize(goal, 240);
        if (string.IsNullOrWhiteSpace(safeGoal))
            throw new ArgumentException("Goal cannot be empty after sanitization.", nameof(goal));

        DataAgentAnalysisSession session = new(
            Guid.NewGuid().ToString("N"),
            safeCallerId,
            safeGoal,
            DataAgentAnalysisSessionStatus.Active,
            now,
            now,
            null,
            null,
            null,
            []);

        using NpgsqlConnection connection = Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        UpsertSession(connection, transaction, Snapshot(session));
        transaction.Commit();
        return Snapshot(session);
    }

    public DataAgentAnalysisSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        using NpgsqlConnection connection = Open();
        return LoadSession(connection, null, sessionId, forUpdate: false) is { } session
            ? Snapshot(session)
            : null;
    }

    public DataAgentAnalysisSession Save(DataAgentAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        DataAgentAnalysisSession incoming = Snapshot(session);
        using NpgsqlConnection connection = Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        DataAgentAnalysisSession? current = LoadSession(connection, transaction, incoming.SessionId, forUpdate: true);
        if (current is not null &&
            current.Status == DataAgentAnalysisSessionStatus.Ended &&
            incoming.Status != DataAgentAnalysisSessionStatus.Ended)
        {
            transaction.Commit();
            return Snapshot(current);
        }

        UpsertSession(connection, transaction, incoming);
        ReplaceTurns(connection, transaction, incoming);
        transaction.Commit();
        return Snapshot(incoming);
    }

    public DataAgentAnalysisSession? Update(
        string sessionId,
        Func<DataAgentAnalysisSession, DataAgentAnalysisSession> update)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        ArgumentNullException.ThrowIfNull(update);

        using NpgsqlConnection connection = Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        DataAgentAnalysisSession? current = LoadSession(connection, transaction, sessionId, forUpdate: true);
        if (current is null)
            return null;

        if (current.Status == DataAgentAnalysisSessionStatus.Ended)
        {
            transaction.Commit();
            return Snapshot(current);
        }

        DataAgentAnalysisSession updated = update(Snapshot(current));
        ArgumentNullException.ThrowIfNull(updated);

        if (string.Equals(updated.SessionId, sessionId, StringComparison.Ordinal) == false)
            throw new InvalidOperationException("Session update cannot change the session id.");

        DataAgentAnalysisSession snapshot = Snapshot(updated);
        UpsertSession(connection, transaction, snapshot);
        ReplaceTurns(connection, transaction, snapshot);
        transaction.Commit();
        return Snapshot(snapshot);
    }

    public bool End(string sessionId, DateTimeOffset now)
    {
        DataAgentAnalysisSession? updated = Update(
            sessionId,
            current => current with
            {
                Status = DataAgentAnalysisSessionStatus.Ended,
                UpdatedAt = now
            });

        return updated is not null;
    }

    NpgsqlConnection Open()
    {
        NpgsqlConnection connection = new(connectionString);
        connection.Open();
        return connection;
    }

    static DataAgentAnalysisSession? LoadSession(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sessionId,
        bool forUpdate)
    {
        using NpgsqlCommand sessionCommand = connection.CreateCommand();
        sessionCommand.Transaction = transaction;
        sessionCommand.CommandText = $"""
            SELECT session_id, caller_id, goal, status, created_at, updated_at,
                   last_dataset, last_summary, pending_clarification_question
            FROM dataagent_analysis_session
            WHERE session_id = @session_id
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;
        sessionCommand.Parameters.Add(new NpgsqlParameter("session_id", sessionId));

        using NpgsqlDataReader sessionReader = sessionCommand.ExecuteReader();
        if (sessionReader.Read() == false)
            return null;

        string loadedSessionId = sessionReader.GetString(0);
        string callerId = sessionReader.GetString(1);
        string goal = sessionReader.GetString(2);
        DataAgentAnalysisSessionStatus status = (DataAgentAnalysisSessionStatus)sessionReader.GetInt32(3);
        DateTimeOffset createdAt = DateTimeOffset.Parse(sessionReader.GetString(4));
        DateTimeOffset updatedAt = DateTimeOffset.Parse(sessionReader.GetString(5));
        string? lastDataset = sessionReader.IsDBNull(6) ? null : sessionReader.GetString(6);
        string? lastSummary = sessionReader.IsDBNull(7) ? null : sessionReader.GetString(7);
        string? pendingClarificationQuestion = sessionReader.IsDBNull(8) ? null : sessionReader.GetString(8);
        sessionReader.Close();

        IReadOnlyList<DataAgentAnalysisTurn> turns = LoadTurns(connection, transaction, loadedSessionId);
        return new DataAgentAnalysisSession(
            loadedSessionId,
            callerId,
            goal,
            status,
            createdAt,
            updatedAt,
            lastDataset,
            lastSummary,
            pendingClarificationQuestion,
            turns);
    }

    static IReadOnlyList<DataAgentAnalysisTurn> LoadTurns(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sessionId)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT turn_id, turn_index, question, intent, created_at, dataset, sql,
                   row_count, summary, validated, rejected_reason
            FROM dataagent_analysis_turn
            WHERE session_id = @session_id
            ORDER BY turn_index ASC, turn_id ASC
            """;
        command.Parameters.Add(new NpgsqlParameter("session_id", sessionId));

        using NpgsqlDataReader reader = command.ExecuteReader();
        List<DataAgentAnalysisTurn> turns = [];
        while (reader.Read())
        {
            turns.Add(new DataAgentAnalysisTurn(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                (DataAgentAnalysisTurnIntent)reader.GetInt32(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.GetBoolean(9),
                reader.GetString(10)));
        }

        return turns.ToArray();
    }

    static void UpsertSession(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DataAgentAnalysisSession session)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dataagent_analysis_session (
                session_id, caller_id, goal, status, created_at, updated_at,
                last_dataset, last_summary, pending_clarification_question)
            VALUES (
                @session_id, @caller_id, @goal, @status, @created_at, @updated_at,
                @last_dataset, @last_summary, @pending_clarification_question)
            ON CONFLICT (session_id) DO UPDATE SET
                caller_id = EXCLUDED.caller_id,
                goal = EXCLUDED.goal,
                status = EXCLUDED.status,
                created_at = EXCLUDED.created_at,
                updated_at = EXCLUDED.updated_at,
                last_dataset = EXCLUDED.last_dataset,
                last_summary = EXCLUDED.last_summary,
                pending_clarification_question = EXCLUDED.pending_clarification_question
            """;
        command.Parameters.Add(new NpgsqlParameter("session_id", session.SessionId));
        command.Parameters.Add(new NpgsqlParameter("caller_id", session.CallerId));
        command.Parameters.Add(new NpgsqlParameter("goal", session.Goal));
        command.Parameters.Add(new NpgsqlParameter("status", (int)session.Status));
        command.Parameters.Add(new NpgsqlParameter("created_at", session.CreatedAt.ToUniversalTime().ToString("O")));
        command.Parameters.Add(new NpgsqlParameter("updated_at", session.UpdatedAt.ToUniversalTime().ToString("O")));
        command.Parameters.Add(new NpgsqlParameter("last_dataset", (object?)session.LastDataset ?? DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("last_summary", (object?)session.LastSummary ?? DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter(
            "pending_clarification_question",
            (object?)session.PendingClarificationQuestion ?? DBNull.Value));
        command.ExecuteNonQuery();
    }

    static void ReplaceTurns(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DataAgentAnalysisSession session)
    {
        using NpgsqlCommand delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM dataagent_analysis_turn WHERE session_id = @session_id";
        delete.Parameters.Add(new NpgsqlParameter("session_id", session.SessionId));
        delete.ExecuteNonQuery();

        foreach (DataAgentAnalysisTurn turn in session.Turns)
            InsertTurn(connection, transaction, session.SessionId, turn);
    }

    static void InsertTurn(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sessionId,
        DataAgentAnalysisTurn turn)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dataagent_analysis_turn (
                turn_id, session_id, turn_index, question, intent, created_at, dataset, sql,
                row_count, summary, validated, rejected_reason)
            VALUES (
                @turn_id, @session_id, @turn_index, @question, @intent, @created_at, @dataset, @sql,
                @row_count, @summary, @validated, @rejected_reason)
            """;
        command.Parameters.Add(new NpgsqlParameter("turn_id", turn.TurnId));
        command.Parameters.Add(new NpgsqlParameter("session_id", sessionId));
        command.Parameters.Add(new NpgsqlParameter("turn_index", turn.Index));
        command.Parameters.Add(new NpgsqlParameter("question", turn.Question));
        command.Parameters.Add(new NpgsqlParameter("intent", (int)turn.Intent));
        command.Parameters.Add(new NpgsqlParameter("created_at", turn.CreatedAt.ToUniversalTime().ToString("O")));
        command.Parameters.Add(new NpgsqlParameter("dataset", turn.Dataset));
        command.Parameters.Add(new NpgsqlParameter("sql", turn.Sql));
        command.Parameters.Add(new NpgsqlParameter("row_count", turn.RowCount));
        command.Parameters.Add(new NpgsqlParameter("summary", turn.Summary));
        command.Parameters.Add(new NpgsqlParameter("validated", turn.Validated));
        command.Parameters.Add(new NpgsqlParameter("rejected_reason", turn.RejectedReason));
        command.ExecuteNonQuery();
    }

    static DataAgentAnalysisSession Snapshot(DataAgentAnalysisSession session)
    {
        return session with { Turns = Array.AsReadOnly(session.Turns.ToArray()) };
    }
}
```

- [ ] **Step 4: Run PostgreSQL session-store tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentPostgresAnalysisSessionStoreTests" -v:minimal
```

Expected without live PostgreSQL connection:

```text
DataAgentPostgresAnalysisSessionStoreTests.SourceUsesTransactionsAndRowLockForUpdates: PASS
Live PostgreSQL tests skipped or ignored because ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set
0 failed
```

Expected with `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`:

```text
All DataAgentPostgresAnalysisSessionStoreTests pass
0 failed
```

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentAnalysisSessionStore.cs
git add Tests/Alife.Test.DataAgent/DataAgentPostgresAnalysisSessionStoreTests.cs
git commit -m "Add PostgreSQL DataAgent checkpoint session store"
```

Expected: commit succeeds with only Task 1 files.

---

### Task 2: Analysis Session Store Factory and Runtime Wiring

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSessionStoreFactory.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreFactoryTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Write failing factory tests**

Create `Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreFactoryTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisSessionStoreFactoryTests
{
    [Test]
    public void CreateDefaultUsesInMemorySessionStore()
    {
        IDataAgentAnalysisSessionStore store = DataAgentAnalysisSessionStoreFactory.Create(
            new DataAgentAnalysisSessionStoreOptions(
                ProviderName: string.Empty,
                PostgresConnectionString: string.Empty));

        Assert.That(store, Is.TypeOf<InMemoryDataAgentAnalysisSessionStore>());
    }

    [Test]
    public void CreateSupportsExplicitMemoryProvider()
    {
        IDataAgentAnalysisSessionStore store = DataAgentAnalysisSessionStoreFactory.Create(
            new DataAgentAnalysisSessionStoreOptions(
                ProviderName: "memory",
                PostgresConnectionString: string.Empty));

        Assert.That(store, Is.TypeOf<InMemoryDataAgentAnalysisSessionStore>());
    }

    [Test]
    public void CreateRejectsUnknownProvider()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentAnalysisSessionStoreFactory.Create(new DataAgentAnalysisSessionStoreOptions(
                ProviderName: "redis",
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("Unsupported DataAgent analysis session store provider"));
    }

    [Test]
    public void CreateRejectsPostgresWithoutConnectionString()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentAnalysisSessionStoreFactory.Create(new DataAgentAnalysisSessionStoreOptions(
                ProviderName: "postgres",
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION"));
    }

    [Test]
    public void FromEnvironmentDefaultsToMemoryProvider()
    {
        using EnvironmentScope scope = new();
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER", null);
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION", null);
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", null);

        DataAgentAnalysisSessionStoreOptions options =
            DataAgentAnalysisSessionStoreFactory.FromEnvironment();

        Assert.Multiple(() =>
        {
            Assert.That(options.ProviderName, Is.Empty);
            Assert.That(options.PostgresConnectionString, Is.Empty);
        });
    }

    [Test]
    public void FromEnvironmentUsesDedicatedCheckpointConnectionBeforeSharedPostgresConnection()
    {
        using EnvironmentScope scope = new();
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER", "postgres");
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION", "Host=checkpoint;Database=alife_checkpoint");
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", "Host=query;Database=alife_query");

        DataAgentAnalysisSessionStoreOptions options =
            DataAgentAnalysisSessionStoreFactory.FromEnvironment();

        Assert.Multiple(() =>
        {
            Assert.That(options.ProviderName, Is.EqualTo("postgres"));
            Assert.That(options.PostgresConnectionString, Is.EqualTo("Host=checkpoint;Database=alife_checkpoint"));
        });
    }

    [Test]
    public void FromEnvironmentFallsBackToSharedPostgresConnection()
    {
        using EnvironmentScope scope = new();
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER", "postgres");
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION", null);
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", "Host=shared;Database=alife");

        DataAgentAnalysisSessionStoreOptions options =
            DataAgentAnalysisSessionStoreFactory.FromEnvironment();

        Assert.That(options.PostgresConnectionString, Is.EqualTo("Host=shared;Database=alife"));
    }

    sealed class EnvironmentScope : IDisposable
    {
        readonly Dictionary<string, string?> previousValues = new(StringComparer.Ordinal);

        public void Set(string name, string? value)
        {
            if (previousValues.ContainsKey(name) == false)
                previousValues[name] = Environment.GetEnvironmentVariable(name);

            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in previousValues)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
```

- [ ] **Step 2: Update module static tests before implementation**

Modify `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs` by adding this test after `AwakeUsesConfiguredDataAgentStoreBoundary`:

```csharp
[Test]
public void AwakeUsesConfiguredAnalysisSessionStoreBoundary()
{
    string source = ReadModuleSource();

    Assert.Multiple(() =>
    {
        Assert.That(source, Does.Contain("IDataAgentAnalysisSessionStore"));
        Assert.That(source, Does.Contain("DataAgentAnalysisSessionStoreFactory.Create"));
        Assert.That(source, Does.Contain("DataAgentAnalysisSessionStoreFactory.FromEnvironment"));
        Assert.That(source, Does.Not.Contain("new InMemoryDataAgentAnalysisSessionStore()"));
    });
}
```

- [ ] **Step 3: Run factory and module tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisSessionStoreFactoryTests|FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: compile/test failure until `DataAgentAnalysisSessionStoreFactory` exists and `DataAgentModuleService` uses it.

- [ ] **Step 4: Add the analysis session store factory**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSessionStoreFactory.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentAnalysisSessionStoreOptions(
    string ProviderName,
    string PostgresConnectionString);

public static class DataAgentAnalysisSessionStoreFactory
{
    public static IDataAgentAnalysisSessionStore Create(DataAgentAnalysisSessionStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string provider = string.IsNullOrWhiteSpace(options.ProviderName)
            ? "memory"
            : options.ProviderName.Trim().ToLowerInvariant();

        return provider switch
        {
            "memory" => new InMemoryDataAgentAnalysisSessionStore(),
            "postgres" => CreatePostgres(options.PostgresConnectionString),
            _ => throw new InvalidOperationException(
                $"Unsupported DataAgent analysis session store provider: {options.ProviderName}")
        };
    }

    public static DataAgentAnalysisSessionStoreOptions FromEnvironment()
    {
        string dedicatedPostgresConnection =
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION") ??
            string.Empty;
        string sharedPostgresConnection =
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_CONNECTION") ??
            string.Empty;

        return new DataAgentAnalysisSessionStoreOptions(
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER") ?? string.Empty,
            string.IsNullOrWhiteSpace(dedicatedPostgresConnection)
                ? sharedPostgresConnection
                : dedicatedPostgresConnection);
    }

    static IDataAgentAnalysisSessionStore CreatePostgres(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION or ALIFE_DATAAGENT_POSTGRES_CONNECTION is required when DataAgent postgres analysis session store is selected.");
        }

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        return store;
    }
}
```

- [ ] **Step 5: Wire DataAgentModuleService through the factory**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`.

Replace:

```csharp
InMemoryDataAgentAnalysisSessionStore analysisSessionStore = new InMemoryDataAgentAnalysisSessionStore();
```

with:

```csharp
IDataAgentAnalysisSessionStore analysisSessionStore = DataAgentAnalysisSessionStoreFactory.Create(
    DataAgentAnalysisSessionStoreFactory.FromEnvironment());
```

- [ ] **Step 6: Run factory and module tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisSessionStoreFactoryTests|FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected:

```text
0 failed
```

- [ ] **Step 7: Run existing session-store behavior tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisSessionStoreTests|FullyQualifiedName~DataAgentAnalysisServiceTests|FullyQualifiedName~DataAgentAnalysisOrchestratorTests" -v:minimal -m:1
```

Expected:

```text
0 failed
```

- [ ] **Step 8: Commit Task 2**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSessionStoreFactory.cs
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs
git add Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreFactoryTests.cs
git add Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs
git commit -m "Wire configurable DataAgent checkpoint session store"
```

Expected: commit succeeds with only Task 2 files.

---

### Task 3: V2.13 Readiness and QChat Map Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Update readiness tests before implementation**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, replace:

```csharp
Assert.That(checks, Has.Count.EqualTo(67));
```

with:

```csharp
Assert.That(checks, Has.Count.EqualTo(68));
```

Add near the existing store assertions:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresCheckpointPersistencePresent"));
DataAgentReadinessCheck postgresCheckpointCheck = checks.Single(check => check.Name == "PostgresCheckpointPersistencePresent");
Assert.That(postgresCheckpointCheck.Detail, Does.Contain("session_store=true"));
Assert.That(postgresCheckpointCheck.Detail, Does.Contain("factory=true"));
Assert.That(postgresCheckpointCheck.Detail, Does.Contain("module_wiring=true"));
Assert.That(postgresCheckpointCheck.Detail, Does.Contain("live_test_gated="));
```

Replace script summary:

```csharp
"  Summary: 81 required passed, 0 required missing"
```

with:

```csharp
"  Summary: 82 required passed, 0 required missing"
```

Replace every expected script count assertion:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 81"));
```

with:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 82"));
```

Add this contract test near the V2.12 contract test:

```csharp
[Test]
public void ReadinessScriptProtectsV213PostgresCheckpointPersistenceContract()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "PostgresCheckpointPersistencePresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("PostgresDataAgentAnalysisSessionStore.cs"));
        Assert.That(declaration, Does.Contain("DataAgentAnalysisSessionStoreFactory.cs"));
        Assert.That(declaration, Does.Contain("DataAgentModuleService.cs"));
        Assert.That(declaration, Does.Contain("dataagent_analysis_session"));
        Assert.That(declaration, Does.Contain("dataagent_analysis_turn"));
        Assert.That(declaration, Does.Contain("FOR UPDATE"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER"));
        Assert.That(declaration, Does.Contain("DataAgentPostgresAnalysisSessionStoreTests"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION"));
        Assert.That(declaration, Does.Contain("session_store=true"));
        Assert.That(declaration, Does.Contain("factory=true"));
        Assert.That(declaration, Does.Contain("module_wiring=true"));
        Assert.That(declaration, Does.Contain("live_test_gated="));
    });
}
```

In `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`, replace:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 81"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 56"));
```

with:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 82"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 57"));
```

- [ ] **Step 2: Update QChat engineering-map tests before implementation**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, append this to `RequiredV2Checks`:

```csharp
"DataAgent PostgreSQL checkpoint persistence"
```

Add this test after `RuntimeScenarioContextActivationCheckRequiresDataAgentRuntimeAndQChatBoundary`:

```csharp
[Test]
public void PostgresCheckpointPersistenceCheckRequiresDataAgentRuntimeAndQChatBoundary()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindAddCheckDeclaration(script, "DataAgent PostgreSQL checkpoint persistence");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("PostgresCheckpointPersistencePresent"));
        Assert.That(declaration, Does.Contain("PostgresDataAgentAnalysisSessionStore"));
        Assert.That(declaration, Does.Contain("DataAgentAnalysisSessionStoreFactory"));
        Assert.That(declaration, Does.Contain("DataAgentModuleService"));
        Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
    });
}
```

In `QChatDoesNotDirectlyImportDataAgentScenarioContextBuilder`, extend `forbiddenMarkers`:

```csharp
"PostgresDataAgentAnalysisSessionStore",
"DataAgentAnalysisSessionStoreFactory"
```

- [ ] **Step 3: Run readiness and QChat tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: failures until readiness and engineering-map gates are added.

- [ ] **Step 4: Add runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, add this check after `PostgresLiveTestsEnvironmentGated`:

```csharp
bool postgresCheckpointSessionStoreReady =
    typeof(IDataAgentAnalysisSessionStore).IsAssignableFrom(typeof(PostgresDataAgentAnalysisSessionStore));
bool postgresCheckpointFactoryReady =
    DataAgentAnalysisSessionStoreFactory.Create(new DataAgentAnalysisSessionStoreOptions(
        string.Empty,
        string.Empty)) is InMemoryDataAgentAnalysisSessionStore &&
    typeof(DataAgentAnalysisSessionStoreFactory)
        .GetMethod(nameof(DataAgentAnalysisSessionStoreFactory.FromEnvironment)) is not null;
bool postgresCheckpointModuleWiringReady =
    typeof(DataAgentModuleService).Assembly.GetReferencedAssemblies().Any(assemblyName =>
        string.Equals(assemblyName.Name, "Alife.Function.QChat", StringComparison.Ordinal)) == false;
bool postgresCheckpointLiveTestGated =
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION"));
bool postgresCheckpointReady =
    postgresCheckpointSessionStoreReady &&
    postgresCheckpointFactoryReady &&
    postgresCheckpointModuleWiringReady;
checks.Add(postgresCheckpointReady
    ? Pass(
        "PostgresCheckpointPersistencePresent",
        $"session_store=true;factory=true;module_wiring=true;live_test_gated={LowerBool(postgresCheckpointLiveTestGated)}")
    : Fail(
        "PostgresCheckpointPersistencePresent",
        $"session_store={LowerBool(postgresCheckpointSessionStoreReady)};factory={LowerBool(postgresCheckpointFactoryReady)};module_wiring={LowerBool(postgresCheckpointModuleWiringReady)};live_test_gated={LowerBool(postgresCheckpointLiveTestGated)}"));
```

If `LowerBool` already exists in `DataAgentReadiness.cs`, reuse it. If it does not, add:

```csharp
static string LowerBool(bool value)
{
    return value ? "true" : "false";
}
```

- [ ] **Step 5: Add DataAgent readiness script gate**

In `tools/check-dataagent-readiness.ps1`, add this check in the `Store` group after `PostgresLiveTestsEnvironmentGated`:

```powershell
    New-Check -Group "Store" -Name "PostgresCheckpointPersistencePresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentAnalysisSessionStore.cs" @("PostgresDataAgentAnalysisSessionStore", "IDataAgentAnalysisSessionStore", "dataagent_analysis_session", "dataagent_analysis_turn", "BeginTransaction", "FOR UPDATE", "Save(DataAgentAnalysisSession session)", "Update(", "End(")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSessionStoreFactory.cs" @("DataAgentAnalysisSessionStoreFactory", "DataAgentAnalysisSessionStoreOptions", "ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER", "ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION", "ALIFE_DATAAGENT_POSTGRES_CONNECTION", "PostgresDataAgentAnalysisSessionStore")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("IDataAgentAnalysisSessionStore", "DataAgentAnalysisSessionStoreFactory.Create", "DataAgentAnalysisSessionStoreFactory.FromEnvironment")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentPostgresAnalysisSessionStoreTests.cs" @("DataAgentPostgresAnalysisSessionStoreTests", "ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION", "LivePostgresStorePersistsReloadsUpdatesAndEndsCheckpointSession", "SourceUsesTransactionsAndRowLockForUpdates")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("PostgresCheckpointPersistencePresent", "session_store=true", "factory=true", "module_wiring=true", "live_test_gated="))) -Detail "V2.13 PostgreSQL analysis checkpoint persistence markers"
```

Replace:

```powershell
$expectedRequired = 81
```

with:

```powershell
$expectedRequired = 82
```

- [ ] **Step 6: Add QChat engineering-map gate**

In `tools/check-qchat-engineering-map.ps1`, add this check after `DataAgent store provider boundary`:

```powershell
Add-Check -Group "Harness" -Name "DataAgent PostgreSQL checkpoint persistence" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("PostgresCheckpointPersistencePresent", "PostgresDataAgentAnalysisSessionStore", "DataAgentAnalysisSessionStoreFactory", "DataAgentModuleService", "session_store=true", "factory=true") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("PostgresDataAgentAnalysisSessionStore", "DataAgentAnalysisSessionStoreFactory")
```

Replace:

```powershell
$expectedRequired = 56
```

with:

```powershell
$expectedRequired = 57
```

- [ ] **Step 7: Run readiness tests and scripts**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentPostgresAnalysisSessionStoreTests|FullyQualifiedName~DataAgentAnalysisSessionStoreFactoryTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent tests: PASS
QChat tests: PASS
DataAgent readiness: Summary: 82 required passed, 0 required missing
QChat engineering map: Summary: 57 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 8: Commit Task 3**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs
git add tools/check-dataagent-readiness.ps1
git add tools/check-qchat-engineering-map.ps1
git add Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git add Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs
git add Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Gate DataAgent PostgreSQL checkpoint persistence"
```

Expected: commit succeeds with only Task 3 files.

---

### Task 4: Documentation and Roadmap Correction

**Files:**
- Create: `docs/dataagent/dataagent-v2.13-postgres-checkpoint-hardening.md`
- Modify: `docs/superpowers/plans/2026-07-03-alife-v2.10-capability-agent-orchestration.md`

- [ ] **Step 1: Create V2.13 runtime boundary document**

Create `docs/dataagent/dataagent-v2.13-postgres-checkpoint-hardening.md`:

```markdown
# DataAgent V2.13 PostgreSQL Checkpoint Hardening

## Purpose

V2.13 productizes optional PostgreSQL persistence for DataAgent analysis checkpoints. The goal is to let long multi-turn DataAgent analysis sessions recover across process restarts without changing QueryPlan authority, SQL safety, Tool Broker routing, QChat ownership, or the V2.12 scenario-context prompt boundary.

## What PostgreSQL Persists

- `DataAgentAnalysisSession`
- `DataAgentAnalysisTurn`
- Session status
- Last dataset
- Last summary
- Pending clarification question
- Turn history used to derive `DataAgentOrchestrationCheckpoint`

The persisted state is the recovery source for `checkpoint_session_id`, `checkpoint_status`, `checkpoint_turn_count`, `checkpoint_can_continue`, `checkpoint_can_summarize`, and `checkpoint_terminal`.

## What PostgreSQL Does Not Change

- SQL is still generated only through QueryPlan compilation.
- SQL safety validation remains deterministic C# code.
- Query execution remains read-only and parameterized.
- Scenario context remains a hint only.
- Tool Broker route state remains required for DataAgent tools.
- QChat does not load or own DataAgent checkpoint providers.
- Progress diagnostics remain a runtime/owner observation stream, not the checkpoint authority.

## Runtime Configuration

Default runtime remains in-memory:

```text
ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER=
```

Enable PostgreSQL checkpoint persistence:

```text
ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER=postgres
ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION=<connection string>
```

If `ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION` is not set, the factory falls back to:

```text
ALIFE_DATAAGENT_POSTGRES_CONNECTION=<connection string>
```

## Recovery Flow

1. `DataAgentModuleService.AwakeAsync(...)` creates the query/audit store through `DataAgentStoreFactory`.
2. It creates the analysis checkpoint/session store through `DataAgentAnalysisSessionStoreFactory`.
3. `DataAgentAnalysisService` and `DataAgentAnalysisOrchestrator` use the configured `IDataAgentAnalysisSessionStore`.
4. Start/continue/summarize/end operations update the persisted session.
5. `DataAgentOrchestrationContextProvider` emits checkpoint fields from the recovered session state.

## Non-Goals

- No LangGraph runtime.
- No StateGraph.
- No Python sidecar.
- No ORM or migration framework.
- No progress-stream persistence.
- No QChat main-loop refactor.
- No natural-language command auto-execution.
```

- [ ] **Step 2: Correct the V2.10 later-work roadmap**

In `docs/superpowers/plans/2026-07-03-alife-v2.10-capability-agent-orchestration.md`, replace the stale `Later Work` paragraph in the embedded design spec:

```markdown
V2.11 may wire DataAgent scenario-pack context into planner prompts. V2.12 may productize PostgreSQL. V2.13 may add the disabled-by-default LangGraph sidecar contract. V2.14 may pilot a DataQueryGraph that calls C# safety services instead of bypassing them.
```

with:

```markdown
V2.11 wires DataAgent scenario-pack context into planner prompts and owner diagnostics. V2.12 activates that scenario context in the real DataAgent runtime path. V2.13 productizes optional PostgreSQL checkpoint/session persistence for recoverable DataAgent analysis chains. V2.14 may add the disabled-by-default LangGraph sidecar contract. V2.15 may pilot a DataQueryGraph that calls C# safety services instead of bypassing them.
```

Replace the `V2.11 Handoff` block near the end:

```text
V2.11 DataAgent Scenario Context Integration
```

with:

```text
V2.13 DataAgent PostgreSQL Checkpoint Hardening
```

Replace the final V2.11 handoff sentence with:

```markdown
After V2.12, the next safe implementation step is V2.13: persist DataAgent analysis sessions and checkpoint state behind the existing `IDataAgentAnalysisSessionStore` boundary. This should remain optional, environment-gated, and DataAgent-owned; it must not introduce LangGraph runtime behavior or move SQL authority into PostgreSQL.
```

- [ ] **Step 3: Commit Task 4**

Run:

```powershell
git add -f docs/dataagent/dataagent-v2.13-postgres-checkpoint-hardening.md
git add -f docs/superpowers/plans/2026-07-03-alife-v2.10-capability-agent-orchestration.md
git commit -m "Document DataAgent V2.13 checkpoint persistence"
```

Expected: commit succeeds.

---

### Task 5: Final Verification and Scope Audit

**Files:**
- Verify all changed V2.13 files.
- Commit only final correction files if verification exposes small issues.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentPostgresAnalysisSessionStoreTests|FullyQualifiedName~DataAgentAnalysisSessionStoreFactoryTests|FullyQualifiedName~DataAgentAnalysisSessionStoreTests|FullyQualifiedName~DataAgentAnalysisServiceTests|FullyQualifiedName~DataAgentAnalysisOrchestratorTests|FullyQualifiedName~DataAgentModuleServiceTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests" -v:minimal -m:1
```

Expected:

```text
0 failed
```

Live PostgreSQL tests may be skipped/ignored when `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is not set.

- [ ] **Step 2: Run focused QChat boundary tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected:

```text
0 failed
```

- [ ] **Step 3: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent readiness: Summary: 82 required passed, 0 required missing
QChat engineering map: Summary: 57 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Verify forbidden runtime shapes were not introduced**

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.DataAgent\*.cs -Pattern "LangGraph|Python sidecar|StateGraph"
Select-String -Path sources\Alife.Function\Alife.Function.QChat\*.cs -Pattern "PostgresDataAgentAnalysisSessionStore|DataAgentAnalysisSessionStoreFactory|DataAgentScenarioKnowledgePackProvider|DataAgentScenarioContextBuilder|DataAgentToolScopePolicy"
```

Expected: no matches.

- [ ] **Step 5: Verify formatting and repository state**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: no whitespace errors. The recurring warning about `C:\Users\hu shu/.config/git/ignore` can appear and does not block V2.13.

- [ ] **Step 6: Run solution verification**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal -m:1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected:

```text
restore succeeds
build succeeds with 0 errors
test succeeds with 0 failed
```

Existing environment-gated tests may remain skipped.

- [ ] **Step 7: Commit final verification corrections if any were made**

Run only if verification required small corrections:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent
git add Tests/Alife.Test.DataAgent
git add Tests/Alife.Test.QChat
git add tools/check-dataagent-readiness.ps1
git add tools/check-qchat-engineering-map.ps1
git add -f docs/dataagent/dataagent-v2.13-postgres-checkpoint-hardening.md
git add -f docs/superpowers/plans/2026-07-03-alife-v2.10-capability-agent-orchestration.md
git commit -m "Finalize DataAgent V2.13 checkpoint persistence"
```

Expected: commit succeeds when corrections exist. Skip this commit when no files changed after Task 4.

---

## Acceptance Criteria

V2.13 is complete only when all of these are true:

- `PostgresDataAgentAnalysisSessionStore` implements `IDataAgentAnalysisSessionStore`.
- PostgreSQL has `dataagent_analysis_session` and `dataagent_analysis_turn` tables.
- PostgreSQL session updates use transactions and row locking.
- PostgreSQL session storage preserves create/get/save/update/end semantics.
- Ended sessions cannot be reopened by stale active snapshots.
- `DataAgentAnalysisSessionStoreFactory` defaults to in-memory storage.
- PostgreSQL checkpoint persistence is enabled only when explicitly configured.
- Dedicated checkpoint connection takes precedence over shared DataAgent PostgreSQL connection.
- `DataAgentModuleService` uses the configured analysis session store boundary.
- QChat does not reference DataAgent checkpoint persistence implementation types.
- DataAgent readiness reports `82 required passed, 0 required missing`.
- QChat engineering map reports `57 required passed, 0 required missing, 0 optional present, 0 optional missing`.
- No LangGraph, StateGraph, Python sidecar, new SQL execution path, QChat main-loop refactor, or natural-language QChat command auto-execution is added.
- Focused tests, readiness scripts, full solution verification, and `git diff --check` pass.

## Interview Framing

This version can be described as:

> V2.13 turns the existing DataAgent checkpoint concept into a recoverable runtime capability without over-agentizing the system. PostgreSQL is not made the new authority for SQL or tool execution; it only persists the analysis session and turns that already drive checkpoint state. The query pipeline still goes through QueryPlan validation, parameterized SQL compilation, SQL safety validation, read-only execution, audit, evidence, progress, and trace. QChat remains the interaction surface, while DataAgent owns checkpoint persistence behind a narrow store interface.

## V2.14 Handoff

After V2.13 is merged and verified, the next safe step is:

```text
V2.14 Disabled-by-default LangGraph Sidecar Contract
```

V2.14 should define an adapter contract and disabled-by-default process boundary only. It should not pilot DataQueryGraph behavior until V2.15, and it must prove that any future graph node calls existing C# safety services instead of bypassing DataAgent validation or SQL safety.
