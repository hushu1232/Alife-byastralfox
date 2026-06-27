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
    const string Prefix = "/qchat";

    public static QChatCommandAccessDecision Evaluate(QChatCommandAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsQChatCommand(context.PlainText) == false)
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
        string trimmed = text?.TrimStart() ?? string.Empty;
        if (trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        return trimmed.Length == Prefix.Length ||
               char.IsWhiteSpace(trimmed[Prefix.Length]);
    }
}
