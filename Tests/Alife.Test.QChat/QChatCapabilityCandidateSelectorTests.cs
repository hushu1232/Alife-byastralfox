using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatCapabilityCandidateSelectorTests
{
    [Test]
    public void SelectsConversationCandidateForNaturalFollowUpWithRecentTurn()
    {
        QChatCapabilityCandidateSelector selector = new();

        QChatCapabilityCandidate candidate = selector.Select(
            "继续刚才的话题",
            hasRecentConversation: true,
            hasApprovedPersona: true);

        Assert.That(candidate.Kind, Is.EqualTo(QChatCapabilityCandidateKind.ConversationContext));
    }

    [Test]
    public void SelectsPersonaCandidateForExplicitRoleFactQuestion()
    {
        QChatCapabilityCandidateSelector selector = new();

        QChatCapabilityCandidate candidate = selector.Select(
            "你的说话风格是什么",
            hasRecentConversation: true,
            hasApprovedPersona: true);

        Assert.That(candidate.Kind, Is.EqualTo(QChatCapabilityCandidateKind.PersonaFact));
    }

    [Test]
    public void DoesNotOfferCapabilityForOrdinaryChatOrMissingSource()
    {
        QChatCapabilityCandidateSelector selector = new();

        Assert.Multiple(() =>
        {
            Assert.That(selector.Select("今天天气不错", true, true).Kind, Is.EqualTo(QChatCapabilityCandidateKind.None));
            Assert.That(selector.Select("继续刚才的话题", false, true).Kind, Is.EqualTo(QChatCapabilityCandidateKind.None));
            Assert.That(selector.Select("你的说话风格是什么", true, false).Kind, Is.EqualTo(QChatCapabilityCandidateKind.None));
        });
    }
}
