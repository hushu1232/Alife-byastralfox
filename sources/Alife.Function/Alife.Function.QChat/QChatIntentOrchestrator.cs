namespace Alife.Function.QChat;

public enum QChatIntentActionKind
{
    None,
    RecallMessage,
    UploadGroupFile,
    SetQuietMode,
    UpdateAllowlist
}

public sealed record QChatIntentOrchestrationContext(
    QChatIntentDecision Intent,
    QChatSenderRole SenderRole,
    string AgentId,
    long BotId = 0,
    long OwnerId = 0,
    long CurrentGroupId = 0,
    bool IsTrustedWakeUser = false,
    string ProtectedUserIds = "",
    string AllowedAgentIds = "xiayu");

public sealed record QChatIntentAction(
    QChatIntentActionKind Kind,
    bool Allowed,
    QChatCapability Capability,
    string Reason,
    QChatCapabilityRiskLevel RiskLevel,
    bool RequiresOwnerEventOutbox,
    bool RequiresOwnerApproval,
    string? TargetText = null,
    long? TargetId = null,
    string? FilePath = null);

public static class QChatIntentOrchestrator
{
    public static QChatIntentAction Decide(QChatIntentOrchestrationContext context)
    {
        (QChatCapability capability, QChatIntentActionKind actionKind) = MapIntent(context.Intent.Kind);
        if (actionKind == QChatIntentActionKind.None)
            return Denied(capability, "unsupported_intent");

        if (context.Intent.IsConfirmed == false)
            return Denied(capability, "intent_not_confirmed");

        QChatCapabilityDecision capabilityDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: capability,
            SenderRole: GetEffectiveSenderRole(context, capability),
            AgentId: context.AgentId,
            BotId: context.BotId,
            OwnerId: context.OwnerId,
            ProtectedUserIds: context.ProtectedUserIds,
            AllowedAgentIds: context.AllowedAgentIds));

        if (capabilityDecision.Allowed == false)
        {
            return new QChatIntentAction(
                QChatIntentActionKind.None,
                false,
                capability,
                capabilityDecision.Reason,
                capabilityDecision.RiskLevel,
                capabilityDecision.RequiresOwnerEventOutbox,
                capabilityDecision.RequiresOwnerApproval,
                context.Intent.TargetText,
                context.Intent.TargetId,
                context.Intent.FilePath);
        }

        return new QChatIntentAction(
            actionKind,
            true,
            capability,
            context.Intent.Reason,
            capabilityDecision.RiskLevel,
            capabilityDecision.RequiresOwnerEventOutbox,
            capabilityDecision.RequiresOwnerApproval,
            context.Intent.TargetText,
            context.Intent.TargetId,
            context.Intent.FilePath);
    }

    static (QChatCapability Capability, QChatIntentActionKind ActionKind) MapIntent(QChatIntentKind intentKind)
    {
        return intentKind switch
        {
            QChatIntentKind.RecallMessage => (QChatCapability.RecallMessage, QChatIntentActionKind.RecallMessage),
            QChatIntentKind.GroupFileUpload => (QChatCapability.GroupFileUpload, QChatIntentActionKind.UploadGroupFile),
            QChatIntentKind.QuietMode => (QChatCapability.QuietModeControl, QChatIntentActionKind.SetQuietMode),
            QChatIntentKind.AllowlistUpdate => (QChatCapability.AllowlistUpdate, QChatIntentActionKind.UpdateAllowlist),
            _ => (QChatCapability.NormalChat, QChatIntentActionKind.None)
        };
    }

    static QChatSenderRole GetEffectiveSenderRole(QChatIntentOrchestrationContext context, QChatCapability capability)
    {
        if (capability == QChatCapability.QuietModeControl && context.IsTrustedWakeUser)
            return QChatSenderRole.Owner;
        return context.SenderRole;
    }

    static QChatIntentAction Denied(
        QChatCapability capability,
        string reason)
    {
        QChatCapabilityDecision capabilityDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: capability,
            SenderRole: QChatSenderRole.PrivateGuest,
            AgentId: ""));
        return new QChatIntentAction(
            QChatIntentActionKind.None,
            false,
            capability,
            reason,
            capabilityDecision.RiskLevel,
            capabilityDecision.RequiresOwnerEventOutbox,
            capabilityDecision.RequiresOwnerApproval);
    }
}
