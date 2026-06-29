using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public static class DataAgentSchemaInitializer
{
    public static void Initialize(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string? directory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(directory) == false)
            Directory.CreateDirectory(directory);

        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS engineering_gate (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                category TEXT NOT NULL,
                required INTEGER NOT NULL,
                status TEXT NOT NULL,
                evidence_path TEXT NOT NULL,
                last_checked_at TEXT NOT NULL,
                source TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS runtime_readiness_check (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                capability TEXT NOT NULL,
                account TEXT NOT NULL,
                endpoint TEXT NOT NULL,
                status TEXT NOT NULL,
                required INTEGER NOT NULL,
                failure_reason TEXT NOT NULL,
                last_checked_at TEXT NOT NULL,
                evidence_path TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS module_capability (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                module_name TEXT NOT NULL,
                capability_name TEXT NOT NULL,
                required INTEGER NOT NULL,
                status TEXT NOT NULL,
                test_project TEXT NOT NULL,
                evidence_path TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS test_run (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                suite_name TEXT NOT NULL,
                passed INTEGER NOT NULL,
                failed INTEGER NOT NULL,
                skipped INTEGER NOT NULL,
                total INTEGER NOT NULL,
                ran_at TEXT NOT NULL,
                command TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS document_index (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL,
                doc_type TEXT NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                tags TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS query_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                question TEXT NOT NULL,
                dataset TEXT NOT NULL,
                query_plan_json TEXT NOT NULL,
                generated_sql TEXT NOT NULL,
                validated INTEGER NOT NULL,
                rejected_reason TEXT NOT NULL,
                row_count INTEGER NOT NULL,
                elapsed_ms INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tool_broker_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                tool_name TEXT NOT NULL,
                allowed INTEGER NOT NULL,
                reason_code TEXT NOT NULL,
                reason TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
