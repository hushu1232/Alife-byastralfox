using System;
using System.Collections.Generic;
using System.Linq;

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

    readonly object gate = new();
    readonly int maxEntriesPerSession;
    readonly TimeSpan ttl;
    readonly List<QChatRecentDiagnosticEntry> entries = [];

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
            entries.Add(entry);
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
            PruneExpiredLocked(now);
            return entries
                .Where(entry => entry.Kind == kind && string.Equals(entry.SessionKey, normalizedSessionKey, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.CreatedAt)
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
            PruneExpiredLocked(now);
            return entries
                .Where(entry => string.Equals(entry.SessionKey, normalizedSessionKey, StringComparison.Ordinal))
                .OrderBy(entry => entry.CreatedAt)
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
        entries.RemoveAll(entry => now - entry.CreatedAt > ttl);
    }

    void PruneCapacityLocked(string sessionKey)
    {
        List<QChatRecentDiagnosticEntry> sessionEntries = entries
            .Where(entry => string.Equals(entry.SessionKey, sessionKey, StringComparison.Ordinal))
            .OrderBy(entry => entry.CreatedAt)
            .ToList();

        int excess = sessionEntries.Count - maxEntriesPerSession;
        if (excess <= 0)
            return;

        foreach (QChatRecentDiagnosticEntry entryToRemove in sessionEntries.Take(excess))
        {
            int index = entries.FindIndex(entry => ReferenceEquals(entry, entryToRemove));
            if (index >= 0)
                entries.RemoveAt(index);
        }
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
               text.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("SELECT ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("INSERT ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("DELETE ", StringComparison.OrdinalIgnoreCase);
    }
}
