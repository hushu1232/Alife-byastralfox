using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed record DataAgentToolBrokerAuditRecord(
    string SessionId,
    string ToolName,
    bool Allowed,
    string ReasonCode,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed class DataAgentToolBrokerAuditLog(string databasePath)
{
    public void Record(DataAgentToolBrokerAuditRecord record)
    {
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tool_broker_audit (session_id, tool_name, allowed, reason_code, reason, created_at)
            VALUES ($sessionId, $toolName, $allowed, $reasonCode, $reason, $createdAt)
            """;
        command.Parameters.AddWithValue("$sessionId", record.SessionId);
        command.Parameters.AddWithValue("$toolName", record.ToolName);
        command.Parameters.AddWithValue("$allowed", record.Allowed ? 1 : 0);
        command.Parameters.AddWithValue("$reasonCode", record.ReasonCode);
        command.Parameters.AddWithValue("$reason", record.Reason);
        command.Parameters.AddWithValue("$createdAt", record.CreatedAt.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadAll()
    {
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, tool_name, allowed, reason_code, reason, created_at
            FROM tool_broker_audit
            ORDER BY id ASC
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        List<DataAgentToolBrokerAuditRecord> records = [];

        while (reader.Read())
        {
            records.Add(new DataAgentToolBrokerAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return records;
    }
}