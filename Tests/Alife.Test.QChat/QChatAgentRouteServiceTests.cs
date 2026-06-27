using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatAgentRouteServiceTests
{
    [Test]
    public void ResolvePrivateOwnerForXiaYuBuildsStablePrivateSessionKey()
    {
        QChatAgentRouteService service = new(new QChatAgentRouteConfig
        {
            OwnerUserId = 3045846738,
            BotAgents =
            {
                [2905391496] = "xiayu",
                [3340947887] = "mixu"
            }
        });

        OneBotBasicMessageEvent message = new()
        {
            UserId = 3045846738
        };

        QChatAgentRoute route = service.Resolve(2905391496, message);

        Assert.Multiple(() =>
        {
            Assert.That(route.AgentId, Is.EqualTo("xiayu"));
            Assert.That(route.BotAccountId, Is.EqualTo(2905391496));
            Assert.That(route.ConversationKind, Is.EqualTo(QChatConversationKind.Private));
            Assert.That(route.PeerId, Is.EqualTo(3045846738));
            Assert.That(route.SenderId, Is.EqualTo(3045846738));
            Assert.That(route.IsOwner, Is.True);
            Assert.That(route.SessionKey, Is.EqualTo("qq:xiayu:2905391496:private:3045846738"));
        });
    }

    [Test]
    public void ResolveGroupForMixuBuildsGroupSessionKeyWithoutOwnerPrivateLeak()
    {
        QChatAgentRouteService service = new(new QChatAgentRouteConfig
        {
            OwnerUserId = 3045846738,
            BotAgents =
            {
                [2905391496] = "xiayu",
                [3340947887] = "mixu"
            }
        });

        OneBotBasicMessageEvent message = new()
        {
            UserId = 111111,
            GroupId = 987654
        };

        QChatAgentRoute route = service.Resolve(3340947887, message);

        Assert.Multiple(() =>
        {
            Assert.That(route.AgentId, Is.EqualTo("mixu"));
            Assert.That(route.BotAccountId, Is.EqualTo(3340947887));
            Assert.That(route.ConversationKind, Is.EqualTo(QChatConversationKind.Group));
            Assert.That(route.PeerId, Is.EqualTo(987654));
            Assert.That(route.SenderId, Is.EqualTo(111111));
            Assert.That(route.IsOwner, Is.False);
            Assert.That(route.SessionKey, Is.EqualTo("qq:mixu:3340947887:group:987654"));
        });
    }
}
