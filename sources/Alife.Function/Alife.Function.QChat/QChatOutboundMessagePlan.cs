using System.Collections.Generic;

namespace Alife.Function.QChat;

public enum QChatOutboundItemKind
{
    Text,
    File,
    Image
}

public sealed record QChatOutboundMessageItem(
    QChatOutboundItemKind Kind,
    string Text,
    string? MediaId = null);

public sealed record QChatOutboundMessagePlan(IReadOnlyList<QChatOutboundMessageItem> Items);
