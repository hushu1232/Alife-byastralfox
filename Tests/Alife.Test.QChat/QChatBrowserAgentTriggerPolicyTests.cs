using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatBrowserAgentTriggerPolicyTests
{
    [Test]
    public void Parse_OwnerPrivateBrowserRequest_ReturnsCommand()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            QChatSenderRole.Owner,
            "browse https://example.com/docs");

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.RunBrowserTask));
            Assert.That(trigger.Task, Is.EqualTo("browse https://example.com/docs"));
        });
    }

    [TestCase(QChatSenderRole.GroupMember)]
    [TestCase(QChatSenderRole.PrivateGuest)]
    public void Parse_NonOwnerPrivateBrowserRequest_ReturnsDenied(QChatSenderRole role)
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            role,
            "browse https://example.com/docs");

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.Denied));
            Assert.That(trigger.Reason, Is.EqualTo("browser_agent_owner_required"));
        });
    }

    [Test]
    public void Parse_GroupOwnerMention_DoesNotRunBrowserAutomation()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Group,
            QChatSenderRole.Owner,
            "[CQ:at,qq=999] browse https://example.com/docs");

        Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.None));
    }

    [Test]
    public void Parse_SearchOnlyRequest_DoesNotStealWebResearch()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            QChatSenderRole.Owner,
            "search dotnet release notes");

        Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.None));
    }

    [Test]
    public void Parse_QChatDesktopCommand_DoesNotStealOwnerCommand()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            QChatSenderRole.Owner,
            "/qchat desktop request open notepad");

        Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.None));
    }

    [Test]
    public void Parse_OrdinaryText_ReturnsNone()
    {
        QChatBrowserAgentTrigger trigger = QChatBrowserAgentTriggerPolicy.Parse(
            OneBotMessageType.Private,
            QChatSenderRole.Owner,
            "hello there");

        Assert.That(trigger.Kind, Is.EqualTo(QChatBrowserAgentTriggerKind.None));
    }
}
