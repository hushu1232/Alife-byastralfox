using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticWebResearchServiceTests
{
    [Test]
    public void IsEligible_AllowsEnabledOwnerPrivateMessage()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new();

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.Owner,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsEligible_AllowsEnabledMentionedGroupMessage()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new() { GroupId = 1 };

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.GroupMember,
            isMentionedOrWoken: true);

        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsEligible_DeniesUnmentionedGroupMessage()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new() { GroupId = 1 };

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.GroupMember,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.False);
    }

    [Test]
    public void IsEligible_DeniesPrivateGuest()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new();

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.PrivateGuest,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.False);
    }

    [Test]
    public void IsEligible_DeniesWhenFeatureIsDisabled()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = false };
        OneBotMessageEvent message = new();

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.Owner,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.False);
    }
}
