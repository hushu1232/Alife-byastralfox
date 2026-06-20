using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatAgentIdentityRegistryTests
{
    [Test]
    public void CreateDefaultResolvesKnownBotAccountsToProfiles()
    {
        QChatAgentIdentityRegistry registry = QChatAgentIdentityRegistry.CreateDefault();

        QChatAgentIdentity xiayu = registry.ResolveByBotId(2905391496)!;
        QChatAgentIdentity mixu = registry.ResolveByBotId(3340947887)!;

        Assert.Multiple(() =>
        {
            Assert.That(xiayu.AgentId, Is.EqualTo("xiayu"));
            Assert.That(xiayu.Profile.DisplayName, Is.EqualTo("\u590f\u7fbd"));
            Assert.That(xiayu.Profile.OwnerAddressName, Is.EqualTo("\u672f\u672f"));
            Assert.That(xiayu.Profile.MemoryScope, Is.EqualTo("qchat/xiayu"));

            Assert.That(mixu.AgentId, Is.EqualTo("mixu"));
            Assert.That(mixu.Profile.DisplayName, Is.EqualTo("\u54aa\u7eea"));
            Assert.That(mixu.Profile.OwnerAddressName, Is.EqualTo("\u4e3b\u4eba"));
            Assert.That(mixu.Profile.MemoryScope, Is.EqualTo("qchat/mixu"));
        });
    }

    [Test]
    public void CreateDefaultResolvesFullCharacterAliasWhenBotAccountIsUnknown()
    {
        QChatAgentIdentityRegistry registry = QChatAgentIdentityRegistry.CreateDefault();

        QChatAgentIdentity identity = registry.ResolveByCharacterName("\u96e8\u5bab\u54aa\u7eea")!;

        Assert.That(identity.AgentId, Is.EqualTo("mixu"));
    }

    [Test]
    public void RouteServiceUsesDefaultRegistryWhenBotAgentsAreNotConfigured()
    {
        QChatAgentRouteService service = new(new QChatAgentRouteConfig
        {
            OwnerUserId = 3045846738,
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
            Assert.That(route.SessionKey, Is.EqualTo("qq:mixu:3340947887:group:987654"));
            Assert.That(route.IsOwner, Is.False);
        });
    }
}
