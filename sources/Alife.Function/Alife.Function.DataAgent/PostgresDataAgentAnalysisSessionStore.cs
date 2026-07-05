using System.Data;
using System.Globalization;
using Npgsql;

namespace Alife.Function.DataAgent;

public sealed class PostgresDataAgentAnalysisSessionStore : IDataAgentAnalysisSessionStore
{
    const string SelectSessionSql = """
        SELECT session_id, caller_id, goal, status, created_at, updated_at, last_dataset, last_summary, pending_clarification_question
        FROM dataagent_analysis_session
        WHERE session_id = @session_id
        """;
    const string SelectSessionForUpdateSql = """
        SELECT session_id, caller_id, goal, status, created_at, updated_at, last_dataset, last_summary, pending_clarification_question
        FROM dataagent_analysis_session
        WHERE session_id = @session_id
        FOR UPDATE
        """;

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
                session_id TEXT NOT NULL REFERENCES dataagent_analysis_session(session_id) ON DELETE CASCADE,
                turn_index INTEGER NOT NULL,
                turn_id TEXT NOT NULL,
                question TEXT NOT NULL,
                intent INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                dataset TEXT NOT NULL,
                sql TEXT NOT NULL,
                row_count INTEGER NOT NULL,
                summary TEXT NOT NULL,
                validated BOOLEAN NOT NULL,
                rejected_reason TEXT NOT NULL,
                PRIMARY KEY (session_id, turn_index)
            );

            CREATE INDEX IF NOT EXISTS idx_dataagent_analysis_turn_session_index
            ON dataagent_analysis_turn (session_id, turn_index);
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

        return Save(session);
    }

    public DataAgentAnalysisSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        using NpgsqlConnection connection = Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction(IsolationLevel.RepeatableRead);
        DataAgentAnalysisSession? session = LoadSession(connection, transaction, sessionId, forUpdate: false);
        transaction.Commit();
        return session;
    }

    public DataAgentAnalysisSession Save(DataAgentAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        DataAgentAnalysisSession incoming = Snapshot(session);

        using NpgsqlConnection connection = Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        DataAgentAnalysisSession? current = LoadSession(connection, transaction, incoming.SessionId, forUpdate: true);

        if (current?.Status == DataAgentAnalysisSessionStatus.Ended &&
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
        using NpgsqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        DataAgentAnalysisSession? current = LoadSession(connection, transaction, sessionId, forUpdate: true);
        if (current == null)
        {
            transaction.Commit();
            return null;
        }

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
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        using NpgsqlConnection connection = Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        DataAgentAnalysisSession? current = LoadSession(connection, transaction, sessionId, forUpdate: true);
        if (current == null)
        {
            transaction.Commit();
            return false;
        }

        if (current.Status == DataAgentAnalysisSessionStatus.Ended)
        {
            transaction.Commit();
            return true;
        }

        DataAgentAnalysisSession ended = current with
        {
            Status = DataAgentAnalysisSessionStatus.Ended,
            UpdatedAt = now
        };

        UpsertSession(connection, transaction, ended);
        transaction.Commit();
        return true;
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
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = forUpdate ? SelectSessionForUpdateSql : SelectSessionSql;
        command.Parameters.Add(new NpgsqlParameter("session_id", sessionId));

        using NpgsqlDataReader reader = command.ExecuteReader();
        if (reader.Read() == false)
            return null;

        DataAgentAnalysisSession session = new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            (DataAgentAnalysisSessionStatus)reader.GetInt32(3),
            ParseTimestamp(reader.GetString(4)),
            ParseTimestamp(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            []);
        reader.Close();

        return Snapshot(session with { Turns = LoadTurns(connection, transaction, sessionId) });
    }

    static IReadOnlyList<DataAgentAnalysisTurn> LoadTurns(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sessionId)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT turn_id, turn_index, question, intent, created_at, dataset, sql, row_count, summary, validated, rejected_reason
            FROM dataagent_analysis_turn
            WHERE session_id = @session_id
            ORDER BY turn_index ASC
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
                ParseTimestamp(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.GetBoolean(9),
                reader.GetString(10)));
        }

        return Array.AsReadOnly(turns.ToArray());
    }

    static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
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
                session_id,
                caller_id,
                goal,
                status,
                created_at,
                updated_at,
                last_dataset,
                last_summary,
                pending_clarification_question)
            VALUES (
                @session_id,
                @caller_id,
                @goal,
                @status,
                @created_at,
                @updated_at,
                @last_dataset,
                @last_summary,
                @pending_clarification_question)
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
        command.Parameters.Add(new NpgsqlParameter("created_at", session.CreatedAt.ToString("O")));
        command.Parameters.Add(new NpgsqlParameter("updated_at", session.UpdatedAt.ToString("O")));
        command.Parameters.Add(new NpgsqlParameter("last_dataset", session.LastDataset ?? (object)DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("last_summary", session.LastSummary ?? (object)DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter(
            "pending_clarification_question",
            session.PendingClarificationQuestion ?? (object)DBNull.Value));
        command.ExecuteNonQuery();
    }

    static void ReplaceTurns(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DataAgentAnalysisSession session)
    {
        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM dataagent_analysis_turn WHERE session_id = @session_id";
            command.Parameters.Add(new NpgsqlParameter("session_id", session.SessionId));
            command.ExecuteNonQuery();
        }

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
            """;
        command.Parameters.Add(new NpgsqlParameter("session_id", sessionId));
        command.Parameters.Add(new NpgsqlParameter("turn_index", turn.Index));
        command.Parameters.Add(new NpgsqlParameter("turn_id", turn.TurnId));
        command.Parameters.Add(new NpgsqlParameter("question", turn.Question));
        command.Parameters.Add(new NpgsqlParameter("intent", (int)turn.Intent));
        command.Parameters.Add(new NpgsqlParameter("created_at", turn.CreatedAt.ToString("O")));
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
