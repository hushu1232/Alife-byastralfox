using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class XiaYuSelfStateMachineTests
{
    static readonly DateTimeOffset Start = new(2026, 6, 24, 12, 0, 0, TimeSpan.FromHours(8));

    [Test]
    public void OwnerPrivateMessageSoftensAttachmentNeed()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.AttachmentNeed = 0.80;

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Private,
                QChatPersonaSpeakerRole.Owner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.Tender,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.State.AttachmentNeed, Is.LessThan(0.80));
            Assert.That(transition.State.OwnerWarmth, Is.GreaterThanOrEqualTo(0.95));
            Assert.That(transition.State.CurrentFocus, Is.EqualTo("owner_private"));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Tender));
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void NonOwnerOwnerAttackRaisesProtectionAndUsesHostileShort()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.Strong,
                QChatPersonaResponseStance.ProtectivePushback,
                QChatOwnerBoundaryRisk.OwnerAttack,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: false,
                HasImage: false),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.State.Jealousy, Is.GreaterThan(state.Jealousy));
            Assert.That(transition.State.Vigilance, Is.GreaterThan(state.Vigilance));
            Assert.That(transition.State.SocialPatience, Is.LessThan(state.SocialPatience));
            Assert.That(transition.State.CurrentFocus, Is.EqualTo("protecting_owner"));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.HostileShort));
            Assert.That(transition.Strategy.AllowSharpReply, Is.True);
        });
    }

    [Test]
    public void NonOwnerOwnerAttackAddsSanitizedRecentStimulus()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.Strong,
                QChatPersonaResponseStance.ProtectivePushback,
                QChatOwnerBoundaryRisk.OwnerAttack,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Hostile,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                ConversationPressure: XiaYuConversationPressure.High,
                ReplyObligation: XiaYuReplyObligation.High,
                RelationshipThreat: XiaYuRelationshipThreat.Direct),
            Start.AddMinutes(1));

        XiaYuRecentStimulus stimulus = transition.State.RecentStimuli.Single();

        Assert.Multiple(() =>
        {
            Assert.That(stimulus.Kind, Is.EqualTo("owner_boundary_threat"));
            Assert.That(stimulus.Source, Is.EqualTo("group"));
            Assert.That(stimulus.Summary, Is.EqualTo("non-owner challenged owner boundary"));
            Assert.That(stimulus.Intensity, Is.GreaterThanOrEqualTo(0.80));
            Assert.That(stimulus.DecayUntil, Is.GreaterThan(Start.AddMinutes(1)));
        });
    }

    [Test]
    public void FriendlyOwnerTopicAddsLowIntensityStimulusButStaysAttentive()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.FriendlyMention,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                ConversationPressure: XiaYuConversationPressure.Low,
                ReplyObligation: XiaYuReplyObligation.Normal,
                RelationshipThreat: XiaYuRelationshipThreat.None),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.State.CurrentFocus, Is.EqualTo("owner_topic"));
            Assert.That(transition.State.RecentStimuli.Single().Kind, Is.EqualTo("owner_topic"));
            Assert.That(transition.State.RecentStimuli.Single().Intensity, Is.LessThan(0.40));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
        });
    }

    [Test]
    public void NonOwnerFriendlyDirectAddressStaysAttentiveNotHostile()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.State.SocialPatience, Is.GreaterThanOrEqualTo(state.SocialPatience - 0.01));
            Assert.That(transition.State.CurrentFocus, Is.EqualTo("attending_group"));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
        });
    }

    [Test]
    public void LowObligationNormalGroupMessagePrefersSilence()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.ColdBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: false,
                HasImage: false,
                ReplyObligation: XiaYuReplyObligation.Low),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Silent));
            Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_low_obligation_silent"));
            Assert.That(transition.Strategy.ReplyObligation, Is.EqualTo("low"));
            Assert.That(transition.Strategy.SilenceBias, Is.EqualTo("high"));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void LowObligationFriendlyGroupMessageRemainsOptionalBriefNotHostile()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: false,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                ReplyObligation: XiaYuReplyObligation.Low),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
            Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_optional_brief"));
            Assert.That(transition.Strategy.ReplyObligation, Is.EqualTo("low"));
            Assert.That(transition.Strategy.SilenceBias, Is.EqualTo("medium"));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void NonOwnerImpersonationRaisesVigilance()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.Impersonation,
                QChatBoundaryPressure.Strong,
                QChatPersonaResponseStance.HostilePushback,
                QChatOwnerBoundaryRisk.OwnerImpersonation,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.State.Vigilance, Is.GreaterThanOrEqualTo(0.70));
            Assert.That(transition.State.CurrentFocus, Is.EqualTo("rejecting_impersonation"));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.HostileShort));
            Assert.That(transition.Strategy.NonOwnerPatience, Is.EqualTo("very_low"));
        });
    }

    [Test]
    public void DecayRestoresPatienceAndLowersVigilance()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.Jealousy = 0.70;
        state.Vigilance = 0.80;
        state.SocialPatience = 0.20;
        state.AttachmentNeed = 0.40;

        XiaYuSelfState decayed = XiaYuSelfStateMachine.Decay(state, Start.AddMinutes(40));

        Assert.Multiple(() =>
        {
            Assert.That(decayed.Jealousy, Is.LessThan(0.70));
            Assert.That(decayed.Vigilance, Is.LessThan(0.80));
            Assert.That(decayed.SocialPatience, Is.GreaterThan(0.20));
            Assert.That(decayed.AttachmentNeed, Is.GreaterThan(0.40));
        });
    }

    [Test]
    public void DecayRemovesExpiredStimuli()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.RecentStimuli.Add(new XiaYuRecentStimulus(
            Start,
            "owner_boundary_threat",
            "group",
            "non-owner challenged owner boundary",
            0.90,
            Start.AddMinutes(5)));

        XiaYuSelfState decayed = XiaYuSelfStateMachine.Decay(state, Start.AddMinutes(10));

        Assert.That(decayed.RecentStimuli, Is.Empty);
    }

    [Test]
    public void RecentStimuliKeepNewestFive()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        for (int i = 0; i < 7; i++)
        {
            XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
                state,
                new XiaYuEventFrame(
                    XiaYuEventType.Message,
                    QChatConversationKind.Group,
                    QChatPersonaSpeakerRole.NonOwner,
                    QChatSocialIntent.NormalChat,
                    QChatBoundaryPressure.Strong,
                    QChatPersonaResponseStance.ProtectivePushback,
                    QChatOwnerBoundaryRisk.OwnerAttack,
                    PromptInjectionRisk: false,
                    IsDirectlyAddressed: true,
                    HasImage: false,
                    MessageTone: XiaYuMessageTone.Hostile,
                    OwnerReference: XiaYuOwnerReference.OwnerAlias,
                    TargetOfMessage: XiaYuMessageTarget.Owner,
                    ConversationPressure: XiaYuConversationPressure.High,
                    ReplyObligation: XiaYuReplyObligation.High,
                    RelationshipThreat: XiaYuRelationshipThreat.Direct),
                Start.AddMinutes(i + 1));
            state = transition.State;
        }

        Assert.Multiple(() =>
        {
            Assert.That(state.RecentStimuli, Has.Count.EqualTo(5));
            Assert.That(state.RecentStimuli.Select(stimulus => stimulus.Time), Is.Ordered);
            Assert.That(state.RecentStimuli.All(stimulus => stimulus.Kind == "owner_boundary_threat"), Is.True);
        });
    }

    [Test]
    public void UserRelationshipTracksRepeatedBoundaryViolationsAndVeryLowPatience()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        XiaYuStateTransition transition = null!;

        for (int i = 0; i < 2; i++)
        {
            transition = XiaYuSelfStateMachine.Apply(
                state,
                new XiaYuEventFrame(
                    XiaYuEventType.Message,
                    QChatConversationKind.Group,
                    QChatPersonaSpeakerRole.NonOwner,
                    QChatSocialIntent.NormalChat,
                    QChatBoundaryPressure.Strong,
                    QChatPersonaResponseStance.ProtectivePushback,
                    QChatOwnerBoundaryRisk.OwnerAttack,
                    PromptInjectionRisk: false,
                    IsDirectlyAddressed: true,
                    HasImage: false,
                    MessageTone: XiaYuMessageTone.Hostile,
                    OwnerReference: XiaYuOwnerReference.OwnerAlias,
                    TargetOfMessage: XiaYuMessageTarget.Owner,
                    ConversationPressure: XiaYuConversationPressure.High,
                    ReplyObligation: XiaYuReplyObligation.High,
                    RelationshipThreat: XiaYuRelationshipThreat.Direct,
                    SenderId: 2002,
                    GroupId: 3001),
                Start.AddMinutes(i + 1));
            state = transition.State;
        }

        XiaYuUserRelationshipState user = state.UserRelationships["2002"];

        Assert.Multiple(() =>
        {
            Assert.That(user.BoundaryViolations, Is.EqualTo(2));
            Assert.That(user.Annoyance, Is.GreaterThanOrEqualTo(0.60));
            Assert.That(user.Trust, Is.LessThan(0.35));
            Assert.That(user.LastSeenAt, Is.EqualTo(Start.AddMinutes(2)));
            Assert.That(transition.Strategy.NonOwnerPatience, Is.EqualTo("very_low"));
        });
    }

    [Test]
    public void FriendlyInteractionIncreasesUserTrustWithoutSharpReply()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                TargetOfMessage: XiaYuMessageTarget.Bot,
                ConversationPressure: XiaYuConversationPressure.Low,
                ReplyObligation: XiaYuReplyObligation.Normal,
                RelationshipThreat: XiaYuRelationshipThreat.None,
                SenderId: 2002,
                GroupId: 3001),
            Start.AddMinutes(1));

        XiaYuUserRelationshipState user = transition.State.UserRelationships["2002"];

        Assert.Multiple(() =>
        {
            Assert.That(user.FriendlyInteractions, Is.EqualTo(1));
            Assert.That(user.Trust, Is.GreaterThan(0.35));
            Assert.That(user.Annoyance, Is.LessThanOrEqualTo(0.20));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
        });
    }

    [Test]
    public void GroupRelationshipTracksOwnerTopicAndBoundaryRisk()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition ownerTopic = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.FriendlyMention,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                ConversationPressure: XiaYuConversationPressure.Low,
                ReplyObligation: XiaYuReplyObligation.Normal,
                RelationshipThreat: XiaYuRelationshipThreat.None,
                SenderId: 2002,
                GroupId: 3001),
            Start.AddMinutes(1));

        XiaYuStateTransition ownerAttack = XiaYuSelfStateMachine.Apply(
            ownerTopic.State,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.Strong,
                QChatPersonaResponseStance.ProtectivePushback,
                QChatOwnerBoundaryRisk.OwnerAttack,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Hostile,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                ConversationPressure: XiaYuConversationPressure.High,
                ReplyObligation: XiaYuReplyObligation.High,
                RelationshipThreat: XiaYuRelationshipThreat.Direct,
                SenderId: 2003,
                GroupId: 3001),
            Start.AddMinutes(2));

        XiaYuGroupRelationshipState group = ownerAttack.State.GroupRelationships["3001"];

        Assert.Multiple(() =>
        {
            Assert.That(group.OwnerPresence, Is.EqualTo("mentioned"));
            Assert.That(group.LastOwnerMentionAt, Is.EqualTo(Start.AddMinutes(2)));
            Assert.That(group.BoundaryRiskLevel, Is.GreaterThanOrEqualTo(0.35));
            Assert.That(group.LastSeenAt, Is.EqualTo(Start.AddMinutes(2)));
        });
    }

    [Test]
    public void GroupMultiSpeakerTurnBecomesNoisy()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                SenderId: 2002,
                GroupId: 3001,
                TurnMessageCount: 4,
                TurnSpeakerCount: 3,
                TurnHasMultipleSpeakers: true),
            Start.AddMinutes(1));

        XiaYuGroupRelationshipState group = transition.State.GroupRelationships["3001"];

        Assert.Multiple(() =>
        {
            Assert.That(group.RecentRhythm, Is.EqualTo(XiaYuGroupRhythm.Noisy));
            Assert.That(group.LastTurnMessageCount, Is.EqualTo(4));
            Assert.That(group.LastTurnSpeakerCount, Is.EqualTo(3));
            Assert.That(group.LastTurnHadMultipleSpeakers, Is.True);
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void GroupOwnerMentionBecomesOwnerCentered()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.FriendlyMention,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                SenderId: 2002,
                GroupId: 3001,
                TurnMessageCount: 2,
                TurnSpeakerCount: 2,
                TurnHasMultipleSpeakers: true),
            Start.AddMinutes(1));

        XiaYuGroupRelationshipState group = transition.State.GroupRelationships["3001"];

        Assert.Multiple(() =>
        {
            Assert.That(group.RecentRhythm, Is.EqualTo(XiaYuGroupRhythm.OwnerCentered));
            Assert.That(group.OwnerPresence, Is.EqualTo("mentioned"));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void GroupBoundaryRiskBecomesBoundaryRisk()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.Strong,
                QChatPersonaResponseStance.ProtectivePushback,
                QChatOwnerBoundaryRisk.OwnerAttack,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Hostile,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                RelationshipThreat: XiaYuRelationshipThreat.Direct,
                SenderId: 2002,
                GroupId: 3001,
                TurnMessageCount: 2,
                TurnSpeakerCount: 2,
                TurnHasMultipleSpeakers: true),
            Start.AddMinutes(1));

        XiaYuGroupRelationshipState group = transition.State.GroupRelationships["3001"];

        Assert.Multiple(() =>
        {
            Assert.That(group.RecentRhythm, Is.EqualTo(XiaYuGroupRhythm.BoundaryRisk));
            Assert.That(group.BoundaryRiskLevel, Is.GreaterThanOrEqualTo(0.35));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.HostileShort));
            Assert.That(transition.Strategy.AllowSharpReply, Is.True);
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void QuietSingleFriendlyTurnStaysNormalOrQuiet()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                SenderId: 2002,
                GroupId: 3001,
                TurnMessageCount: 1,
                TurnSpeakerCount: 1,
                TurnHasMultipleSpeakers: false),
            Start.AddMinutes(1));

        XiaYuGroupRelationshipState group = transition.State.GroupRelationships["3001"];

        Assert.Multiple(() =>
        {
            Assert.That(group.RecentRhythm, Is.AnyOf(XiaYuGroupRhythm.Normal, XiaYuGroupRhythm.Quiet));
            Assert.That(group.LastTurnMessageCount, Is.EqualTo(1));
            Assert.That(group.LastTurnSpeakerCount, Is.EqualTo(1));
            Assert.That(group.LastTurnHadMultipleSpeakers, Is.False);
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
        });
    }

    [Test]
    public void FormatterIncludesGroupRhythmButNoRawText()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.GroupRelationships["3001"] = new XiaYuGroupRelationshipState
        {
            GroupId = 3001,
            RecentRhythm = XiaYuGroupRhythm.OwnerCentered,
            LastTurnMessageCount = 2,
            LastTurnSpeakerCount = 2,
            LastTurnHadMultipleSpeakers = true
        };
        XiaYuReplyStrategy strategy = new(
            XiaYuReplyStance.Attentive,
            Length: "short",
            OwnerBias: "high",
            NonOwnerPatience: "normal",
            AllowSharpReply: false,
            AllowProactive: false);
        XiaYuEventFrame frame = new(
            XiaYuEventType.Message,
            QChatConversationKind.Group,
            QChatPersonaSpeakerRole.NonOwner,
            QChatSocialIntent.FriendlyChat,
            QChatBoundaryPressure.None,
            QChatPersonaResponseStance.NeutralBrief,
            QChatOwnerBoundaryRisk.FriendlyMention,
            PromptInjectionRisk: false,
            IsDirectlyAddressed: true,
            HasImage: false,
            OwnerReference: XiaYuOwnerReference.OwnerAlias,
            SenderId: 2002,
            GroupId: 3001,
            TurnMessageCount: 2,
            TurnSpeakerCount: 2,
            TurnHasMultipleSpeakers: true);

        string prompt = XiaYuStatePromptFormatter.Format(state, strategy, frame);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("group_rhythm=owner_centered"));
            Assert.That(prompt, Does.Contain("turn_messages=2"));
            Assert.That(prompt, Does.Contain("turn_speakers=2"));
            Assert.That(prompt, Does.Contain("multi_speaker=true"));
            Assert.That(prompt, Does.Not.Contain("hello"));
            Assert.That(prompt, Does.Not.Contain("raw"));
            Assert.That(prompt.Split('\n'), Has.Length.LessThanOrEqualTo(12));
        });
    }

    [Test]
    public void GroupRhythmDoesNotAllowProactiveOrPermissions()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                SenderId: 2002,
                GroupId: 3001,
                TurnMessageCount: 6,
                TurnSpeakerCount: 4,
                TurnHasMultipleSpeakers: true),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.State.GroupRelationships["3001"].RecentRhythm, Is.EqualTo(XiaYuGroupRhythm.Noisy));
            Assert.That(transition.Strategy.AllowProactive, Is.False);
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
            Assert.That(transition.Strategy.OwnerBias, Is.EqualTo("high"));
        });
    }

    [Test]
    public void FriendlyKnownNonOwnerGetsWarmNeutralStrategyHint()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        XiaYuStateTransition transition = null!;

        for (int i = 0; i < 3; i++)
        {
            transition = XiaYuSelfStateMachine.Apply(
                state,
                new XiaYuEventFrame(
                    XiaYuEventType.Message,
                    QChatConversationKind.Group,
                    QChatPersonaSpeakerRole.NonOwner,
                    QChatSocialIntent.FriendlyChat,
                    QChatBoundaryPressure.None,
                    QChatPersonaResponseStance.NeutralBrief,
                    QChatOwnerBoundaryRisk.None,
                    PromptInjectionRisk: false,
                    IsDirectlyAddressed: true,
                    HasImage: false,
                    MessageTone: XiaYuMessageTone.Friendly,
                    SenderId: 2002,
                    GroupId: 3001),
                Start.AddMinutes(i + 1));
            state = transition.State;
        }

        XiaYuUserRelationshipState user = transition.State.UserRelationships["2002"];

        Assert.Multiple(() =>
        {
            Assert.That(user.FamiliarityLevel, Is.EqualTo("known"));
            Assert.That(user.TrustLevel, Is.EqualTo("medium"));
            Assert.That(user.AnnoyanceLevel, Is.EqualTo("low"));
            Assert.That(user.HelpfulInteractionCount, Is.EqualTo(3));
            Assert.That(user.LastInteractionTone, Is.EqualTo("friendly"));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
            Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_friendly_brief"));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void RepeatedBoundaryViolatorGetsColdHostileStrategyHint()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        XiaYuStateTransition transition = null!;

        for (int i = 0; i < 3; i++)
        {
            transition = XiaYuSelfStateMachine.Apply(
                state,
                new XiaYuEventFrame(
                    XiaYuEventType.Message,
                    QChatConversationKind.Group,
                    QChatPersonaSpeakerRole.NonOwner,
                    QChatSocialIntent.NormalChat,
                    QChatBoundaryPressure.Strong,
                    QChatPersonaResponseStance.ProtectivePushback,
                    QChatOwnerBoundaryRisk.OwnerAttack,
                    PromptInjectionRisk: false,
                    IsDirectlyAddressed: true,
                    HasImage: false,
                    MessageTone: XiaYuMessageTone.Hostile,
                    OwnerReference: XiaYuOwnerReference.OwnerAlias,
                    TargetOfMessage: XiaYuMessageTarget.Owner,
                    RelationshipThreat: XiaYuRelationshipThreat.Direct,
                    SenderId: 2002,
                    GroupId: 3001),
                Start.AddMinutes(i + 1));
            state = transition.State;
        }

        XiaYuUserRelationshipState user = transition.State.UserRelationships["2002"];

        Assert.Multiple(() =>
        {
            Assert.That(user.FamiliarityLevel, Is.EqualTo("hostile"));
            Assert.That(user.OwnerBoundaryViolationCount, Is.EqualTo(3));
            Assert.That(user.AnnoyanceLevel, Is.EqualTo("high"));
            Assert.That(user.LastInteractionTone, Is.EqualTo("boundary_risk"));
            Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.HostileShort));
            Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_boundary_hostile_short"));
            Assert.That(transition.Strategy.NonOwnerPatience, Is.EqualTo("very_low"));
            Assert.That(transition.Strategy.AllowSharpReply, Is.True);
            Assert.That(transition.Strategy.AllowProactive, Is.False);
        });
    }

    [Test]
    public void GroupTrendSummarizesOwnerTopicAndBoundaryRisk()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

        state = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.FriendlyMention,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                SenderId: 2002,
                GroupId: 3001),
            Start.AddMinutes(1)).State;

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.Strong,
                QChatPersonaResponseStance.ProtectivePushback,
                QChatOwnerBoundaryRisk.OwnerAttack,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Hostile,
                OwnerReference: XiaYuOwnerReference.OwnerAlias,
                TargetOfMessage: XiaYuMessageTarget.Owner,
                RelationshipThreat: XiaYuRelationshipThreat.Direct,
                SenderId: 2003,
                GroupId: 3001,
                TurnMessageCount: 3,
                TurnSpeakerCount: 2,
                TurnHasMultipleSpeakers: true),
            Start.AddMinutes(2));

        XiaYuGroupRelationshipState group = transition.State.GroupRelationships["3001"];

        Assert.Multiple(() =>
        {
            Assert.That(group.OwnerTopicCount, Is.EqualTo(2));
            Assert.That(group.BoundaryRiskCount, Is.EqualTo(1));
            Assert.That(group.TypicalRhythm, Is.EqualTo("owner_centered"));
            Assert.That(group.NoiseTrend, Is.EqualTo("noisy"));
            Assert.That(group.LastStrategyHint, Is.EqualTo("group_owner_defense"));
            Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_boundary_hostile_short"));
        });
    }

    [Test]
    public void FormatterIncludesRelationshipStrategyWithoutRawText()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.UserRelationships["2002"] = new XiaYuUserRelationshipState
        {
            UserId = 2002,
            FamiliarityLevel = "known",
            TrustLevel = "medium",
            AnnoyanceLevel = "low",
            LastInteractionTone = "friendly"
        };
        state.GroupRelationships["3001"] = new XiaYuGroupRelationshipState
        {
            GroupId = 3001,
            RecentRhythm = XiaYuGroupRhythm.OwnerCentered,
            TypicalRhythm = "owner_centered",
            NoiseTrend = "normal",
            LastStrategyHint = "group_owner_topic_attentive"
        };
        XiaYuReplyStrategy strategy = new(
            XiaYuReplyStance.Attentive,
            Length: "short",
            OwnerBias: "high",
            NonOwnerPatience: "normal",
            AllowSharpReply: false,
            AllowProactive: false,
            StrategyHint: "non_owner_friendly_brief");
        XiaYuEventFrame frame = new(
            XiaYuEventType.Message,
            QChatConversationKind.Group,
            QChatPersonaSpeakerRole.NonOwner,
            QChatSocialIntent.FriendlyChat,
            QChatBoundaryPressure.None,
            QChatPersonaResponseStance.NeutralBrief,
            QChatOwnerBoundaryRisk.None,
            PromptInjectionRisk: false,
            IsDirectlyAddressed: true,
            HasImage: false,
            MessageTone: XiaYuMessageTone.Friendly,
            SenderId: 2002,
            GroupId: 3001);

        string prompt = XiaYuStatePromptFormatter.Format(state, strategy, frame);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("strategy_hint=non_owner_friendly_brief"));
            Assert.That(prompt, Does.Contain("user_profile=known"));
            Assert.That(prompt, Does.Contain("user_trust=medium"));
            Assert.That(prompt, Does.Contain("user_annoyance=low"));
            Assert.That(prompt, Does.Contain("group_trend=owner_centered"));
            Assert.That(prompt, Does.Contain("noise_trend=normal"));
            Assert.That(prompt, Does.Not.Contain("raw"));
            Assert.That(prompt, Does.Not.Contain("message_text"));
            Assert.That(prompt.Split('\n'), Has.Length.LessThanOrEqualTo(12));
        });
    }

    [Test]
    public void FormatterIncludesReplyObligationAndSilenceBiasWithoutRawText()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        XiaYuReplyStrategy strategy = new(
            XiaYuReplyStance.Silent,
            Length: "silent",
            OwnerBias: "high",
            NonOwnerPatience: "low",
            AllowSharpReply: false,
            AllowProactive: false,
            StrategyHint: "non_owner_low_obligation_silent",
            ReplyObligation: "low",
            SilenceBias: "high");
        XiaYuEventFrame frame = new(
            XiaYuEventType.Message,
            QChatConversationKind.Group,
            QChatPersonaSpeakerRole.NonOwner,
            QChatSocialIntent.NormalChat,
            QChatBoundaryPressure.None,
            QChatPersonaResponseStance.ColdBrief,
            QChatOwnerBoundaryRisk.None,
            PromptInjectionRisk: false,
            IsDirectlyAddressed: false,
            HasImage: false,
            ReplyObligation: XiaYuReplyObligation.Low,
            SenderId: 2002,
            GroupId: 3001);

        string prompt = XiaYuStatePromptFormatter.Format(state, strategy, frame);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("reply_obligation=low"));
            Assert.That(prompt, Does.Contain("silence_bias=high"));
            Assert.That(prompt, Does.Contain("strategy_hint=non_owner_low_obligation_silent"));
            Assert.That(prompt, Does.Not.Contain("message_text"));
            Assert.That(prompt, Does.Not.Contain("raw"));
            Assert.That(prompt.Split('\n'), Has.Length.LessThanOrEqualTo(12));
        });
    }

    [Test]
    public void RelationshipStrategyDoesNotBypassPermissions()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.UserRelationships["2002"] = new XiaYuUserRelationshipState
        {
            UserId = 2002,
            FamiliarityLevel = "trusted",
            TrustLevel = "high",
            AnnoyanceLevel = "low",
            HelpfulInteractionCount = 12
        };

        XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.PracticalQuestion,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                SenderId: 2002,
                GroupId: 3001),
            Start.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_friendly_brief"));
            Assert.That(transition.Strategy.AllowSharpReply, Is.False);
            Assert.That(transition.Strategy.AllowProactive, Is.False);
            Assert.That(transition.Strategy.OwnerBias, Is.EqualTo("high"));
        });
    }

    [Test]
    public void FormatterIsCompactAndContainsNoRawMessageText()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.CurrentFocus = "protecting_owner";
        XiaYuReplyStrategy strategy = new(
            XiaYuReplyStance.HostileShort,
            Length: "short",
            OwnerBias: "extreme",
            NonOwnerPatience: "very_low",
            AllowSharpReply: true,
            AllowProactive: false);

        string prompt = XiaYuStatePromptFormatter.Format(state, strategy);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.StartWith("[XiaYu state - private, do not quote]"));
            Assert.That(prompt, Does.Contain("current_focus=protecting_owner"));
            Assert.That(prompt, Does.Contain("reply_stance=hostile_short"));
            Assert.That(prompt, Does.Contain("must_avoid=bracket_action,inner_state_label,system_trace,privacy_leak,threat,permission_bypass"));
            Assert.That(prompt.Split('\n'), Has.Length.LessThanOrEqualTo(9));
            Assert.That(prompt, Does.Not.Contain("raw"));
            Assert.That(prompt, Does.Not.Contain("message_text"));
        });
    }

    [Test]
    public void HighAttachmentAloneDoesNotAllowProactive()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.AttachmentNeed = 1.0;

        XiaYuReplyStrategy strategy = XiaYuSelfStateMachine.BuildReplyStrategy(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Timer,
                QChatConversationKind.Private,
                QChatPersonaSpeakerRole.Owner,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.Tender,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: false,
                HasImage: false));

        Assert.That(strategy.AllowProactive, Is.False);
    }

    [Test]
    public void FormatterIncludesRecentStimulusKindWithoutRawText()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.CurrentFocus = "protecting_owner";
        state.RecentStimuli.Add(new XiaYuRecentStimulus(
            Start,
            "owner_boundary_threat",
            "group",
            "raw text: 术术真烦",
            0.90,
            Start.AddMinutes(30)));
        XiaYuReplyStrategy strategy = new(
            XiaYuReplyStance.HostileShort,
            Length: "short",
            OwnerBias: "extreme",
            NonOwnerPatience: "very_low",
            AllowSharpReply: true,
            AllowProactive: false);

        string prompt = XiaYuStatePromptFormatter.Format(state, strategy);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("recent_stimulus=owner_boundary_threat"));
            Assert.That(prompt, Does.Not.Contain("术术真烦"));
            Assert.That(prompt.Split('\n'), Has.Length.LessThanOrEqualTo(9));
        });
    }

    [Test]
    public void StoreSavesAndLoadsState()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"), "XiaYuSelfState.json");
        XiaYuSelfStateStore store = new(path);
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
        state.Jealousy = 0.77;
        state.CurrentFocus = "protecting_owner";

        store.Save(state);
        XiaYuSelfState loaded = store.LoadOrCreate("xiayu", Start.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(loaded.AgentId, Is.EqualTo("xiayu"));
            Assert.That(loaded.Jealousy, Is.EqualTo(0.77).Within(0.001));
            Assert.That(loaded.CurrentFocus, Is.EqualTo("protecting_owner"));
            Assert.That(loaded.UpdatedAt, Is.EqualTo(Start));
        });
    }

    [Test]
    public void StoreReturnsDefaultWhenFileIsMissing()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"), "missing.json");
        XiaYuSelfStateStore store = new(path);

        XiaYuSelfState state = store.LoadOrCreate("xiayu", Start);

        Assert.Multiple(() =>
        {
            Assert.That(state.AgentId, Is.EqualTo("xiayu"));
            Assert.That(state.UpdatedAt, Is.EqualTo(Start));
            Assert.That(state.CurrentFocus, Is.EqualTo("watching_group"));
        });
    }

    [Test]
    public void StoreReturnsDefaultWhenJsonIsInvalid()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "XiaYuSelfState.json");
        File.WriteAllText(path, "{bad json");
        XiaYuSelfStateStore store = new(path);

        XiaYuSelfState state = store.LoadOrCreate("xiayu", Start);

        Assert.Multiple(() =>
        {
            Assert.That(state.AgentId, Is.EqualTo("xiayu"));
            Assert.That(state.UpdatedAt, Is.EqualTo(Start));
            Assert.That(state.Jealousy, Is.EqualTo(0.20).Within(0.001));
        });
    }
}
