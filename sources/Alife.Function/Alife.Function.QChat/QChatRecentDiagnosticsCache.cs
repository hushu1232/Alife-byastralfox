using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public enum QChatRecentDiagnosticKind
{
    SemanticState,
    DataAgentEvidence,
    DataAgentTrace,
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
        if (QChatDiagnosticTextSanitizer.ContainsUnsafeDiagnosticText(text))
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
            QChatDiagnosticTextSanitizer.NormalizeDiagnosticText(text, MaxTextChars),
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

    static string NormalizeToken(string value)
    {
        return value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
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
