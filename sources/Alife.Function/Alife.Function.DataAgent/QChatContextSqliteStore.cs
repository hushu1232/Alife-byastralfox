using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed class QChatContextSqliteStore
{
    const int MaxStoredTextCharacters = 6000;

    static readonly Regex SafeCodePattern = new("^[A-Za-z0-9_.-]{1,128}$", RegexOptions.CultureInvariant);
    static readonly Regex SafeConversationKeyPattern = new("^[A-Za-z0-9:_-]{1,256}$", RegexOptions.CultureInvariant);
    static readonly Regex SafeSourceMessageKeyPattern = new("^[A-Za-z0-9:_-]{1,128}$", RegexOptions.CultureInvariant);
    static readonly Regex SensitiveValuePattern = new(
        "api[_-]?key|access[_-]?token|client[_-]?secret|token|cookie|password|authorization|bearer|connection\\s*string|private\\s*key|-----BEGIN [^-]+PRIVATE KEY-----|sk-[A-Za-z0-9_-]{8,}|nb_[A-Za-z0-9_-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex UrlPattern = new(@"https?://[^\s\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex MentionPattern = new(@"@\d{5,12}\b", RegexOptions.CultureInvariant);
    static readonly Regex QuotedSpeakerPattern = new(@"(?<=“)\d{5,12}(?=：)", RegexOptions.CultureInvariant);
    static readonly Regex ManagedFilePattern = new(@"\[QQ file:[^\]]+\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    readonly string databasePath;

    public QChatContextSqliteStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = databasePath;
    }

    public void RecordConversationTurn(QChatConversationTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.ConversationKey);
        if (SafeConversationKeyPattern.IsMatch(turn.ConversationKey.Trim()) == false)
            throw new ArgumentException("Conversation key is invalid.", nameof(turn));
        if (turn.Sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(turn));
        if (Enum.IsDefined(typeof(QChatConversationSpeaker), turn.Speaker) == false)
            throw new ArgumentOutOfRangeException(nameof(turn));
        if (turn.IsRecalled == false)
            ArgumentException.ThrowIfNullOrWhiteSpace(turn.Content);
        if (string.IsNullOrWhiteSpace(turn.SourceMessageKey) == false &&
            SafeSourceMessageKeyPattern.IsMatch(turn.SourceMessageKey.Trim()) == false)
        {
            throw new ArgumentException("Source message key is invalid.", nameof(turn));
        }

        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO qchat_conversation_turn (
                conversation_key, sequence, speaker, content, occurred_at_utc, is_recalled, source_message_key)
            VALUES ($conversation_key, $sequence, $speaker, $content, $occurred_at_utc, $is_recalled, $source_message_key)
            ON CONFLICT(conversation_key, sequence) DO UPDATE SET
                speaker = excluded.speaker,
                content = excluded.content,
                occurred_at_utc = excluded.occurred_at_utc,
                is_recalled = excluded.is_recalled,
                source_message_key = excluded.source_message_key
            WHERE qchat_conversation_turn.is_recalled = 0;
            """;
        command.Parameters.AddWithValue("$conversation_key", turn.ConversationKey.Trim());
        command.Parameters.AddWithValue("$sequence", turn.Sequence);
        command.Parameters.AddWithValue("$speaker", turn.Speaker.ToString());
        command.Parameters.AddWithValue("$content", turn.IsRecalled ? string.Empty : SanitizeText(turn.Content));
        command.Parameters.AddWithValue("$occurred_at_utc", FormatTimestamp(turn.OccurredAt));
        command.Parameters.AddWithValue("$is_recalled", turn.IsRecalled ? 1 : 0);
        command.Parameters.AddWithValue("$source_message_key", turn.SourceMessageKey?.Trim() ?? string.Empty);
        command.ExecuteNonQuery();
    }

    public int MarkConversationTurnsRecalled(string sourceMessageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageKey);
        if (SafeSourceMessageKeyPattern.IsMatch(sourceMessageKey.Trim()) == false)
            throw new ArgumentException("Source message key is invalid.", nameof(sourceMessageKey));

        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE qchat_conversation_turn
            SET content = '', is_recalled = 1
            WHERE source_message_key = $source_message_key AND is_recalled = 0;
            """;
        command.Parameters.AddWithValue("$source_message_key", sourceMessageKey.Trim());
        return command.ExecuteNonQuery();
    }

    public QChatTopicReplayResult SearchTopicReplay(QChatTopicReplayQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.ConversationKey);
        if (SafeConversationKeyPattern.IsMatch(query.ConversationKey.Trim()) == false)
            throw new ArgumentException("Conversation key is invalid.", nameof(query));
        ArgumentNullException.ThrowIfNull(query.ExcludedSequences);

        int maxTurns = Math.Clamp(query.MaxTurns, 1, 12);
        int maxCharacters = Math.Clamp(query.MaxCharacters, 1, 3000);
        string[] terms = ExtractTerms(query.QueryText);
        if (terms.Length == 0)
            return new QChatTopicReplayResult([], false);

        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        // ponytail: linear per-conversation scan; add FTS only when archive size makes replay latency measurable.
        List<QChatConversationTurn> allTurns = ReadVisibleTurns(connection, query.ConversationKey.Trim());
        List<int> matchingIndexes = allTurns
            .Select((turn, index) => new { turn, index })
            .Where(item => query.ExcludedSequences.Contains(item.turn.Sequence) == false)
            .Where(item => terms.Any(term => item.turn.Content.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.index)
            .ToList();
        if (matchingIndexes.Count == 0)
            return new QChatTopicReplayResult([], false);

        int matchingIndex = matchingIndexes[^1];
        List<QChatConversationTurn> replay = [];
        int characters = 0;
        bool budgetTruncated = false;
        int[] candidateIndexes = [matchingIndex, matchingIndex - 1, matchingIndex + 1];
        foreach (int index in candidateIndexes.Where(index => index >= 0 && index < allTurns.Count).Distinct())
        {
            if (replay.Count >= maxTurns)
                break;
            QChatConversationTurn turn = allTurns[index];
            if (query.ExcludedSequences.Contains(turn.Sequence))
                continue;
            int nextCharacters = characters + turn.Content.Length;
            if (nextCharacters > maxCharacters)
            {
                int remainingCharacters = maxCharacters - characters;
                if (remainingCharacters > 0 && replay.Count == 0)
                    replay.Add(turn with { Content = turn.Content[..remainingCharacters] });
                budgetTruncated = true;
                break;
            }
            replay.Add(turn);
            characters = nextCharacters;
        }

        bool hasOlderMatches = budgetTruncated || matchingIndexes.Any(index => index < matchingIndex - 1);
        return new QChatTopicReplayResult(replay.OrderBy(turn => turn.Sequence).ToList(), hasOlderMatches);
    }

    public void RecordRuntimeAudit(QChatRuntimeAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.EventKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Summary);

        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO qchat_runtime_audit (agent_id, event_kind, outcome, summary, occurred_at_utc)
            VALUES ($agent_id, $event_kind, $outcome, $summary, $occurred_at_utc);
            """;
        command.Parameters.AddWithValue("$agent_id", SanitizeCode(record.AgentId));
        command.Parameters.AddWithValue("$event_kind", SanitizeCode(record.EventKind));
        command.Parameters.AddWithValue("$outcome", SanitizeCode(record.Outcome));
        command.Parameters.AddWithValue("$summary", SanitizeText(record.Summary));
        command.Parameters.AddWithValue("$occurred_at_utc", FormatTimestamp(record.OccurredAt));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<QChatRuntimeAuditRecord> ReadRuntimeAudit(int maxRecords)
    {
        int limit = Math.Clamp(maxRecords, 1, 200);
        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT agent_id, event_kind, outcome, summary, occurred_at_utc
            FROM qchat_runtime_audit
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = command.ExecuteReader();
        List<QChatRuntimeAuditRecord> records = [];
        while (reader.Read())
        {
            records.Add(new QChatRuntimeAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4))));
        }

        return records;
    }

    static List<QChatConversationTurn> ReadVisibleTurns(SqliteConnection connection, string conversationKey)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT conversation_key, sequence, speaker, content, occurred_at_utc, is_recalled, source_message_key
            FROM qchat_conversation_turn
            WHERE conversation_key = $conversation_key AND is_recalled = 0
            ORDER BY sequence ASC;
            """;
        command.Parameters.AddWithValue("$conversation_key", conversationKey);

        using SqliteDataReader reader = command.ExecuteReader();
        List<QChatConversationTurn> turns = [];
        while (reader.Read())
        {
            if (Enum.TryParse(reader.GetString(2), ignoreCase: true, out QChatConversationSpeaker speaker) == false)
                continue;
            turns.Add(new QChatConversationTurn(
                reader.GetString(0),
                reader.GetInt64(1),
                speaker,
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)),
                reader.GetInt64(5) != 0,
                reader.GetString(6)));
        }

        return turns;
    }

    static string[] ExtractTerms(string value)
    {
        List<string> terms = [];
        foreach (string token in (value ?? string.Empty)
                     .Split([' ', '\t', '\r', '\n', '，', '。', '、', '：', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length < 2)
                continue;

            terms.Add(token);
            for (int index = 0; index < token.Length - 1; index++)
            {
                if (IsCjk(token[index]) && IsCjk(token[index + 1]))
                    terms.Add(token.Substring(index, 2));
            }
        }

        return terms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    static bool IsCjk(char value) => value is >= '\u4e00' and <= '\u9fff';

    static string SanitizeText(string value)
    {
        string text = value.Trim();
        if (SensitiveValuePattern.IsMatch(text))
            return "[redacted]";

        text = UrlPattern.Replace(text, "[url-hidden]");
        text = MentionPattern.Replace(text, "@participant");
        text = QuotedSpeakerPattern.Replace(text, "participant");
        text = ManagedFilePattern.Replace(text, "[QQ file received]");
        return text.Length <= MaxStoredTextCharacters ? text : text[..MaxStoredTextCharacters];
    }

    static string SanitizeCode(string value)
    {
        string text = value.Trim();
        return SafeCodePattern.IsMatch(text) ? text : "[redacted]";
    }

    static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
