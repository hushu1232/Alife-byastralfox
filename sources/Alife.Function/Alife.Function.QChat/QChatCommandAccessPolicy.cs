using System;

namespace Alife.Function.QChat;

public enum QChatCommandAccessAction
{
    NotCommand,
    AllowOwnerCommand,
    DropSilently
}

public sealed record QChatCommandAccessContext(
    string? PlainText,
    QChatSenderRole SenderRole);

public sealed record QChatCommandAccessDecision(
    QChatCommandAccessAction Action,
    string Reason);

public static class QChatCommandAccessPolicy
{
    const string QChatPrefix = "/qchat";
    const string DataAgentPrefix = "/dataagent";

    public static QChatCommandAccessDecision Evaluate(QChatCommandAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsOwnerDiagnosticCommand(context.PlainText) == false)
            return new QChatCommandAccessDecision(
                QChatCommandAccessAction.NotCommand,
                "not_qchat_command");

        if (context.SenderRole == QChatSenderRole.Owner)
            return new QChatCommandAccessDecision(
                QChatCommandAccessAction.AllowOwnerCommand,
                "owner_qchat_command");

        return new QChatCommandAccessDecision(
            QChatCommandAccessAction.DropSilently,
            "non_owner_qchat_command");
    }

    public static bool IsQChatCommand(string? text)
    {
        return IsCommandWithPrefix(text, QChatPrefix);
    }

    public static bool IsOwnerDiagnosticCommand(string? text)
    {
        return IsCommandWithPrefix(text, QChatPrefix) ||
               IsDataAgentDiagnosticCommand(text);
    }

    static bool IsCommandWithPrefix(string? text, string prefix)
    {
        string trimmed = text?.TrimStart() ?? string.Empty;
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        return trimmed.Length == prefix.Length ||
               char.IsWhiteSpace(trimmed[prefix.Length]);
    }

    static bool IsDataAgentDiagnosticCommand(string? text)
    {
        string trimmed = text?.TrimStart() ?? string.Empty;
        if (trimmed.StartsWith(DataAgentPrefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        if (trimmed.Length <= DataAgentPrefix.Length ||
            char.IsWhiteSpace(trimmed[DataAgentPrefix.Length]) == false)
        {
            return false;
        }

        string command = trimmed[DataAgentPrefix.Length..].Trim();
        command = StripCopiedMenuDescription(command);
        return command.Equals("diag evidence", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("diagnostics evidence", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("diag trace", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("diagnostics trace", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("diag progress", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("diagnostics progress", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("diag graph", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("diagnostics graph", StringComparison.OrdinalIgnoreCase);
    }

    static string StripCopiedMenuDescription(string command)
    {
        int descriptionStart = command.IndexOf(" - ", StringComparison.Ordinal);
        return descriptionStart >= 0 ? command[..descriptionStart].TrimEnd() : command;
    }
}
