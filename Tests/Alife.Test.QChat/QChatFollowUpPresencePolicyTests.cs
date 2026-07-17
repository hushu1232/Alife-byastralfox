using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatFollowUpPresencePolicyTests
{
    static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.FromHours(8));

    [Test]
    public void RiskTaskGroupOrPendingMediaAlwaysReturnsDoNotInterrupt()
    {
        QChatFollowUpPresencePolicy policy = new();
        QChatFollowUpPresenceContext[] contexts =
        [
            OwnerPrivateContext() with { IsRiskConversation = true },
            OwnerPrivateContext() with { IsDeterministicTask = true },
            OwnerPrivateContext() with { HasPendingMedia = true },
            OwnerPrivateContext() with { IsQuiet = true },
            OwnerPrivateContext() with { ModelReplyWasBlocked = true },
            OwnerPrivateContext() with { MessageType = OneBotMessageType.Group }
        ];

        foreach (QChatFollowUpPresenceContext context in contexts)
        {
            QChatFollowUpPresence presence = policy.Evaluate(context, new MixuFollowUpPresenceAdapter());
            Assert.That(presence.Intent, Is.EqualTo(QChatFollowUpIntent.DoNotInterrupt));
        }
    }

    [Test]
    public void XiayuSoftOwnerPrivateClosingCueBecomesWarmCodaWithoutMutatingState()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Now);
        state.Mood = "softened";
        state.CurrentFocus = "owner_private";
        XiaYuSelfState before = state.Clone();
        QChatFollowUpPresencePolicy policy = new();

        QChatFollowUpPresence presence = policy.Evaluate(
            OwnerPrivateContext("我先去忙了", "好，别太累"),
            new XiaYuFollowUpPresenceAdapter(state, TenderStrategy()));

        Assert.Multiple(() =>
        {
            Assert.That(presence.Intent, Is.EqualTo(QChatFollowUpIntent.WarmCoda));
            Assert.That(state.Mood, Is.EqualTo(before.Mood));
            Assert.That(state.CurrentFocus, Is.EqualTo(before.CurrentFocus));
            Assert.That(state.RecentStimuli, Is.EqualTo(before.RecentStimuli));
        });
    }

    [Test]
    public void XiayuVigilanceOrSilentStrategyStopsWhileMixuUsesHerOwnIntent()
    {
        XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Now);
        state.Vigilance = 0.85;
        QChatFollowUpPresencePolicy policy = new();
        QChatFollowUpPresenceContext context = OwnerPrivateContext("晚安", "晚安");

        QChatFollowUpPresence xiaYu = policy.Evaluate(
            context,
            new XiaYuFollowUpPresenceAdapter(state, SilentStrategy()));
        QChatFollowUpPresence mixu = policy.Evaluate(
            context with { AgentId = "mixu" },
            new MixuFollowUpPresenceAdapter());

        Assert.Multiple(() =>
        {
            Assert.That(xiaYu.Intent, Is.EqualTo(QChatFollowUpIntent.DoNotInterrupt));
            Assert.That(mixu.Intent, Is.EqualTo(QChatFollowUpIntent.EmotionalAfterthought));
        });
    }

    static QChatFollowUpPresenceContext OwnerPrivateContext(
        string sourceText = "晚安",
        string replyText = "好") => new(
        "xiayu",
        OneBotMessageType.Private,
        QChatSenderRole.Owner,
        sourceText,
        replyText,
        IsRiskConversation: false,
        IsDeterministicTask: false,
        HasPendingMedia: false,
        IsQuiet: false,
        ModelReplyWasBlocked: false,
        IsTimerState: false,
        IsHighConversationPressure: false);

    static XiaYuReplyStrategy TenderStrategy() => new(
        XiaYuReplyStance.Tender,
        "short",
        "extreme",
        "normal",
        AllowSharpReply: false,
        AllowProactive: false);

    static XiaYuReplyStrategy SilentStrategy() => new(
        XiaYuReplyStance.Silent,
        "silent",
        "extreme",
        "normal",
        AllowSharpReply: false,
        AllowProactive: false);
}
