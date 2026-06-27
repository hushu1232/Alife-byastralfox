using Alife.Function.Interpreter;

namespace Alife.Function.Agent;

public enum AgentExecutionDecisionStatus
{
    AllowedAutomatically,
    OwnerConfirmationRequired,
    Blocked
}

public sealed record AgentExecutionGatewayDecision(
    AgentExecutionDecisionStatus Status,
    bool AllowedNow,
    bool RequiresOwnerConfirmation,
    AgentActorPriority Priority,
    AgentRiskLevel RiskLevel,
    string Action,
    string Reason);

public class AgentActionAuthorizationService
{
    public AgentExecutionGatewayDecision EvaluateExecution(
        AgentPermissionRequest request,
        AgentPermissionConfig config)
    {
        AgentPermissionPolicy policy = new(config);
        AgentPermissionDecision decision = policy.Evaluate(request);
        string action = string.IsNullOrWhiteSpace(request.Action) ? "agent action" : request.Action.Trim();

        if (decision.Allowed)
        {
            return new AgentExecutionGatewayDecision(
                AgentExecutionDecisionStatus.AllowedAutomatically,
                AllowedNow: true,
                RequiresOwnerConfirmation: false,
                decision.Priority,
                request.RiskLevel,
                action,
                decision.Reason);
        }

        if (request.RiskLevel == AgentRiskLevel.High
            && decision.Priority != AgentActorPriority.Owner
            && config.OwnerUserIds.Count > 0)
        {
            return new AgentExecutionGatewayDecision(
                AgentExecutionDecisionStatus.OwnerConfirmationRequired,
                AllowedNow: false,
                RequiresOwnerConfirmation: true,
                decision.Priority,
                request.RiskLevel,
                action,
                decision.Reason);
        }

        return new AgentExecutionGatewayDecision(
            AgentExecutionDecisionStatus.Blocked,
            AllowedNow: false,
            RequiresOwnerConfirmation: false,
            decision.Priority,
            request.RiskLevel,
            action,
            decision.Reason);
    }

    public XmlFunctionExecutionDecision AuthorizeXmlFunction(
        XmlFunction function,
        AgentPermissionRequest request,
        AgentPermissionConfig config)
    {
        AgentExecutionGatewayDecision decision = EvaluateExecution(request with {
            RiskLevel = ToAgentRiskLevel(function.RiskLevel),
            Action = $"xml.{function.Name}"
        }, config);

        return new XmlFunctionExecutionDecision(decision.AllowedNow, decision.Reason);
    }

    public static AgentRiskLevel ToAgentRiskLevel(XmlFunctionRiskLevel riskLevel) => riskLevel switch {
        XmlFunctionRiskLevel.High => AgentRiskLevel.High,
        XmlFunctionRiskLevel.Medium => AgentRiskLevel.Medium,
        _ => AgentRiskLevel.Low,
    };
}
