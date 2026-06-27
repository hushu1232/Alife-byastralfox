using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatOwnerTrustedFastPathPolicyTests
{
    [Test]
    public void Apply_WhenOwnerRecallCandidateWithoutMeta_MarksDecisionConfirmed()
    {
        QChatConfig config = new()
        {
            EnableOwnerTrustedFastPath = true,
            OwnerFastPathAllowsRecall = true
        };
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.Recall,
            config);

        Assert.That(result.IsConfirmed, Is.True);
        Assert.That(result.TargetKind, Is.EqualTo(QChatIntentTargetKind.RecentBotMessage));
        Assert.That(result.Reason, Does.Contain("owner trusted fast path"));
    }

    [Test]
    public void Apply_WhenNonOwnerUsesSameCandidate_DoesNotConfirm()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.PrivateGuest,
            QChatOwnerTrustedFastPathAction.Recall,
            new QChatConfig());

        Assert.That(result.IsConfirmed, Is.False);
        Assert.That(result.Reason, Is.EqualTo(decision.Reason));
    }

    [Test]
    public void Apply_WhenOwnerMessageIsMetaDiscussion_DoesNotConfirm()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: true,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.Recall,
            new QChatConfig());

        Assert.That(result.IsConfirmed, Is.False);
    }

    [Test]
    public void Apply_WhenOwnerFastPathDisabled_DoesNotConfirm()
    {
        QChatConfig config = new()
        {
            EnableOwnerTrustedFastPath = false,
            OwnerFastPathAllowsRecall = true
        };
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.Recall,
            config);

        Assert.That(result.IsConfirmed, Is.False);
    }

    [Test]
    public void Apply_WhenMemoryPurgeActionRequested_DoesNotConfirmByDefault()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.None,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.7,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "memory purge candidate");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.MemoryPurge,
            new QChatConfig());

        Assert.That(result.IsConfirmed, Is.False);
    }

    [Test]
    public void Apply_WhenOwnerRecallActionReceivesGroupFileUploadCandidate_DoesNotConfirm()
    {
        QChatConfig config = new()
        {
            EnableOwnerTrustedFastPath = true,
            OwnerFastPathAllowsRecall = true,
            OwnerFastPathAllowsFileUploadIntent = true
        };
        QChatIntentDecision decision = new(
            QChatIntentKind.GroupFileUpload,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: "C:\\temp\\report.txt",
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "file upload candidate");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.Recall,
            config);

        Assert.That(result, Is.EqualTo(decision));
    }

    [Test]
    public void Apply_WhenMemoryPurgeActionAllowedByConfig_DoesNotConfirm()
    {
        QChatConfig config = new()
        {
            EnableOwnerTrustedFastPath = true,
            OwnerFastPathAllowsMemoryPurge = true
        };
        QChatIntentDecision decision = new(
            QChatIntentKind.None,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.7,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "memory purge candidate");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.MemoryPurge,
            config);

        Assert.That(result, Is.EqualTo(decision));
    }

    [Test]
    public void Apply_WhenReservedGenericInternetControlActionAllowedByConfig_DoesNotConfirm()
    {
        QChatConfig config = new()
        {
            EnableOwnerTrustedFastPath = true,
            OwnerFastPathAllowsInternetControls = true
        };
        QChatIntentDecision decision = new(
            QChatIntentKind.None,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "internet control candidate");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.InternetControl,
            config);

        Assert.That(result, Is.EqualTo(decision));
    }

    [Test]
    public void Apply_WhenGroupFileUploadHasNoExplicitCommandText_DoesNotConfirm()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.GroupFileUpload,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.2,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: "D:\\tmp\\report.c",
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "file-upload keywords came from metadata or incomplete command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.GroupFileUpload,
            new QChatConfig(),
            "这个 D:\\tmp\\report.c 文件先别管");

        Assert.That(result, Is.EqualTo(decision));
    }

    [Test]
    public void Apply_WhenGroupFileUploadHasExplicitCommandText_MarksDecisionConfirmed()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.GroupFileUpload,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.2,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: "D:\\tmp\\report.c",
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "file-upload keywords came from metadata or incomplete command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.GroupFileUpload,
            new QChatConfig(),
            "把 D:\\tmp\\report.c 上传到群文件");

        Assert.That(result.IsConfirmed, Is.True);
        Assert.That(result.TargetKind, Is.EqualTo(QChatIntentTargetKind.CurrentSession));
    }
}
