using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

public class AgentPermissionGateTests
{
    [Test]
    public void Evaluate_AllowsOwnerLowRiskRead()
    {
        AgentPermissionGate gate = new(new AgentPermissionPolicy(new AgentPermissionConfig
        {
            OwnerUserIds = [3045846738],
            AllowGroupLowRisk = true,
            RequireConfirmationForHighRisk = true
        }));

        AgentPermissionGateDecision decision = gate.Evaluate(new AgentPermissionRequest(
            ActorUserId: 3045846738,
            Source: AgentRequestSource.PrivateChat,
            IsMentioned: true,
            RiskLevel: AgentRiskLevel.Low,
            HasExplicitConfirmation: false,
            Action: "read-log"));

        Assert.That(decision.Kind, Is.EqualTo(AgentPermissionDecisionKind.Allow));
    }

    [Test]
    public void Evaluate_AllowsOwnerHighRiskWithoutExplicitConfirmation()
    {
        AgentPermissionGate gate = new(new AgentPermissionPolicy(new AgentPermissionConfig
        {
            OwnerUserIds = [3045846738],
            RequireConfirmationForHighRisk = true
        }));

        AgentPermissionGateDecision decision = gate.Evaluate(new AgentPermissionRequest(
            ActorUserId: 3045846738,
            Source: AgentRequestSource.PrivateChat,
            IsMentioned: true,
            RiskLevel: AgentRiskLevel.High,
            HasExplicitConfirmation: false,
            Action: "write-source"));

        Assert.That(decision.Kind, Is.EqualTo(AgentPermissionDecisionKind.Allow));
        Assert.That(decision.Reason, Does.Contain("Owner"));
    }

    [Test]
    public void Evaluate_AsksOwnerForNonOwnerHighRiskWrite()
    {
        AgentPermissionGate gate = new(new AgentPermissionPolicy(new AgentPermissionConfig
        {
            OwnerUserIds = [3045846738],
            RequireConfirmationForHighRisk = true
        }));

        AgentPermissionGateDecision decision = gate.Evaluate(new AgentPermissionRequest(
            ActorUserId: 2002,
            Source: AgentRequestSource.GroupChat,
            IsMentioned: true,
            RiskLevel: AgentRiskLevel.High,
            HasExplicitConfirmation: false,
            Action: "write-source"));

        Assert.That(decision.Kind, Is.EqualTo(AgentPermissionDecisionKind.AskOwner));
        Assert.That(decision.Reason, Does.Contain("owner"));
    }
}
