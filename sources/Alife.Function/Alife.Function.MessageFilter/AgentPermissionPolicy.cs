using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.Agent;

public enum AgentRiskLevel
{
    Low,
    Medium,
    High
}

public enum AgentRequestSource
{
    PrivateChat,
    GroupChat,
    System
}

public enum AgentActorPriority
{
    Guest,
    GroupParticipant,
    Owner,
    System
}

public sealed class AgentPermissionConfig
{
    public HashSet<long> OwnerUserIds { get; set; } = [];
    public bool AllowGroupLowRisk { get; set; } = true;
    public bool AllowGroupMediumRiskWhenMentioned { get; set; } = true;
    public bool RequireConfirmationForHighRisk { get; set; } = true;
}

public sealed record AgentPermissionRequest(
    long? ActorUserId,
    AgentRequestSource Source,
    bool IsMentioned,
    AgentRiskLevel RiskLevel,
    bool HasExplicitConfirmation,
    string Action);

public sealed record AgentPermissionDecision(
    bool Allowed,
    AgentActorPriority Priority,
    string Reason);

public class AgentPermissionPolicy(AgentPermissionConfig? config = null)
{
    readonly AgentPermissionConfig config = config ?? new AgentPermissionConfig();

    public AgentPermissionDecision Evaluate(AgentPermissionRequest request)
    {
        AgentActorPriority priority = GetPriority(request);
        string action = string.IsNullOrWhiteSpace(request.Action) ? "agent action" : request.Action.Trim();

        if (priority == AgentActorPriority.System)
            return Allow(priority, "System-originated action.");

        if (request.RiskLevel == AgentRiskLevel.High)
        {
            if (priority != AgentActorPriority.Owner)
                return Deny(priority, $"High-risk action '{action}' requires owner authority.");
            return Allow(priority, $"Owner authorized high-risk action '{action}'.");
        }

        if (priority == AgentActorPriority.Owner)
            return Allow(priority, "Owner authority.");

        if (request.Source == AgentRequestSource.PrivateChat)
        {
            return request.RiskLevel == AgentRiskLevel.Low
                ? Allow(priority, "Low-risk private chat action.")
                : Deny(priority, $"Private non-owner action '{action}' is limited to low risk.");
        }

        if (request.Source == AgentRequestSource.GroupChat)
        {
            if (request.RiskLevel == AgentRiskLevel.Low && config.AllowGroupLowRisk)
                return Allow(priority, "Low-risk group action.");
            if (request.RiskLevel == AgentRiskLevel.Medium && config.AllowGroupMediumRiskWhenMentioned && request.IsMentioned)
                return Allow(priority, "Mentioned group medium-risk action.");

            return Deny(priority, $"Group action '{action}' is not allowed at {request.RiskLevel} risk without required mention/authority.");
        }

        return Deny(priority, $"Action '{action}' is not allowed.");
    }

    AgentActorPriority GetPriority(AgentPermissionRequest request)
    {
        if (request.Source == AgentRequestSource.System)
            return AgentActorPriority.System;
        if (request.ActorUserId != null && IsOwner(request.ActorUserId.Value))
            return AgentActorPriority.Owner;
        if (request.Source == AgentRequestSource.GroupChat)
            return AgentActorPriority.GroupParticipant;
        return AgentActorPriority.Guest;
    }

    public bool IsOwner(long userId)
    {
        return userId != 0 && config.OwnerUserIds.Contains(userId);
    }

    static AgentPermissionDecision Allow(AgentActorPriority priority, string reason) => new(true, priority, reason);
    static AgentPermissionDecision Deny(AgentActorPriority priority, string reason) => new(false, priority, reason);
}
