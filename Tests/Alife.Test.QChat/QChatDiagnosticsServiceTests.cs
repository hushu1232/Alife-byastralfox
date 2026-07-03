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
    public void TryHandleToolBrokerDiagnosticsShowsRecentRouteStateForOwner()
    {
        QChatDiagnosticsRuntimeState state = new(
            RecentToolRouteTrace: "allowed=dataagent_analysis_continue; denied=dataagent_query; reason=route_allowed");

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag toolbroker",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("Tool Broker"));
            Assert.That(result.Text, Does.Contain("dataagent_analysis_continue"));
            Assert.That(result.Text, Does.Not.Contain("[tool_route_context]"));
        });
    }

    [TestCase("/qchat diag semantic")]
    [TestCase("/qchat diagnostics semantic")]
    public void TryHandleSemanticDiagnosticsShowsRecentEstimateForOwner(string command)
    {
        string semanticText = QChatSemanticDiagnosticsFormatter.Format(new QChatSemanticDiagnosticsSnapshot(
            new QChatSemanticStateEstimate(
                SemanticCompletion: 0.7345,
                ContinuationLikelihood: 0.2214,
                TopicStability: 0.8,
                SummaryIntent: 0.05,
                ShouldWait: false,
                ShouldAnswer: true,
                ShouldSummarize: false,
                ReasonCode: "semantic_completion_stable"),
            WindowMessageCount: 1,
            WindowAge: TimeSpan.FromSeconds(6),
            LastUpdateAge: TimeSpan.FromSeconds(3)));
        QChatDiagnosticsRuntimeState state = new(RecentSemanticEstimate: semanticText);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            command,
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("QChat semantic diagnostics"));
            Assert.That(result.Text, Does.Contain("semantic_completion=0.735"));
            Assert.That(result.Text, Does.Contain("continuation_likelihood=0.221"));
            Assert.That(result.Text, Does.Contain("should_answer=true"));
            Assert.That(result.Text, Does.Contain("reason_code=semantic_completion_stable"));
        });
    }

    [Test]
    public void TryHandleSemanticDiagnosticsReturnsUnavailableWhenNoEstimateExists()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag semantic",
            CreateRoute(),
            CreateProfile(),
            new QChatDiagnosticsRuntimeState());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("QChat semantic diagnostics"));
            Assert.That(result.Text, Does.Contain("state=unavailable"));
            Assert.That(result.Text, Does.Contain("reason=semantic_window_empty"));
        });
    }

    [TestCase("/dataagent diag evidence")]
    [TestCase("/dataagent diagnostics evidence")]
    [TestCase("/qchat diag dataagent evidence")]
    [TestCase("/qchat diagnostics dataagent evidence")]
    public void TryHandleDataAgentEvidenceDiagnosticsShowsRecentEvidenceForOwner(string command)
    {
        string evidenceText = string.Join(Environment.NewLine,
            "DataAgent evidence diagnostics",
            "analysis_confidence=0.781",
            "answer_stability=0.733",
            "clarification_need=0.242",
            "risk_level=0.287",
            "state_estimate_reason_code=analysis_evidence_stable",
            "route_allowed=true",
            "route_allows_query=true",
            "executed_sql=true",
            "terminal=false",
            "tool_broker_audit_allowed=true");
        QChatDiagnosticsRuntimeState state = new(RecentDataAgentEvidence: evidenceText);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            command,
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(result.Text, Does.Contain("analysis_confidence=0.781"));
            Assert.That(result.Text, Does.Contain("risk_level=0.287"));
            Assert.That(result.Text, Does.Contain("state_estimate_reason_code=analysis_evidence_stable"));
        });
    }

    [Test]
    public void TryHandleDataAgentEvidenceDiagnosticsReturnsUnavailableWhenNoEvidenceExists()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            CreateRoute(),
            CreateProfile(),
            new QChatDiagnosticsRuntimeState());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(result.Text, Does.Contain("state=unavailable"));
            Assert.That(result.Text, Does.Contain("reason=evidence_pack_unavailable"));
        });
    }

    [TestCase("/dataagent diag trace")]
    [TestCase("/dataagent diagnostics trace")]
    [TestCase("/qchat diag dataagent trace")]
    [TestCase("/qchat diagnostics dataagent trace")]
    public void TryHandleDataAgentTraceDiagnosticsShowsRecentTraceForOwner(string command)
    {
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentTrace: "DataAgent trace diagnostics\nsession=session-1\n1 RouteGate Succeeded reason=route_allowed");

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            command,
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(result.Text, Does.Contain("RouteGate Succeeded"));
        });
    }

    [Test]
    public void TryHandleDataAgentTraceDiagnosticsReturnsUnavailableWhenNoTraceExists()
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag trace",
            CreateRoute(),
            CreateProfile(),
            new QChatDiagnosticsRuntimeState());

        string[] expectedLines =
        [
            "DataAgent trace diagnostics",
            "state=unavailable",
            "reason=trace_unavailable"
        ];

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
        });
    }

    [Test]
    public void TryHandleDataAgentTraceDiagnosticsPrefersSessionCacheOverLegacyTrace()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.DataAgentTrace,
            "qq:xiayu:2905391496:private:3045846738",
            "dataagent_trace",
            string.Join(Environment.NewLine,
                "DataAgent trace diagnostics",
                "reason=from_cache"),
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentTrace: "legacy trace text",
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag trace",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("reason=from_cache"));
            Assert.That(result.Text, Does.Not.Contain("legacy trace text"));
        });
    }

    [Test]
    public void TryHandleDataAgentTraceDiagnosticsPreservesLongCachedTraceWindow()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        string traceText = string.Join(Environment.NewLine,
            "DataAgent trace diagnostics",
            "trace_filler=" + new string('a', 1_000),
            "late_trace_marker=preserved");
        cache.Record(
            QChatRecentDiagnosticKind.DataAgentTrace,
            "qq:xiayu:2905391496:private:3045846738",
            "dataagent_trace",
            traceText,
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag trace",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("late_trace_marker=preserved"));
            Assert.That(result.Text.Length, Is.GreaterThan(900));
            Assert.That(result.Text.Length, Is.LessThanOrEqualTo(1800));
        });
    }

    [Test]
    public void TryHandleDataAgentTraceDiagnosticsRedactsUnsafeLegacyFallbackText()
    {
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentTrace: "sql=SELECT COUNT(*) FROM users; Bearer token-abcdef123456");

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag trace",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(result.Text, Does.Contain("state=redacted"));
            Assert.That(result.Text, Does.Not.Contain("SELECT"));
            Assert.That(result.Text, Does.Not.Contain("token-abcdef123456"));
        });
    }

    [TestCase("/dataagent nope")]
    [TestCase("/dataagent")]
    public void TryHandleDataAgentEvidenceDiagnosticsDoesNotHandleUnknownDataAgentCommands(string command)
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            command,
            CreateRoute(),
            CreateProfile(),
            new QChatDiagnosticsRuntimeState());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void TryHandleDiagnosticsRedactsHiddenToolContext()
    {
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentEvidence: "[tool_route_context]\nAllowed XML tools: dataagent_query\n[/tool_route_context]");

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(result.Text, Does.Contain("state=redacted"));
            Assert.That(result.Text, Does.Contain("reason=hidden_context_redacted"));
            Assert.That(result.Text, Does.Not.Contain("[tool_route_context]"));
            Assert.That(result.Text, Does.Not.Contain("Allowed XML tools"));
        });
    }

    [Test]
    public void TryHandleDataAgentEvidenceDiagnosticsRedactsRawEvidencePackContext()
    {
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentEvidence: "[data_agent_evidence_pack]\nanalysis_confidence=0.9\n[/data_agent_evidence_pack]");

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(result.Text, Does.Contain("state=redacted"));
            Assert.That(result.Text, Does.Contain("reason=hidden_context_redacted"));
            Assert.That(result.Text, Does.Not.Contain("[data_agent_evidence_pack]"));
            Assert.That(result.Text, Does.Not.Contain("[/data_agent_evidence_pack]"));
        });
    }

    [TestCase("connection_string=Host=localhost;Username=alife;Password=secret", "connection_string")]
    [TestCase("Server=db.internal;Uid=alife;Pwd=secret", "Pwd=secret")]
    [TestCase("api_key=sk-test-secret", "sk-test-secret")]
    [TestCase("Authorization: Bearer token-abcdef123456", "token-abcdef123456")]
    [TestCase("Bearer token-abcdef123456", "token-abcdef123456")]
    [TestCase("SELECT*FROM users", "SELECT")]
    [TestCase("SELECT COUNT(*) FROM users", "SELECT")]
    [TestCase("SELECT u.id FROM users", "SELECT")]
    [TestCase("SELECT very_long_column_name_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa FROM users", "SELECT")]
    [TestCase("CREATE TABLE secrets(id int)", "CREATE TABLE")]
    public void TryHandleDataAgentEvidenceDiagnosticsRedactsUnsafeLegacyFallbackText(
        string unsafeText,
        string forbiddenText)
    {
        QChatDiagnosticsRuntimeState state = new(RecentDataAgentEvidence: unsafeText);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(result.Text, Does.Contain("state=redacted"));
            Assert.That(result.Text, Does.Contain("reason=hidden_context_redacted"));
            Assert.That(result.Text, Does.Not.Contain(forbiddenText));
            Assert.That(result.Text, Does.Not.Contain("Password=secret"));
        });
    }

    [Test]
    public void TryHandleToolBrokerDiagnosticsRedactsUnsafeLegacyFallbackTrace()
    {
        QChatDiagnosticsRuntimeState state = new(
            RecentToolRouteTrace: "allowed=none; Bearer token-abcdef123456; SELECT COUNT(*) FROM users");

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag toolbroker",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("Tool Broker diagnostics"));
            Assert.That(result.Text, Does.Contain("recent=redacted"));
            Assert.That(result.Text, Does.Not.Contain("token-abcdef123456"));
            Assert.That(result.Text, Does.Not.Contain("SELECT"));
        });
    }

    [Test]
    public void TryHandleRecentDiagnosticsReturnsSessionCacheSummary()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 8, ttl: TimeSpan.FromMinutes(30));
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "qq:xiayu:2905391496:private:3045846738", "qchat_semantic_window", "QChat semantic diagnostics", now.AddSeconds(-3));
        cache.Record(QChatRecentDiagnosticKind.DataAgentEvidence, "qq:xiayu:2905391496:private:3045846738", "dataagent_analysis", "DataAgent evidence diagnostics", now.AddSeconds(-12));
        cache.Record(QChatRecentDiagnosticKind.ToolRoute, "qq:xiayu:2905391496:private:3045846738", "tool_broker", "allowed=dataagent_analysis_start", now.AddSeconds(-2));

        QChatDiagnosticsRuntimeState state = new(
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag recent",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("QChat recent diagnostics"));
            Assert.That(result.Text, Does.Contain("semantic_state_recent=available age_seconds=3"));
            Assert.That(result.Text, Does.Contain("dataagent_evidence_recent=available age_seconds=12"));
            Assert.That(result.Text, Does.Contain("tool_route_recent=available age_seconds=2"));
            Assert.That(result.Text, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
        });
    }

    [Test]
    public void TryHandleRecentDiagnosticsUsesRouteSessionKeyWhenRuntimeSessionKeyIsOmitted()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatAgentRoute route = CreateRoute();
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 8, ttl: TimeSpan.FromMinutes(30));
        cache.Record(QChatRecentDiagnosticKind.SemanticState, route.SessionKey, "qchat_semantic_window", "QChat semantic diagnostics", now.AddSeconds(-3));

        QChatDiagnosticsRuntimeState state = new(
            RecentDiagnosticsCache: cache,
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag recent",
            route,
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("semantic_state_recent=available age_seconds=3"));
            Assert.That(result.Text, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
        });
    }

    [Test]
    public void TryHandleRecentDiagnosticsReturnsUnavailableWhenSessionCacheIsEmpty()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatDiagnosticsRuntimeState state = new(
            RecentDiagnosticsCache: new QChatRecentDiagnosticsCache(),
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag recent",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("QChat recent diagnostics"));
            Assert.That(result.Text, Does.Contain("state=unavailable"));
            Assert.That(result.Text, Does.Contain("reason=recent_diagnostics_empty"));
        });
    }

    [Test]
    public void TryHandleSemanticDiagnosticsPrefersSessionCacheOverLegacyRecentString()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.SemanticState,
            "qq:xiayu:2905391496:private:3045846738",
            "qchat_semantic_window",
            string.Join(Environment.NewLine,
                "QChat semantic diagnostics",
                "semantic_completion=0.901",
                "reason_code=from_cache"),
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentSemanticEstimate: "legacy semantic text",
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag semantic",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("semantic_completion=0.901"));
            Assert.That(result.Text, Does.Contain("reason_code=from_cache"));
            Assert.That(result.Text, Does.Not.Contain("legacy semantic text"));
        });
    }

    [Test]
    public void TryHandleDataAgentEvidenceDiagnosticsPrefersSessionCacheOverLegacyRecentString()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.DataAgentEvidence,
            "qq:xiayu:2905391496:private:3045846738",
            "dataagent_analysis",
            string.Join(Environment.NewLine,
                "DataAgent evidence diagnostics",
                "analysis_confidence=0.912",
                "risk_level=0.101"),
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentEvidence: "legacy evidence text",
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("analysis_confidence=0.912"));
            Assert.That(result.Text, Does.Contain("risk_level=0.101"));
            Assert.That(result.Text, Does.Not.Contain("legacy evidence text"));
        });
    }

    [Test]
    public void TryHandleToolBrokerDiagnosticsPrefersSessionCacheOverLegacyTrace()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.ToolRoute,
            "qq:xiayu:2905391496:private:3045846738",
            "tool_broker",
            string.Join(Environment.NewLine,
                "Tool Broker diagnostics",
                "recent=allowed=dataagent_analysis_start; denied=none; reason=route_allowed"),
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentToolRouteTrace: "legacy-route-trace",
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag toolbroker",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("allowed=dataagent_analysis_start"));
            Assert.That(result.Text, Does.Not.Contain("legacy-route-trace"));
        });
    }

    [Test]
    public void TryHandleSemanticDiagnosticsUsesRouteSessionKeyWhenRuntimeSessionKeyIsOmitted()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatAgentRoute route = CreateRoute();
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.SemanticState,
            route.SessionKey,
            "qchat_semantic_window",
            string.Join(Environment.NewLine,
                "QChat semantic diagnostics",
                "semantic_completion=0.801",
                "reason_code=route_session_cache"),
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentSemanticEstimate: "legacy semantic text",
            RecentDiagnosticsCache: cache,
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag semantic",
            route,
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("semantic_completion=0.801"));
            Assert.That(result.Text, Does.Contain("reason_code=route_session_cache"));
            Assert.That(result.Text, Does.Not.Contain("legacy semantic text"));
        });
    }

    [Test]
    public void TryHandleDataAgentEvidenceDiagnosticsUsesRouteSessionKeyWhenRuntimeSessionKeyIsOmitted()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatAgentRoute route = CreateRoute();
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.DataAgentEvidence,
            route.SessionKey,
            "dataagent_analysis",
            string.Join(Environment.NewLine,
                "DataAgent evidence diagnostics",
                "analysis_confidence=0.812",
                "risk_level=0.202"),
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentEvidence: "legacy evidence text",
            RecentDiagnosticsCache: cache,
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            route,
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("analysis_confidence=0.812"));
            Assert.That(result.Text, Does.Contain("risk_level=0.202"));
            Assert.That(result.Text, Does.Not.Contain("legacy evidence text"));
        });
    }

    [Test]
    public void TryHandleToolBrokerDiagnosticsUsesRouteSessionKeyWhenRuntimeSessionKeyIsOmitted()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatAgentRoute route = CreateRoute();
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.ToolRoute,
            route.SessionKey,
            "tool_broker",
            string.Join(Environment.NewLine,
                "Tool Broker diagnostics",
                "recent=allowed=dataagent_analysis_start; denied=none; reason=route_session_cache"),
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentToolRouteTrace: "legacy-route-trace",
            RecentDiagnosticsCache: cache,
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag toolbroker",
            route,
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("allowed=dataagent_analysis_start"));
            Assert.That(result.Text, Does.Contain("reason=route_session_cache"));
            Assert.That(result.Text, Does.Not.Contain("legacy-route-trace"));
        });
    }

    [Test]
    public void TryHandleSemanticDiagnosticsFallsBackToLegacyRecentStringWhenCacheMissesKind()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.ToolRoute,
            "qq:xiayu:2905391496:private:3045846738",
            "tool_broker",
            "Tool Broker diagnostics",
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentSemanticEstimate: "legacy semantic text",
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag semantic",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("legacy semantic text"));
            Assert.That(result.Text, Does.Not.Contain("Tool Broker diagnostics"));
        });
    }

    [Test]
    public void TryHandleDataAgentEvidenceDiagnosticsFallsBackToLegacyRecentStringWhenCacheMissesKind()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.SemanticState,
            "qq:xiayu:2905391496:private:3045846738",
            "qchat_semantic_window",
            "QChat semantic diagnostics",
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentEvidence: "legacy evidence text",
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("legacy evidence text"));
            Assert.That(result.Text, Does.Not.Contain("QChat semantic diagnostics"));
        });
    }

    [Test]
    public void TryHandleToolBrokerDiagnosticsFallsBackToLegacyTraceWhenCacheMissesKind()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.DataAgentEvidence,
            "qq:xiayu:2905391496:private:3045846738",
            "dataagent_analysis",
            "DataAgent evidence diagnostics",
            now);
        QChatDiagnosticsRuntimeState state = new(
            RecentToolRouteTrace: "legacy-route-trace",
            RecentDiagnosticsCache: cache,
            SessionKey: "qq:xiayu:2905391496:private:3045846738",
            DiagnosticsNow: now);

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            "/qchat diag toolbroker",
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("legacy-route-trace"));
            Assert.That(result.Text, Does.Not.Contain("DataAgent evidence diagnostics"));
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
