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

            CREATE TABLE IF NOT EXISTS qchat_latency_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_id TEXT NOT NULL,
                conversation_kind TEXT NOT NULL,
                outcome TEXT NOT NULL,
                elapsed_ms INTEGER NOT NULL,
                first_content_ms INTEGER NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS qchat_conversation_turn (
                conversation_key TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                speaker TEXT NOT NULL,
                content TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                is_recalled INTEGER NOT NULL DEFAULT 0,
                source_message_key TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (conversation_key, sequence)
            );

            CREATE INDEX IF NOT EXISTS ix_qchat_conversation_turn_lookup
                ON qchat_conversation_turn (conversation_key, sequence);

            CREATE TABLE IF NOT EXISTS qchat_runtime_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_id TEXT NOT NULL,
                event_kind TEXT NOT NULL,
                outcome TEXT NOT NULL,
                summary TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_qchat_runtime_audit_recent
                ON qchat_runtime_audit (agent_id, occurred_at_utc DESC);

            CREATE TABLE IF NOT EXISTS image_asset (
                asset_id TEXT PRIMARY KEY,
                sha256 TEXT NOT NULL UNIQUE,
                perceptual_hash TEXT NOT NULL,
                managed_file_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                media_type TEXT NOT NULL,
                byte_length INTEGER NOT NULL,
                pixel_width INTEGER NOT NULL,
                pixel_height INTEGER NOT NULL,
                visual_summary TEXT NOT NULL,
                ocr_text TEXT NOT NULL,
                first_seen_at_utc TEXT NOT NULL,
                last_seen_at_utc TEXT NOT NULL,
                seen_count INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_image_asset_perceptual_hash
                ON image_asset (perceptual_hash);

            CREATE INDEX IF NOT EXISTS ix_image_asset_last_seen
                ON image_asset (last_seen_at_utc DESC);

            CREATE TABLE IF NOT EXISTS langgraph_shadow_artifact (
                artifact_id TEXT NOT NULL,
                session_id TEXT NOT NULL,
                replay_id TEXT NOT NULL,
                outcome TEXT NOT NULL CHECK (outcome IN ('Accepted', 'GateRejected', 'ProtocolRejected', 'Timeout', 'Fallback')),
                reason_code TEXT NOT NULL,
                summary TEXT NOT NULL,
                context_chars INTEGER NOT NULL,
                diff_gate_passed INTEGER NOT NULL,
                fallback_required INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_langgraph_shadow_artifact_scope_created
                ON langgraph_shadow_artifact (session_id, replay_id, created_at, artifact_id);

            CREATE INDEX IF NOT EXISTS ix_langgraph_shadow_artifact_expires_at
                ON langgraph_shadow_artifact (expires_at);
            """;
        command.ExecuteNonQuery();
        EnsureQChatConversationTurnSourceMessageKeyColumn(connection);
    }

    static void EnsureQChatConversationTurnSourceMessageKeyColumn(SqliteConnection connection)
    {
        bool exists;
        using (SqliteCommand columns = connection.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(qchat_conversation_turn);";
            using SqliteDataReader reader = columns.ExecuteReader();
            exists = false;
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "source_message_key", StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists == false)
        {
            using SqliteCommand migration = connection.CreateCommand();
            migration.CommandText = "ALTER TABLE qchat_conversation_turn ADD COLUMN source_message_key TEXT NOT NULL DEFAULT '';";
            migration.ExecuteNonQuery();
        }

        using SqliteCommand index = connection.CreateCommand();
        index.CommandText = "CREATE INDEX IF NOT EXISTS ix_qchat_conversation_turn_source_message ON qchat_conversation_turn (source_message_key);";
        index.ExecuteNonQuery();
    }
}
