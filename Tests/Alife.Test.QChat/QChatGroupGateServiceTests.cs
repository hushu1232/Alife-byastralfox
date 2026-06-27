using Alife.Function.QChat;
using NUnit.Framework;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatGroupGateServiceTests
{
    [Test]
    public void UnmentionedGroupMessageIsListenOnlyAndRecordedAsPendingContext()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        QChatGroupGateDecision decision = service.Evaluate(
            route,
            "\u4eca\u5929\u5929\u6c14\u8fd8\u884c",
            isMentionedOrWoken: false,
            isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatInboundDecisionKind.ListenOnly));
            Assert.That(decision.PendingContextText, Is.EqualTo("\u4eca\u5929\u5929\u6c14\u8fd8\u884c"));
            Assert.That(decision.ContextBeforeDispatch, Is.Empty);
        });
    }

    [Test]
    public void MentionedGroupMessageDispatchesWithPendingContextAndDrainsIt()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        service.Evaluate(route, "\u524d\u6587\u4e00", isMentionedOrWoken: false, isAggressive: false);
        QChatGroupGateDecision firstDispatch = service.Evaluate(route, "@\u590f\u7fbd \u770b\u8fd9\u4e2a", isMentionedOrWoken: true, isAggressive: false);
        QChatGroupGateDecision secondDispatch = service.Evaluate(route, "@\u590f\u7fbd \u518d\u770b\u4e00\u6b21", isMentionedOrWoken: true, isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(firstDispatch.Kind, Is.EqualTo(QChatInboundDecisionKind.DispatchToModel));
            Assert.That(firstDispatch.ContextBeforeDispatch, Does.Contain("\u524d\u6587\u4e00"));
            Assert.That(secondDispatch.Kind, Is.EqualTo(QChatInboundDecisionKind.DispatchToModel));
            Assert.That(secondDispatch.ContextBeforeDispatch, Does.Not.Contain("\u524d\u6587\u4e00"));
        });
    }

    [Test]
    public void AggressiveMessageCanDispatchForXiaYuPolicy()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        QChatGroupGateDecision decision = service.Evaluate(
            route,
            "\u4f60\u6765\u5435\u4e00\u67b6\u8bd5\u8bd5",
            isMentionedOrWoken: false,
            isAggressive: true);

        Assert.That(decision.Kind, Is.EqualTo(QChatInboundDecisionKind.DispatchToModel));
    }

    [Test]
    public void SemanticGroupReplyDispatchesWithPendingContextAndDrainsIt()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        service.Evaluate(route, "\u524d\u9762\u7684\u666e\u901a\u7fa4\u804a", isMentionedOrWoken: false, isAggressive: false);
        QChatGroupGateDecision decision = service.Evaluate(
            route,
            "\u672f\u672f\u521a\u521a\u8bf4\u7684\u90a3\u4e2a\u8bbe\u7f6e\u662f\u4ec0\u4e48",
            isMentionedOrWoken: false,
            isAggressive: false,
            isSemanticReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatInboundDecisionKind.DispatchToModel));
            Assert.That(decision.Reason, Is.EqualTo("semantic group reply"));
            Assert.That(decision.ContextBeforeDispatch, Does.Contain("\u524d\u9762\u7684\u666e\u901a\u7fa4\u804a"));
        });
    }

    [Test]
    public void OwnerGroupMessageDispatchesWithoutMention()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute(senderId: 3045846738, isOwner: true);

        QChatGroupGateDecision decision = service.Evaluate(
            route,
            "\u590f\u7fbd\u770b\u4e0b",
            isMentionedOrWoken: false,
            isAggressive: false);

        Assert.That(decision.Kind, Is.EqualTo(QChatInboundDecisionKind.DispatchToModel));
    }

    [Test]
    public void PrivateRouteBypassesGroupGate()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreatePrivateRoute();

        QChatGroupGateDecision decision = service.Evaluate(
            route,
            "\u79c1\u804a\u6d88\u606f",
            isMentionedOrWoken: false,
            isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatInboundDecisionKind.DispatchToModel));
            Assert.That(decision.PendingContextText, Is.Empty);
            Assert.That(decision.ContextBeforeDispatch, Is.Empty);
        });
    }

    [Test]
    public void PendingContextIsScopedBySessionKey()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute firstGroup = CreateGroupRoute(peerId: 12345, sessionKey: "qq:xiayu:2905391496:group:12345");
        QChatAgentRoute secondGroup = CreateGroupRoute(peerId: 67890, sessionKey: "qq:xiayu:2905391496:group:67890");

        service.Evaluate(firstGroup, "first-session-context", isMentionedOrWoken: false, isAggressive: false);
        QChatGroupGateDecision secondGroupDispatch = service.Evaluate(secondGroup, "@xiayu second", isMentionedOrWoken: true, isAggressive: false);
        QChatGroupGateDecision firstGroupDispatch = service.Evaluate(firstGroup, "@xiayu first", isMentionedOrWoken: true, isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(secondGroupDispatch.ContextBeforeDispatch, Does.Not.Contain("first-session-context"));
            Assert.That(firstGroupDispatch.ContextBeforeDispatch, Does.Contain("first-session-context"));
        });
    }

    [Test]
    public void PendingContextCapsOldItems()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        for (int i = 1; i <= 14; i++)
            service.Evaluate(route, $"pending-{i:00}", isMentionedOrWoken: false, isAggressive: false);

        QChatGroupGateDecision decision = service.Evaluate(route, "@xiayu summarize", isMentionedOrWoken: true, isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ContextBeforeDispatch, Does.Not.Contain("pending-01"));
            Assert.That(decision.ContextBeforeDispatch, Does.Not.Contain("pending-02"));
            Assert.That(decision.ContextBeforeDispatch, Does.Contain("pending-03"));
            Assert.That(decision.ContextBeforeDispatch, Does.Contain("pending-14"));
        });
    }

    [Test]
    public void EmptyListenOnlyTextIsNotRecorded()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        QChatGroupGateDecision listenOnly = service.Evaluate(route, " \r\n ", isMentionedOrWoken: false, isAggressive: false);
        QChatGroupGateDecision dispatch = service.Evaluate(route, "@xiayu", isMentionedOrWoken: true, isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(listenOnly.Kind, Is.EqualTo(QChatInboundDecisionKind.ListenOnly));
            Assert.That(listenOnly.PendingContextText, Is.Empty);
            Assert.That(dispatch.ContextBeforeDispatch, Is.Empty);
        });
    }

    [Test]
    public void ListenOnlyMessageAfterDrainCanStillBeRecordedAndDrained()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        service.Evaluate(route, "before-drain", isMentionedOrWoken: false, isAggressive: false);
        service.Evaluate(route, "@xiayu drain", isMentionedOrWoken: true, isAggressive: false);
        service.Evaluate(route, "after-drain", isMentionedOrWoken: false, isAggressive: false);

        QChatGroupGateDecision finalDispatch = service.Evaluate(route, "@xiayu final", isMentionedOrWoken: true, isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(finalDispatch.ContextBeforeDispatch, Does.Contain("after-drain"));
            Assert.That(finalDispatch.ContextBeforeDispatch, Does.Not.Contain("before-drain"));
        });
    }

    [Test]
    public async Task ConcurrentListenOnlyAndActivatedDrainKeepsSessionUsable()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();
        string[] pendingIds = Enumerable.Range(0, 8)
            .Select(index => $"pending-race-{index:00}")
            .ToArray();
        ConcurrentBag<string> observedContexts = new();
        Task[] tasks = new Task[pendingIds.Length * 2];

        for (int i = 0; i < pendingIds.Length; i++)
        {
            int index = i;
            tasks[index * 2] = Task.Run(() =>
            {
                service.Evaluate(route, pendingIds[index], isMentionedOrWoken: false, isAggressive: false);
            });
            tasks[(index * 2) + 1] = Task.Run(() =>
            {
                QChatGroupGateDecision decision = service.Evaluate(route, "@xiayu", isMentionedOrWoken: true, isAggressive: false);
                if (decision.ContextBeforeDispatch.Length > 0)
                    observedContexts.Add(decision.ContextBeforeDispatch);
            });
        }

        await Task.WhenAll(tasks);

        QChatGroupGateDecision finalDispatch = service.Evaluate(route, "@xiayu final", isMentionedOrWoken: true, isAggressive: false);
        if (finalDispatch.ContextBeforeDispatch.Length > 0)
            observedContexts.Add(finalDispatch.ContextBeforeDispatch);

        string joinedContexts = string.Join('\n', observedContexts);
        Assert.Multiple(() =>
        {
            foreach (string pendingId in pendingIds)
                Assert.That(joinedContexts, Does.Contain(pendingId));
        });
    }

    [Test]
    public void NullRouteThrowsArgumentNullException()
    {
        QChatGroupGateService service = new();

        Assert.Throws<ArgumentNullException>(() => service.Evaluate(null!, "text", isMentionedOrWoken: true, isAggressive: false));
    }

    [Test]
    public void NullRawTextIsTreatedAsEmpty()
    {
        QChatGroupGateService service = new();
        QChatAgentRoute route = CreateGroupRoute();

        QChatGroupGateDecision listenOnly = service.Evaluate(route, null!, isMentionedOrWoken: false, isAggressive: false);
        QChatGroupGateDecision dispatch = service.Evaluate(route, "@xiayu", isMentionedOrWoken: true, isAggressive: false);

        Assert.Multiple(() =>
        {
            Assert.That(listenOnly.Kind, Is.EqualTo(QChatInboundDecisionKind.ListenOnly));
            Assert.That(listenOnly.PendingContextText, Is.Empty);
            Assert.That(dispatch.ContextBeforeDispatch, Is.Empty);
        });
    }

    static QChatAgentRoute CreateGroupRoute(
        long peerId = 12345,
        long senderId = 111,
        bool isOwner = false,
        string sessionKey = "qq:xiayu:2905391496:group:12345")
    {
        return new QChatAgentRoute(
            "xiayu",
            2905391496,
            QChatConversationKind.Group,
            peerId,
            senderId,
            isOwner,
            sessionKey);
    }

    static QChatAgentRoute CreatePrivateRoute()
    {
        return new QChatAgentRoute(
            "xiayu",
            2905391496,
            QChatConversationKind.Private,
            3045846738,
            3045846738,
            true,
            "qq:xiayu:2905391496:private:3045846738");
    }
}
