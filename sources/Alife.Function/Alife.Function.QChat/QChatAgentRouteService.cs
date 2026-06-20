using System;
using System.Collections.Generic;

namespace Alife.Function.QChat;

public enum QChatConversationKind
{
    Private,
    Group
}

public sealed class QChatAgentRouteConfig
{
    public Dictionary<long, string> BotAgents { get; set; } = [];
    public long OwnerUserId { get; set; }
}

public sealed record QChatAgentRoute(
    string AgentId,
    long BotAccountId,
    QChatConversationKind ConversationKind,
    long PeerId,
    long SenderId,
    bool IsOwner,
    string SessionKey);

public sealed class QChatAgentRouteService(QChatAgentRouteConfig? config = null, QChatAgentIdentityRegistry? identityRegistry = null)
{
    readonly QChatAgentRouteConfig config = config ?? new QChatAgentRouteConfig();
    readonly QChatAgentIdentityRegistry identityRegistry = identityRegistry ?? QChatAgentIdentityRegistry.CreateDefault();

    public QChatAgentRoute Resolve(long botAccountId, OneBotBasicMessageEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string agentId = config.BotAgents.TryGetValue(botAccountId, out string? configuredAgent)
            ? configuredAgent
            : identityRegistry.ResolveByBotId(botAccountId)?.AgentId ?? $"qq-{botAccountId}";

        QChatConversationKind kind = message.MessageType == OneBotMessageType.Group
            ? QChatConversationKind.Group
            : QChatConversationKind.Private;

        long peerId = kind == QChatConversationKind.Group
            ? message.GroupId
            : message.UserId;

        string kindSegment = kind == QChatConversationKind.Group ? "group" : "private";
        string sessionKey = $"qq:{agentId}:{botAccountId}:{kindSegment}:{peerId}";

        return new QChatAgentRoute(
            agentId,
            botAccountId,
            kind,
            peerId,
            message.UserId,
            config.OwnerUserId != 0 && message.UserId == config.OwnerUserId,
            sessionKey);
    }
}
