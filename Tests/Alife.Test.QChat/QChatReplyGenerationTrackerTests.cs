using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatReplyGenerationTrackerTests
{
    [Test]
    public void BeginCancelsOnlyThePreviousGenerationOfTheSameConversation()
    {
        QChatReplyGenerationTracker tracker = new();
        QChatReplyGenerationLease first = tracker.Begin(99, OneBotMessageType.Private, 42);
        QChatReplyGenerationLease unrelated = tracker.Begin(99, OneBotMessageType.Private, 43);
        QChatReplyGenerationLease second = tracker.Begin(99, OneBotMessageType.Private, 42);

        Assert.Multiple(() =>
        {
            Assert.That(first.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(unrelated.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(second.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(tracker.IsCurrent(second), Is.True);
            Assert.That(tracker.IsCurrent(first), Is.False);
        });

        tracker.Release(first);
        Assert.That(tracker.IsCurrent(second), Is.True);

        tracker.Release(second);
        tracker.Release(unrelated);
    }
}
