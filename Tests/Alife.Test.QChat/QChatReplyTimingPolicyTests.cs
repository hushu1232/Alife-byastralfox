using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public class QChatReplyTimingPolicyTests
{
    [Test]
    public void SelectDelayUsesOwnerPrivateRange()
    {
        QChatReplyTimingPolicy policy = new(new FixedRandom(0.5));

        TimeSpan delay = policy.SelectDelay(new QChatReplyTimingContext(
            OneBotMessageType.Private,
            QChatSenderRole.Owner,
            QChatReplyAction.ReplyNormally,
            IsToolConfirmation: false));

        Assert.That(delay, Is.InRange(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1800)));
    }

    [Test]
    public void SelectDelayUsesNormalGroupRange()
    {
        QChatReplyTimingPolicy policy = new(new FixedRandom(0.5));

        TimeSpan delay = policy.SelectDelay(new QChatReplyTimingContext(
            OneBotMessageType.Group,
            QChatSenderRole.GroupMember,
            QChatReplyAction.ReplyNormally,
            IsToolConfirmation: false));

        Assert.That(delay, Is.InRange(TimeSpan.FromMilliseconds(1200), TimeSpan.FromMilliseconds(6500)));
    }

    [Test]
    public void SelectDelayKeepsToolConfirmationFast()
    {
        QChatReplyTimingPolicy policy = new(new FixedRandom(1.0));

        TimeSpan delay = policy.SelectDelay(new QChatReplyTimingContext(
            OneBotMessageType.Group,
            QChatSenderRole.Owner,
            QChatReplyAction.ReplyNormally,
            IsToolConfirmation: true));

        Assert.That(delay, Is.InRange(TimeSpan.Zero, TimeSpan.FromMilliseconds(500)));
    }

    [Test]
    public void CanStartProactiveTopicHonorsCooldownAndPendingWork()
    {
        QChatReplyTimingPolicy policy = new(new FixedRandom(0.0));
        DateTimeOffset now = new(2026, 6, 19, 20, 0, 0, TimeSpan.FromHours(8));

        Assert.Multiple(() =>
        {
            Assert.That(policy.CanStartProactiveTopic(now, now.AddMinutes(-30), hasRecentContext: true, hasPendingToolOrApproval: false, cooldown: TimeSpan.FromMinutes(20)), Is.True);
            Assert.That(policy.CanStartProactiveTopic(now, now.AddMinutes(-5), hasRecentContext: true, hasPendingToolOrApproval: false, cooldown: TimeSpan.FromMinutes(20)), Is.False);
            Assert.That(policy.CanStartProactiveTopic(now, null, hasRecentContext: false, hasPendingToolOrApproval: false, cooldown: TimeSpan.FromMinutes(20)), Is.False);
            Assert.That(policy.CanStartProactiveTopic(now, null, hasRecentContext: true, hasPendingToolOrApproval: true, cooldown: TimeSpan.FromMinutes(20)), Is.False);
        });
    }

    sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }
}
