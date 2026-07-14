using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public enum DataAgentLangGraphShadowArtifactOutcome
{
    Accepted,
    GateRejected,
    ProtocolRejected,
    Timeout,
    Fallback
}

public sealed record DataAgentLangGraphShadowArtifact(
    string ArtifactId,
    string SessionId,
    string ReplayId,
    DataAgentLangGraphShadowArtifactOutcome Outcome,
    string ReasonCode,
    string Summary,
    int ContextChars,
    bool DiffGatePassed,
    bool FallbackRequired,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record DataAgentLangGraphShadowArtifactAggregate(
    int Total,
    int Accepted,
    int GateRejected,
    int ProtocolRejected,
    int Timeout,
    int Fallback,
    string LatestReasonCode,
    DateTimeOffset? OldestCreatedAt,
    DateTimeOffset? NewestCreatedAt,
    int RetentionDays,
    int PerScopeLimit);

public sealed record DataAgentLangGraphShadowArtifactWriteResult(bool Written, string ReasonCode);

public sealed class DataAgentLangGraphShadowArtifactStore(string databasePath)
{
    public const int RetentionDays = 90;
    public const int PerScopeLimit = 20;

    static readonly Regex AdditionalUnsafeMarkerPattern = new(
        @"\b(?:secret|token|credential)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public DataAgentLangGraphShadowArtifactWriteResult Write(
        DataAgentLangGraphShadowArtifact artifact,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (HasUnsafeOrMissingMetadata(artifact))
            return new(false, "unsafe_artifact_metadata");

        try
        {
            using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
            using SqliteTransaction transaction = connection.BeginTransaction();
            string nowText = ToStorageText(now);

            ExecuteNonQuery(connection, transaction, "DELETE FROM langgraph_shadow_artifact WHERE expires_at <= $now", command =>
                command.Parameters.AddWithValue("$now", nowText));

            DateTimeOffset expiresAt = CapExpiry(artifact.ExpiresAt, artifact.CreatedAt);
            ExecuteNonQuery(connection, transaction, """
                INSERT INTO langgraph_shadow_artifact (
                    artifact_id, session_id, replay_id, outcome, reason_code, summary, context_chars,
                    diff_gate_passed, fallback_required, created_at, expires_at)
                VALUES (
                    $artifactId, $sessionId, $replayId, $outcome, $reasonCode, $summary, $contextChars,
                    $diffGatePassed, $fallbackRequired, $createdAt, $expiresAt)
                """, command =>
            {
                command.Parameters.AddWithValue("$artifactId", artifact.ArtifactId);
                command.Parameters.AddWithValue("$sessionId", artifact.SessionId);
                command.Parameters.AddWithValue("$replayId", artifact.ReplayId);
                command.Parameters.AddWithValue("$outcome", artifact.Outcome.ToString());
                command.Parameters.AddWithValue("$reasonCode", artifact.ReasonCode);
                command.Parameters.AddWithValue("$summary", artifact.Summary);
                command.Parameters.AddWithValue("$contextChars", Math.Max(0, artifact.ContextChars));
                command.Parameters.AddWithValue("$diffGatePassed", artifact.DiffGatePassed ? 1 : 0);
                command.Parameters.AddWithValue("$fallbackRequired", artifact.FallbackRequired ? 1 : 0);
                command.Parameters.AddWithValue("$createdAt", ToStorageText(artifact.CreatedAt));
                command.Parameters.AddWithValue("$expiresAt", ToStorageText(expiresAt));
            });

            ExecuteNonQuery(connection, transaction, """
                DELETE FROM langgraph_shadow_artifact
                WHERE rowid IN (
                    SELECT rowid
                    FROM langgraph_shadow_artifact
                    WHERE session_id = $sessionId AND replay_id = $replayId
                    ORDER BY created_at DESC, artifact_id DESC
                    LIMIT -1 OFFSET $limit)
                """, command =>
            {
                command.Parameters.AddWithValue("$sessionId", artifact.SessionId);
                command.Parameters.AddWithValue("$replayId", artifact.ReplayId);
                command.Parameters.AddWithValue("$limit", PerScopeLimit);
            });

            transaction.Commit();
            return new(true, "stored");
        }
        catch (SqliteException)
        {
            return new(false, "artifact_write_failed");
        }
    }

    public DataAgentLangGraphShadowArtifactAggregate ReadAggregate(DateTimeOffset now)
    {
        try
        {
            using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
            using SqliteTransaction transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, "DELETE FROM langgraph_shadow_artifact WHERE expires_at <= $now", command =>
                command.Parameters.AddWithValue("$now", ToStorageText(now)));

            using SqliteCommand aggregateCommand = connection.CreateCommand();
            aggregateCommand.Transaction = transaction;
            aggregateCommand.CommandText = """
                SELECT
                    COUNT(*),
                    COALESCE(SUM(CASE WHEN outcome = 'Accepted' THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN outcome = 'GateRejected' THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN outcome = 'ProtocolRejected' THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN outcome = 'Timeout' THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN outcome = 'Fallback' THEN 1 ELSE 0 END), 0),
                    MIN(created_at),
                    MAX(created_at)
                FROM langgraph_shadow_artifact
                """;
            using SqliteDataReader reader = aggregateCommand.ExecuteReader();
            reader.Read();
            int total = reader.GetInt32(0);
            int accepted = reader.GetInt32(1);
            int gateRejected = reader.GetInt32(2);
            int protocolRejected = reader.GetInt32(3);
            int timeout = reader.GetInt32(4);
            int fallback = reader.GetInt32(5);
            DateTimeOffset? oldest = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6));
            DateTimeOffset? newest = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7));
            reader.Close();

            using SqliteCommand latestReasonCommand = connection.CreateCommand();
            latestReasonCommand.Transaction = transaction;
            latestReasonCommand.CommandText = """
                SELECT reason_code
                FROM langgraph_shadow_artifact
                ORDER BY created_at DESC, artifact_id DESC
                LIMIT 1
                """;
            string latestReasonCode = Convert.ToString(latestReasonCommand.ExecuteScalar()) ?? string.Empty;
            transaction.Commit();

            return new(
                total,
                accepted,
                gateRejected,
                protocolRejected,
                timeout,
                fallback,
                latestReasonCode,
                oldest,
                newest,
                RetentionDays,
                PerScopeLimit);
        }
        catch (SqliteException)
        {
            return new(0, 0, 0, 0, 0, 0, string.Empty, null, null, RetentionDays, PerScopeLimit);
        }
    }

    static bool HasUnsafeOrMissingMetadata(DataAgentLangGraphShadowArtifact artifact)
    {
        return IsUnsafeOrMissing(artifact.ArtifactId) ||
               IsUnsafeOrMissing(artifact.SessionId) ||
               IsUnsafeOrMissing(artifact.ReplayId) ||
               IsUnsafeOrMissing(artifact.ReasonCode) ||
               IsUnsafeOrMissing(artifact.Summary);
    }

    static bool IsUnsafeOrMissing(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) ||
               AdditionalUnsafeMarkerPattern.IsMatch(value);
    }

    static DateTimeOffset CapExpiry(DateTimeOffset expiresAt, DateTimeOffset createdAt)
    {
        DateTimeOffset policyExpiry = createdAt.AddDays(RetentionDays);
        return expiresAt <= policyExpiry ? expiresAt : policyExpiry;
    }

    static string ToStorageText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        Action<SqliteCommand> configure)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        configure(command);
        command.ExecuteNonQuery();
    }
}
