using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public class QChatRecentEventMemoryTests
{
    [Test]
    public void BuildRecallReturnsCachedGroupMessageForRecallNotice()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 12345,
            UserId = 3045846738,
            GroupId = 925402131,
            RawMessage = "撤回前的消息"
        }, "撤回前的消息", now);

        QChatRecallSnapshot recall = memory.BuildRecall(new OneBotNoticeEvent
        {
            SelfId = 2905391496,
            NoticeType = "group_recall",
            MessageId = 12345,
            UserId = 3045846738,
            GroupId = 925402131,
            OperatorId = 3045846738
        });

        Assert.Multiple(() =>
        {
            Assert.That(recall.MessageId, Is.EqualTo(12345));
            Assert.That(recall.Message, Is.Not.Null);
            Assert.That(recall.Message!.RawMessage, Is.EqualTo("撤回前的消息"));
            Assert.That(recall.Message.ReadableMessage, Is.EqualTo("撤回前的消息"));
            Assert.That(recall.Message.MessageType, Is.EqualTo(OneBotMessageType.Group));
        });
    }

    [Test]
    public void BuildRecallDoesNotReturnExpiredCachedMessage()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset oldTime = new(2026, 6, 19, 18, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 12345,
            UserId = 3045846738,
            GroupId = 0,
            RawMessage = "太早的消息"
        }, "太早的消息", oldTime);

        memory.Prune(new DateTimeOffset(2026, 6, 19, 18, 31, 0, TimeSpan.FromHours(8)));
        QChatRecallSnapshot recall = memory.BuildRecall(new OneBotNoticeEvent
        {
            SelfId = 2905391496,
            NoticeType = "friend_recall",
            MessageId = 12345,
            UserId = 3045846738
        });

        Assert.That(recall.Message, Is.Null);
    }

    [Test]
    public void GetRecentConversationKeepsSessionsSeparated()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 1,
            UserId = 100,
            GroupId = 925402131,
            RawMessage = "目标群消息"
        }, "目标群消息", now);
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 2,
            UserId = 100,
            GroupId = 1072509877,
            RawMessage = "其他群消息"
        }, "其他群消息", now);

        IReadOnlyList<QChatRecentMessageSnapshot> recent = memory.GetRecentConversation(
            2905391496,
            OneBotMessageType.Group,
            925402131,
            limit: 5,
            now);

        Assert.That(recent.Select(message => message.RawMessage), Is.EqualTo(new[] { "目标群消息" }));
    }

    [Test]
    public void BuildRecentContextBlockFormatsRecentMessagesInSessionOrder()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 1,
            UserId = 100,
            GroupId = 925402131,
            RawMessage = "first"
        }, "first", now);
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 2,
            UserId = 200,
            GroupId = 925402131,
            RawMessage = "second"
        }, "second", now.AddMinutes(1));

        string context = memory.BuildRecentContextBlock(
            2905391496,
            OneBotMessageType.Group,
            925402131,
            limit: 6,
            now.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.StartWith("[Recent QQ context]"));
            Assert.That(context, Does.Contain("- 19:00 other user 100: first"));
            Assert.That(context, Does.Contain("- 19:01 other user 200: second"));
            Assert.That(context, Does.EndWith("[/Recent QQ context]"));
        });
    }

    [Test]
    public void BuildRecentContextBlockLabelsOwnerAndOtherSpeakers()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 1,
            UserId = 3045846738,
            GroupId = 925402131,
            RawMessage = "owner says alpha"
        }, "owner says alpha", now);
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 2,
            UserId = 200,
            GroupId = 925402131,
            RawMessage = "other says beta"
        }, "other says beta", now.AddMinutes(1));

        string context = memory.BuildRecentContextBlock(
            2905391496,
            OneBotMessageType.Group,
            925402131,
            limit: 6,
            now.AddMinutes(1),
            ownerUserId: 3045846738,
            botUserId: 2905391496);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("- 19:00 owner user 3045846738: owner says alpha"));
            Assert.That(context, Does.Contain("- 19:01 other user 200: other says beta"));
        });
    }

    [Test]
    public void BuildRecentContextBlockMarksRecalledMessages()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 1,
            UserId = 3045846738,
            RawMessage = "secret"
        }, "secret", now);

        memory.RememberRecall(new OneBotNoticeEvent
        {
            SelfId = 2905391496,
            NoticeType = "friend_recall",
            MessageId = 1,
            UserId = 3045846738
        }, now.AddMinutes(1));

        string context = memory.BuildRecentContextBlock(
            2905391496,
            OneBotMessageType.Private,
            3045846738,
            limit: 6,
            now.AddMinutes(1));

        Assert.That(context, Does.Contain("- 19:00 other user 3045846738 recalled: secret"));
    }

    [Test]
    public void BuildRecentContextBlockCanHideRecalledMessagesForModelContext()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 1,
            UserId = 3045846738,
            RawMessage = "secret"
        }, "secret", now);
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 2,
            UserId = 3045846738,
            RawMessage = "visible"
        }, "visible", now.AddSeconds(1));

        memory.RememberRecall(new OneBotNoticeEvent
        {
            SelfId = 2905391496,
            NoticeType = "friend_recall",
            MessageId = 1,
            UserId = 3045846738
        }, now.AddMinutes(1));

        string context = memory.BuildRecentContextBlock(
            2905391496,
            OneBotMessageType.Private,
            3045846738,
            limit: 6,
            now.AddMinutes(1),
            includeRecalledMessages: false);

        Assert.That(context, Does.Not.Contain("secret"));
        Assert.That(context, Does.Contain("visible"));
    }

    [Test]
    public void BuildRecentContextBlockHonorsCharacterBudgetAndKeepsNewestMessages()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 1,
            UserId = 100,
            GroupId = 925402131,
            RawMessage = "old"
        }, $"old {new string('a', 220)}", now);
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 2,
            UserId = 100,
            GroupId = 925402131,
            RawMessage = "middle"
        }, $"middle {new string('b', 220)}", now.AddMinutes(1));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 3,
            UserId = 100,
            GroupId = 925402131,
            RawMessage = "latest"
        }, $"latest {new string('c', 40)}", now.AddMinutes(2));

        string context = memory.BuildRecentContextBlock(
            2905391496,
            OneBotMessageType.Group,
            925402131,
            limit: 6,
            now.AddMinutes(2),
            includeRecalledMessages: true,
            maxCharacters: 180);

        Assert.Multiple(() =>
        {
            Assert.That(context.Length, Is.LessThanOrEqualTo(180));
            Assert.That(context, Does.Contain("latest"));
            Assert.That(context, Does.Not.Contain("old"));
            Assert.That(context, Does.Not.Contain("middle"));
        });
    }

    [Test]
    public void BuildRecentRecallContextBlockDescribesRecallWithoutLeakingOriginalText()
    {
        QChatRecentEventMemory memory = new(maxMessages: 10, retention: TimeSpan.FromMinutes(30));
        DateTimeOffset now = new(2026, 6, 19, 19, 0, 0, TimeSpan.FromHours(8));
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 1,
            UserId = 3045846738,
            RawMessage = "private secret"
        }, "private secret", now);

        memory.RememberRecall(new OneBotNoticeEvent
        {
            SelfId = 2905391496,
            NoticeType = "friend_recall",
            MessageId = 1,
            UserId = 3045846738,
            OperatorId = 3045846738
        }, now.AddMinutes(1));

        string context = memory.BuildRecentRecallContextBlock(
            2905391496,
            OneBotMessageType.Private,
            3045846738,
            limit: 3,
            now.AddMinutes(2));

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.StartWith("[Recent QQ events]"));
            Assert.That(context, Does.Contain("user 3045846738 recalled a recent private message"));
            Assert.That(context, Does.Contain("message_id=1"));
            Assert.That(context, Does.Not.Contain("private secret"));
        });
    }
}
