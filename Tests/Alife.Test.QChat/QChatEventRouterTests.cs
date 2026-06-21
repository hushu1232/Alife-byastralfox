using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatEventRouterTests
{
    [Test]
    public void RouteClassifiesPrivateMessage()
    {
        QChatEventRoute route = QChatEventRouter.Route(new OneBotMessageEvent
        {
            UserId = 1001,
            RawMessage = "hello"
        }, QChatSenderRole.PrivateGuest);

        Assert.Multiple(() =>
        {
            Assert.That(route.Kind, Is.EqualTo(QChatEventRouteKind.PrivateMessage));
            Assert.That(route.MessageType, Is.EqualTo(OneBotMessageType.Private));
            Assert.That(route.IntentKind, Is.EqualTo(QChatIntentKind.None));
            Assert.That(route.Reason, Is.EqualTo("private message"));
        });
    }

    [Test]
    public void RouteClassifiesGroupMessage()
    {
        QChatEventRoute route = QChatEventRouter.Route(new OneBotMessageEvent
        {
            UserId = 1001,
            GroupId = 3001,
            RawMessage = "hello group"
        }, QChatSenderRole.GroupMember);

        Assert.Multiple(() =>
        {
            Assert.That(route.Kind, Is.EqualTo(QChatEventRouteKind.GroupMessage));
            Assert.That(route.MessageType, Is.EqualTo(OneBotMessageType.Group));
            Assert.That(route.Reason, Is.EqualTo("group message"));
        });
    }

    [Test]
    public void RouteClassifiesOwnerDiagnosticsCommandBeforeOrdinaryMessage()
    {
        QChatEventRoute route = QChatEventRouter.Route(new OneBotMessageEvent
        {
            UserId = 1001,
            GroupId = 3001,
            RawMessage = "/qchat status"
        }, QChatSenderRole.Owner);

        Assert.Multiple(() =>
        {
            Assert.That(route.Kind, Is.EqualTo(QChatEventRouteKind.OwnerCommand));
            Assert.That(route.CommandText, Is.EqualTo("/qchat status"));
            Assert.That(route.Reason, Is.EqualTo("owner command"));
        });
    }

    [Test]
    public void RouteClassifiesNaturalIntentCandidateBeforeOrdinaryMessage()
    {
        QChatEventRoute route = QChatEventRouter.Route(new OneBotMessageEvent
        {
            UserId = 1001,
            GroupId = 3001,
            RawMessage = "\u64a4\u4e86\u5427"
        }, QChatSenderRole.Owner);

        Assert.Multiple(() =>
        {
            Assert.That(route.Kind, Is.EqualTo(QChatEventRouteKind.IntentCommandCandidate));
            Assert.That(route.IntentKind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(route.IntentConfirmed, Is.True);
            Assert.That(route.Reason, Is.EqualTo("intent candidate"));
        });
    }

    [Test]
    public void RouteClassifiesNoticeEvent()
    {
        QChatEventRoute route = QChatEventRouter.Route(new OneBotNoticeEvent
        {
            UserId = 1001,
            GroupId = 3001,
            NoticeType = "group_upload"
        }, QChatSenderRole.GroupMember);

        Assert.Multiple(() =>
        {
            Assert.That(route.Kind, Is.EqualTo(QChatEventRouteKind.NoticeEvent));
            Assert.That(route.MessageType, Is.EqualTo(OneBotMessageType.Group));
            Assert.That(route.Reason, Is.EqualTo("notice event"));
        });
    }

    [Test]
    public void RouteClassifiesRequestEvent()
    {
        QChatEventRoute route = QChatEventRouter.Route(new OneBotRequestEvent
        {
            RequestType = "friend"
        }, QChatSenderRole.PrivateGuest);

        Assert.Multiple(() =>
        {
            Assert.That(route.Kind, Is.EqualTo(QChatEventRouteKind.RequestEvent));
            Assert.That(route.Reason, Is.EqualTo("request event"));
        });
    }

    [Test]
    public void RouteClassifiesMetaEventAsUnsupported()
    {
        QChatEventRoute route = QChatEventRouter.Route(new OneBotMetaEvent
        {
            MetaEventType = OneBotMetaType.Heartbeat
        }, QChatSenderRole.PrivateGuest);

        Assert.Multiple(() =>
        {
            Assert.That(route.Kind, Is.EqualTo(QChatEventRouteKind.Unsupported));
            Assert.That(route.Reason, Is.EqualTo("unsupported event"));
        });
    }
}
