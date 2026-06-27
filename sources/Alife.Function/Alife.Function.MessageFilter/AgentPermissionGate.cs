namespace Alife.Function.Agent;

public enum AgentPermissionDecisionKind
{
    Allow,
    AskOwner,
    Deny
}

public sealed record AgentPermissionGateDecision(
    AgentPermissionDecisionKind Kind,
    string Reason);

public sealed class AgentPermissionGate(AgentPermissionPolicy policy)
{
    public AgentPermissionGateDecision Evaluate(AgentPermissionRequest request)
    {
        bool actorIsOwner = request.ActorUserId is long actorUserId && policy.IsOwner(actorUserId);
        string action = string.IsNullOrWhiteSpace(request.Action) ? "agent action" : request.Action.Trim();

        if (request.Source == AgentRequestSource.System)
            return new AgentPermissionGateDecision(AgentPermissionDecisionKind.Allow, "System-originated action.");

        if (request.RiskLevel == AgentRiskLevel.High && actorIsOwner == false)
            return new AgentPermissionGateDecision(AgentPermissionDecisionKind.AskOwner, $"high-risk action '{action}' requires owner confirmation");

        if (request.RiskLevel == AgentRiskLevel.Medium
            && actorIsOwner == false
            && request.Source == AgentRequestSource.GroupChat)
        {
            return new AgentPermissionGateDecision(AgentPermissionDecisionKind.AskOwner, $"medium-risk group action '{action}' requires owner confirmation");
        }

        AgentPermissionDecision decision = policy.Evaluate(request);
        return decision.Allowed
            ? new AgentPermissionGateDecision(AgentPermissionDecisionKind.Allow, decision.Reason)
            : new AgentPermissionGateDecision(AgentPermissionDecisionKind.Deny, decision.Reason);
    }
}
