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
        @"(?:client|access)[\s_-]*(?:secret|token)|(?<![A-Za-z])(?:secret|secrets|token|credential)(?![A-Za-z])|authorization\s*:\s*(?:basic|bearer)\b|\bbasic\s+\S+|-----BEGIN [A-Z0-9 ]+-----|-----END [A-Z0-9 ]+-----",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex SafeTokenPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex AbsolutePathPattern = new(
        @"[A-Za-z]:[\\/]|(?:^|(?<=[^A-Za-z0-9_:/.-]))/[^\s]*|\\",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public DataAgentLangGraphShadowArtifactWriteResult Write(
        DataAgentLangGraphShadowArtifact artifact,
        DateTimeOffset now)
    {
        PurgeExpired(now);
        ArgumentNullException.ThrowIfNull(artifact);

        if (Enum.IsDefined(typeof(DataAgentLangGraphShadowArtifactOutcome), artifact.Outcome) == false)
            return new(false, "invalid_artifact_outcome");

        string summary = NormalizeSummary(artifact.Summary);
        if (HasUnsafeOrOutOfBoundsMetadata(artifact, summary))
            return new(false, "unsafe_artifact_metadata");

        DateTimeOffset expiresAt;
        try
        {
            expiresAt = artifact.CreatedAt.AddDays(RetentionDays);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new(false, "invalid_artifact_timestamp");
        }

        if (artifact.CreatedAt > now)
            return new(false, "future_artifact");

        if (expiresAt <= now)
            return new(false, "expired_artifact");

        try
        {
            using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
            using SqliteTransaction transaction = connection.BeginTransaction();
            string nowText = ToStorageText(now);

            ExecuteNonQuery(connection, transaction, "DELETE FROM langgraph_shadow_artifact WHERE julianday(expires_at) <= julianday($now)", command =>
                command.Parameters.AddWithValue("$now", nowText));

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
                command.Parameters.AddWithValue("$summary", summary);
                command.Parameters.AddWithValue("$contextChars", artifact.ContextChars);
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
            ExecuteNonQuery(connection, transaction, "DELETE FROM langgraph_shadow_artifact WHERE julianday(expires_at) <= julianday($now)", command =>
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

    static bool HasUnsafeOrOutOfBoundsMetadata(
        DataAgentLangGraphShadowArtifact artifact,
        string normalizedSummary)
    {
        return IsSafeToken(artifact.ArtifactId) == false ||
               IsSafeToken(artifact.SessionId) == false ||
               IsSafeToken(artifact.ReplayId) == false ||
               IsSafeToken(artifact.ReasonCode) == false ||
               IsSafeSummary(normalizedSummary) == false ||
               artifact.ContextChars < 0 ||
               artifact.ContextChars > DataAgentGraphHandshakeLimits.MaxContextContributionChars;
    }

    static bool IsSafeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               SafeTokenPattern.IsMatch(value) &&
               ContainsUnsafe(value) == false;
    }

    static bool IsSafeSummary(string value)
    {
        return value.Length <= DataAgentV42OperatorEvidencePacketBuilder.MaxSummaryChars &&
               ContainsUnsafe(value) == false;
    }

    static string NormalizeSummary(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    static bool ContainsUnsafe(string value) =>
        DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) ||
        AdditionalUnsafeMarkerPattern.IsMatch(value) ||
        AbsolutePathPattern.IsMatch(value) ||
        value.Any(char.IsControl);

    static string ToStorageText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    void PurgeExpired(DateTimeOffset now)
    {
        try
        {
            using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM langgraph_shadow_artifact WHERE julianday(expires_at) <= julianday($now)";
            command.Parameters.AddWithValue("$now", ToStorageText(now));
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
        }
    }

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
