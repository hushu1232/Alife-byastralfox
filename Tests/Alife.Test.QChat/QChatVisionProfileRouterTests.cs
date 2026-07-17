using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVisionProfileRouterTests
{
    [Test]
    public void Resolve_PrefersBotIdForXiayu()
    {
        QChatVisionProfileConfig config = QChatVisionProfileConfig.CreateDefault();

        QChatVisionProfileDecision decision = QChatVisionProfileRouter.Resolve(config, "mixu", 2905391496);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVisionProfileDecisionKind.Allow));
            Assert.That(decision.Profile!.AgentId, Is.EqualTo("xiayu"));
            Assert.That(decision.Reason, Is.EqualTo("bot_id_profile_matched"));
        });
    }

    [Test]
    public void Resolve_PrefersBotIdForMixu()
    {
        QChatVisionProfileConfig config = QChatVisionProfileConfig.CreateDefault();

        QChatVisionProfileDecision decision = QChatVisionProfileRouter.Resolve(config, "xiayu", 3340947887);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVisionProfileDecisionKind.Allow));
            Assert.That(decision.Profile!.AgentId, Is.EqualTo("mixu"));
            Assert.That(decision.Profile.BotId, Is.EqualTo(3340947887));
        });
    }

    [TestCase("xiayu", 2905391496)]
    [TestCase("mixu", 3340947887)]
    public void Resolve_DefaultProfilesUseAgnesWithGrokFallback(string agentId, long botId)
    {
        QChatVisionProfileDecision decision = QChatVisionProfileRouter.Resolve(
            QChatVisionProfileConfig.CreateDefault(), agentId, botId);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVisionProfileDecisionKind.Allow));
            Assert.That(decision.Profile!.PrimaryProvider, Is.EqualTo("agnes"));
            Assert.That(decision.Profile.FallbackProvider, Is.EqualTo("grok"));
            Assert.That(decision.Profile.ComplexRequestProvider, Is.EqualTo("grok"));
        });
    }

    [Test]
    public void Resolve_FallsBackToAgentId()
    {
        QChatVisionProfileConfig config = QChatVisionProfileConfig.CreateDefault();

        QChatVisionProfileDecision decision = QChatVisionProfileRouter.Resolve(config, "xiayu", 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVisionProfileDecisionKind.Allow));
            Assert.That(decision.Profile!.BotId, Is.EqualTo(2905391496));
            Assert.That(decision.Reason, Is.EqualTo("agent_id_profile_matched"));
        });
    }

    [Test]
    public void Resolve_DisabledProfileDenies()
    {
        QChatVisionProfileConfig config = QChatVisionProfileConfig.CreateDefault();
        config.Profiles.Single(profile => profile.AgentId == "xiayu").Enabled = false;

        QChatVisionProfileDecision decision = QChatVisionProfileRouter.Resolve(config, "xiayu", 2905391496);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVisionProfileDecisionKind.Deny));
            Assert.That(decision.Profile, Is.Null);
            Assert.That(decision.Reason, Is.EqualTo("vision_profile_disabled"));
        });
    }
}
