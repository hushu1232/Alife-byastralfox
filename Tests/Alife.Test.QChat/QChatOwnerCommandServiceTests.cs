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
    public async Task TryHandleDiagnosticsCommandAsyncDeniesNonOwnerWithoutRouteLeak()
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
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Message, Does.Contain("Only the owner"));
            Assert.That(sent[0].Message, Does.Not.Contain("session="));
            Assert.That(sent[0].Message, Does.Not.Contain("agent=xiayu"));
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
    public async Task TryHandleStatusCommandAsyncDeniesNonOwnerWithoutFormattingStatus()
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
            Assert.That(sent.Single(), Does.Contain("Only the owner"));
            Assert.That(sent.Single(), Does.Not.Contain("should not leak"));
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
    [TestCase("/qchatx route", false)]
    [TestCase("hello /qchat route", false)]
    public void IsDiagnosticsCommandMatchesOnlyQChatCommandPrefix(string text, bool expected)
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
