using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatImageRecognitionPolicyTests
{
    [Test]
    public void OwnerPrivateImageIsAnalyzedWhenEnabled()
    {
        QChatImageRecognitionPolicyDecision decision = Decide(
            QChatSenderRole.Owner,
            OneBotMessageType.Private,
            isMentionedOrWoken: false,
            isPassiveGroupMessage: false);

        Assert.That(decision.Action, Is.EqualTo(QChatImageRecognitionAction.Analyze));
    }

    [Test]
    public void OwnerGroupImageIsAnalyzedWhenEnabled()
    {
        QChatImageRecognitionPolicyDecision decision = Decide(
            QChatSenderRole.Owner,
            OneBotMessageType.Group,
            isMentionedOrWoken: false,
            isPassiveGroupMessage: true);

        Assert.That(decision.Action, Is.EqualTo(QChatImageRecognitionAction.Analyze));
    }

    [Test]
    public void NonOwnerMentionedGroupImageIsAnalyzedWhenEnabled()
    {
        QChatImageRecognitionPolicyDecision decision = Decide(
            QChatSenderRole.GroupMember,
            OneBotMessageType.Group,
            isMentionedOrWoken: true,
            isPassiveGroupMessage: false);

        Assert.That(decision.Action, Is.EqualTo(QChatImageRecognitionAction.Analyze));
    }

    [Test]
    public void NonOwnerPassiveGroupImageIsSkippedByDefault()
    {
        QChatImageRecognitionPolicyDecision decision = Decide(
            QChatSenderRole.GroupMember,
            OneBotMessageType.Group,
            isMentionedOrWoken: false,
            isPassiveGroupMessage: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatImageRecognitionAction.Skip));
            Assert.That(decision.Reason, Is.EqualTo("passive_group_image_disabled"));
        });
    }

    [Test]
    public void DisabledConfigSkipsRecognition()
    {
        QChatConfig config = EnabledConfig();
        config.EnableImageRecognition = false;

        QChatImageRecognitionPolicyDecision decision = QChatImageRecognitionPolicy.Decide(
            new QChatImageRecognitionPolicyContext(
                config,
                QChatSenderRole.Owner,
                OneBotMessageType.Private,
                IsMentionedOrWoken: false,
                IsPassiveGroupMessage: false,
                ImageCount: 1));

        Assert.That(decision.Action, Is.EqualTo(QChatImageRecognitionAction.Skip));
    }

    [Test]
    public void TooManyImagesSkipsRecognition()
    {
        QChatConfig config = EnabledConfig();
        config.MaxImagesPerMessage = 1;

        QChatImageRecognitionPolicyDecision decision = QChatImageRecognitionPolicy.Decide(
            new QChatImageRecognitionPolicyContext(
                config,
                QChatSenderRole.Owner,
                OneBotMessageType.Private,
                IsMentionedOrWoken: false,
                IsPassiveGroupMessage: false,
                ImageCount: 2));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatImageRecognitionAction.Skip));
            Assert.That(decision.Reason, Is.EqualTo("too_many_images"));
        });
    }

    static QChatImageRecognitionPolicyDecision Decide(
        QChatSenderRole senderRole,
        OneBotMessageType messageType,
        bool isMentionedOrWoken,
        bool isPassiveGroupMessage)
    {
        return QChatImageRecognitionPolicy.Decide(new QChatImageRecognitionPolicyContext(
            EnabledConfig(),
            senderRole,
            messageType,
            isMentionedOrWoken,
            isPassiveGroupMessage,
            ImageCount: 1));
    }

    static QChatConfig EnabledConfig()
    {
        return new QChatConfig
        {
            EnableImageRecognition = true,
            AnalyzeOwnerPrivateImages = true,
            AnalyzeOwnerGroupImages = true,
            AnalyzePrivateGuestImages = true,
            AnalyzeMentionedGroupImages = true,
            AnalyzePassiveGroupImages = false,
            MaxImagesPerMessage = 2
        };
    }
}
