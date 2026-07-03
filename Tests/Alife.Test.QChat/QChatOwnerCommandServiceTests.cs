using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public class QChatOwnerCommandServiceTests
{
    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncSendsOwnerDiagnostics()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        List<string> diagnostics = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat route"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (eventName, _, _, _) => diagnostics.Add(eventName));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Type, Is.EqualTo(OneBotMessageType.Private));
            Assert.That(sent[0].TargetId, Is.EqualTo(3045846738));
            Assert.That(sent[0].Message, Does.Contain("agent=xiayu"));
            Assert.That(sent[0].Message, Does.Contain("bot=2905391496"));
            Assert.That(sent[0].Message, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
            Assert.That(diagnostics, Does.Contain("qchat-diagnostics-command-handled"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncPassesToolBrokerTraceToOwnerDiagnostics()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat diag toolbroker"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (_, _, _, _) => { },
            recentToolRouteTrace: () => "allowed=dataagent_analysis_continue; denied=dataagent_query; reason=route_allowed");

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Message, Does.Contain("Tool Broker"));
            Assert.That(sent[0].Message, Does.Contain("dataagent_analysis_continue"));
            Assert.That(sent[0].Message, Does.Not.Contain("[tool_route_context]"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncPassesRecentDiagnosticsCacheToOwnerDiagnostics()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 8, ttl: TimeSpan.FromMinutes(30));
        cache.Record(
            QChatRecentDiagnosticKind.SemanticState,
            "qq:xiayu:2905391496:private:3045846738",
            "qchat_semantic_window",
            "QChat semantic diagnostics",
            now.AddSeconds(-5));
        cache.Record(
            QChatRecentDiagnosticKind.DataAgentEvidence,
            "qq:xiayu:2905391496:private:3045846738",
            "dataagent_analysis",
            "DataAgent evidence diagnostics",
            now.AddSeconds(-11));

        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat diag recent"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (_, _, _, _) => { },
            recentDiagnosticsCache: cache,
            diagnosticsNow: () => now);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Message, Does.Contain("QChat recent diagnostics"));
            Assert.That(sent[0].Message, Does.Contain("semantic_state_recent=available age_seconds=5"));
            Assert.That(sent[0].Message, Does.Contain("dataagent_evidence_recent=available age_seconds=11"));
            Assert.That(sent[0].Message, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
            Assert.That(sent[0].Message, Does.Not.Contain("reason=recent_diagnostics_empty"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncSendsOwnerDataAgentEvidenceDiagnostics()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/dataagent diag evidence"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (_, _, _, _) => { },
            recentDataAgentEvidence: () => string.Join(Environment.NewLine,
                "DataAgent evidence diagnostics",
                "analysis_confidence=0.781",
                "risk_level=0.287"));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Message, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(sent[0].Message, Does.Contain("analysis_confidence=0.781"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncPassesRecentTraceToOwnerDiagnostics()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/dataagent diag trace"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (_, _, _, _) => { },
            recentDataAgentTrace: () => "DataAgent trace diagnostics\n1 RouteGate Succeeded reason=route_allowed");

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Message, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(sent[0].Message, Does.Contain("RouteGate Succeeded"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncPassesRecentProgressToOwnerDiagnostics()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/dataagent diag progress"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (_, _, _, _) => { },
            recentDataAgentProgress: () => "DataAgent progress diagnostics\nRouteGate:Completed:Succeeded reason=route_allowed");

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Message, Does.Contain("DataAgent progress diagnostics"));
            Assert.That(sent[0].Message, Does.Contain("RouteGate:Completed:Succeeded"));
        });
    }


    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncSilentlyDropsNonOwnerRecentDiagnosticsWithoutInvokingCallbacks()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        QChatRecentDiagnosticsCache cache = new();
        cache.Record(
            QChatRecentDiagnosticKind.SemanticState,
            "qq:xiayu:2905391496:private:100200300",
            "qchat_semantic_window",
            "should not leak",
            now);
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        int recentToolRouteTraceCalls = 0;
        int recentSemanticEstimateCalls = 0;
        int recentDataAgentEvidenceCalls = 0;
        int recentDataAgentTraceCalls = 0;
        int recentDataAgentProgressCalls = 0;
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 100200300,
            RawMessage = "/qchat diag recent"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.PrivateGuest,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (_, _, _, _) => { },
            recentToolRouteTrace: () =>
            {
                recentToolRouteTraceCalls++;
                return "tool callback should not run";
            },
            recentSemanticEstimate: () =>
            {
                recentSemanticEstimateCalls++;
                return "semantic callback should not run";
            },
            recentDataAgentEvidence: () =>
            {
                recentDataAgentEvidenceCalls++;
                return "evidence callback should not run";
            },
            recentDataAgentTrace: () =>
            {
                recentDataAgentTraceCalls++;
                return "trace callback should not run";
            },
            recentDataAgentProgress: () =>
            {
                recentDataAgentProgressCalls++;
                return "progress callback should not run";
            },
            recentDiagnosticsCache: cache,
            diagnosticsNow: () => now);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Is.Empty);
            Assert.That(recentToolRouteTraceCalls, Is.Zero);
            Assert.That(recentSemanticEstimateCalls, Is.Zero);
            Assert.That(recentDataAgentEvidenceCalls, Is.Zero);
            Assert.That(recentDataAgentTraceCalls, Is.Zero);
            Assert.That(recentDataAgentProgressCalls, Is.Zero);
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncSilentlyDropsNonOwnerWithoutRouteLeak()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        List<string> diagnostics = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 100200300,
            RawMessage = "/qchat route"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.PrivateGuest,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (eventName, _, _, _) => diagnostics.Add(eventName));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Is.Empty);
            Assert.That(diagnostics, Does.Contain("qchat-diagnostics-denied"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncSilentlyDropsNonOwnerDataAgentEvidenceDiagnostics()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        List<string> diagnostics = [];
        OneBotMessageEvent messageEvent = new()
        {
            SelfId = 2905391496,
            UserId = 100200300,
            RawMessage = "/dataagent diag evidence"
        };

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            messageEvent,
            QChatSenderRole.PrivateGuest,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (eventName, _, _, _) => diagnostics.Add(eventName),
            recentDataAgentEvidence: () => "should not leak");

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Is.Empty);
            Assert.That(diagnostics, Does.Contain("qchat-diagnostics-denied"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncIgnoresOrdinaryText()
    {
        List<string> sent = [];

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "hello"
            },
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (_, _, message) =>
            {
                sent.Add(message);
                return Task.CompletedTask;
            },
            (_, _, _, _) => { });

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(sent, Is.Empty);
        });
    }

    [Test]
    public async Task TryHandleStatusCommandAsyncSendsOwnerTaskStatus()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
        List<string> diagnostics = [];

        bool handled = await QChatOwnerCommandService.TryHandleStatusCommandAsync(
            new OneBotMessageEvent
            {
                UserId = 3045846738,
                RawMessage = "/tasks"
            },
            QChatSenderRole.Owner,
            () => "task status",
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (eventName, _, _, _) => diagnostics.Add(eventName));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Is.EqualTo(new[] { (OneBotMessageType.Private, 3045846738L, "task status") }));
            Assert.That(diagnostics, Does.Contain("agent-status-command"));
        });
    }

    [Test]
    public async Task TryHandleDiagnosticsCommandAsyncMapsNaturalQChatStatusAlias()
    {
        List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];

        bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
            new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u7fbd\uff0c\u770b\u770bQQ\u804a\u5929\u72b6\u6001"
            },
            QChatSenderRole.Owner,
            new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738
            },
            (type, targetId, message) =>
            {
                sent.Add((type, targetId, message));
                return Task.CompletedTask;
            },
            (_, _, _, _) => { });

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent.Single().TargetId, Is.EqualTo(3045846738));
            Assert.That(sent.Single().Message, Does.Contain("status=online"));
            Assert.That(sent.Single().Message, Does.Contain("agent=xiayu"));
        });
    }

    [Test]
    public async Task TryHandleStatusCommandAsyncSilentlyDropsNonOwnerWithoutFormattingStatus()
    {
        List<string> sent = [];
        int formatCalls = 0;

        bool handled = await QChatOwnerCommandService.TryHandleStatusCommandAsync(
            new OneBotMessageEvent
            {
                UserId = 100200300,
                RawMessage = "/status"
            },
            QChatSenderRole.PrivateGuest,
            () =>
            {
                formatCalls++;
                return "should not leak";
            },
            (_, _, message) =>
            {
                sent.Add(message);
                return Task.CompletedTask;
            },
            (_, _, _, _) => { });

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(formatCalls, Is.Zero);
            Assert.That(sent, Is.Empty);
        });
    }

    [Test]
    public async Task TryHandleStatusCommandAsyncIgnoresOrdinaryText()
    {
        List<string> sent = [];

        bool handled = await QChatOwnerCommandService.TryHandleStatusCommandAsync(
            new OneBotMessageEvent
            {
                UserId = 3045846738,
                RawMessage = "status"
            },
            QChatSenderRole.Owner,
            () => "task status",
            (_, _, message) =>
            {
                sent.Add(message);
                return Task.CompletedTask;
            },
            (_, _, _, _) => { });

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(sent, Is.Empty);
        });
    }

    [TestCase("/approve 42", true, "approve", 42L)]
    [TestCase("/deny 77", true, "deny", 77L)]
    [TestCase(" /APPROVE 5 ", true, "approve", 5L)]
    [TestCase("/approve", false, "", 0L)]
    [TestCase("/approve abc", false, "", 0L)]
    [TestCase("approve 42", false, "", 0L)]
    public void TryParseApprovalCommandParsesOnlyExplicitSlashCommands(
        string text,
        bool expected,
        string expectedCommand,
        long expectedId)
    {
        bool parsed = QChatOwnerCommandService.TryParseApprovalCommand(text, out string command, out long approvalId);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.EqualTo(expected));
            Assert.That(command, Is.EqualTo(expectedCommand));
            Assert.That(approvalId, Is.EqualTo(expectedId));
        });
    }

    [TestCase("/qchat", true)]
    [TestCase("/qchat route", true)]
    [TestCase("  /QCHAT identity  ", true)]
    [TestCase("/dataagent diag evidence", true)]
    [TestCase("/dataagent diag evidence - DataAgent evidence diagnostics", true)]
    [TestCase("/dataagent diagnostics evidence", true)]
    [TestCase("/dataagent diag trace", true)]
    [TestCase("/dataagent diagnostics trace", true)]
    [TestCase("/dataagent diag progress", true)]
    [TestCase("/dataagent diagnostics progress", true)]
    [TestCase("/qchatx route", false)]
    [TestCase("/dataagent", false)]
    [TestCase("/dataagent nope", false)]
    [TestCase("/dataagentx diag evidence", false)]
    [TestCase("hello /qchat route", false)]
    public void IsDiagnosticsCommandMatchesOnlyQChatAndDataAgentDiagnosticsCommands(string text, bool expected)
    {
        Assert.That(QChatOwnerCommandService.IsDiagnosticsCommand(text.Trim()), Is.EqualTo(expected));
    }

    [TestCase("/status", true)]
    [TestCase("/tasks", true)]
    [TestCase("/STATUS", true)]
    [TestCase("/task", false)]
    [TestCase("status", false)]
    public void IsStatusCommandMatchesStatusAndTasks(string text, bool expected)
    {
        Assert.That(QChatOwnerCommandService.IsStatusCommand(text), Is.EqualTo(expected));
    }

    [TestCase("\u7fbd\uff0c\u770b\u770bQQ\u804a\u5929\u72b6\u6001", true)]
    [TestCase("\u770b\u4e00\u4e0bQChat\u72b6\u6001", true)]
    [TestCase("\u73b0\u5728\u94fe\u8def\u72b6\u6001\u600e\u4e48\u6837", true)]
    [TestCase("\u68c0\u67e5\u4e00\u4e0b\u5de5\u7a0b\u72b6\u6001", true)]
    [TestCase("\u7fbd\uff0c\u770b\u770b\u4f60\u73b0\u5728\u7684\u72b6\u6001", false)]
    [TestCase("status", false)]
    public void IsNaturalDiagnosticsStatusCommandMatchesEngineeringStatusOnly(string text, bool expected)
    {
        Assert.That(QChatOwnerCommandService.IsNaturalDiagnosticsStatusCommand(text), Is.EqualTo(expected));
    }

    [TestCase("撤回刚才那条", true)]
    [TestCase("收回上一条", true)]
    [TestCase("删除刚刚发的", true)]
    [TestCase("撤了吧", true)]
    [TestCase("把那条撤了", true)]
    [TestCase("不要撤回，我只是解释", false)]
    [TestCase("他是不是不会撤回", false)]
    [TestCase("hello", false)]
    public void IsRecallCommandDetectsRecallIntent(string text, bool expected)
    {
        Assert.That(QChatOwnerCommandService.IsRecallCommand(text), Is.EqualTo(expected));
    }

    [Test]
    public async Task TryHandleAsyncStopsAfterFirstHandledCommand()
    {
        List<string> calls = [];
        QChatOwnerCommandService service = new([
            context =>
            {
                calls.Add("first");
                return Task.FromResult(false);
            },
            context =>
            {
                calls.Add("second");
                return Task.FromResult(true);
            },
            context =>
            {
                calls.Add("third");
                return Task.FromResult(true);
            }
        ]);

        bool handled = await service.TryHandleAsync(new QChatOwnerCommandContext(
            new OneBotMessageEvent { RawMessage = "/status" },
            QChatSenderRole.Owner,
            "/status"));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second" }));
        });
    }

    [Test]
    public async Task TryHandleAsyncReturnsFalseAfterAllHandlersDecline()
    {
        List<string> calls = [];
        QChatOwnerCommandService service = new([
            context =>
            {
                calls.Add($"{context.SenderRole}:{context.ReadableMessage}");
                return Task.FromResult(false);
            },
            context =>
            {
                calls.Add(context.MessageEvent.RawMessage);
                return Task.FromResult(false);
            }
        ]);

        bool handled = await service.TryHandleAsync(new QChatOwnerCommandContext(
            new OneBotMessageEvent { RawMessage = "hello" },
            QChatSenderRole.GroupMember,
            "hello-readable"));

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "GroupMember:hello-readable", "hello" }));
        });
    }
}
