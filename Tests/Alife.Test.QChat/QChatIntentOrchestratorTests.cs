using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatIntentOrchestratorTests
{
    [Test]
    public void OwnerConfirmedRecallExecutesRecall()
    {
        QChatIntentDecision intent = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: true,
            Confidence: 0.9,
            TargetKind: QChatIntentTargetKind.RecentBotMessage,
            TargetText: null,
            TargetId: 12345,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "confirmed recall command");

        QChatIntentAction action = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu",
            BotId: 2905391496,
            OwnerId: 3045846738,
            CurrentGroupId: 925402131));

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(QChatIntentActionKind.RecallMessage));
            Assert.That(action.Allowed, Is.True);
            Assert.That(action.Capability, Is.EqualTo(QChatCapability.RecallMessage));
            Assert.That(action.Reason, Is.EqualTo("confirmed recall command"));
        });
    }

    [Test]
    public void MetaRecallDoesNotExecute()
    {
        QChatIntentDecision intent = new(
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

        QChatIntentAction action = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu"));

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(QChatIntentActionKind.None));
            Assert.That(action.Allowed, Is.False);
            Assert.That(action.Capability, Is.EqualTo(QChatCapability.RecallMessage));
            Assert.That(action.Reason, Is.EqualTo("intent_not_confirmed"));
        });
    }

    [Test]
    public void NonOwnerConfirmedRecallIsDenied()
    {
        QChatIntentDecision intent = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: true,
            Confidence: 0.9,
            TargetKind: QChatIntentTargetKind.RecentBotMessage,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "confirmed recall command");

        QChatIntentAction action = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "xiayu"));

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(QChatIntentActionKind.None));
            Assert.That(action.Allowed, Is.False);
            Assert.That(action.Capability, Is.EqualTo(QChatCapability.RecallMessage));
            Assert.That(action.Reason, Is.EqualTo("owner_required"));
        });
    }

    [Test]
    public void ConfirmedFileUploadRequiresOwnerPermission()
    {
        QChatIntentDecision intent = new(
            QChatIntentKind.GroupFileUpload,
            IsCandidate: true,
            IsConfirmed: true,
            Confidence: 0.86,
            TargetKind: QChatIntentTargetKind.CurrentSession,
            TargetText: null,
            TargetId: null,
            FilePath: @"D:\tmp\hello_world.c",
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "confirmed explicit file upload request");

        QChatIntentAction ownerAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu",
            CurrentGroupId: 925402131));
        QChatIntentAction memberAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "xiayu",
            CurrentGroupId: 925402131));
        QChatIntentAction mixuAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "mixu",
            CurrentGroupId: 925402131));

        Assert.Multiple(() =>
        {
            Assert.That(ownerAction.Kind, Is.EqualTo(QChatIntentActionKind.UploadGroupFile));
            Assert.That(ownerAction.Allowed, Is.True);
            Assert.That(ownerAction.Capability, Is.EqualTo(QChatCapability.GroupFileUpload));
            Assert.That(ownerAction.FilePath, Is.EqualTo(@"D:\tmp\hello_world.c"));
            Assert.That(memberAction.Kind, Is.EqualTo(QChatIntentActionKind.None));
            Assert.That(memberAction.Allowed, Is.False);
            Assert.That(memberAction.Reason, Is.EqualTo("owner_required"));
            Assert.That(mixuAction.Kind, Is.EqualTo(QChatIntentActionKind.None));
            Assert.That(mixuAction.Allowed, Is.False);
            Assert.That(mixuAction.Reason, Is.EqualTo("agent_not_allowed"));
        });
    }

    [Test]
    public void QuietModeCanBeChangedOnlyByOwnerOrTrustedWakeUser()
    {
        QChatIntentDecision intent = new(
            QChatIntentKind.QuietMode,
            IsCandidate: true,
            IsConfirmed: true,
            Confidence: 0.88,
            TargetKind: QChatIntentTargetKind.CurrentSession,
            TargetText: "wake",
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "confirmed quiet-mode wake request");

        QChatIntentAction ownerAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu"));
        QChatIntentAction trustedAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "xiayu",
            IsTrustedWakeUser: true));
        QChatIntentAction memberAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "xiayu"));

        Assert.Multiple(() =>
        {
            Assert.That(ownerAction.Kind, Is.EqualTo(QChatIntentActionKind.SetQuietMode));
            Assert.That(ownerAction.Allowed, Is.True);
            Assert.That(ownerAction.TargetText, Is.EqualTo("wake"));
            Assert.That(trustedAction.Kind, Is.EqualTo(QChatIntentActionKind.SetQuietMode));
            Assert.That(trustedAction.Allowed, Is.True);
            Assert.That(memberAction.Kind, Is.EqualTo(QChatIntentActionKind.None));
            Assert.That(memberAction.Allowed, Is.False);
            Assert.That(memberAction.Reason, Is.EqualTo("owner_required"));
        });
    }

    [Test]
    public void ConfirmedAllowlistUpdateRequiresOwner()
    {
        QChatIntentDecision intent = new(
            QChatIntentKind.AllowlistUpdate,
            IsCandidate: true,
            IsConfirmed: true,
            Confidence: 0.88,
            TargetKind: QChatIntentTargetKind.ExplicitGroup,
            TargetText: "group:add",
            TargetId: 925402131,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "confirmed group allowlist update");

        QChatIntentAction ownerAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu"));
        QChatIntentAction memberAction = QChatIntentOrchestrator.Decide(new QChatIntentOrchestrationContext(
            Intent: intent,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "xiayu"));

        Assert.Multiple(() =>
        {
            Assert.That(ownerAction.Kind, Is.EqualTo(QChatIntentActionKind.UpdateAllowlist));
            Assert.That(ownerAction.Allowed, Is.True);
            Assert.That(ownerAction.TargetId, Is.EqualTo(925402131));
            Assert.That(memberAction.Kind, Is.EqualTo(QChatIntentActionKind.None));
            Assert.That(memberAction.Allowed, Is.False);
            Assert.That(memberAction.Reason, Is.EqualTo("owner_required"));
        });
    }
}
