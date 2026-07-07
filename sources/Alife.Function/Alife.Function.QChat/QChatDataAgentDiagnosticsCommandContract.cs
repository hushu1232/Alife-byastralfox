using System;
using System.Collections.Generic;

namespace Alife.Function.QChat;

public enum QChatDataAgentDiagnosticsTopic
{
    Evidence,
    Trace,
    Progress,
    Graph
}

public static class QChatDataAgentDiagnosticsCommandContract
{
    const string DataAgentPrefix = "/dataagent";

    public static IReadOnlyList<string> SupportedDataAgentCommandSuffixes { get; } =
    [
        "diag evidence",
        "diagnostics evidence",
        "diag trace",
        "diagnostics trace",
        "diag progress",
        "diagnostics progress",
        "diag graph",
        "diagnostics graph"
    ];

    public static bool TryParseDataAgentCommand(
        string? command,
        out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = default;

        string trimmed = command?.TrimStart() ?? string.Empty;
        if (trimmed.StartsWith(DataAgentPrefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        if (trimmed.Length <= DataAgentPrefix.Length ||
            char.IsWhiteSpace(trimmed[DataAgentPrefix.Length]) == false)
        {
            return false;
        }

        string suffix = trimmed[DataAgentPrefix.Length..].Trim();
        return TryParseDataAgentCommandSuffix(suffix, out topic);
    }

    public static bool TryParseDataAgentCommandSuffix(
        string? command,
        out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = default;

        string[] tokens = SplitCommandTokens(StripCopiedMenuDescription(command));
        if (tokens.Length != 2)
            return false;

        return IsDiagnosticsVerb(tokens[0]) &&
               TryParseTopic(tokens[1], out topic);
    }

    public static bool TryParseQChatDataAgentDiagnosticsCommandSuffix(
        string? command,
        out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = default;

        string[] tokens = SplitCommandTokens(StripCopiedMenuDescription(command));
        if (tokens.Length != 3)
            return false;

        return IsDiagnosticsVerb(tokens[0]) &&
               tokens[1].Equals("dataagent", StringComparison.OrdinalIgnoreCase) &&
               TryParseTopic(tokens[2], out topic);
    }

    static string StripCopiedMenuDescription(string? command)
    {
        string trimmed = command?.Trim() ?? string.Empty;
        int descriptionStart = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        return descriptionStart >= 0 ? trimmed[..descriptionStart].TrimEnd() : trimmed;
    }

    static string[] SplitCommandTokens(string command)
    {
        return command.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);
    }

    static bool IsDiagnosticsVerb(string value)
    {
        return value.Equals("diag", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("diagnostics", StringComparison.OrdinalIgnoreCase);
    }

    static bool TryParseTopic(string value, out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = value.ToLowerInvariant() switch
        {
            "evidence" => QChatDataAgentDiagnosticsTopic.Evidence,
            "trace" => QChatDataAgentDiagnosticsTopic.Trace,
            "progress" => QChatDataAgentDiagnosticsTopic.Progress,
            "graph" => QChatDataAgentDiagnosticsTopic.Graph,
            _ => default
        };

        return value.Equals("evidence", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("trace", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("progress", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("graph", StringComparison.OrdinalIgnoreCase);
    }
}
