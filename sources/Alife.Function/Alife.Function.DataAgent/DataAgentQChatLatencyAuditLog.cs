using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed record DataAgentQChatLatencyAuditRecord(
    string AgentId,
    string ConversationKind,
    string Outcome,
    int ElapsedMilliseconds,
    int? FirstContentMilliseconds,
    DateTimeOffset CreatedAt);

public sealed class DataAgentQChatLatencyAuditLog(string databasePath)
{
    public void Record(DataAgentQChatLatencyAuditRecord record)
    {
        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO qchat_latency_audit
                (agent_id, conversation_kind, outcome, elapsed_ms, first_content_ms, created_at)
            VALUES ($agentId, $conversationKind, $outcome, $elapsedMs, $firstContentMs, $createdAt)
            """;
        command.Parameters.AddWithValue("$agentId", record.AgentId);
        command.Parameters.AddWithValue("$conversationKind", record.ConversationKind);
        command.Parameters.AddWithValue("$outcome", record.Outcome);
        command.Parameters.AddWithValue("$elapsedMs", record.ElapsedMilliseconds);
        command.Parameters.AddWithValue("$firstContentMs", record.FirstContentMilliseconds is { } first ? first : DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", record.CreatedAt.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DataAgentQChatLatencyAuditRecord> ReadAll()
    {
        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT agent_id, conversation_kind, outcome, elapsed_ms, first_content_ms, created_at
            FROM qchat_latency_audit
            ORDER BY id ASC
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        List<DataAgentQChatLatencyAuditRecord> records = [];
        while (reader.Read())
        {
            records.Add(new DataAgentQChatLatencyAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return records;
    }
}
