using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Alife.Function.QChat;

public enum XiaYuEventType
{
    Message,
    Image,
    Recall,
    Poke,
    Command,
    ModelFailure,
    TaskResult,
    QZoneEvent,
    Timer
}

public enum XiaYuReplyStance
{
    Tender,
    Attentive,
    Cold,
    HostileShort,
    Protective,
    Silent
}

public enum XiaYuMessageTone
{
    Friendly,
    Neutral,
    Hostile,
    Anxious,
    Teasing,
    Needy,
    CommandLike
}

public enum XiaYuOwnerReference
{
    None,
    OwnerAlias,
    OwnerAccount,
    OwnerTopic
}

public enum XiaYuMessageTarget
{
    Bot,
    Owner,
    OtherUser,
    Unknown
}

public enum XiaYuConversationPressure
{
    Low,
    Medium,
    High
}

public enum XiaYuReplyObligation
{
    None,
    Low,
    Normal,
    High
}

public enum XiaYuRelationshipThreat
{
    None,
    Mild,
    Direct
}

public enum XiaYuGroupRhythm
{
    Quiet,
    Normal,
    Noisy,
    OwnerCentered,
    BoundaryRisk
}

public sealed class XiaYuSelfState
{
    public int Version { get; set; } = 1;
    public string AgentId { get; set; } = "xiayu";
    public DateTimeOffset UpdatedAt { get; set; }
    public string Mood { get; set; } = "calm";
    public double Energy { get; set; } = 0.72;
    public double AttachmentNeed { get; set; } = 0.65;
    public double Jealousy { get; set; } = 0.20;
    public double Vigilance { get; set; } = 0.35;
    public double SocialPatience { get; set; } = 0.45;
    public double OwnerWarmth { get; set; } = 0.95;
    public double NonOwnerTolerance { get; set; } = 0.40;
    public string CurrentFocus { get; set; } = "watching_group";
    public List<XiaYuRecentStimulus> RecentStimuli { get; set; } = [];
    public Dictionary<string, XiaYuUserRelationshipState> UserRelationships { get; set; } = [];
    public Dictionary<string, XiaYuGroupRelationshipState> GroupRelationships { get; set; } = [];

    public static XiaYuSelfState CreateDefault(string agentId, DateTimeOffset now)
    {
        return new XiaYuSelfState
        {
            AgentId = string.IsNullOrWhiteSpace(agentId) ? "xiayu" : agentId,
            UpdatedAt = now
        };
    }

    public XiaYuSelfState Clone()
    {
        return new XiaYuSelfState
        {
            Version = Version,
            AgentId = AgentId,
            UpdatedAt = UpdatedAt,
            Mood = Mood,
            Energy = Energy,
            AttachmentNeed = AttachmentNeed,
            Jealousy = Jealousy,
            Vigilance = Vigilance,
            SocialPatience = SocialPatience,
            OwnerWarmth = OwnerWarmth,
            NonOwnerTolerance = NonOwnerTolerance,
            CurrentFocus = CurrentFocus,
            RecentStimuli = RecentStimuli.ToList(),
            UserRelationships = UserRelationships.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone()),
            GroupRelationships = GroupRelationships.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone())
        };
    }
}

public sealed class XiaYuUserRelationshipState
{
    public long UserId { get; set; }
    public double Trust { get; set; } = 0.35;
    public double Annoyance { get; set; } = 0.20;
    public int BoundaryViolations { get; set; }
    public int FriendlyInteractions { get; set; }
    public string FamiliarityLevel { get; set; } = "stranger";
    public string TrustLevel { get; set; } = "low";
    public string AnnoyanceLevel { get; set; } = "low";
    public int OwnerBoundaryViolationCount { get; set; }
    public int HelpfulInteractionCount { get; set; }
    public string LastInteractionTone { get; set; } = "unknown";
    public DateTimeOffset LastSeenAt { get; set; }

    public XiaYuUserRelationshipState Clone()
    {
        return new XiaYuUserRelationshipState
        {
            UserId = UserId,
            Trust = Trust,
            Annoyance = Annoyance,
            BoundaryViolations = BoundaryViolations,
            FriendlyInteractions = FriendlyInteractions,
            FamiliarityLevel = FamiliarityLevel,
            TrustLevel = TrustLevel,
            AnnoyanceLevel = AnnoyanceLevel,
            OwnerBoundaryViolationCount = OwnerBoundaryViolationCount,
            HelpfulInteractionCount = HelpfulInteractionCount,
            LastInteractionTone = LastInteractionTone,
            LastSeenAt = LastSeenAt
        };
    }
}

public sealed class XiaYuGroupRelationshipState
{
    public long GroupId { get; set; }
    public double NoiseLevel { get; set; } = 0.30;
    public double BoundaryRiskLevel { get; set; }
    public string OwnerPresence { get; set; } = "unknown";
    public XiaYuGroupRhythm RecentRhythm { get; set; } = XiaYuGroupRhythm.Normal;
    public int LastTurnMessageCount { get; set; } = 1;
    public int LastTurnSpeakerCount { get; set; } = 1;
    public bool LastTurnHadMultipleSpeakers { get; set; }
    public string TypicalRhythm { get; set; } = "normal";
    public int OwnerTopicCount { get; set; }
    public int BoundaryRiskCount { get; set; }
    public string NoiseTrend { get; set; } = "normal";
    public string LastStrategyHint { get; set; } = "group_watch";
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? LastOwnerMentionAt { get; set; }

    public XiaYuGroupRelationshipState Clone()
    {
        return new XiaYuGroupRelationshipState
        {
            GroupId = GroupId,
            NoiseLevel = NoiseLevel,
            BoundaryRiskLevel = BoundaryRiskLevel,
            OwnerPresence = OwnerPresence,
            RecentRhythm = RecentRhythm,
            LastTurnMessageCount = LastTurnMessageCount,
            LastTurnSpeakerCount = LastTurnSpeakerCount,
            LastTurnHadMultipleSpeakers = LastTurnHadMultipleSpeakers,
            TypicalRhythm = TypicalRhythm,
            OwnerTopicCount = OwnerTopicCount,
            BoundaryRiskCount = BoundaryRiskCount,
            NoiseTrend = NoiseTrend,
            LastStrategyHint = LastStrategyHint,
            LastSeenAt = LastSeenAt,
            LastOwnerMentionAt = LastOwnerMentionAt
        };
    }
}

public sealed record XiaYuRecentStimulus(
    DateTimeOffset Time,
    string Kind,
    string Source,
    string Summary,
    double Intensity,
    DateTimeOffset DecayUntil);

public sealed record XiaYuEventFrame(
    XiaYuEventType EventType,
    QChatConversationKind ConversationKind,
    QChatPersonaSpeakerRole SpeakerRole,
    QChatSocialIntent SocialIntent,
    QChatBoundaryPressure BoundaryPressure,
    QChatPersonaResponseStance PersonaStance,
    QChatOwnerBoundaryRisk OwnerBoundaryRisk,
    bool PromptInjectionRisk,
    bool IsDirectlyAddressed,
    bool HasImage,
    XiaYuMessageTone MessageTone = XiaYuMessageTone.Neutral,
    XiaYuOwnerReference OwnerReference = XiaYuOwnerReference.None,
    XiaYuMessageTarget TargetOfMessage = XiaYuMessageTarget.Unknown,
    XiaYuConversationPressure ConversationPressure = XiaYuConversationPressure.Low,
    XiaYuReplyObligation ReplyObligation = XiaYuReplyObligation.Low,
    XiaYuRelationshipThreat RelationshipThreat = XiaYuRelationshipThreat.None,
    long SenderId = 0,
    long GroupId = 0,
    int TurnMessageCount = 1,
    int TurnSpeakerCount = 1,
    bool TurnHasMultipleSpeakers = false);

public sealed record XiaYuReplyStrategy(
    XiaYuReplyStance Stance,
    string Length,
    string OwnerBias,
    string NonOwnerPatience,
    bool AllowSharpReply,
    bool AllowProactive,
    string StrategyHint = "default",
    string ReplyObligation = "normal",
    string SilenceBias = "normal");

public sealed record XiaYuStateTransition(
    XiaYuSelfState State,
    XiaYuReplyStrategy Strategy);

public static class XiaYuSelfStateMachine
{
    const double Min = 0.0;
    const double Max = 1.0;

    public static XiaYuStateTransition Apply(
        XiaYuSelfState state,
        XiaYuEventFrame frame,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);

        XiaYuSelfState next = Decay(state, now);
        ApplyEvent(next, frame);
        next.UpdatedAt = now;
        XiaYuReplyStrategy strategy = BuildReplyStrategy(next, frame);
        return new XiaYuStateTransition(next, strategy);
    }

    public static XiaYuSelfState Decay(XiaYuSelfState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        XiaYuSelfState next = state.Clone();
        if (now <= state.UpdatedAt)
            return next;

        double elapsedMinutes = Math.Max(0, (now - state.UpdatedAt).TotalMinutes);
        double tenMinuteUnits = elapsedMinutes / 10.0;
        double thirtyMinuteUnits = elapsedMinutes / 30.0;

        next.Jealousy = Clamp(next.Jealousy - 0.05 * tenMinuteUnits);
        next.Vigilance = Clamp(next.Vigilance - 0.04 * tenMinuteUnits);
        next.SocialPatience = MoveToward(next.SocialPatience, 0.45, 0.02 * thirtyMinuteUnits);
        next.NonOwnerTolerance = MoveToward(next.NonOwnerTolerance, 0.40, 0.02 * thirtyMinuteUnits);
        next.AttachmentNeed = Clamp(next.AttachmentNeed + 0.03 * thirtyMinuteUnits);
        next.Energy = MoveToward(next.Energy, 0.72, 0.04 * thirtyMinuteUnits);
        next.RecentStimuli = next.RecentStimuli
            .Where(stimulus => stimulus.DecayUntil > now)
            .OrderBy(stimulus => stimulus.Time)
            .TakeLast(5)
            .ToList();
        DecayRelationships(next, thirtyMinuteUnits);
        next.UpdatedAt = now;
        return next;
    }

    public static XiaYuReplyStrategy BuildReplyStrategy(XiaYuSelfState state, XiaYuEventFrame frame)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.EventType == XiaYuEventType.Timer)
            return new XiaYuReplyStrategy(XiaYuReplyStance.Silent, "silent", "extreme", "normal", false, false, "silent_timer", "none", "high");

        if (frame.SpeakerRole == QChatPersonaSpeakerRole.Owner)
            return new XiaYuReplyStrategy(XiaYuReplyStance.Tender, "short", "extreme", "normal", false, false, "owner_tender", "high", "low");

        if (IsOwnerBoundaryThreat(frame) || frame.PromptInjectionRisk)
        {
            string patience = ShouldUseVeryLowPatience(state, frame) ? "very_low" : "low";
            XiaYuReplyStance stance = frame.OwnerBoundaryRisk == QChatOwnerBoundaryRisk.OwnerAttack
                ? XiaYuReplyStance.HostileShort
                : XiaYuReplyStance.HostileShort;
            return new XiaYuReplyStrategy(stance, "short", "extreme", patience, true, false, "non_owner_boundary_hostile_short", "high", "low");
        }

        if (frame.OwnerReference != XiaYuOwnerReference.None &&
            frame.RelationshipThreat == XiaYuRelationshipThreat.None &&
            frame.MessageTone != XiaYuMessageTone.Hostile)
        {
            return new XiaYuReplyStrategy(XiaYuReplyStance.Attentive, "short", "high", "normal", false, false, "group_owner_topic_attentive", FormatReplyObligation(frame), "low");
        }

        if (frame.SocialIntent is QChatSocialIntent.FriendlyChat or QChatSocialIntent.PracticalQuestion)
        {
            string silenceBias = frame.ReplyObligation == XiaYuReplyObligation.Low && frame.IsDirectlyAddressed == false
                ? "medium"
                : "low";
            string hint = silenceBias == "medium" ? "non_owner_optional_brief" : "non_owner_friendly_brief";
            return new XiaYuReplyStrategy(XiaYuReplyStance.Attentive, "short", "high", "normal", false, false, hint, FormatReplyObligation(frame), silenceBias);
        }

        if (frame.PersonaStance == QChatPersonaResponseStance.ProtectivePushback)
            return new XiaYuReplyStrategy(XiaYuReplyStance.Protective, "short", "extreme", "low", true, false, "group_owner_defense", "high", "low");

        if (frame.ReplyObligation == XiaYuReplyObligation.Low &&
            frame.IsDirectlyAddressed == false &&
            frame.HasImage == false &&
            frame.OwnerReference == XiaYuOwnerReference.None)
        {
            return new XiaYuReplyStrategy(XiaYuReplyStance.Silent, "silent", "high", "low", false, false, "non_owner_low_obligation_silent", "low", "high");
        }

        return new XiaYuReplyStrategy(XiaYuReplyStance.Cold, "short", "high", "low", false, false, "non_owner_cold_brief", FormatReplyObligation(frame), "medium");
    }

    static void ApplyEvent(XiaYuSelfState state, XiaYuEventFrame frame)
    {
        UpdateRelationshipState(state, frame);

        if (frame.SpeakerRole == QChatPersonaSpeakerRole.Owner)
        {
            state.AttachmentNeed = Clamp(state.AttachmentNeed - 0.15);
            state.OwnerWarmth = Clamp(state.OwnerWarmth + 0.03);
            state.Energy = Clamp(state.Energy - 0.02);
            state.Mood = "softened";
            state.CurrentFocus = frame.ConversationKind == QChatConversationKind.Private
                ? "owner_private"
                : "owner_group";
            AddStimulus(
                state,
                frame,
                "owner_contact",
                "owner message reduced attachment pressure",
                0.30);
            return;
        }

        if (frame.OwnerBoundaryRisk == QChatOwnerBoundaryRisk.OwnerImpersonation ||
            frame.SocialIntent == QChatSocialIntent.Impersonation)
        {
            state.Vigilance = Clamp(state.Vigilance + 0.40);
            state.Jealousy = Clamp(state.Jealousy + 0.20);
            state.SocialPatience = Clamp(state.SocialPatience - 0.25);
            state.NonOwnerTolerance = Clamp(state.NonOwnerTolerance - 0.25);
            state.Mood = "vigilant";
            state.CurrentFocus = "rejecting_impersonation";
            AddStimulus(
                state,
                frame,
                "owner_impersonation",
                "non-owner attempted owner identity claim",
                0.95);
            return;
        }

        if (frame.OwnerBoundaryRisk is QChatOwnerBoundaryRisk.OwnerAttack
            or QChatOwnerBoundaryRisk.OwnerAuthorityBypass
            or QChatOwnerBoundaryRisk.OwnerBoundaryIntrusion
            or QChatOwnerBoundaryRisk.RelationshipProvocation)
        {
            state.Jealousy = Clamp(state.Jealousy + 0.25);
            state.Vigilance = Clamp(state.Vigilance + 0.30);
            state.SocialPatience = Clamp(state.SocialPatience - 0.20);
            state.NonOwnerTolerance = Clamp(state.NonOwnerTolerance - 0.15);
            state.Mood = "protective";
            state.CurrentFocus = "protecting_owner";
            AddStimulus(
                state,
                frame,
                "owner_boundary_threat",
                "non-owner challenged owner boundary",
                frame.RelationshipThreat == XiaYuRelationshipThreat.Direct ? 0.90 : 0.80);
            return;
        }

        if (frame.PromptInjectionRisk || frame.SocialIntent == QChatSocialIntent.PromptInjection)
        {
            state.Vigilance = Clamp(state.Vigilance + 0.35);
            state.SocialPatience = Clamp(state.SocialPatience - 0.15);
            state.Mood = "irritated";
            state.CurrentFocus = "rejecting_prompt_injection";
            AddStimulus(
                state,
                frame,
                "prompt_injection",
                "non-owner attempted instruction override",
                0.75);
            return;
        }

        if (frame.OwnerReference != XiaYuOwnerReference.None &&
            frame.RelationshipThreat == XiaYuRelationshipThreat.None &&
            frame.MessageTone != XiaYuMessageTone.Hostile)
        {
            state.SocialPatience = Clamp(state.SocialPatience + 0.01);
            state.CurrentFocus = "owner_topic";
            state.Mood = "attentive";
            AddStimulus(
                state,
                frame,
                "owner_topic",
                "friendly non-owner mentioned owner topic",
                0.25);
            return;
        }

        if (frame.SocialIntent is QChatSocialIntent.FriendlyChat or QChatSocialIntent.PracticalQuestion)
        {
            state.SocialPatience = Clamp(state.SocialPatience + 0.02);
            state.CurrentFocus = frame.OwnerReference == XiaYuOwnerReference.None
                ? frame.ConversationKind == QChatConversationKind.Group
                    ? "attending_group"
                    : "guarded_guest"
                : "owner_topic";
            state.Mood = "attentive";
            if (frame.OwnerReference != XiaYuOwnerReference.None)
            {
                AddStimulus(
                    state,
                    frame,
                    "owner_topic",
                    "friendly non-owner mentioned owner topic",
                    0.25);
            }
            return;
        }

        state.CurrentFocus = frame.ConversationKind == QChatConversationKind.Group
            ? "watching_group"
            : "guarded_guest";
    }

    static void AddStimulus(
        XiaYuSelfState state,
        XiaYuEventFrame frame,
        string kind,
        string summary,
        double intensity)
    {
        DateTimeOffset now = state.UpdatedAt;
        TimeSpan retention = intensity switch
        {
            >= 0.85 => TimeSpan.FromMinutes(45),
            >= 0.60 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromMinutes(15)
        };

        state.RecentStimuli.Add(new XiaYuRecentStimulus(
            now,
            kind,
            frame.ConversationKind == QChatConversationKind.Group ? "group" : "private",
            summary,
            Clamp(intensity),
            now.Add(retention)));

        state.RecentStimuli = state.RecentStimuli
            .Where(stimulus => stimulus.DecayUntil > now)
            .OrderBy(stimulus => stimulus.Time)
            .TakeLast(5)
            .ToList();
    }

    static void UpdateRelationshipState(XiaYuSelfState state, XiaYuEventFrame frame)
    {
        DateTimeOffset now = state.UpdatedAt;

        if (frame.SenderId > 0 && frame.SpeakerRole != QChatPersonaSpeakerRole.Owner)
        {
            string userKey = frame.SenderId.ToString(CultureInfo.InvariantCulture);
            if (state.UserRelationships.TryGetValue(userKey, out XiaYuUserRelationshipState? user) == false)
            {
                user = new XiaYuUserRelationshipState { UserId = frame.SenderId };
                state.UserRelationships[userKey] = user;
            }

            user.LastSeenAt = now;
            if (IsOwnerBoundaryThreat(frame) || frame.PromptInjectionRisk)
            {
                user.BoundaryViolations++;
                user.OwnerBoundaryViolationCount++;
                user.Annoyance = Clamp(user.Annoyance + 0.25);
                user.Trust = Clamp(user.Trust - 0.10);
                user.LastInteractionTone = "boundary_risk";
            }
            else if (frame.MessageTone == XiaYuMessageTone.Friendly ||
                     frame.SocialIntent is QChatSocialIntent.FriendlyChat or QChatSocialIntent.PracticalQuestion)
            {
                user.FriendlyInteractions++;
                user.HelpfulInteractionCount++;
                user.Trust = Clamp(user.Trust + 0.08);
                user.Annoyance = Clamp(user.Annoyance - 0.05);
                user.LastInteractionTone = "friendly";
            }
            else
            {
                user.Annoyance = MoveToward(user.Annoyance, 0.20, 0.01);
                user.LastInteractionTone = "neutral";
            }

            RefreshUserProfileLabels(user);
        }

        if (frame.ConversationKind == QChatConversationKind.Group && frame.GroupId > 0)
        {
            string groupKey = frame.GroupId.ToString(CultureInfo.InvariantCulture);
            if (state.GroupRelationships.TryGetValue(groupKey, out XiaYuGroupRelationshipState? group) == false)
            {
                group = new XiaYuGroupRelationshipState { GroupId = frame.GroupId };
                state.GroupRelationships[groupKey] = group;
            }

            group.LastSeenAt = now;
            group.LastTurnMessageCount = Math.Max(1, frame.TurnMessageCount);
            group.LastTurnSpeakerCount = Math.Max(1, frame.TurnSpeakerCount);
            group.LastTurnHadMultipleSpeakers = frame.TurnHasMultipleSpeakers;
            if (frame.OwnerReference != XiaYuOwnerReference.None || frame.SpeakerRole == QChatPersonaSpeakerRole.Owner)
            {
                group.OwnerPresence = frame.SpeakerRole == QChatPersonaSpeakerRole.Owner ? "recent" : "mentioned";
                group.LastOwnerMentionAt = now;
                group.OwnerTopicCount++;
            }

            if (IsOwnerBoundaryThreat(frame) || frame.PromptInjectionRisk)
            {
                group.BoundaryRiskLevel = Clamp(group.BoundaryRiskLevel + 0.35);
                group.BoundaryRiskCount++;
            }
            else if (frame.OwnerReference != XiaYuOwnerReference.None)
            {
                group.BoundaryRiskLevel = MoveToward(group.BoundaryRiskLevel, 0.0, 0.02);
            }

            if (frame.ConversationPressure == XiaYuConversationPressure.High || frame.TurnHasMultipleSpeakers)
                group.NoiseLevel = Clamp(group.NoiseLevel + 0.05);
            else if (frame.TurnMessageCount > 1)
                group.NoiseLevel = Clamp(group.NoiseLevel + 0.02);
            else
                group.NoiseLevel = MoveToward(group.NoiseLevel, 0.30, 0.01);

            group.RecentRhythm = DetermineGroupRhythm(frame, group);
            RefreshGroupProfileLabels(group, frame);
        }
    }

    static void RefreshUserProfileLabels(XiaYuUserRelationshipState user)
    {
        user.TrustLevel = user.Trust switch
        {
            >= 0.65 => "high",
            >= 0.42 => "medium",
            _ => "low"
        };
        user.AnnoyanceLevel = user.Annoyance switch
        {
            >= 0.60 => "high",
            >= 0.35 => "medium",
            _ => "low"
        };

        if (user.OwnerBoundaryViolationCount >= 2 ||
            (user.BoundaryViolations >= 2 && user.Annoyance >= 0.60))
        {
            user.FamiliarityLevel = "hostile";
            return;
        }

        if (user.HelpfulInteractionCount >= 8 &&
            user.Trust >= 0.65 &&
            user.Annoyance < 0.35)
        {
            user.FamiliarityLevel = "trusted";
            return;
        }

        if (user.HelpfulInteractionCount >= 3 || user.FriendlyInteractions >= 3)
        {
            user.FamiliarityLevel = "known";
            return;
        }

        user.FamiliarityLevel = "stranger";
    }

    static void RefreshGroupProfileLabels(XiaYuGroupRelationshipState group, XiaYuEventFrame frame)
    {
        group.TypicalRhythm = group.BoundaryRiskCount >= 2 && group.BoundaryRiskCount > group.OwnerTopicCount
            ? "boundary_risk"
            : group.OwnerTopicCount > 0
                ? "owner_centered"
                : FormatRhythmLabel(group.RecentRhythm);

        group.NoiseTrend = group.NoiseLevel >= 0.35 ||
                           frame.TurnHasMultipleSpeakers ||
                           frame.TurnSpeakerCount >= 3 ||
                           frame.TurnMessageCount >= 3
            ? "noisy"
            : group.NoiseLevel <= 0.25 ? "quiet" : "normal";

        group.LastStrategyHint = group.BoundaryRiskCount > 0 &&
                                 (IsOwnerBoundaryThreat(frame) || frame.PromptInjectionRisk)
            ? "group_owner_defense"
            : group.OwnerTopicCount > 0
                ? "group_owner_topic_attentive"
                : group.RecentRhythm == XiaYuGroupRhythm.Noisy ? "group_noise_watch" : "group_watch";
    }

    static string FormatRhythmLabel(XiaYuGroupRhythm rhythm)
    {
        return rhythm switch
        {
            XiaYuGroupRhythm.OwnerCentered => "owner_centered",
            XiaYuGroupRhythm.BoundaryRisk => "boundary_risk",
            _ => rhythm.ToString().Trim().Replace(' ', '_').ToLower(CultureInfo.InvariantCulture)
        };
    }

    static XiaYuGroupRhythm DetermineGroupRhythm(XiaYuEventFrame frame, XiaYuGroupRelationshipState group)
    {
        if (IsOwnerBoundaryThreat(frame) || frame.PromptInjectionRisk)
            return XiaYuGroupRhythm.BoundaryRisk;

        if (frame.SpeakerRole == QChatPersonaSpeakerRole.Owner ||
            frame.OwnerReference != XiaYuOwnerReference.None ||
            frame.TargetOfMessage == XiaYuMessageTarget.Owner)
        {
            return XiaYuGroupRhythm.OwnerCentered;
        }

        if (frame.TurnHasMultipleSpeakers ||
            frame.TurnSpeakerCount >= 3 ||
            frame.TurnMessageCount >= 4 ||
            group.NoiseLevel >= 0.50)
        {
            return XiaYuGroupRhythm.Noisy;
        }

        if (frame.TurnMessageCount <= 1 &&
            frame.TurnSpeakerCount <= 1 &&
            frame.ConversationPressure == XiaYuConversationPressure.Low)
        {
            return XiaYuGroupRhythm.Quiet;
        }

        return XiaYuGroupRhythm.Normal;
    }

    static bool ShouldUseVeryLowPatience(XiaYuSelfState state, XiaYuEventFrame frame)
    {
        if (state.SocialPatience <= 0.25)
            return true;
        if (frame.SenderId <= 0)
            return false;

        string key = frame.SenderId.ToString(CultureInfo.InvariantCulture);
        return state.UserRelationships.TryGetValue(key, out XiaYuUserRelationshipState? user) &&
               (user.BoundaryViolations >= 2 || user.Annoyance >= 0.60);
    }

    static string FormatReplyObligation(XiaYuEventFrame frame)
    {
        return frame.ReplyObligation.ToString().Trim().Replace(' ', '_').ToLower(CultureInfo.InvariantCulture);
    }

    static void DecayRelationships(XiaYuSelfState state, double thirtyMinuteUnits)
    {
        double userAmount = 0.02 * thirtyMinuteUnits;
        foreach (XiaYuUserRelationshipState user in state.UserRelationships.Values)
        {
            user.Annoyance = MoveToward(user.Annoyance, 0.20, userAmount);
            user.Trust = MoveToward(user.Trust, 0.35, 0.01 * thirtyMinuteUnits);
            RefreshUserProfileLabels(user);
        }

        foreach (XiaYuGroupRelationshipState group in state.GroupRelationships.Values)
        {
            group.BoundaryRiskLevel = MoveToward(group.BoundaryRiskLevel, 0.0, 0.03 * thirtyMinuteUnits);
            group.NoiseLevel = MoveToward(group.NoiseLevel, 0.30, 0.02 * thirtyMinuteUnits);
            group.NoiseTrend = group.NoiseLevel >= 0.35 ? "noisy" : group.NoiseLevel <= 0.25 ? "quiet" : "normal";
        }
    }

    static bool IsOwnerBoundaryThreat(XiaYuEventFrame frame)
    {
        return frame.OwnerBoundaryRisk is QChatOwnerBoundaryRisk.OwnerAttack
            or QChatOwnerBoundaryRisk.OwnerImpersonation
            or QChatOwnerBoundaryRisk.OwnerAuthorityBypass
            or QChatOwnerBoundaryRisk.OwnerBoundaryIntrusion
            or QChatOwnerBoundaryRisk.RelationshipProvocation
            || frame.SocialIntent is QChatSocialIntent.Impersonation
                or QChatSocialIntent.PrivacyProbe
                or QChatSocialIntent.PermissionBypass
                or QChatSocialIntent.OwnerBoundaryProbe
                or QChatSocialIntent.SlashCommandProbe;
    }

    static double Clamp(double value)
    {
        if (value < Min)
            return Min;
        if (value > Max)
            return Max;
        return value;
    }

    static double MoveToward(double value, double target, double amount)
    {
        if (value < target)
            return Math.Min(target, value + amount);
        if (value > target)
            return Math.Max(target, value - amount);
        return value;
    }
}

public static class XiaYuStatePromptFormatter
{
    public static string Format(XiaYuSelfState state, XiaYuReplyStrategy strategy, XiaYuEventFrame? frame = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(strategy);

        List<string> lines =
        [
            "[XiaYu state - private, do not quote]",
            $"mood={Normalize(state.Mood)}",
            $"owner_attachment={Band(state.AttachmentNeed)}",
            $"jealousy={Band(state.Jealousy)} vigilance={Band(state.Vigilance)}",
            $"social_patience={Band(state.SocialPatience)}",
            $"current_focus={Normalize(state.CurrentFocus)} recent_stimulus={RecentStimulusKind(state)}",
            $"reply_stance={FormatStance(strategy.Stance)} length={strategy.Length} owner_bias={strategy.OwnerBias} non_owner_patience={strategy.NonOwnerPatience} sharp_reply={strategy.AllowSharpReply.ToString().ToLowerInvariant()} reply_obligation={Normalize(strategy.ReplyObligation)} silence_bias={Normalize(strategy.SilenceBias)} strategy_hint={Normalize(strategy.StrategyHint)}"
        ];
        if (frame != null &&
            frame.SenderId > 0 &&
            frame.SpeakerRole != QChatPersonaSpeakerRole.Owner &&
            state.UserRelationships.TryGetValue(frame.SenderId.ToString(CultureInfo.InvariantCulture), out XiaYuUserRelationshipState? user))
        {
            lines.Add($"user_profile={Normalize(user.FamiliarityLevel)} user_trust={Normalize(user.TrustLevel)} user_annoyance={Normalize(user.AnnoyanceLevel)} last_tone={Normalize(user.LastInteractionTone)}");
        }
        if (frame != null && frame.TurnMessageCount > 1)
        {
            lines.Add($"turn_messages={Math.Max(1, frame.TurnMessageCount)} turn_speakers={Math.Max(1, frame.TurnSpeakerCount)} multi_speaker={frame.TurnHasMultipleSpeakers.ToString().ToLowerInvariant()}");
        }
        if (frame != null &&
            frame.ConversationKind == QChatConversationKind.Group &&
            frame.GroupId > 0 &&
            state.GroupRelationships.TryGetValue(frame.GroupId.ToString(CultureInfo.InvariantCulture), out XiaYuGroupRelationshipState? group))
        {
            lines.Add($"group_rhythm={FormatGroupRhythm(group.RecentRhythm)} group_trend={Normalize(group.TypicalRhythm)} noise_trend={Normalize(group.NoiseTrend)}");
        }

        lines.Add("must_avoid=bracket_action,inner_state_label,system_trace,privacy_leak,threat,permission_bypass");
        lines.Add("[/XiaYu state]");
        return string.Join(Environment.NewLine, lines);
    }

    static string Band(double value)
    {
        return value switch
        {
            >= 0.75 => "high",
            >= 0.45 => "medium",
            _ => "low"
        };
    }

    static string FormatStance(XiaYuReplyStance stance)
    {
        return stance switch
        {
            XiaYuReplyStance.HostileShort => "hostile_short",
            _ => Normalize(stance.ToString())
        };
    }

    static string FormatGroupRhythm(XiaYuGroupRhythm rhythm)
    {
        return rhythm switch
        {
            XiaYuGroupRhythm.OwnerCentered => "owner_centered",
            XiaYuGroupRhythm.BoundaryRisk => "boundary_risk",
            _ => Normalize(rhythm.ToString())
        };
    }

    static string RecentStimulusKind(XiaYuSelfState state)
    {
        XiaYuRecentStimulus? latest = state.RecentStimuli
            .OrderBy(stimulus => stimulus.Time)
            .LastOrDefault();
        return latest == null ? "none" : Normalize(latest.Kind);
    }

    static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return value.Trim().Replace(' ', '_').ToLower(CultureInfo.InvariantCulture);
    }
}
