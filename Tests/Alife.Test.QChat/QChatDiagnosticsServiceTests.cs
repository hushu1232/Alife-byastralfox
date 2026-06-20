using System;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatDiagnosticsServiceTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("hello")]
    [TestCase(" qchat route")]
    public void TryHandleReturnsNotHandledForOrdinaryText(string? text)
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(text, null!, null!);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void TryHandleRouteReturnsRouteSnapshot()
    {
        QChatAgentRoute route = CreateRoute();
        QChatAgentProfile profile = CreateProfile();

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("  /QCHAT ROUTE  ", route, profile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("agent=xiayu"));
            Assert.That(result.Text, Does.Contain("bot=2905391496"));
            Assert.That(result.Text, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
            Assert.That(result.Text, Does.Contain("conversation=Private"));
            Assert.That(result.Text, Does.Contain("peer=3045846738"));
            Assert.That(result.Text, Does.Contain("owner=True"));
        });
    }

    [Test]
    public void TryHandleProfileReturnsProfileSnapshot()
    {
        QChatAgentRoute route = CreateRoute();
        QChatAgentProfile profile = CreateProfile();

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat profile", route, profile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("agent=xiayu"));
            Assert.That(result.Text, Does.Contain("display=\u590f\u7fbd"));
            Assert.That(result.Text, Does.Contain("model=deepseek-v4-flash"));
            Assert.That(result.Text, Does.Contain("memory=qchat/xiayu"));
            Assert.That(result.Text, Does.Contain(@"persona=C:\Users\hu shu\Desktop\personalitysetting"));
        });
    }

    [Test]
    public void TryHandleStatusReturnsOnlineStatus()
    {
        QChatAgentRoute route = CreateRoute();
        QChatAgentProfile profile = CreateProfile();

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat status", route, profile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("agent=xiayu"));
            Assert.That(result.Text, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
            Assert.That(result.Text, Does.Contain("model=deepseek-v4-flash"));
            Assert.That(result.Text, Does.Contain("status=online"));
        });
    }

    [Test]
    public void TryHandleStatusReturnsRuntimeTimingState()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat status",
            CreateRoute(),
            CreateProfile(),
            new QChatDiagnosticsRuntimeState(
                ReplyTimingDelayEnabled: true,
                ConversationSettleWindowEnabled: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("bot=2905391496"));
            Assert.That(result.Text, Does.Contain("reply_timing_delay=enabled"));
            Assert.That(result.Text, Does.Contain("conversation_settle_window=enabled"));
        });
    }

    [Test]
    public void TryHandleIdentityReturnsUnifiedRouteAndProfileSnapshot()
    {
        QChatAgentRoute route = CreateRoute();
        QChatAgentProfile profile = CreateProfile();

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat identity", route, profile);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("agent=xiayu"));
            Assert.That(result.Text, Does.Contain("bot=2905391496"));
            Assert.That(result.Text, Does.Contain("display=\u590f\u7fbd"));
            Assert.That(result.Text, Does.Contain("owner_address=\u672f\u672f"));
            Assert.That(result.Text, Does.Contain("memory=qchat/xiayu"));
            Assert.That(result.Text, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
        });
    }

    [TestCase("/qchat files", "files=pending:0 downloaded:0 deleted:0")]
    [TestCase("/qchat approvals", "approvals=pending:0")]
    [TestCase("/qchat failures", "failures=0")]
    [TestCase("/qchat recent private", "recent.private=empty")]
    [TestCase("/qchat recent group", "recent.group=empty")]
    public void TryHandleEmptyStateCommandsReturnTruthfulDefaults(string command, string expectedText)
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(command, CreateRoute(), CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain(expectedText));
        });
    }

    [Test]
    public void TryHandleUnknownQChatCommandReturnsHelp()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat nope", CreateRoute(), CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("Supported commands:"));
            Assert.That(result.Text, Does.Contain("/qchat route"));
            Assert.That(result.Text, Does.Contain("/qchat identity"));
            Assert.That(result.Text, Does.Contain("/qchat profile"));
            Assert.That(result.Text, Does.Contain("/qchat status"));
            Assert.That(result.Text, Does.Contain("/qchat timing on|off|status"));
            Assert.That(result.Text, Does.Contain("/qchat memory status"));
            Assert.That(result.Text, Does.Contain("/qchat memory recent"));
            Assert.That(result.Text, Does.Contain("/qchat memory forget"));
            Assert.That(result.Text, Does.Contain("/qchat memory purge"));
            Assert.That(result.Text, Does.Contain("/qchat desktop status"));
            Assert.That(result.Text, Does.Contain("/qchat desktop capabilities"));
            Assert.That(result.Text, Does.Contain("/qchat files"));
            Assert.That(result.Text, Does.Contain("/qchat approvals"));
            Assert.That(result.Text, Does.Contain("/qchat failures"));
            Assert.That(result.Text, Does.Contain("/qchat recent private"));
            Assert.That(result.Text, Does.Contain("/qchat recent group"));
            Assert.That(result.Text, Does.Contain("show route/session ids"));
            Assert.That(result.Text, Does.Contain("show agent identity"));
            Assert.That(result.Text, Does.Contain("show model/persona/memory"));
            Assert.That(result.Text, Does.Contain("show online and timing state"));
            Assert.That(result.Text, Does.Contain("toggle humanlike reply timing"));
            Assert.That(result.Text, Does.Contain("show QChat memory layer wiring"));
            Assert.That(result.Text, Does.Contain("show recent memory events"));
            Assert.That(result.Text, Does.Contain("remove a memory from current context"));
            Assert.That(result.Text, Does.Contain("move a memory archive to trash"));
            Assert.That(result.Text, Does.Contain("read-only desktop status"));
            Assert.That(result.Text, Does.Contain("show enabled read-only desktop capabilities"));
            Assert.That(result.Text, Does.Contain("show file task summary"));
            Assert.That(result.Text, Does.Contain("show pending approvals"));
            Assert.That(result.Text, Does.Contain("show failure count"));
            Assert.That(result.Text, Does.Contain("show recent private context"));
            Assert.That(result.Text, Does.Contain("show recent group context"));
        });
    }

    [Test]
    public void TryHandleQChatCommandThrowsForNullRoute()
    {
        Assert.That(
            () => QChatDiagnosticsService.TryHandle("/qchat route", null!, CreateProfile()),
            Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("route"));
    }

    [Test]
    public void TryHandleQChatCommandThrowsForNullProfile()
    {
        Assert.That(
            () => QChatDiagnosticsService.TryHandle("/qchat profile", CreateRoute(), null!),
            Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("profile"));
    }

    static QChatAgentRoute CreateRoute()
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

    static QChatAgentProfile CreateProfile()
    {
        return new QChatAgentProfile(
            "xiayu",
            "\u590f\u7fbd",
            @"C:\Users\hu shu\Desktop\personalitysetting",
            "qchat/xiayu",
            "deepseek-v4-flash",
            "\u672f\u672f",
            ["17-year-old-girl"],
            new QChatAgentCapabilities(
                AllowComputerFileTools: true,
                AllowProjectModification: true,
                AllowRecall: true,
                AllowPoke: true));
    }
}
