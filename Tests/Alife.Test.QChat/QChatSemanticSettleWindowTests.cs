using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticSettleWindowTests
{
    [Test]
    public void AddMessage_BurstMessagesSettleAsSingleWindow()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions
        {
            SettleDelay = TimeSpan.FromSeconds(3),
            MaxWindowDuration = TimeSpan.FromSeconds(30),
            MaxMessages = 10
        }, now);

        window.AddMessage(CreateMessage("first fragment", now));
        window.AddMessage(CreateMessage("second fragment", now.AddSeconds(1)));

        Assert.That(window.ShouldSettle(now.AddSeconds(2)), Is.False);
        Assert.That(window.ShouldSettle(now.AddSeconds(5)), Is.True);
        Assert.That(window.Snapshot().Messages.Select(message => message.Text), Is.EqualTo(new[] { "first fragment", "second fragment" }));
    }

    [Test]
    public void RemoveMessage_DropsRecalledMessageBeforeSnapshot()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions(), now);
        window.AddMessage(CreateMessage("keep", now, messageId: 1));
        window.AddMessage(CreateMessage("recall", now.AddSeconds(1), messageId: 2));

        window.RemoveMessage(2);

        Assert.That(window.Snapshot().Messages.Single().Text, Is.EqualTo("keep"));
    }

    [Test]
    public void ShouldSettle_MaxMessagesForcesSettle()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions { MaxMessages = 2 }, now);

        window.AddMessage(CreateMessage("one", now));
        window.AddMessage(CreateMessage("two", now.AddSeconds(1)));

        Assert.That(window.ShouldSettle(now.AddSeconds(1)), Is.True);
    }

    [Test]
    public void ShouldSettle_IncompleteTrailingTextWaitsUntilMaxWindow()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions
        {
            SettleDelay = TimeSpan.FromSeconds(1),
            MaxWindowDuration = TimeSpan.FromSeconds(5)
        }, now);

        window.AddMessage(CreateMessage("wait,", now));

        Assert.That(window.ShouldSettle(now.AddSeconds(2)), Is.False);
        Assert.That(window.ShouldSettle(now.AddSeconds(5)), Is.True);
    }

    [Test]
    public void ShouldSettleWaitsWhenLatestMessageLooksLikeContinuation()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        QChatSemanticSettleWindow window = new(createdAt: start);
        window.AddMessage(CreateMessage("关于 DataAgent 还有", start));

        bool shouldSettle = window.ShouldSettle(start.AddSeconds(10));

        Assert.That(shouldSettle, Is.False);
    }

    [Test]
    public void ShouldSettleUsesStableSemanticCompletionAfterDelay()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        QChatSemanticSettleWindow window = new(createdAt: start);
        window.AddMessage(CreateMessage("DataAgent V2.5 应该怎么接卡尔曼滤波？", start));

        bool shouldSettle = window.ShouldSettle(start.AddSeconds(6));

        Assert.That(shouldSettle, Is.True);
    }

    [Test]
    public void ShouldSettleUsesStablePlainCompletionAfterDelay()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        QChatSemanticSettleWindow window = new(createdAt: start);
        window.AddMessage(CreateMessage("Tell me about DataAgent", start));

        bool shouldSettle = window.ShouldSettle(start.AddSeconds(6));

        Assert.That(shouldSettle, Is.True);
    }

    [Test]
    public void EmptyWindowNeverSettles()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions
        {
            SettleDelay = TimeSpan.Zero,
            MaxWindowDuration = TimeSpan.Zero,
            MaxMessages = 1
        }, now);

        Assert.That(window.ShouldSettle(now.AddMinutes(1)), Is.False);
    }

    [Test]
    public void SnapshotPreservesWindowTimestampsAndMessageOrder()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions(), now);

        window.AddMessage(CreateMessage("first", now.AddSeconds(1), messageId: 10));
        window.AddMessage(CreateMessage("second", now.AddSeconds(3), messageId: 11));

        QChatSemanticWindowSnapshot snapshot = window.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CreatedAt, Is.EqualTo(now));
            Assert.That(snapshot.LastUpdatedAt, Is.EqualTo(now.AddSeconds(3)));
            Assert.That(snapshot.Messages.Select(message => message.MessageId), Is.EqualTo(new long[] { 10, 11 }));
        });
    }

    [Test]
    public void MaxWindowDurationForcesIncompleteTrailingTextToSettle()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions
        {
            SettleDelay = TimeSpan.FromMinutes(10),
            MaxWindowDuration = TimeSpan.FromSeconds(5),
            MaxMessages = 10
        }, now);

        window.AddMessage(CreateMessage("still incomplete,", now.AddSeconds(1)));

        Assert.That(window.ShouldSettle(now.AddSeconds(5)), Is.True);
    }

    static QChatSemanticWindowMessage CreateMessage(string text, DateTimeOffset timestamp, long messageId = 0)
    {
        return new QChatSemanticWindowMessage(messageId, 10001, text, HasImage: false, Timestamp: timestamp);
    }
}
