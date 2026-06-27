using System;
using System.Linq;

namespace Alife.Function.QChat;

public enum QChatCapability
{
    NormalChat,
    OwnerDiagnostics,
    ApprovalDecision,
    AllowlistUpdate,
    RecallMessage,
    ManagedFileRead,
    GroupFileUpload,
    QuietModeControl,
    ProfileLearning,
    RiskLocalBlock,
    RiskFriendDelete,
    DesktopBusinessTask,
    QZoneProactiveAction,
    InternetLookup
}

public enum QChatCapabilityRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record QChatCapabilityContext(
    QChatCapability Capability,
    QChatSenderRole SenderRole,
    string AgentId,
    long UserId = 0,
    long BotId = 0,
    long OwnerId = 0,
    string ProtectedUserIds = "",
    string AllowedAgentIds = "xiayu");

public sealed record QChatCapabilityDecision(
    bool Allowed,
    string Reason,
    QChatCapabilityRiskLevel RiskLevel,
    bool RequiresOwnerEventOutbox,
    bool RequiresOwnerApproval);

public static class QChatCapabilityPolicy
{
    public static QChatCapabilityDecision Evaluate(QChatCapabilityContext context)
    {
        QChatCapabilityRiskLevel riskLevel = GetRiskLevel(context.Capability);
        bool requiresOwnerOutbox = RequiresOwnerOutbox(context.Capability);
        bool requiresOwnerApproval = RequiresOwnerApproval(context.Capability);

        QChatCapabilityDecision Deny(string reason)
        {
            return new QChatCapabilityDecision(
                false,
                reason,
                riskLevel,
                requiresOwnerOutbox,
                requiresOwnerApproval);
        }

        if (RequiresOwner(context.Capability) && context.SenderRole != QChatSenderRole.Owner)
            return Deny("owner_required");

        if (RequiresAllowedAgent(context.Capability) &&
            ContainsToken(context.AllowedAgentIds, context.AgentId) == false)
        {
            return Deny("agent_not_allowed");
        }

        if (context.Capability == QChatCapability.RiskFriendDelete)
        {
            if (context.UserId > 0 && context.OwnerId > 0 && context.UserId == context.OwnerId)
                return Deny("owner_protected");
            if (context.UserId > 0 && context.BotId > 0 && context.UserId == context.BotId)
                return Deny("bot_protected");
            if (ContainsId(context.ProtectedUserIds, context.UserId))
                return Deny("protected_user");
        }

        return new QChatCapabilityDecision(
            true,
            "allowed",
            riskLevel,
            requiresOwnerOutbox,
            requiresOwnerApproval);
    }

    static bool RequiresOwner(QChatCapability capability)
    {
        return capability is QChatCapability.OwnerDiagnostics
            or QChatCapability.ApprovalDecision
            or QChatCapability.AllowlistUpdate
            or QChatCapability.RecallMessage
            or QChatCapability.ManagedFileRead
            or QChatCapability.GroupFileUpload
            or QChatCapability.QuietModeControl
            or QChatCapability.DesktopBusinessTask
            or QChatCapability.InternetLookup;
    }

    static bool RequiresAllowedAgent(QChatCapability capability)
    {
        return capability is QChatCapability.RiskFriendDelete
            or QChatCapability.GroupFileUpload
            or QChatCapability.DesktopBusinessTask
            or QChatCapability.InternetLookup;
    }

    static QChatCapabilityRiskLevel GetRiskLevel(QChatCapability capability)
    {
        return capability switch
        {
            QChatCapability.NormalChat => QChatCapabilityRiskLevel.Low,
            QChatCapability.OwnerDiagnostics => QChatCapabilityRiskLevel.Low,
            QChatCapability.AllowlistUpdate => QChatCapabilityRiskLevel.High,
            QChatCapability.ProfileLearning => QChatCapabilityRiskLevel.Medium,
            QChatCapability.RecallMessage => QChatCapabilityRiskLevel.Medium,
            QChatCapability.ManagedFileRead => QChatCapabilityRiskLevel.Medium,
            QChatCapability.QuietModeControl => QChatCapabilityRiskLevel.Medium,
            QChatCapability.InternetLookup => QChatCapabilityRiskLevel.Medium,
            QChatCapability.QZoneProactiveAction => QChatCapabilityRiskLevel.High,
            QChatCapability.ApprovalDecision => QChatCapabilityRiskLevel.High,
            QChatCapability.GroupFileUpload => QChatCapabilityRiskLevel.High,
            QChatCapability.RiskLocalBlock => QChatCapabilityRiskLevel.High,
            QChatCapability.RiskFriendDelete => QChatCapabilityRiskLevel.Critical,
            QChatCapability.DesktopBusinessTask => QChatCapabilityRiskLevel.Critical,
            _ => QChatCapabilityRiskLevel.Medium
        };
    }

    static bool RequiresOwnerOutbox(QChatCapability capability)
    {
        return capability is QChatCapability.RiskFriendDelete
            or QChatCapability.DesktopBusinessTask;
    }

    static bool RequiresOwnerApproval(QChatCapability capability)
    {
        return capability is QChatCapability.DesktopBusinessTask;
    }

    static bool ContainsId(string? csv, long id)
    {
        if (id <= 0)
            return false;

        return (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => long.TryParse(item, out long parsed) && parsed == id);
    }

    static bool ContainsToken(string? csv, string value)
    {
        string normalized = (value ?? "").Trim();
        if (normalized.Length == 0)
            return false;

        return (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
