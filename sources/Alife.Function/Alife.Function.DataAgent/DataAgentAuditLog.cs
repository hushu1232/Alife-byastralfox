using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed class DataAgentAuditLog(string databasePath)
{
    public void RecordAccepted(
        string question,
        string dataset,
        string queryPlanJson,
        string generatedSql,
        int rowCount,
        TimeSpan elapsed)
    {
        Insert(question, dataset, queryPlanJson, generatedSql, true, string.Empty, rowCount, elapsed);
    }

    public void RecordRejected(
        string question,
        string dataset,
        string queryPlanJson,
        string generatedSql,
        string rejectedReason,
        TimeSpan elapsed)
    {
        Insert(question, dataset, queryPlanJson, generatedSql, false, rejectedReason, 0, elapsed);
    }

    public IReadOnlyList<DataAgentAuditRecord> ReadAll()
    {
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT question, dataset, query_plan_json, generated_sql, validated, rejected_reason, row_count, elapsed_ms, created_at
            FROM query_audit
            ORDER BY id ASC
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        List<DataAgentAuditRecord> records = [];

        while (reader.Read())
        {
            records.Add(new DataAgentAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4) != 0,
                reader.GetString(5),
                checked((int)reader.GetInt64(6)),
                TimeSpan.FromMilliseconds(reader.GetInt64(7)),
                DateTimeOffset.Parse(reader.GetString(8))));
        }

        return records;
    }

    void Insert(
        string question,
        string dataset,
        string queryPlanJson,
        string generatedSql,
        bool validated,
        string rejectedReason,
        int rowCount,
        TimeSpan elapsed)
    {
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO query_audit (question, dataset, query_plan_json, generated_sql, validated, rejected_reason, row_count, elapsed_ms, created_at)
            VALUES ($question, $dataset, $queryPlanJson, $generatedSql, $validated, $rejectedReason, $rowCount, $elapsedMs, $createdAt)
            """;
        command.Parameters.AddWithValue("$question", question);
        command.Parameters.AddWithValue("$dataset", dataset);
        command.Parameters.AddWithValue("$queryPlanJson", queryPlanJson);
        command.Parameters.AddWithValue("$generatedSql", generatedSql);
        command.Parameters.AddWithValue("$validated", validated ? 1 : 0);
        command.Parameters.AddWithValue("$rejectedReason", rejectedReason);
        command.Parameters.AddWithValue("$rowCount", rowCount);
        command.Parameters.AddWithValue("$elapsedMs", checked((long)elapsed.TotalMilliseconds));
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}

public sealed record DataAgentAuditRecord(
    string Question,
    string Dataset,
    string QueryPlanJson,
    string GeneratedSql,
    bool Validated,
    string RejectedReason,
    int RowCount,
    TimeSpan Elapsed,
    DateTimeOffset CreatedAt);
