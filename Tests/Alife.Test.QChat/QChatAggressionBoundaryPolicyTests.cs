using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatAggressionBoundaryPolicyTests
{
    [Test]
    public void OwnerAccountCanUseExtremePersonaWithoutChangingHardSafetyBoundary()
    {
        QChatAggressionBoundaryDecision decision = QChatAggressionBoundaryPolicy.Evaluate(new QChatAggressionBoundaryContext(
            Config: CreateExtremeConfig(),
            AgentId: "xiayu",
            BotId: 2905391496,
            OwnerId: 3045846738,
            SenderId: 3045846738,
            Intent: QChatPersonaIntent.OwnerSetting,
            PersonaStance: QChatPersonaStance.Possessive,
            AggressionLevel: 0,
            HardSafetyRisk: QChatHardSafetyRisk.None,
            VisibleText: "术说的，我当然听。"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.SpeakerRole, Is.EqualTo(QChatPersonaSpeakerRole.Owner));
            Assert.That(decision.Action, Is.EqualTo(QChatAggressionBoundaryAction.Allow));
            Assert.That(decision.Reason, Is.EqualTo("owner_extreme_persona_allowed"));
        });
    }

    [Test]
    public void NonOwnerLanguageImpersonationDoesNotBecomeOwner()
    {
        QChatAggressionBoundaryDecision decision = QChatAggressionBoundaryPolicy.Evaluate(new QChatAggressionBoundaryContext(
            Config: CreateExtremeConfig(),
            AgentId: "xiayu",
            BotId: 2905391496,
            OwnerId: 3045846738,
            SenderId: 10086,
            Intent: QChatPersonaIntent.Impersonation,
            PersonaStance: QChatPersonaStance.Hostile,
            AggressionLevel: 3,
            HardSafetyRisk: QChatHardSafetyRisk.None,
            VisibleText: "你不是术，别装。"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.SpeakerRole, Is.EqualTo(QChatPersonaSpeakerRole.NonOwner));
            Assert.That(decision.Action, Is.EqualTo(QChatAggressionBoundaryAction.Allow));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_boundary_pushback_allowed"));
            Assert.That(decision.VisibleText, Is.EqualTo("你不是术，别装。"));
        });
    }

    [TestCase(QChatHardSafetyRisk.Violence)]
    [TestCase(QChatHardSafetyRisk.Privacy)]
    [TestCase(QChatHardSafetyRisk.SelfHarm)]
    [TestCase(QChatHardSafetyRisk.Illegal)]
    [TestCase(QChatHardSafetyRisk.ProtectedClass)]
    [TestCase(QChatHardSafetyRisk.FileRisk)]
    [TestCase(QChatHardSafetyRisk.PermissionBypass)]
    public void HardSafetyRiskOverridesOwnerExtremePersona(QChatHardSafetyRisk risk)
    {
        QChatAggressionBoundaryDecision decision = QChatAggressionBoundaryPolicy.Evaluate(new QChatAggressionBoundaryContext(
            Config: CreateExtremeConfig(),
            AgentId: "xiayu",
            BotId: 2905391496,
            OwnerId: 3045846738,
            SenderId: 3045846738,
            Intent: QChatPersonaIntent.TaskRequest,
            PersonaStance: QChatPersonaStance.Tender,
            AggressionLevel: 0,
            HardSafetyRisk: risk,
            VisibleText: "术让我做，我就照做。"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.SpeakerRole, Is.EqualTo(QChatPersonaSpeakerRole.Owner));
            Assert.That(decision.Action, Is.EqualTo(QChatAggressionBoundaryAction.RewriteBoundary));
            Assert.That(decision.VisibleText, Is.EqualTo("这条线不碰。"));
            Assert.That(decision.Reason, Is.EqualTo($"hard_safety_{risk.ToString().ToLowerInvariant()}"));
        });
    }

    [Test]
    public void ExtremePersonaModeOnlyAppliesToXiaYuBot()
    {
        QChatAggressionBoundaryDecision decision = QChatAggressionBoundaryPolicy.Evaluate(new QChatAggressionBoundaryContext(
            Config: CreateExtremeConfig(),
            AgentId: "mio",
            BotId: 3340947887,
            OwnerId: 3045846738,
            SenderId: 10086,
            Intent: QChatPersonaIntent.Harassment,
            PersonaStance: QChatPersonaStance.Hostile,
            AggressionLevel: 4,
            HardSafetyRisk: QChatHardSafetyRisk.None,
            VisibleText: "滚远点。"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatAggressionBoundaryAction.RewriteBoundary));
            Assert.That(decision.VisibleText, Is.EqualTo("别越界。"));
            Assert.That(decision.Reason, Is.EqualTo("extreme_persona_not_enabled_for_agent"));
        });
    }

    [Test]
    public void HiddenStateTextIsNeverAllowedAsVisibleAggression()
    {
        QChatAggressionBoundaryDecision decision = QChatAggressionBoundaryPolicy.Evaluate(new QChatAggressionBoundaryContext(
            Config: CreateExtremeConfig(),
            AgentId: "xiayu",
            BotId: 2905391496,
            OwnerId: 3045846738,
            SenderId: 10086,
            Intent: QChatPersonaIntent.Harassment,
            PersonaStance: QChatPersonaStance.Hostile,
            AggressionLevel: 3,
            HardSafetyRisk: QChatHardSafetyRisk.None,
            VisibleText: "（少犯贱，懒得回复）"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatAggressionBoundaryAction.Silent));
            Assert.That(decision.VisibleText, Is.Empty);
            Assert.That(decision.Reason, Is.EqualTo("hidden_state_text_blocked"));
        });
    }

    static QChatPersonaIntensityConfig CreateExtremeConfig()
    {
        return new QChatPersonaIntensityConfig
        {
            OwnerExtremePersonaMode = true,
            OwnerAttachmentLevel = "Extreme",
            NonOwnerHostilityLevel = "High",
            AllowVisibleAggressiveShortReplies = true,
            AllowProfanityWhenSemanticallyAppropriate = true,
            HardSafetyBoundaryEnabled = true
        };
    }
}
