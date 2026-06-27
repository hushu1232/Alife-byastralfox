using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public static class DataAgentFixtureImporter
{
    static readonly string Timestamp = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero).ToString("O");

    public static void Import(string databasePath)
    {
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);

        Execute(connection, "DELETE FROM engineering_gate");
        Execute(connection, "DELETE FROM runtime_readiness_check");
        Execute(connection, "DELETE FROM module_capability");
        Execute(connection, "DELETE FROM test_run");
        Execute(connection, "DELETE FROM document_index");

        Execute(
            connection,
            """
            INSERT INTO engineering_gate (name, category, required, status, evidence_path, last_checked_at, source)
            VALUES ('Runtime readiness script', 'Harness', 1, 'passed', 'tools/check-qchat-runtime-readiness.ps1', $timestamp, 'fixture')
            """,
            new SqliteParameter("$timestamp", Timestamp));

        Execute(
            connection,
            """
            INSERT INTO engineering_gate (name, category, required, status, evidence_path, last_checked_at, source)
            VALUES ('DataAgent readiness script', 'Harness', 0, 'missing', 'tools/check-dataagent-readiness.ps1', $timestamp, 'fixture')
            """,
            new SqliteParameter("$timestamp", Timestamp));

        Execute(
            connection,
            """
            INSERT INTO runtime_readiness_check (capability, account, endpoint, status, required, failure_reason, last_checked_at, evidence_path)
            VALUES ('MixuTts9881Reachable', 'mixu', '127.0.0.1:9881', 'missing', 1, 'mixu_tts_endpoint_unreachable', $timestamp, 'tools/check-qchat-runtime-readiness.ps1')
            """,
            new SqliteParameter("$timestamp", Timestamp));

        Execute(
            connection,
            """
            INSERT INTO test_run (suite_name, passed, failed, skipped, total, ran_at, command)
            VALUES ('Alife.Test.QChat', 1168, 0, 10, 1178, $timestamp, 'dotnet test Alife.slnx --no-restore --no-build -v:minimal')
            """,
            new SqliteParameter("$timestamp", Timestamp));

        Execute(
            connection,
            """
            INSERT INTO document_index (path, doc_type, title, summary, tags, updated_at)
            VALUES ('docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md', 'spec', 'DataAgent NL2SQL Design', 'DataAgent QueryPlan-first NL2SQL design.', 'dataagent,nl2sql,chatbi', $timestamp)
            """,
            new SqliteParameter("$timestamp", Timestamp));
    }

    static void Execute(SqliteConnection connection, string sql, params SqliteParameter[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        command.ExecuteNonQuery();
    }
}
