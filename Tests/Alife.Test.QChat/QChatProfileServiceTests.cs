using Alife.Function.QChat;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatProfileServiceTests
{
    [Test]
    public void XiaYuProfileIsSeventeenYearOldGirlWithProjectToolsAndOwnerAddressName()
    {
        QChatProfileService service = QChatProfileService.CreateDefault();

        QChatAgentProfile profile = service.Get("xiayu");

        Assert.Multiple(() =>
        {
            Assert.That(profile.AgentId, Is.EqualTo("xiayu"));
            Assert.That(profile.DisplayName, Is.EqualTo("\u590f\u7fbd"));
            Assert.That(profile.PersonaPath, Is.EqualTo(@"C:\Users\hu shu\Desktop\personalitysetting"));
            Assert.That(profile.MemoryScope, Is.EqualTo("qchat/xiayu"));
            Assert.That(profile.Model, Is.EqualTo("deepseek-v4-flash"));
            Assert.That(profile.OwnerAddressName, Is.EqualTo("\u672f\u672f"));
            Assert.That(profile.PersonaTags, Does.Contain("17-year-old-girl"));
            Assert.That(profile.PersonaTags, Does.Contain("high-intelligence"));
            Assert.That(profile.PersonaTags, Does.Contain("cold-to-others"));
            Assert.That(profile.PersonaTags, Does.Contain("warm-to-owner"));
            Assert.That(profile.PersonaTags, Does.Not.Contain("catgirl"));
            Assert.That(profile.Capabilities.AllowComputerFileTools, Is.True);
            Assert.That(profile.Capabilities.AllowProjectModification, Is.True);
            Assert.That(profile.Capabilities.AllowRecall, Is.True);
            Assert.That(profile.Capabilities.AllowPoke, Is.True);
        });
    }

    [Test]
    public void MixuProfileCanRemainCatgirlWithoutSharingXiaYuMemory()
    {
        QChatProfileService service = QChatProfileService.CreateDefault();

        QChatAgentProfile profile = service.Get("mixu");
        QChatAgentProfile xiaYu = service.Get("xiayu");

        Assert.Multiple(() =>
        {
            Assert.That(profile.AgentId, Is.EqualTo("mixu"));
            Assert.That(profile.DisplayName, Is.EqualTo("\u54aa\u7eea"));
            Assert.That(profile.OwnerAddressName, Is.EqualTo("\u4e3b\u4eba"));
            Assert.That(profile.MemoryScope, Is.EqualTo("qchat/mixu"));
            Assert.That(profile.Model, Is.EqualTo("deepseek-v4-flash"));
            Assert.That(profile.PersonaTags, Does.Contain("catgirl"));
            Assert.That(profile.MemoryScope, Is.Not.EqualTo(xiaYu.MemoryScope));
            Assert.That(profile.Capabilities.AllowComputerFileTools, Is.EqualTo(xiaYu.Capabilities.AllowComputerFileTools));
            Assert.That(profile.Capabilities.AllowProjectModification, Is.EqualTo(xiaYu.Capabilities.AllowProjectModification));
            Assert.That(profile.Capabilities.AllowRecall, Is.EqualTo(xiaYu.Capabilities.AllowRecall));
            Assert.That(profile.Capabilities.AllowPoke, Is.EqualTo(xiaYu.Capabilities.AllowPoke));
        });
    }

    [Test]
    public void GetByRouteUsesAgentIdFromRoute()
    {
        QChatProfileService service = QChatProfileService.CreateDefault();
        QChatAgentRoute route = new(
            "xiayu",
            2905391496,
            QChatConversationKind.Private,
            3045846738,
            3045846738,
            true,
            "qq:xiayu:2905391496:private:3045846738");

        QChatAgentProfile profile = service.Get(route);

        Assert.That(profile.AgentId, Is.EqualTo("xiayu"));
    }

    [Test]
    public void CustomDictionaryLookupIsCaseInsensitive()
    {
        QChatAgentProfile profile = CreateProfile("custom-agent");
        Dictionary<string, QChatAgentProfile> profiles = new()
        {
            ["Custom-Agent"] = profile
        };
        QChatProfileService service = new(profiles);

        QChatAgentProfile resolved = service.Get("custom-agent");

        Assert.That(resolved, Is.SameAs(profile));
    }

    [Test]
    public void MutatingOriginalDictionaryAfterConstructionDoesNotChangeLookup()
    {
        QChatAgentProfile original = CreateProfile("custom-agent", displayName: "original");
        QChatAgentProfile replacement = CreateProfile("custom-agent", displayName: "replacement");
        Dictionary<string, QChatAgentProfile> profiles = new()
        {
            ["custom-agent"] = original
        };
        QChatProfileService service = new(profiles);

        profiles["custom-agent"] = replacement;

        Assert.That(service.Get("custom-agent"), Is.SameAs(original));
    }

    [Test]
    public void ConstructorNullProfilesThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new QChatProfileService(null!));
    }

    static QChatAgentProfile CreateProfile(string agentId, string displayName = "Test Agent")
    {
        return new QChatAgentProfile(
            agentId,
            displayName,
            string.Empty,
            $"qchat/{agentId}",
            "test-model",
            "owner",
            [],
            new QChatAgentCapabilities(
                AllowComputerFileTools: false,
                AllowProjectModification: false,
                AllowRecall: false,
                AllowPoke: false));
    }
}
