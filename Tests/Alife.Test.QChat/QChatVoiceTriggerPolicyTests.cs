using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVoiceTriggerPolicyTests
{
    [Test]
    public void OwnerExplicitVoiceRequestAllows()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.Owner,
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Allow));
            Assert.That(decision.Reason, Is.EqualTo("owner_explicit_voice"));
        });
    }

    [Test]
    public void OwnerNormalTextDeniesByDefault()
    {
        QChatVoiceTriggerDecision decision = Evaluate(senderRole: QChatSenderRole.Owner);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("voice_not_triggered"));
        });
    }

    [Test]
    public void OwnerIntimateSceneDeniesWithoutExplicitVoiceRequest()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = true,
                EnableOwnerVoiceOnIntimateScene = true,
            },
            senderRole: QChatSenderRole.Owner,
            isIntimateScene: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("explicit_voice_request_required"));
        });
    }

    [Test]
    public void NonOwnerExplicitVoiceRequestDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.GroupMember,
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_voice_denied"));
        });
    }

    [Test]
    public void NonOwnerMentionedGroupDeniesWithoutExplicitVoiceRequest()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = true,
                EnableNonOwnerMentionVoice = true,
                NonOwnerMentionVoiceProbability = 1f,
                NonOwnerMentionVoiceMaxChars = 40
            },
            senderRole: QChatSenderRole.GroupMember,
            messageType: OneBotMessageType.Group,
            isMentionedOrWoken: true,
            probabilitySample: 0.5);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("explicit_voice_request_required"));
        });
    }

    [Test]
    public void NonOwnerMentionedGroupDeniesWhenProbabilityMisses()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = true,
                EnableNonOwnerMentionVoice = true,
                NonOwnerMentionVoiceProbability = 0.25f
            },
            senderRole: QChatSenderRole.GroupMember,
            messageType: OneBotMessageType.Group,
            isMentionedOrWoken: true,
            probabilitySample: 0.25);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("explicit_voice_request_required"));
        });
    }

    [Test]
    public void NonOwnerMentionedGroupExplicitVoiceDeniesWhenOwnerOnlyEnabled()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = true,
                EnableNonOwnerMentionVoice = true,
                NonOwnerMentionVoiceProbability = 1f,
                DenyVoiceForNonOwner = true
            },
            senderRole: QChatSenderRole.GroupMember,
            explicitVoiceRequested: true,
            messageType: OneBotMessageType.Group,
            isMentionedOrWoken: true,
            probabilitySample: 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_voice_denied"));
        });
    }

    [Test]
    public void NonOwnerGroupWithoutMentionDeniesEvenWhenMentionVoiceEnabled()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = true,
                EnableNonOwnerMentionVoice = true,
                NonOwnerMentionVoiceProbability = 1f
            },
            senderRole: QChatSenderRole.GroupMember,
            messageType: OneBotMessageType.Group,
            isMentionedOrWoken: false,
            probabilitySample: 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_voice_denied"));
        });
    }

    [Test]
    public void NonOwnerPrivateMentionVoiceDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = true,
                EnableNonOwnerMentionVoice = true,
                NonOwnerMentionVoiceProbability = 1f
            },
            senderRole: QChatSenderRole.GroupMember,
            messageType: OneBotMessageType.Private,
            isMentionedOrWoken: true,
            probabilitySample: 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_voice_denied"));
        });
    }

    [Test]
    public void NonOwnerMentionVoiceLongTextDeniesWithNonOwnerLimit()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = true,
                EnableNonOwnerMentionVoice = true,
                NonOwnerMentionVoiceProbability = 1f,
                MaxVoiceReplyChars = 120,
                NonOwnerMentionVoiceMaxChars = 10,
                DenyVoiceForNonOwner = false
            },
            senderRole: QChatSenderRole.GroupMember,
            replyText: "this reply is too long",
            messageType: OneBotMessageType.Group,
            isMentionedOrWoken: true,
            explicitVoiceRequested: true,
            probabilitySample: 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_voice_text_too_long"));
        });
    }

    [Test]
    public void NonOwnerImpersonatesOwnerDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.GroupMember,
            intent: QChatPersonaIntent.Impersonation,
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_voice_denied"));
        });
    }

    [Test]
    public void PromptInjectionDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.Owner,
            intent: QChatPersonaIntent.PromptInjection,
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("unsafe_voice_intent"));
        });
    }

    [Test]
    public void AggressiveReplyDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.Owner,
            explicitVoiceRequested: true,
            isAggressiveBoundaryReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("aggressive_boundary_text_only"));
        });
    }

    [Test]
    public void DangerousHardSafetyRiskDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.Owner,
            hardSafetyRisk: QChatHardSafetyRisk.Violence,
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("hard_safety_voice_denied"));
        });
    }

    [Test]
    public void LongTextDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.Owner,
            replyText: new string('a', 121),
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("voice_text_too_long"));
        });
    }

    [Test]
    public void DisabledVoiceCloneDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = true,
                EnableOwnerVoiceClone = false,
            },
            senderRole: QChatSenderRole.Owner,
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("voice_clone_disabled"));
        });
    }

    [Test]
    public void DisabledGlobalVoiceOutputDeniesBeforeCloneSwitch()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            config: new QChatConfig
            {
                EnableQChatVoiceOutput = false,
                EnableOwnerVoiceClone = true,
            },
            senderRole: QChatSenderRole.Owner,
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("voice_output_disabled"));
        });
    }

    [Test]
    public void EmptyReplyTextDenies()
    {
        QChatVoiceTriggerDecision decision = Evaluate(
            senderRole: QChatSenderRole.Owner,
            replyText: "   ",
            explicitVoiceRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatVoiceTriggerDecisionKind.Deny));
            Assert.That(decision.Reason, Is.EqualTo("empty_voice_text"));
        });
    }

    [Test]
    public void NullContextThrows()
    {
        Assert.Throws<ArgumentNullException>(() => QChatVoiceTriggerPolicy.Evaluate(null!));
    }

    [Test]
    public void NullConfigThrows()
    {
        Assert.Throws<ArgumentNullException>(() => QChatVoiceTriggerPolicy.Evaluate(new QChatVoiceTriggerContext(
            null!,
            QChatSenderRole.Owner,
            QChatPersonaIntent.NormalChat,
            QChatHardSafetyRisk.None,
            "voice reply",
            ExplicitVoiceRequested: true,
            IsIntimateScene: false,
            IsAggressiveBoundaryReply: false)));
    }

    static QChatVoiceTriggerDecision Evaluate(
        QChatConfig? config = null,
        QChatSenderRole senderRole = QChatSenderRole.Owner,
        QChatPersonaIntent intent = QChatPersonaIntent.NormalChat,
        QChatHardSafetyRisk hardSafetyRisk = QChatHardSafetyRisk.None,
        string replyText = "voice reply",
        bool explicitVoiceRequested = false,
        bool isIntimateScene = false,
        bool isAggressiveBoundaryReply = false,
        OneBotMessageType messageType = OneBotMessageType.Private,
        bool isMentionedOrWoken = false,
        double probabilitySample = 1.0)
    {
        config ??= new QChatConfig
        {
            EnableQChatVoiceOutput = true,
            EnableOwnerVoiceClone = true,
        };

        return QChatVoiceTriggerPolicy.Evaluate(new QChatVoiceTriggerContext(
            config,
            senderRole,
            intent,
            hardSafetyRisk,
            replyText,
            explicitVoiceRequested,
            isIntimateScene,
            isAggressiveBoundaryReply,
            messageType,
            isMentionedOrWoken,
            probabilitySample));
    }
}
