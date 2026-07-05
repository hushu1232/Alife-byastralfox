using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Alife.Function.QChat;

public static class QChatRecentDiagnosticsFormatter
{
    public static string FormatSummary(
        IReadOnlyList<QChatRecentDiagnosticEntry> entries,
        string sessionKey,
        DateTimeOffset now)
    {
        if (entries.Count == 0)
            return string.Join(Environment.NewLine,
                "QChat recent diagnostics",
                "state=unavailable",
                "reason=recent_diagnostics_empty",
                "session=" + NormalizeToken(sessionKey));

        return string.Join(Environment.NewLine,
            "QChat recent diagnostics",
            FormatKindLine("semantic_state_recent", entries, QChatRecentDiagnosticKind.SemanticState, now),
            FormatKindLine("dataagent_evidence_recent", entries, QChatRecentDiagnosticKind.DataAgentEvidence, now),
            FormatKindLine("dataagent_trace_recent", entries, QChatRecentDiagnosticKind.DataAgentTrace, now),
            FormatKindLine("dataagent_progress_recent", entries, QChatRecentDiagnosticKind.DataAgentProgress, now),
            FormatKindLine("tool_route_recent", entries, QChatRecentDiagnosticKind.ToolRoute, now),
            "session=" + NormalizeToken(sessionKey));
    }

    public static string FormatRedacted(QChatRecentDiagnosticKind kind)
    {
        return string.Join(Environment.NewLine,
            Title(kind),
            "state=redacted",
            "reason=hidden_context_redacted");
    }

    public static string Title(QChatRecentDiagnosticKind kind)
    {
        return kind switch
        {
            QChatRecentDiagnosticKind.SemanticState => "QChat semantic diagnostics",
            QChatRecentDiagnosticKind.DataAgentEvidence => "DataAgent evidence diagnostics",
            QChatRecentDiagnosticKind.DataAgentTrace => "DataAgent trace diagnostics",
            QChatRecentDiagnosticKind.DataAgentProgress => "DataAgent progress diagnostics",
            QChatRecentDiagnosticKind.ToolRoute => "Tool Broker diagnostics",
            _ => "QChat diagnostics"
        };
    }

    static string FormatKindLine(
        string label,
        IReadOnlyList<QChatRecentDiagnosticEntry> entries,
        QChatRecentDiagnosticKind kind,
        DateTimeOffset now)
    {
        QChatRecentDiagnosticEntry? latest = entries
            .Select((entry, index) => new { Entry = entry, Index = index })
            .Where(entry => entry.Entry.Kind == kind)
            .OrderByDescending(entry => entry.Entry.CreatedAt)
            .ThenByDescending(entry => entry.Index)
            .Select(entry => entry.Entry)
            .FirstOrDefault();

        if (latest is null)
            return label + "=missing";

        double ageSeconds = Math.Max(0, (now - latest.CreatedAt).TotalSeconds);
        return string.Join(' ',
            label + "=available",
            "age_seconds=" + ageSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "source=" + NormalizeToken(latest.Source),
            "redacted=" + (latest.Redacted ? "true" : "false"));
    }

    static string NormalizeToken(string value)
    {
        return string.Join(' ', (value ?? string.Empty)
            .ReplaceLineEndings(" ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
