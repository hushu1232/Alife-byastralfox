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
    public void TryHandleRouteAcceptsCopiedMenuLineWithoutReturningHelp()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat route - show route/session ids",
            CreateRoute(),
            CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
            Assert.That(result.Text, Does.Not.Contain("Supported commands:"));
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
    public void TryHandleStatusReturnsInternetAccessState()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat status",
            CreateRoute(),
            CreateProfile(),
            new QChatDiagnosticsRuntimeState(
                InternetAccessEnabled: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("internet=disabled"));
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
    public void TryHandleUnknownQChatCommandReturnsShortChineseRootMenu()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat nope", CreateRoute(), CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("QChat 指令菜单"));
            Assert.That(result.Text, Does.Contain("只限术术账号使用"));
            Assert.That(result.Text, Does.Contain("/qchat status"));
            Assert.That(result.Text, Does.Contain("/qchat timing"));
            Assert.That(result.Text, Does.Contain("/qchat memory"));
            Assert.That(result.Text, Does.Contain("/qchat desktop"));
            Assert.That(result.Text, Does.Contain("/qchat web"));
            Assert.That(result.Text, Does.Contain("/qchat rag"));
            Assert.That(result.Text, Does.Contain("/qchat events"));
            Assert.That(result.Text, Does.Contain("/qchat diag"));
            Assert.That(result.Text, Does.Not.Contain("/qchat desktop draft approve"));
            Assert.That(result.Text, Does.Not.Contain("/qchat memory purge <id> confirm"));
            Assert.That(result.Text, Does.Not.Contain("/qchat rag add <url>"));
            Assert.That(result.Text, Does.Not.Contain("/qchat files"));
        });
    }

    [TestCase("/qchat memory", "记忆指令", "/qchat memory status", "/qchat memory purge <id> confirm")]
    [TestCase("/qchat desktop", "桌面指令", "/qchat desktop status", "/qchat desktop file policy")]
    [TestCase("/qchat timing", "回复延时", "/qchat timing status", "/qchat timing off")]
    [TestCase("/qchat events", "主人事件", "/qchat events status", "/qchat events retry")]
    [TestCase("/qchat diag", "诊断指令", "/qchat route", "/qchat profile")]
    [TestCase("/qchat internet", "联网指令", "/qchat internet <url>", "仅公网 HTTP/HTTPS")]
    [TestCase("/qchat web", "Web", "/qchat web snapshot <url>", "HTTP/HTTPS")]
    [TestCase("/qchat rag", "外部 RAG 管理", "/qchat rag add <url>", "/qchat rag status")]
    public void TryHandleSecondLevelMenuReturnsChineseUsage(string command, string title, string firstCommand, string secondCommand)
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(command, CreateRoute(), CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain(title));
            Assert.That(result.Text, Does.Contain(firstCommand));
            Assert.That(result.Text, Does.Contain(secondCommand));
            Assert.That(result.Text, Does.Not.Contain("Supported commands:"));
        });
    }

    [Test]
    public void TryHandleWebSmokeReturnsManualLiveChecklist()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat web smoke", CreateRoute(), CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("QQ 联网研究 smoke checklist"));
            Assert.That(result.Text, Does.Contain("1. 主人私聊：查一下 dotnet 9 release notes"));
            Assert.That(result.Text, Does.Contain("2. 群聊成员：@bot 搜 dotnet 9 release notes"));
            Assert.That(result.Text, Does.Contain("3. 非主人私聊：/search dotnet 9"));
            Assert.That(result.Text, Does.Contain("预期：主人可自动读公开 HTTP/HTTPS 页面"));
            Assert.That(result.Text, Does.Contain("预期：群成员只拿公开搜索证据"));
            Assert.That(result.Text, Does.Contain("不得触发：点击、登录、下载、表单提交、JS 执行、私网或 file URL"));
        });
    }

    [Test]
    public void TryHandleWebBrowserAgentReturnsOwnerOnlyPhaseOneSummary()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat web browser-agent", CreateRoute(), CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("browser-agent=phase1"));
            Assert.That(result.Text, Does.Contain("owner-only"));
            Assert.That(result.Text, Does.Contain("no-login"));
            Assert.That(result.Text, Does.Contain("image-ok"));
            Assert.That(result.Text, Does.Contain("video-link-only"));
            Assert.That(result.Text, Does.Contain("image-return=connected"));
            Assert.That(result.Text, Does.Contain("video-return=link-only"));
            Assert.That(result.Text, Does.Contain(@"media-cache=D:\Alife\Runtime\BrowserAgentMedia"));
        });
    }

    [Test]
    public void TryHandleWebBrowserAgentSmokeReturnsLiveChecklist()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat web browser-agent smoke",
            CreateRoute(),
            CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("browser-agent-live-smoke"));
            Assert.That(result.Text, Does.Contain("status=manual"));
            Assert.That(result.Text, Does.Contain("live-smoke=pending"));
            Assert.That(result.Text, Does.Contain("owner-private-text"));
            Assert.That(result.Text, Does.Contain("owner-private-image"));
            Assert.That(result.Text, Does.Contain("owner-private-video"));
            Assert.That(result.Text, Does.Contain("non-owner-denied"));
            Assert.That(result.Text, Does.Contain("group-denied"));
            Assert.That(result.Text, Does.Contain("image-return=connected"));
            Assert.That(result.Text, Does.Contain("video-return=link-only"));
            Assert.That(result.Text, Does.Contain(@"media-cache=D:\Alife\Runtime\BrowserAgentMedia"));
            Assert.That(result.Text, Does.Contain("blocked=no-login no-form-submit no-video-download no-local-upload no-js no-private-network"));
        });
    }

    [Test]
    public void TryHandleRagMenuReturnsOwnerManagementUsage()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle("/qchat rag", CreateRoute(), CreateProfile());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("外部 RAG 管理"));
            Assert.That(result.Text, Does.Contain("/qchat rag add <url>"));
            Assert.That(result.Text, Does.Contain("/qchat rag status"));
            Assert.That(result.Text, Does.Contain("群成员"));
            Assert.That(result.Text, Does.Contain("/rag <question>"));
            Assert.That(result.Text, Does.Contain("不能添加、删除、刷新或配置来源"));
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
