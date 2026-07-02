using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public enum QChatRecentDiagnosticKind
{
    SemanticState,
    DataAgentEvidence,
    ToolRoute
}

public sealed record QChatRecentDiagnosticEntry(
    QChatRecentDiagnosticKind Kind,
    string SessionKey,
    string Source,
    string Text,
    DateTimeOffset CreatedAt,
    bool Redacted,
    string ReasonCode);

public sealed class QChatRecentDiagnosticsCache
{
    const int MaxTextChars = 900;
    const string TruncationEllipsis = "...";

    static readonly Regex ApiKeyPattern = new(@"\bapi[-_]?key\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex AuthorizationBearerPattern = new(@"\bauthorization\s*:\s*bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex ConnectionSecretPattern = new(@"\b(connection_string|host|username|user\s*id|password)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex SqlStatementPattern = new(@"\b(select|insert|update|delete)\b\s+|\bdrop\s+(table|database|schema)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    readonly object gate = new();
    readonly int maxEntriesPerSession;
    readonly TimeSpan ttl;
    readonly List<QChatRecentDiagnosticRecord> entries = [];
    long nextSequence;

    public QChatRecentDiagnosticsCache(int maxEntriesPerSession = 12, TimeSpan? ttl = null)
    {
        this.maxEntriesPerSession = Math.Max(1, maxEntriesPerSession);
        this.ttl = ttl ?? TimeSpan.FromMinutes(30);
    }

    public void Record(
        QChatRecentDiagnosticKind kind,
        string sessionKey,
        string source,
        string? text,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(sessionKey) || string.IsNullOrWhiteSpace(text))
            return;

        string normalizedSessionKey = NormalizeToken(sessionKey);
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "unknown" : NormalizeToken(source);
        QChatRecentDiagnosticEntry entry = CreateEntry(kind, normalizedSessionKey, normalizedSource, text, createdAt);

        lock (gate)
        {
            PruneExpiredLocked(createdAt);
            entries.Add(new QChatRecentDiagnosticRecord(entry, nextSequence++));
            PruneCapacityLocked(normalizedSessionKey);
        }
    }

    public QChatRecentDiagnosticEntry? GetLatest(
        string sessionKey,
        QChatRecentDiagnosticKind kind,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return null;

        string normalizedSessionKey = NormalizeToken(sessionKey);
        lock (gate)
        {
            return entries
                .Where(entry => IsExpired(entry, now) == false &&
                                entry.Entry.Kind == kind &&
                                string.Equals(entry.Entry.SessionKey, normalizedSessionKey, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.Entry.CreatedAt)
                .ThenByDescending(entry => entry.Sequence)
                .Select(entry => entry.Entry)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<QChatRecentDiagnosticEntry> GetRecent(string sessionKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return [];

        string normalizedSessionKey = NormalizeToken(sessionKey);
        lock (gate)
        {
            return entries
                .Where(entry => IsExpired(entry, now) == false &&
                                string.Equals(entry.Entry.SessionKey, normalizedSessionKey, StringComparison.Ordinal))
                .OrderBy(entry => entry.Entry.CreatedAt)
                .ThenBy(entry => entry.Sequence)
                .Select(entry => entry.Entry)
                .ToArray();
        }
    }

    QChatRecentDiagnosticEntry CreateEntry(
        QChatRecentDiagnosticKind kind,
        string sessionKey,
        string source,
        string text,
        DateTimeOffset createdAt)
    {
        if (ContainsUnsafeDiagnosticText(text))
        {
            return new QChatRecentDiagnosticEntry(
                kind,
                sessionKey,
                source,
                QChatRecentDiagnosticsFormatter.FormatRedacted(kind),
                createdAt,
                Redacted: true,
                ReasonCode: "hidden_context_redacted");
        }

        return new QChatRecentDiagnosticEntry(
            kind,
            sessionKey,
            source,
            NormalizeDiagnosticText(text),
            createdAt,
            Redacted: false,
            ReasonCode: "ok");
    }

    void PruneExpiredLocked(DateTimeOffset now)
    {
        entries.RemoveAll(entry => IsExpired(entry, now));
    }

    bool IsExpired(QChatRecentDiagnosticRecord entry, DateTimeOffset now)
    {
        return now - entry.Entry.CreatedAt > ttl;
    }

    void PruneCapacityLocked(string sessionKey)
    {
        List<QChatRecentDiagnosticRecord> sessionEntries = entries
            .Where(entry => string.Equals(entry.Entry.SessionKey, sessionKey, StringComparison.Ordinal))
            .OrderBy(entry => entry.Entry.CreatedAt)
            .ThenBy(entry => entry.Sequence)
            .ToList();

        int excess = sessionEntries.Count - maxEntriesPerSession;
        if (excess <= 0)
            return;

        foreach (QChatRecentDiagnosticRecord entryToRemove in sessionEntries.Take(excess))
            entries.Remove(entryToRemove);
    }

    static string NormalizeDiagnosticText(string text)
    {
        string normalized = text.ReplaceLineEndings(Environment.NewLine).Trim();
        return normalized.Length <= MaxTextChars
            ? normalized
            : normalized[..(MaxTextChars - TruncationEllipsis.Length)] + TruncationEllipsis;
    }

    static string NormalizeToken(string value)
    {
        return value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
    }

    static bool ContainsUnsafeDiagnosticText(string text)
    {
        return text.Contains("[tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Allowed XML tools", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connection_string", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
               ApiKeyPattern.IsMatch(text) ||
               AuthorizationBearerPattern.IsMatch(text) ||
               ConnectionSecretPattern.IsMatch(text) ||
               SqlStatementPattern.IsMatch(text);
    }

    sealed class QChatRecentDiagnosticRecord
    {
        public QChatRecentDiagnosticRecord(QChatRecentDiagnosticEntry entry, long sequence)
        {
            Entry = entry;
            Sequence = sequence;
        }

        public QChatRecentDiagnosticEntry Entry { get; }

        public long Sequence { get; }
    }
}
