using Alife.Framework;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public static class QChatAgentEventAdapter
{
    public const string SenderRoleKey = "qchat.senderRole";
    public const string ShouldActivateKey = "qchat.shouldActivate";
    public const string IsMentionedOrWokenKey = "qchat.isMentionedOrWoken";
    public const string PermissionRequestKey = "agent.permissionRequest";
    public const string PermissionConfigKey = "agent.permissionConfig";

    public static AgentEvent ToAgentEvent(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        bool isMentionedOrWoken,
        string text,
        string rawMessage,
        AgentControlCenterConfig? controlConfig = null)
    {
        string type = messageEvent.MessageType == OneBotMessageType.Group
            ? "qq.message.group"
            : "qq.message.private";
        string sessionId = messageEvent.MessageType == OneBotMessageType.Group
            ? $"qq:group:{messageEvent.GroupId}"
            : $"qq:private:{messageEvent.UserId}";
        string? actorId = messageEvent.UserId == 0 ? null : $"qq:{messageEvent.UserId}";

        AgentEvent agentEvent = new(
            Type: type,
            Source: "qq",
            SessionId: sessionId,
            ActorId: actorId,
            Text: text);

        QChatSenderRole role = QChatMessageSecurity.Classify(config, messageEvent);
        AgentPermissionConfig permissionConfig = QChatMessageSecurity.BuildPermissionConfig(config, controlConfig);
        AgentPermissionRequest permissionRequest = QChatMessageSecurity.BuildPermissionRequest(
            config,
            messageEvent,
            isMentionedOrWoken,
            rawMessage);

        bool shouldActivate = messageEvent.MessageType == OneBotMessageType.Group
            ? QChatMessageSecurity.ShouldActivateGroup(config, messageEvent, isMentionedOrWoken, controlConfig)
            : QChatMessageSecurity.ShouldAcceptPrivateMessage(config, messageEvent);

        agentEvent.State[SenderRoleKey] = role;
        agentEvent.State[ShouldActivateKey] = shouldActivate;
        agentEvent.State[IsMentionedOrWokenKey] = isMentionedOrWoken;
        agentEvent.State[PermissionRequestKey] = permissionRequest;
        agentEvent.State[PermissionConfigKey] = permissionConfig;

        return agentEvent;
    }
}
