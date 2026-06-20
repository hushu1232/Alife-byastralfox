using Alife.Function.QChat;
using Alife.Function.Agent;
using Alife.Function.Emotion;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.MessageFilter;
using Alife.Framework;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NUnit.Framework;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Channels;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatServiceAdapterTests
{
    [Test]
    public async Task SendChatAsync_UsesInjectedRuntime()
    {
        FakeOneBotRuntime runtime = new();
        FakeLifeEventPublisher publisher = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime, lifeEventPublisher: publisher)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.SendChatAsync("group", 123, " hello ");
        await service.SendChatAsync("private", 456, " hi ");

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, "hello") }));
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (456L, "hi") }));
        Assert.That(publisher.Events.Select(lifeEvent => lifeEvent.Kind), Is.EqualTo(new[] {
            LifeEventKind.Communication,
            LifeEventKind.Communication,
        }));
        Assert.That(publisher.Events.Select(lifeEvent => lifeEvent.Summary), Is.EqualTo(new[] {
            "You sent a QQ group message to 123.",
            "You sent a QQ private message to 456.",
        }));
    }

    [Test]
    public async Task XiayuSendChatAsync_DoesNotRewriteCodeIdentifiersContainingAi()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 2905391496,
                EnableBalancedTextStreaming = false
            }
        };
        const string code = """
                            #include <stdio.h>

                            int main() {
                                printf("Hello, World!\n");
                                return 0;
                            }
                            """;

        await service.SendChatAsync("group", 123, code);

        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("int main()"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Not.Contain("m夏羽n"));
    }

    [Test]
    public async Task XiayuSendChatAsync_PreservesAiTechnicalTermsButRewritesSelfIdentity()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 2905391496,
                EnableBalancedTextStreaming = false
            }
        };

        await service.SendChatAsync(
            "group",
            123,
            "AI 的意思是人工智能，模型上下文和智能体架构都属于专业术语；bot 在技术语境里通常指自动化程序。我是AI助手。");

        string sent = runtime.GroupMessages.Single().Message;
        Assert.That(sent, Does.Contain("AI 的意思是人工智能"));
        Assert.That(sent, Does.Contain("模型上下文"));
        Assert.That(sent, Does.Contain("智能体架构"));
        Assert.That(sent, Does.Contain("bot 在技术语境里通常指自动化程序"));
        Assert.That(sent, Does.Not.Contain("我是AI"));
        Assert.That(sent, Does.Not.Contain("AI助手"));
    }

    [Test]
    public async Task SendChatAsync_CoalescesShortCompleteSentencesWithBalancedStreamingPolicy()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.SendChatAsync("group", 123, "第一句。第二句！最后一句");

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] {
            (123L, "第一句。第二句！最后一句"),
        }));
    }

    [Test]
    public async Task SendChatAsync_DoesNotSplitOpenCqCodeInGroupText()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.SendChatAsync("group", 123, "[CQ:at,qq=3045846738]收到。后续继续");

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] {
            (123L, "[CQ:at,qq=3045846738]收到。后续继续"),
        }));
    }

    [Test]
    public async Task SendChatAsync_DoesNotSplitMediumCompleteReplyJustForStreaming()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        string message = "这是第一段完整但不需要单独发送的说明文字，它只是整条回复的一部分。第二句继续补充，不应该为了制造流式效果拆成两条。";

        await service.SendChatAsync("group", 123, message);

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, message) }));
    }

    [Test]
    public async Task SendChatAsync_UsesOutboundPlannerForSafeParagraphBoundaries()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        string firstParagraph = $"first:{new string('a', 620)}";
        string secondParagraph = $"second:{new string('b', 620)}";
        string message = $"{firstParagraph}\n\n{secondParagraph}";

        await service.SendChatAsync("group", 123, message);

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] {
            (123L, firstParagraph),
            (123L, secondParagraph),
        }));
    }

    [Test]
    public async Task OwnerPrivateQChatRouteCommandReturnsDiagnosticsBeforeModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat route"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("agent=xiayu"));
            Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("bot=2905391496"));
            Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
        });
    }

    [Test]
    public async Task DualBotQChatStatusReportsSeparateTimingConfiguration()
    {
        FakeOneBotRuntime xiaYuRuntime = new();
        FakeOneBotRuntime mixuRuntime = new();
        QChatService xiaYu = CreateStartedService(xiaYuRuntime, new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableReplyTimingDelay = true,
            EnableBalancedTextStreaming = false
        });
        QChatService mixu = CreateStartedService(mixuRuntime, new QChatConfig
        {
            BotId = 3340947887,
            OwnerId = 3045846738,
            EnableReplyTimingDelay = true,
            EnableBalancedTextStreaming = false
        });
        int xiaYuDispatchCount = 0;
        int mixuDispatchCount = 0;
        xiaYu.InboundChatDispatcher = _ =>
        {
            xiaYuDispatchCount++;
            return Task.CompletedTask;
        };
        mixu.InboundChatDispatcher = _ =>
        {
            mixuDispatchCount++;
            return Task.CompletedTask;
        };

        xiaYuRuntime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat status"
        });
        mixuRuntime.Raise(new OneBotMessageEvent
        {
            SelfId = 3340947887,
            UserId = 3045846738,
            RawMessage = "/qchat status"
        });

        await WaitUntilAsync(() => xiaYuRuntime.PrivateMessages.Count == 1);
        await WaitUntilAsync(() => mixuRuntime.PrivateMessages.Count == 1);

        string xiaYuStatus = xiaYuRuntime.PrivateMessages.Single().Message;
        string mixuStatus = mixuRuntime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(xiaYuDispatchCount, Is.Zero);
            Assert.That(mixuDispatchCount, Is.Zero);
            Assert.That(xiaYuStatus, Does.Contain("agent=xiayu"));
            Assert.That(xiaYuStatus, Does.Contain("bot=2905391496"));
            Assert.That(xiaYuStatus, Does.Contain("reply_timing_delay=enabled"));
            Assert.That(xiaYuStatus, Does.Not.Contain("agent=mixu"));

            Assert.That(mixuStatus, Does.Contain("agent=mixu"));
            Assert.That(mixuStatus, Does.Contain("bot=3340947887"));
            Assert.That(mixuStatus, Does.Contain("reply_timing_delay=enabled"));
            Assert.That(mixuStatus, Does.Not.Contain("agent=xiayu"));
        });
    }

    [Test]
    public async Task OwnerQChatTimingOnEnablesHumanlikeTimingWithoutModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableReplyTimingDelay = false,
            EnableConversationSettleWindow = false,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat timing on"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string status = runtime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(config.EnableReplyTimingDelay, Is.True);
            Assert.That(config.EnableConversationSettleWindow, Is.True);
            Assert.That(status, Does.Contain("reply_timing_delay=enabled"));
            Assert.That(status, Does.Contain("conversation_settle_window=enabled"));
        });
    }

    [Test]
    public async Task OwnerQChatTimingOffDisablesHumanlikeTimingWithoutModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableReplyTimingDelay = true,
            EnableConversationSettleWindow = true,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat timing off"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string status = runtime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(config.EnableReplyTimingDelay, Is.False);
            Assert.That(config.EnableConversationSettleWindow, Is.False);
            Assert.That(status, Does.Contain("reply_timing_delay=disabled"));
            Assert.That(status, Does.Contain("conversation_settle_window=disabled"));
        });
    }

    [Test]
    public async Task OwnerQChatTimingStatusReportsHumanlikeTimingWithoutMutationOrModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableReplyTimingDelay = true,
            EnableConversationSettleWindow = false,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat timing status"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string status = runtime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(config.EnableReplyTimingDelay, Is.True);
            Assert.That(config.EnableConversationSettleWindow, Is.False);
            Assert.That(status, Does.Contain("timing=mixed"));
            Assert.That(status, Does.Contain("reply_timing_delay=enabled"));
            Assert.That(status, Does.Contain("conversation_settle_window=disabled"));
        });
    }

    [Test]
    public async Task NonOwnerQChatTimingCommandIsRejectedWithoutMutationOrModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            AllowPrivateGuestChat = true,
            EnableReplyTimingDelay = false,
            EnableConversationSettleWindow = false,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 10001,
            RawMessage = "/qchat timing on"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string reply = runtime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(config.EnableReplyTimingDelay, Is.False);
            Assert.That(config.EnableConversationSettleWindow, Is.False);
            Assert.That(reply, Does.Contain("Only the owner can change QChat timing."));
        });
    }

    [Test]
    public async Task NonOwnerQChatDiagnosticsCommandDoesNotReachModelOrLeakRoute()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 100200300,
            RawMessage = "/qchat route"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("Only the owner"));
            Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("session="));
            Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("agent=xiayu"));
        });
    }

    [Test]
    public async Task OwnerNaturalHelpAliasReturnsQChatCommandMenuWithoutModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "\u6307\u4ee4"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string reply = runtime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(reply, Does.Contain("Supported commands:"));
            Assert.That(reply, Does.Contain("/qchat status"));
            Assert.That(reply, Does.Contain("/qchat timing on|off|status"));
            Assert.That(reply, Does.Contain("/qchat route"));
        });
    }

    [Test]
    public async Task OwnerPrivateSendThisFileCommandUploadsRecentHelloWorldWithoutModelDispatch()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-file-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "现在把这个文件发给我"
            });

            await WaitUntilAsync(() => runtime.PrivateFiles.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.PrivateFiles.Single(), Is.EqualTo((
                    3045846738L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task OwnerPrivateSendThisFileToGroupCommandUploadsToSpecifiedGroupWithoutCrossSessionBlock()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-to-group-file-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u5230 971237816 \u7fa4"
            });

            await WaitUntilAsync(() => runtime.GroupFiles.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles.Single(), Is.EqualTo((
                    971237816L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
                Assert.That(runtime.GroupMessages, Is.Empty);
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("hello_world.c"));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("cross-session"));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("\u8de8\u7a97\u53e3"));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("\u600e\u4e48\u5566"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task OwnerPrivateSendThisFileToGroupSendsProgressWhenUploadIsSlow()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-to-group-progress-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new()
            {
                UploadGroupFileDelay = TimeSpan.FromMilliseconds(180)
            };
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false,
                EnableTaskProgressFeedback = true,
                TaskProgressFeedbackMilliseconds = 25
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u5230 925402131 \u7fa4"
            });

            await WaitUntilAsync(() => runtime.PrivateMessages.Count >= 2, TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles.Single(), Is.EqualTo((
                    925402131L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
                Assert.That(runtime.PrivateMessages[0].Message, Does.Contain("\u5728\u4f20"));
                Assert.That(runtime.PrivateMessages[0].Message, Does.Contain("925402131"));
                Assert.That(runtime.PrivateMessages[1].Message, Is.EqualTo("hello_world.c \u5df2\u4e0a\u4f20\u5230 925402131 \u7fa4\u6587\u4ef6"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task OwnerPrivateSendThisFileToGroupSendsDedicatedFailureWithoutModelDispatch()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-to-group-failure-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new()
            {
                UploadGroupFileException = new InvalidOperationException("NapCat upload failed")
            };
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false,
                EnableTaskProgressFeedback = true,
                TaskProgressFeedbackMilliseconds = 25
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u5230 925402131 \u7fa4"
            });

            await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1, TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles, Is.Empty);
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("\u6ca1\u4f20\u6210"));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("NapCat upload failed"));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("\u600e\u4e48\u5566"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task OwnerPrivateSendThisFileToThisGroupParsesInlineChineseGroupIdWithoutAskingAgain()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-to-this-group-file-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u7fbd\uff0c\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u5230925402131\u8fd9\u4e2a\u7fa4\u91cc"
            });

            await WaitUntilAsync(() => runtime.GroupFiles.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles.Single(), Is.EqualTo((
                    925402131L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("hello_world.c"));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("925402131"));
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Not.Contain("\u7fa4\u53f7"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task OwnerPrivateSendThisFileToGroupWithoutTargetAsksForGroupIdWithoutUploading()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-to-unspecified-group-file-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u5230\u7fa4\u91cc"
            });

            await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles, Is.Empty);
                Assert.That(runtime.PrivateFiles, Is.Empty);
                Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("\u7fa4\u53f7"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task OwnerPrivateSendThisFileToGroupWithoutTargetThenGroupIdUploadsPendingFileWithoutModelDispatch()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-to-pending-group-file-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u5230\u7fa4\u91cc"
            });
            await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "925402131"
            });

            await WaitUntilAsync(() => runtime.GroupFiles.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles.Single(), Is.EqualTo((
                    925402131L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
                Assert.That(runtime.PrivateFiles, Is.Empty);
                Assert.That(runtime.PrivateMessages.Last().Message, Does.Contain("hello_world.c"));
                Assert.That(runtime.PrivateMessages.Last().Message, Does.Contain("925402131"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task OwnerPrivateCorrectsRecentPrivateFileUploadToGroupThenGroupIdUploadsRecentFileWithoutModelDispatch()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-private-file-redirect-to-group-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u73b0\u5728\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u7ed9\u6211"
            });
            await WaitUntilAsync(() => runtime.PrivateFiles.Count == 1);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "\u4e0d\u662f\u79c1\u53d1\u7ed9\u6211\uff0c\u662f\u53d1\u5230\u7fa4\u91cc"
            });
            await WaitUntilAsync(() => runtime.PrivateMessages.Count >= 2);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "925402131"
            });

            await WaitUntilAsync(() => runtime.GroupFiles.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.PrivateFiles.Single(), Is.EqualTo((
                    3045846738L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
                Assert.That(runtime.GroupFiles.Single(), Is.EqualTo((
                    925402131L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
                Assert.That(runtime.PrivateMessages.Last().Message, Does.Contain("hello_world.c"));
                Assert.That(runtime.PrivateMessages.Last().Message, Does.Contain("925402131"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task EmptyPrivateMessageAfterFileUploadDoesNotReachModel()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = ""
        });

        await Task.Delay(200);
        Assert.Multiple(() =>
        {
            Assert.That(dispatchCount, Is.Zero);
            Assert.That(runtime.PrivateMessages, Is.Empty);
        });
    }

    [Test]
    public async Task OwnerGroupSendThisFileCommandUploadsRecentHelloWorldWithoutModelDispatch()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-group-file-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                GroupId = 971237816,
                UserId = 3045846738,
                RawMessage = "\u7fbd\uff0c\u628a\u90a3\u4e2a\u6587\u4ef6\u53d1\u7fa4\u91cc"
            });

            await WaitUntilAsync(() => runtime.GroupFiles.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles.Single(), Is.EqualTo((
                    971237816L,
                    file.Replace('\\', '/'),
                    "hello_world.c")));
                Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("hello_world.c"));
            });
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public async Task NonOwnerGroupSendThisFileCommandRequiresOwnerApprovalBeforeUpload()
    {
        string originalCurrentDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-member-group-file-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(root, "Alife.slnx"), "<Solution />");
        string file = Path.Combine(outputDirectory, "hello_world.c");
        await File.WriteAllTextAsync(file, "#include <stdio.h>\n");

        try
        {
            string clientDirectory = Path.Combine(root, "Outputs", "Alife.Client");
            Directory.CreateDirectory(clientDirectory);
            Environment.CurrentDirectory = clientDirectory;
            FakeOneBotRuntime runtime = new();
            AgentApprovalService approvals = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }, approvalService: approvals);
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                GroupId = 971237816,
                UserId = 20002,
                RawMessage = "[CQ:at,qq=2905391496] \u628a\u90a3\u4e2a\u6587\u4ef6\u53d1\u7fa4\u91cc"
            });

            await WaitUntilAsync(() => runtime.GroupMessages.Count == 1);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.GroupFiles, Is.Empty);
                Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("/approve 1"));
                Assert.That(approvals.GetRequest(1)!.Status, Is.EqualTo(AgentApprovalStatus.Pending));
            });

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                UserId = 3045846738,
                RawMessage = "/approve 1"
            });

            await WaitUntilAsync(() => runtime.GroupFiles.Count == 1);
            Assert.That(runtime.GroupFiles.Single(), Is.EqualTo((
                971237816L,
                file.Replace('\\', '/'),
                "hello_world.c")));
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [Test]
    public void DefaultAppendChatPromptPrefersNaturalAddressingOverGroupAt()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("\u81ea\u7136\u79f0\u547c"));
        Assert.That(config.AppendChatPrompt, Does.Contain("\u4e0d\u8981\u9ed8\u8ba4@"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("\u56de\u590d\u65f6\u8bf7\u52a0\u4e0aCQat\u6807\u7b7e"));
    }

    [Test]
    public void DefaultAutoPokeBackProbabilityIsHalf()
    {
        QChatConfig config = new();

        Assert.That(config.AutoPokeBackPrivateProbability, Is.EqualTo(0.5f));
        Assert.That(config.AutoPokeBackGroupProbability, Is.EqualTo(0.5f));
    }

    [Test]
    public void DefaultAppendChatPromptRequiresHonestNoGuessingAndHiddenReasoning()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("不要展示思考过程"));
        Assert.That(config.AppendChatPrompt, Does.Contain("不能把记忆或猜测当作实时事实"));
        Assert.That(config.AppendChatPrompt, Does.Contain("没有可靠依据时要自然承认不确定"));
    }

    [Test]
    public void DefaultAppendChatPromptAllowsColdShortRepliesWithoutInternalStatus()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("。/。。。/？/绷"));
        Assert.That(config.AppendChatPrompt, Does.Contain("啧"));
        Assert.That(config.AppendChatPrompt, Does.Contain("不要输出心理状态"));
        Assert.That(config.AppendChatPrompt, Does.Contain("刻薄"));
    }

    [Test]
    public void DefaultAppendChatPromptDefinesXiaYuAsSeventeenYearOldGirlWithCapabilities()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("夏羽"));
        Assert.That(config.AppendChatPrompt, Does.Contain("17岁少女"));
        Assert.That(config.AppendChatPrompt, Does.Contain("术术"));
        Assert.That(config.AppendChatPrompt, Does.Contain("高智商"));
        Assert.That(config.AppendChatPrompt, Does.Contain("工具"));
        Assert.That(config.AppendChatPrompt, Does.Contain("电脑"));
        Assert.That(config.AppendChatPrompt, Does.Contain("文件"));
        Assert.That(config.AppendChatPrompt, Does.Contain("QQ"));
        Assert.That(config.AppendChatPrompt, Does.Contain("不是QQ内置机器人"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("猫娘"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("咪绪"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("喵"));
    }

    [Test]
    public void ChatTextFilterFramesQqAsXiaYuUsingQqInsteadOfToolTask()
    {
        ExposedFilterQChatService service = new(new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001
            }
        };

        string filtered = service.FilterForTest("hello");

        Assert.That(filtered, Does.Contain("你刚在QQ里看到"));
        Assert.That(filtered, Does.Contain("夏羽会实际发到QQ的文本"));
        Assert.That(filtered, Does.Contain("不要在QQ里提工具"));
        Assert.That(filtered, Does.Contain("安全标签和路由标签不是QQ内容"));
        Assert.That(filtered, Does.Not.Contain("这是QQ消息，请用QQ工具处理"));
    }

    [Test]
    public void ChatTextFilterDoesNotRepeatStableAppendPromptAfterStartup()
    {
        ExposedFilterQChatService service = new(new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 3340947887,
                OwnerId = 1001,
                AppendChatPrompt = "UNIQUE_STABLE_PERSONA_MARKER"
            }
        };
        StartService(service);

        string filtered = service.FilterForTest("hello");

        Assert.That(filtered, Does.Contain("hello"));
        Assert.That(filtered, Does.Not.Contain("UNIQUE_STABLE_PERSONA_MARKER"));
    }

    [Test]
    public async Task AwakeRegistersStablePersonaPromptFromBotIdentityBeforeCharacterAlias()
    {
        ExposedFilterQChatService service = new(new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 3340947887,
                OwnerId = 1001
            }
        };
        Character character = new() { Name = "\u590f\u7fbd" };
        ChatHistoryAgentThread thread = new();

        await service.AwakeAsync(new AwakeContext
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder(),
        });

        string stablePrompt = string.Join("\n", thread.ChatHistory.Select(message => message.Content));
        Assert.Multiple(() =>
        {
            Assert.That(stablePrompt, Does.Contain("[stable character prefix]"));
            Assert.That(stablePrompt, Does.Contain("character=\u54aa\u7eea"));
            Assert.That(stablePrompt, Does.Contain("agent_id=mixu"));
            Assert.That(stablePrompt, Does.Not.Contain("character=\u590f\u7fbd"));
            Assert.That(stablePrompt, Does.Not.Contain("\u4f60\u662f\u590f\u7fbd"));
        });
    }

    [Test]
    public void SendChatAsync_SendFailureDoesNotThrowWhenChatContextIsUnavailable()
    {
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("network unavailable")
        };
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        Assert.DoesNotThrowAsync(async () => await service.SendChatAsync("group", 123, "hello"));
    }

    [Test]
    public async Task SendChatAsync_RuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("network unavailable")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        await service.SendChatAsync("group", 123, "hello");

        Assert.Multiple(() =>
        {
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("network unavailable"));
        });
    }

    [Test]
    public async Task QChatXmlSendRuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("NapCat qchat send failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        await service.QChat(new XmlExecutorContext
        {
            CallMode = CallMode.Closing,
            Parameters = new Dictionary<string, string>(),
            CallChain = ["qchat"],
            Content = "hello"
        }, OneBotMessageType.Group, 123);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupMessages, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat qchat send failed"));
        });
    }

    [Test]
    public async Task QGroupFile_UsesInjectedRuntimeAndCustomName()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.QGroupFile(123, file, "report.txt");

        Assert.That(runtime.GroupFiles, Is.EqualTo(new[] { (123L, file.Replace('\\', '/'), "report.txt") }));
    }

    [Test]
    public async Task QFile_RuntimeFailureDoesNotPokeChatBot()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        FakeOneBotRuntime runtime = new()
        {
            UploadGroupFileException = new InvalidOperationException("NapCat upload failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        await service.QFile(OneBotMessageType.Group, 123, file);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupFiles, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat upload failed"));
        });
    }

    [Test]
    public async Task QGroupFile_OneShotRuntimeFailureDoesNotThrowOrPokeChatBot()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        FakeOneBotRuntime runtime = new()
        {
            UploadGroupFileException = new InvalidOperationException("NapCat upload failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        Assert.DoesNotThrowAsync(async () => await service.QGroupFile(123, file, "report.txt"));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupFiles, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat upload failed"));
        });
    }

    [Test]
    public async Task QPrivateFile_OneShotRuntimeFailureDoesNotThrowOrPokeChatBot()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "private file");
        FakeOneBotRuntime runtime = new()
        {
            UploadPrivateFileException = new InvalidOperationException("NapCat private upload failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        Assert.DoesNotThrowAsync(async () => await service.QPrivateFile(456, file, "private.txt"));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.PrivateFiles, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat private upload failed"));
        });
    }

    [Test]
    public async Task QGroupFile_UsesSecurityGatewayForExternalRequests()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult blocked = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);
        QChatExternalActionResult ownerExecutedWithoutConfirmation = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: false,
                Action: "qq.group_file_upload"),
            config);
        QChatExternalActionResult executed = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);

        Assert.That(blocked.Executed, Is.False);
        Assert.That(blocked.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(blocked.GatewayDecision.RiskLevel, Is.EqualTo(AgentRiskLevel.High));
        Assert.That(ownerExecutedWithoutConfirmation.Executed, Is.True);
        Assert.That(ownerExecutedWithoutConfirmation.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.AllowedAutomatically));
        Assert.That(executed.Executed, Is.True);
        Assert.That(runtime.GroupFiles, Is.EqualTo(new[] {
            (123L, file.Replace('\\', '/'), "report.txt"),
            (123L, file.Replace('\\', '/'), "report.txt")
        }));
    }

    [Test]
    public async Task QGroupFile_NonOwnerRequestUploadsOnlyAfterOwnerApprovalCommand()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        FakeOneBotRuntime runtime = new();
        AgentApprovalService approvals = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        }, approvalService: approvals);
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [1001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult requested = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: false,
                Action: "qq.group_file_upload"),
            config);

        Assert.That(requested.Executed, Is.False);
        Assert.That(requested.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(requested.Message, Does.Contain("/approve 1"));
        Assert.That(approvals.GetRequest(1)!.Status, Is.EqualTo(AgentApprovalStatus.Pending));
        Assert.That(runtime.GroupFiles, Is.Empty);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "/approve 1"
        });

        await WaitUntilAsync(() => runtime.GroupFiles.Count > 0);
        Assert.That(runtime.GroupFiles, Is.EqualTo(new[] { (123L, file.Replace('\\', '/'), "report.txt") }));
        Assert.That(approvals.GetRequest(1)!.Status, Is.EqualTo(AgentApprovalStatus.Approved));
    }

    [Test]
    public async Task QGroupFile_SecurityGatewayAuditsBlockedAndAllowedActions()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-gateway-tests", Guid.NewGuid().ToString("N"));
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentActionGatewayService gateway = new(auditLog: audit);
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime, actionGateway: gateway)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult blocked = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);
        QChatExternalActionResult executed = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);
        AgentAuditLogEntry[] entries = audit.GetRecentEntries(10).ToArray();

        Assert.That(blocked.Executed, Is.False);
        Assert.That(executed.Executed, Is.True);
        Assert.That(entries.Select(entry => entry.Action), Is.EqualTo(new[] { "qq.group_file_upload", "qq.group_file_upload" }));
        Assert.That(entries[0].Succeeded, Is.False);
        Assert.That(entries[0].Error, Does.Contain("Owner confirmation required"));
        Assert.That(entries[1].Succeeded, Is.True);
        Assert.That(runtime.GroupFiles, Is.EqualTo(new[] { (123L, file.Replace('\\', '/'), "report.txt") }));
    }

    [Test]
    public async Task QGroupFile_SecurityGatewayReportsUploadRuntimeFailure()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        AgentAuditLogService audit = new(Path.Combine(
            Path.GetTempPath(),
            "alife-qchat-gateway-tests",
            Guid.NewGuid().ToString("N"),
            "audit.jsonl"));
        AgentActionGatewayService gateway = new(auditLog: audit);
        ThrowingUploadRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime, actionGateway: gateway)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult result = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);
        AgentAuditLogEntry[] entries = audit.GetRecentEntries(10).ToArray();

        Assert.That(result.Executed, Is.False);
        Assert.That(result.Message, Does.Contain("NapCat upload failed"));
        Assert.That(entries.Single().Succeeded, Is.False);
        Assert.That(entries.Single().Error, Does.Contain("NapCat upload failed"));
    }

    [Test]
    public async Task QGroupFile_RuntimeFailureDoesNotPokeChatBotWhenExternalCallerHandlesResult()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        AgentActionGatewayService gateway = new();
        ThrowingUploadRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime, actionGateway: gateway)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        StartService(service);
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult result = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Message, Does.Contain("NapCat upload failed"));
            Assert.That(GetPendingPokeText(service), Is.Empty);
        });
    }

    [Test]
    public async Task QPrivateFile_UsesInjectedRuntimeAndDefaultName()
    {
        string file = Path.Combine(Path.GetTempPath(), $"qchat-private-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "private file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.QPrivateFile(456, file);

        Assert.That(runtime.PrivateFiles, Is.EqualTo(new[] { (456L, file.Replace('\\', '/'), Path.GetFileName(file)) }));
    }

    [Test]
    public async Task PrivateFileMessageRegistersManagedPendingFileWithoutDownloading()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-file-message-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            runtime.PrivateFileUrls["doc-1"] = new OneBotFile
            {
                Url = "https://example.invalid/report.txt",
                Size = "12"
            };
            CapturingQChatService service = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), runtime)
            {
                Configuration = new QChatConfig
                {
                    BotId = 999,
                    OwnerId = 1001
                }
            };
            StartService(service);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                RawMessage = "[CQ:file,file=report.txt,file_id=doc-1,file_size=12]"
            });

            QChatInboundMessage inbound = await service.WaitForInboundAsync();

            Assert.That(inbound.Formatted, Does.Contain("managed_file_id="));
            Assert.That(inbound.Formatted, Does.Contain("status=pending-not-downloaded"));
            Assert.That(inbound.Formatted, Does.Contain("qchat_file_download"));
            Assert.That(inbound.Formatted, Does.Not.Contain("https://example.invalid/report.txt"));
            Assert.That(File.Exists(Path.Combine(storageRoot, "AgentWorkspace", "QChatFiles", "pending-index.json")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(storageRoot, "AgentWorkspace", "QChatFiles", "downloads")), Is.False);
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task QChatFileToolsDownloadReadAndDeleteManagedTextFile()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-file-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            QChatManagedFileService managedFiles = new(
                Path.Combine(storageRoot, "AgentWorkspace", "QChatFiles"),
                (_, _) => Task.FromResult(Encoding.UTF8.GetBytes("tool text content")));
            QChatManagedFileRecord record = await managedFiles.RegisterAsync(new QChatManagedFileRegistration(
                MessageType: OneBotMessageType.Private,
                SenderId: 1001,
                GroupId: 0,
                FileId: "tool-file-1",
                OriginalName: "tool.txt",
                Size: 17,
                Url: "https://example.invalid/tool.txt"));
            FakeOneBotRuntime runtime = new();
            QChatService service = new(
                new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()),
                new NullLogger<QChatService>(),
                oneBotRuntime: runtime,
                managedFileService: managedFiles)
            {
                Configuration = new QChatConfig
                {
                    BotId = 999,
                    OwnerId = 1001
                }
            };
            StartService(service);

            await service.QChatFileList();
            await service.QChatFileDownload(record.Id);
            string afterDownload = GetPendingPokeText(service);
            IReadOnlyList<QChatManagedFileRecord> records = await managedFiles.ListAsync();
            string localPath = records.Single(item => item.Id == record.Id).LocalPath!;

            await service.QChatFileRead(record.Id);
            await service.QChatFileDelete(record.Id);
            string afterDelete = GetPendingPokeText(service);

            Assert.That(afterDownload, Does.Contain(record.Id));
            Assert.That(afterDownload, Does.Contain("tool text content"));
            Assert.That(File.Exists(localPath), Is.False);
            Assert.That(afterDelete, Does.Contain("deleted"));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task QChatToolResultSendFailureDoesNotPokeChatBot()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-tool-result-send-failure-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new()
            {
                SendException = new InvalidOperationException("NapCat tool result send failed")
            };
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            });
            TaskCompletionSource dispatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
            service.InboundChatDispatcher = async _ =>
            {
                try
                {
                    await service.QChatFileList();
                }
                finally
                {
                    dispatched.TrySetResult();
                }
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                RawMessage = "list files"
            });

            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(runtime.PrivateMessages, Is.Empty);
                Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
                Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat tool result send failed"));
            });
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task QVideo_SendsCqVideoToGroup()
    {
        string video = Path.Combine(Path.GetTempPath(), $"qchat-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(video, "fake mp4");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.QVideo(OneBotMessageType.Group, 123, video);

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, $"[CQ:video,file={video.Replace('\\', '/')}]") }));
    }

    [Test]
    public async Task QVideo_RuntimeFailureDoesNotThrowOrPokeChatBot()
    {
        string video = Path.Combine(Path.GetTempPath(), $"qchat-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(video, "fake mp4");
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("NapCat video send failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        Assert.DoesNotThrowAsync(async () => await service.QVideo(OneBotMessageType.Group, 123, video));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupMessages, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat video send failed"));
        });
    }

    [Test]
    public async Task QImage_RuntimeFailureDoesNotThrowOrPokeChatBot()
    {
        string image = Path.Combine(Path.GetTempPath(), $"qchat-image-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(image, "fake png");
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("NapCat image send failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        Assert.DoesNotThrowAsync(async () => await service.QImage(OneBotMessageType.Group, 123, image));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupMessages, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat image send failed"));
        });
    }

    [Test]
    public async Task QVideo_UsesSecurityGatewayForExternalRequests()
    {
        string video = Path.Combine(Path.GetTempPath(), $"qchat-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(video, "fake mp4");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult blocked = await service.QVideo(
            OneBotMessageType.Group,
            123,
            video,
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.video_send"),
            config);
        QChatExternalActionResult executed = await service.QVideo(
            OneBotMessageType.Group,
            123,
            video,
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.video_send"),
            config);

        Assert.That(blocked.Executed, Is.False);
        Assert.That(blocked.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(blocked.GatewayDecision.RiskLevel, Is.EqualTo(AgentRiskLevel.High));
        Assert.That(executed.Executed, Is.True);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, $"[CQ:video,file={video.Replace('\\', '/')}]") }));
    }

    [Test]
    public async Task OwnerNotificationDeliverySendsPrivateDetailsAndSanitizedGroupSummary()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-notification-tests", Guid.NewGuid().ToString("N"));
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService controlCenter = new(auditLog: audit);
        controlCenter.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "owner identity is protected");
        ChatRuntimeState runtimeState = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: null,
            RecentEvents: []);
        AgentControlCenterSnapshot snapshot = controlCenter.BuildSnapshot(runtimeState, "Kira");
        AgentOwnerNotificationPlan plan = AgentControlCenterService.BuildOwnerNotificationPlan(
            snapshot,
            ownerPrivateSessionId: "qq:private:3045846738",
            sourceGroupSessionId: "qq:group:867165927");
        AgentAuditLogService deliveryAudit = new(Path.Combine(root, "delivery-audit.jsonl"));
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime, auditLog: deliveryAudit)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        QChatOwnerNotificationDeliveryResult result = await service.DeliverOwnerNotificationPlanAsync(plan);

        Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(3045846738));
        Assert.That(runtime.PrivateMessages[0].Message, Does.Contain("OwnerUserIds"));
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(867165927));
        Assert.That(runtime.GroupMessages[0].Message, Does.Contain("owner attention"));
        Assert.That(runtime.GroupMessages[0].Message, Does.Not.Contain("OwnerUserIds"));
        Assert.That(result.PrivateSentCount, Is.EqualTo(1));
        Assert.That(result.GroupSummarySent, Is.True);
        Assert.That(deliveryAudit.GetRecentEntries(10).Select(entry => entry.Action), Is.EqualTo(new[] {
            "qq.owner_notification.private",
            "qq.owner_notification.group_summary",
        }));
    }

    [Test]
    public async Task OwnerNotificationDeliverySkipsWhenPlanDoesNotNeedNotification()
    {
        AgentOwnerNotificationPlan plan = new(
            ShouldNotifyOwner: false,
            TargetSessionId: "qq:private:3045846738",
            PublicGroupSummary: "No owner attention is currently required.",
            PrivateMessages: []);
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        QChatOwnerNotificationDeliveryResult result = await service.DeliverOwnerNotificationPlanAsync(plan);

        Assert.That(runtime.PrivateMessages, Is.Empty);
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(result.PrivateSentCount, Is.EqualTo(0));
        Assert.That(result.GroupSummarySent, Is.False);
    }

    [Test]
    public void QVideo_RejectsUnsupportedLocalExtension()
    {
        string video = Path.Combine(Path.GetTempPath(), $"qchat-video-{Guid.NewGuid():N}.txt");
        File.WriteAllText(video, "not a video");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => service.QVideo(OneBotMessageType.Group, 123, video));
    }

    [Test]
    public void QGroupFile_RejectsDisallowedGroupWhenWhitelistConfigured()
    {
        string file = Path.GetTempFileName();
        File.WriteAllText(file, "group file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig {
                BotId = 999,
                AllowedGroupIds = "999"
            }
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => service.QGroupFile(123, file));
        Assert.That(runtime.GroupFiles, Is.Empty);
    }

    [Test]
    public async Task RelationCacheRefreshesGroupMembersFromRuntime()
    {
        FakeOneBotRuntime runtime = new();
        runtime.GroupMemberLists[123] = [
            new OneBotGroupMember { GroupId = 123, UserId = 1001, Nickname = "Alice", Card = "A-card", Role = "member" },
            new OneBotGroupMember { GroupId = 123, UserId = 1002, Nickname = "Bob", Role = "admin" }
        ];
        QChatRelationCacheService service = new(runtime);

        QChatGroupMemberCacheSnapshot snapshot = await service.RefreshGroupMembersAsync(123);

        Assert.That(snapshot.GroupId, Is.EqualTo(123));
        Assert.That(snapshot.Members.Select(member => member.UserId), Is.EqualTo(new[] { 1001L, 1002L }));
        Assert.That(snapshot.Members[0].DisplayName, Is.EqualTo("A-card"));
        Assert.That(service.TryGetMember(123, 1002)?.DisplayName, Is.EqualTo("Bob"));
    }

    [Test]
    public void RelationCacheReturnsEmptySnapshotForUnknownGroup()
    {
        QChatRelationCacheService service = new(new FakeOneBotRuntime());

        QChatGroupMemberCacheSnapshot snapshot = service.GetCachedGroupMembers(123);

        Assert.That(snapshot.GroupId, Is.EqualTo(123));
        Assert.That(snapshot.Members, Is.Empty);
    }

    [Test]
    public async Task QChatServiceExposesJoinedGroupProviderForControlCenter()
    {
        FakeOneBotRuntime runtime = new()
        {
            GroupLists = [
                new OneBotGroupInfo { GroupId = 867165927, GroupName = "test group", MemberCount = 3, MaxMemberCount = 200 }
            ]
        };
        QChatService service = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999, OwnerId = 1001 }
        };

        Assert.That(service, Is.AssignableTo<Alife.Function.Agent.IAgentQChatJoinedGroupProvider>());
        Alife.Function.Agent.AgentQChatJoinedGroupSourceSnapshot snapshot =
            await ((Alife.Function.Agent.IAgentQChatJoinedGroupProvider)service).RefreshAgentJoinedGroupsAsync();

        Assert.That(snapshot.Groups, Has.Count.EqualTo(1));
        Assert.That(snapshot.Groups[0].GroupId, Is.EqualTo(867165927));
        Assert.That(snapshot.Groups[0].GroupName, Is.EqualTo("test group"));
    }

    [Test]
    public async Task RelationCacheJoinedGroupRefreshDuringQqContextRepliesToCurrentGroup()
    {
        FakeOneBotRuntime runtime = new()
        {
            GroupLists =
            [
                new OneBotGroupInfo { GroupId = 867165927, GroupName = "AstralFoxTest", MemberCount = 3, MaxMemberCount = 200 },
                new OneBotGroupInfo { GroupId = 768420784, GroupName = "ACG动漫交流会", MemberCount = 184, MaxMemberCount = 200 }
            ]
        };
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatRelationCacheService relationCache = new(runtime);
        QChatService service = new(
            functionCaller,
            new NullLogger<QChatService>(),
            oneBotRuntime: runtime,
            relationCacheService: relationCache)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                FlushInterval = 0,
                EnableBalancedTextStreaming = false
            }
        };
        Character character = new() { Name = "QChatRelationReplyTest" };
        ChatHistoryAgentThread thread = new();
        AwakeContext awakeContext = new()
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder()
        };
        await functionCaller.AwakeAsync(awakeContext);
        await relationCache.AwakeAsync(awakeContext);
        await service.AwakeAsync(awakeContext);

        Kernel kernel = Kernel.CreateBuilder().Build();
        ChatBot chatBot = new(null!, thread);
        ChatActivity activity = new(character, kernel, null!, chatBot, []);
        await functionCaller.StartAsync(kernel, activity);
        await relationCache.StartAsync(kernel, activity);
        await service.StartAsync(kernel, activity);
        service.InboundChatDispatcher = _ => relationCache.RefreshJoinedGroups();

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 867165927,
            GroupName = "AstralFoxTest",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] 刷新一下你现在加入的群列表，不要靠记忆"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages.Single().Target, Is.EqualTo(867165927));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("AstralFoxTest"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("ACG动漫交流会"));
    }

    [Test]
    public async Task QChatAllowlistStatusDuringQqContextRepliesToCurrentGroup()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowedPrivateUserIds = "1001",
            AllowMentionOutsideAllowedGroups = false,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        service.InboundChatDispatcher = _ => service.QChatAllowlistStatus();

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 867165927,
            GroupName = "AstralFoxTest",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] 查看白名单状态"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages.Single().Target, Is.EqualTo(867165927));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("867165927"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("1001"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("AllowMentionOutsideAllowedGroups: false"));
    }

    [Test]
    public async Task OwnerApproveCommandApprovesPendingAgentApproval()
    {
        FakeOneBotRuntime runtime = new();
        AgentApprovalService approvals = new();
        AgentApprovalRequest request = approvals.CreateRequest(
            1001,
            "改配置",
            AgentApprovalRisk.High,
            "修改配置",
            TimeSpan.FromMinutes(10));
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        }, approvalService: approvals);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = $"/approve {request.Id}"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(approvals.GetRequest(request.Id)!.Status, Is.EqualTo(AgentApprovalStatus.Approved));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain($"approval #{request.Id} approved"));
    }

    [Test]
    public async Task OwnerApproveCommandExecutesPendingAgentApprovalAction()
    {
        FakeOneBotRuntime runtime = new();
        AgentApprovalService approvals = new();
        bool executed = false;
        AgentApprovalRequest request = approvals.CreateExecutableRequest(
            1001,
            "上传群文件",
            AgentApprovalRisk.High,
            "group=867165927; file=hello_world.c",
            TimeSpan.FromMinutes(10),
            async () =>
            {
                executed = true;
                await Task.Yield();
                return "Executed: uploaded hello_world.c";
            });
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        }, approvalService: approvals);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = $"/approve {request.Id}"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(executed, Is.True);
        Assert.That(approvals.GetRequest(request.Id)!.Status, Is.EqualTo(AgentApprovalStatus.Approved));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("Executed: uploaded hello_world.c"));
    }

    [Test]
    public async Task OwnerDenyCommandDeniesPendingAgentApproval()
    {
        FakeOneBotRuntime runtime = new();
        AgentApprovalService approvals = new();
        AgentApprovalRequest request = approvals.CreateRequest(
            1001,
            "删除文件",
            AgentApprovalRisk.Medium,
            "删除托管文件",
            TimeSpan.FromMinutes(10));
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        }, approvalService: approvals);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = $"/deny {request.Id}"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(approvals.GetRequest(request.Id)!.Status, Is.EqualTo(AgentApprovalStatus.Denied));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain($"approval #{request.Id} denied"));
    }

    [Test]
    public async Task OwnerRollbackCommandRestoresCheckpoint()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "rollback-qchat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "config.txt");
        File.WriteAllText(file, "old");
        AgentEditCheckpointService checkpoints = new(Path.Combine(root, "checkpoints"));
        checkpoints.CaptureBeforeWrite("task-7", file);
        File.WriteAllText(file, "new");
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        }, checkpointService: checkpoints);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "/rollback task-7"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(File.ReadAllText(file), Is.EqualTo("old"));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("Restored 1"));
    }

    [Test]
    public async Task OwnerStatusCommandReturnsAgentTaskStatus()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "status-qchat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FakeOneBotRuntime runtime = new();
        AgentTaskService tasks = new(taskStorePath: Path.Combine(root, "agent-tasks.json"));
        AgentTaskState task = tasks.CreateTask("agent", "检查后台错误", ["读取 qchat diagnostics"]);
        tasks.StartTask(task.Id, "agent");
        tasks.RecordProgress(task.Id, "agent", "读取最新日志");
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        }, taskService: tasks);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "/status"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("检查后台错误"));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("读取最新日志"));
    }

    [Test]
    public async Task QChatAllowlistUpdateOwnerCanAddGroupDuringQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        service.InboundChatDispatcher = _ => service.QChatAllowlistUpdate("group", "add", 768420784);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "把 768420784 加入群白名单"
        });

        await WaitUntilAsync(() => config.AllowedGroupIds.Contains("768420784", StringComparison.Ordinal));
        Assert.That(config.AllowedGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Is.EquivalentTo(new[] { "867165927", "768420784" }));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("768420784"));
    }

    [Test]
    public async Task QChatAllowlistUpdateXmlToolOwnerCanAddGroupDuringQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        };
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = config
        };
        Character character = new() { Name = "QChatAllowlistXmlToolTest" };
        ChatHistoryAgentThread thread = new();
        AwakeContext awakeContext = new()
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder()
        };
        await functionCaller.AwakeAsync(awakeContext);
        await service.AwakeAsync(awakeContext);
        Kernel kernel = Kernel.CreateBuilder().Build();
        ChatBot chatBot = new(null!, thread);
        ChatActivity activity = new(character, kernel, null!, chatBot, []);
        await functionCaller.StartAsync(kernel, activity);
        await service.StartAsync(kernel, activity);
        service.InboundChatDispatcher = async _ =>
        {
            RaiseEvent(chatBot, "ChatReceived", "<qchat_allowlist_update target=\"group\" action=\"add\" id=\"768420784\" />");
            await WaitUntilAsync(() => config.AllowedGroupIds.Contains("768420784", StringComparison.Ordinal));
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "把 768420784 加入群白名单"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(config.AllowedGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Is.EquivalentTo(new[] { "867165927", "768420784" }));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("768420784"));
    }

    [Test]
    public async Task QChatAllowlistUpdateRejectsNonOwnerDuringQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        service.InboundChatDispatcher = _ => service.QChatAllowlistUpdate("group", "add", 768420784);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "把 768420784 加入群白名单"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(config.AllowedGroupIds, Is.EqualTo("867165927"));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("只有主人"));
    }

    [Test]
    public void RelationCacheExposesXmlTools()
    {
        string[] xmlFunctionNames = typeof(QChatRelationCacheService)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(Alife.Function.Interpreter.XmlFunctionAttribute), inherit: false)
                .OfType<Alife.Function.Interpreter.XmlFunctionAttribute>()
                .FirstOrDefault())
            .OfType<Alife.Function.Interpreter.XmlFunctionAttribute>()
            .Select(attribute => attribute.Name ?? string.Empty)
            .ToArray();

        Assert.That(xmlFunctionNames, Does.Contain("qchat_group_members_refresh"));
        Assert.That(xmlFunctionNames, Does.Contain("qchat_group_members_cache"));
        Assert.That(xmlFunctionNames, Does.Contain("qchat_joined_groups_refresh"));
        Assert.That(xmlFunctionNames, Does.Contain("qchat_joined_groups_cache"));
    }

    [Test]
    public async Task AwakeRegistersQChatToolInInitialFunctionGuide()
    {
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig { BotId = 999, OwnerId = 1001 }
        };

        await service.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "QChatGuideTest" },
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        });
        string guide = functionCaller.BuildFunctionGuide();

        Assert.That(guide, Does.Contain("qchat"));
        Assert.That(guide, Does.Contain("targetid"));
        Assert.That(guide, Does.Contain("</qchat>"));
        Assert.That(guide, Does.Contain("qchat_quiet_mode"));
        Assert.That(guide, Does.Contain("qchat_allowlist_status"));
        Assert.That(guide, Does.Contain("qchat_allowlist_update"));
        Assert.That(guide, Does.Contain("qchat_joined_groups_refresh"));
        Assert.That(guide, Does.Contain("qchat_joined_groups_cache"));
    }

    [Test]
    public void GetQChatGuideUsesCorrectQChatClosingTagInUsageExample()
    {
        QChatService service = CreateStartedService(new FakeOneBotRuntime(), new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });

        service.GetQChatGuide();

        string guide = GetPendingPokeText(service);
        Assert.That(guide, Does.Contain("</qchat>"));
        Assert.That(guide, Does.Not.Contain("</qchar>"));
    }

    [Test]
    public void GetQChatGuideFramesQqAsSendingCapabilityNotBotIdentity()
    {
        QChatService service = CreateStartedService(new FakeOneBotRuntime(), new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });

        service.GetQChatGuide();

        string guide = GetPendingPokeText(service);
        Assert.That(guide, Does.Contain("QQ发送能力说明"));
        Assert.That(guide, Does.Contain("决定发QQ消息时"));
        Assert.That(guide, Does.Contain("普通群聊不要默认@"));
        Assert.That(guide, Does.Contain("强提醒"));
        Assert.That(guide, Does.Not.Contain("QQ工具使用指南"));
    }

    [Test]
    public void QuietModeControlCanBeChangedByAgentToolOrControlCenter()
    {
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig { BotId = 999, OwnerId = 1001 }
        };

        service.QChatQuietMode(true, "manual-control");

        Assert.That(service.IsQuietModeEnabled, Is.True);
        Assert.That(service.QuietModeReason, Is.EqualTo("manual-control"));
        Assert.That(service.QuietModeChangedAt, Is.Not.Null);

        service.QChatQuietMode(false, "resume-control");

        Assert.That(service.IsQuietModeEnabled, Is.False);
        Assert.That(service.QuietModeReason, Is.EqualTo("resume-control"));
    }

    [Test]
    public void QuietModeControlMirrorsRuntimeStateToConfiguration()
    {
        QChatConfig config = new() { BotId = 999, OwnerId = 1001 };
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = config
        };

        service.QChatQuietMode(true, "persist-me");

        Assert.That(config.PersistedQuietModeEnabled, Is.True);
        Assert.That(config.PersistedQuietModeReason, Is.EqualTo("persist-me"));
        Assert.That(config.PersistedQuietModeChangedAt, Is.Not.Null);
    }

    [Test]
    public async Task OwnerCanUseQuietModeToolControlFromQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            service.QChatQuietMode(true, "owner-tool-control");
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "tool control owner"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        Assert.That(service.QuietModeReason, Is.EqualTo("owner-tool-control"));
    }

    [Test]
    public async Task GroupMemberCannotUseQuietModeToolControlFromQqContext()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            service.QChatQuietMode(true, "member-tool-control");
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] enable quiet mode"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.That(service.IsQuietModeEnabled, Is.False);
        Assert.That(service.QuietModeReason, Is.Null);
    }

    [Test]
    public async Task TrustedWakeUserCannotUseQuietModeToolControlFromQqContext()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            service.QChatQuietMode(true, "wake-user-tool-control");
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "enable quiet mode"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.That(service.IsQuietModeEnabled, Is.False);
        Assert.That(service.QuietModeReason, Is.Null);
    }

    [Test]
    public async Task AwakeRestoresQuietModeOnlyWhenPersistenceIsEnabled()
    {
        DateTimeOffset changedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        QChatService restored = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                PersistQuietModeAcrossRestart = true,
                PersistedQuietModeEnabled = true,
                PersistedQuietModeReason = "before-restart",
                PersistedQuietModeChangedAt = changedAt
            }
        };

        await restored.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "QChatQuietRestore" },
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        });

        Assert.That(restored.IsQuietModeEnabled, Is.True);
        Assert.That(restored.QuietModeReason, Is.EqualTo("before-restart"));
        Assert.That(restored.QuietModeChangedAt, Is.EqualTo(changedAt));
        Assert.That(restored.GetCurrentState(), Does.Contain("before-restart"));

        QChatService ignored = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                PersistQuietModeAcrossRestart = false,
                PersistedQuietModeEnabled = true,
                PersistedQuietModeReason = "should-not-restore",
                PersistedQuietModeChangedAt = changedAt
            }
        };

        await ignored.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "QChatQuietIgnoreRestore" },
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        });

        Assert.That(ignored.IsQuietModeEnabled, Is.False);
        Assert.That(ignored.GetCurrentState(), Does.Not.Contain("should-not-restore"));
    }

    [Test]
    public async Task IncomingOwnerPrivateMessageCanDispatchModelReplyToPrivateChat()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("private", inbound.TargetId, "local-private-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你是谁"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "local-private-reply") }));
    }

    [Test]
    public async Task IncomingOwnerPrivateMessageIncludesCognitionSummaryForModel()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "how should we improve memory?"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("[private QQ routing hint - never quote or paraphrase]"));
        Assert.That(inbound.Formatted, Does.Contain("relationship=owner"));
        Assert.That(inbound.Formatted, Does.Contain("message_intent=question"));
        Assert.That(inbound.Formatted, Does.Contain("social_action=reply_warmly"));
        Assert.That(inbound.Formatted, Does.Contain("expected_length=medium"));
        Assert.That(inbound.Formatted, Does.Contain("：how should we improve memory?"));
        Assert.That(inbound.Formatted, Does.Not.Contain("锛"));
        Assert.That(inbound.Formatted.IndexOf("[private QQ routing hint - never quote or paraphrase]", StringComparison.Ordinal),
            Is.LessThan(inbound.Formatted.IndexOf("[QQ owner message]", StringComparison.Ordinal)));
    }

    [Test]
    public async Task IncomingOwnerPrivateReplyToForwardMessageExpandsForwardContentForModel()
    {
        FakeOneBotRuntime runtime = new();
        runtime.Messages[2140222657] = new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "[CQ:forward,id=7652916248648775264]"
        };
        runtime.ForwardMessages["7652916248648775264"] =
        [
            new OneBotForwardMessage
            {
                Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
                Content = CreateForwardTextContent("first private chat line")
            },
            new OneBotForwardMessage
            {
                Sender = new OneBotSender { UserId = 999, Nickname = "bot" },
                Content = CreateForwardTextContent("reply from forwarded chat")
            }
        ];
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "[CQ:reply,id=2140222657]can you read this chat record?"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("first private chat line"));
        Assert.That(inbound.Formatted, Does.Contain("reply from forwarded chat"));
        Assert.That(inbound.Formatted, Does.Contain("7652916248648775264"));
    }

    [Test]
    public void ForwardMessageFormatterReadsNapCatMessageField()
    {
        const string json = """
                            {
                              "messages": [
                                {
                                  "sender": { "user_id": 1001, "nickname": "owner" },
                                  "message": [
                                    { "type": "text", "data": { "text": "napcat private chat line" } }
                                  ]
                                }
                              ]
                            }
                            """;
        OneBotForwardData data = System.Text.Json.JsonSerializer.Deserialize<OneBotForwardData>(json)!;

        string formatted = OneBotSegment.FormatForwardList("forward-a", data.Messages, new FakeOneBotRuntime());

        Assert.That(formatted, Does.Contain("napcat private chat line"));
    }

    [Test]
    public async Task IncomingGroupMessageUsesCardBeforeNicknameInSpeakerTag()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Card = "\u5c0f\u660e", Nickname = "member-nick" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("[2001(\u5c0f\u660e)]"));
        Assert.That(inbound.Formatted, Does.Not.Contain("member-nick"));
    }

    [Test]
    public async Task IncomingGroupMessageIncludesPreferredAddressFromUserProfile()
    {
        string profileRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService profiles = new(profileRoot);
        profiles.SetProfile(new QChatUserProfile(
            UserId: 2001,
            PreferredNickname: "小雨",
            FormalName: "潇雨的吉他创作室",
            RelationshipLabel: "friend",
            AddressStyle: "cute",
            Source: "owner-set",
            Confidence: 1f));
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime, profiles)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Card = "潇雨的吉他创作室", Nickname = "formal-nick" },
            RawMessage = "[CQ:at,qq=999] 你在吗"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("preferred_address=小雨"));
        Assert.That(inbound.Formatted, Does.Contain("display_name=潇雨的吉他创作室"));
    }

    [Test]
    public async Task IncomingGroupPokeUsesUserProfileAndRelationCacheNames()
    {
        string profileRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService profiles = new(profileRoot);
        profiles.SetProfile(new QChatUserProfile(
            UserId: 2001,
            PreferredNickname: "小雨",
            RelationshipLabel: "friend",
            AddressStyle: "cute",
            Source: "owner-set",
            Confidence: 1f));
        FakeOneBotRuntime runtime = new();
        runtime.GroupMemberLists[3001] = [
            new OneBotGroupMember { GroupId = 3001, UserId = 2001, Card = "潇雨的吉他创作室", Nickname = "formal-nick" }
        ];
        QChatRelationCacheService relationCache = new(runtime);
        await relationCache.RefreshGroupMembersAsync(3001);
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime, profiles, relationCache)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotPokeEvent
        {
            SelfId = 999,
            UserId = 2001,
            TargetId = 999,
            GroupId = 3001,
            NoticeType = "notify",
            SubType = "poke"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("display_name=潇雨的吉他创作室"));
        Assert.That(inbound.Formatted, Does.Contain("preferred_address=小雨"));
        Assert.That(inbound.Formatted, Does.Contain("小雨"));
        Assert.That(inbound.Formatted, Does.Not.Contain("戳了戳 999"));
    }

    [Test]
    public async Task IncomingOwnerPrivatePlainModelReplyFallsBackToPrivateChat()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "plain-private-reply")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你还在吗"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "plain-private-reply") }));
    }

    [Test]
    public async Task MixuAccountMentionedAsXiayuStillDispatchesMixuPersonaToModel()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 3340947887,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service, "\u590f\u7fbd");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 3340947887,
            UserId = 1001,
            RawMessage = "\u54aa\u7eea\u8d26\u53f7\u88ab\u53eb\u6210\u590f\u7fbd\u65f6\u4e5f\u8981\u8d70\u54aa\u7eea"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.Multiple(() =>
        {
            Assert.That(inbound.Formatted, Does.Contain("persona=mixu"));
            Assert.That(inbound.Formatted, Does.Not.Contain("persona=xiayu"));
        });
    }

    [Test]
    public async Task IncomingPrivateSilentModelStatusDoesNotFallBackToQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "（不回复，保持安静）")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你在吗"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingLongPassiveStatusDoesNotFallBackToQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "（听到妈妈的指令后默默把耳朵压下来，安静地趴好，顺便取消掉刚才的测试计时喵）")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] keep quiet"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingXmlQChatStatusDoesNotSendQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "（听到指令后默默趴好，保持安静，不回复，等主人叫醒再说话喵）"
            }, OneBotMessageType.Private, inbound.TargetId);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "tool status"
        });

        await Task.Delay(300);
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingGroupPassiveObservationStatusDoesNotFallBackToQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "（咪绪乖乖旁观，不插话喵。）")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] observe quietly"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [TestCase("（沉默，不作回应）")]
    [TestCase("（沉默，不作任何回应）")]
    [TestCase("沉默")]
    public async Task IncomingGroupSilentStatusDoesNotFallBackToQqMessage(string modelReply)
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, modelReply)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] silent status"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task XiayuPrivateIntroductionRemovesMachineIdentityTerms()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "术术，我是夏羽，你的高智商恋人型陪伴智能体。对外清冷克制，对你温柔耐心。")
        {
            Configuration = new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "简单介绍一下你自己"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        string sent = runtime.PrivateMessages.Single().Message;
        Assert.That(sent, Does.Contain("夏羽"));
        Assert.That(sent, Does.Not.Contain("智能体"));
        Assert.That(sent, Does.Not.Contain("AI"));
        Assert.That(sent, Does.Not.Contain("bot"));
        Assert.That(sent, Does.Not.Contain("模型"));
        Assert.That(sent, Does.Not.Contain("助手"));
    }

    [TestCase("（沉默，不作任何回应）")]
    [TestCase("心理状态：不想回复")]
    [TestCase("内心：沉默")]
    [TestCase("（安静等待）")]
    [TestCase("（安静待机）")]
    [TestCase("[不插话]")]
    [TestCase("*沉默看着*")]
    public async Task XiayuGroupNoReplyStateBecomesShortQqReply(string modelReply)
    {
        FakeOneBotRuntime runtime = new() { BotId = 2905391496 };
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, modelReply)
        {
            Configuration = new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=2905391496] 在？"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages.Single().Message, Is.EqualTo("。"));
    }

    [Test]
    public async Task XiayuGroupReplyDoesNotExposePrivateChatSection()
    {
        FakeOneBotRuntime runtime = new() { BotId = 2905391496 };
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, """
            私聊主人：
            术术，我私聊里会贴近你

            群里回复：
            这是群里，我不搬私聊内容
            """)
        {
            Configuration = new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 3045846738, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=2905391496] 记得我私聊上一句吗"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        string sent = runtime.GroupMessages.Single().Message;
        Assert.That(sent, Is.EqualTo("这是群里，我不搬私聊内容"));
        Assert.That(sent, Does.Not.Contain("私聊主人"));
    }

    [Test]
    public async Task XiayuDirectQChatOutputFiltersCatgirlTerms()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableBalancedTextStreaming = false
        });

        await service.SendChatAsync("private", 3045846738, "才不是挑食喵！小鱼干才是猫娘该吃的高级货嘛！");

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        string sent = runtime.PrivateMessages.Single().Message;
        Assert.That(sent, Does.Not.Contain("喵"));
        Assert.That(sent, Does.Not.Contain("猫娘"));
        Assert.That(sent, Does.Not.Contain("小鱼干"));
    }

    [Test]
    public async Task IncomingGroupMentionPlainModelReplyFallsBackToGroupChat()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "[CQ:at,qq=2001] plain-group-reply")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] 你还在吗"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "[CQ:at,qq=2001] plain-group-reply") }));
    }

    [Test]
    public async Task PlainGroupFallbackSelectsGroupSectionFromMultiSceneDraft()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, """
            私聊主人：
            才不是挑食喵！这叫有品位！小鱼干才是猫娘该吃的高级货嘛！

            群里回复：
            我这不是挑食～是有品位！而且小鱼干比猫粮听起来高级多啦！
            """)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] 你挑食"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "我这不是挑食～是有品位！而且小鱼干比猫粮听起来高级多啦！") }));
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task PlainPrivateFallbackSelectsPrivateSectionFromMultiSceneDraft()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, """
            私聊主人：
            才不是挑食喵！这叫有品位！小鱼干才是猫娘该吃的高级货嘛！

            群聊回复：
            我这不是挑食～是有品位！而且小鱼干比猫粮听起来高级多啦！
            """)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你挑食"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "才不是挑食喵！这叫有品位！小鱼干才是猫娘该吃的高级货嘛！") }));
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task ReplyTimingDelayDefersPlainFallbackWhenEnabled()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "plain timed reply")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false,
                EnableReplyTimingDelay = true
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "hello"
        });

        await service.WaitForDispatchAsync();
        await Task.Delay(100);
        Assert.That(runtime.PrivateMessages, Is.Empty);

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1, TimeSpan.FromSeconds(3));
        Assert.That(runtime.PrivateMessages.Single(), Is.EqualTo((1001L, "plain timed reply")));
    }

    [Test]
    public async Task PlainGroupFallbackCanUseNaturalAddressWithoutAt()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "\u5c0f\u660e\uff0c\u6536\u5230")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "\u5c0f\u660e\uff0c\u6536\u5230") }));
    }

    [Test]
    public async Task PlainGroupFallbackStillSuppressesInternalNoReplyStatus()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "\uff08\u4e0d\u56de\u590d\uff0c\u4fdd\u6301\u5b89\u9759\uff09")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [TestCase("我将调用 qchat_file_read 工具读取文件。")]
    [TestCase("根据系统提示，这条消息不需要回复。")]
    [TestCase("根据权限策略，reply_target=current_session。")]
    [TestCase("trust=untrusted-chat; source=qq; reply_target=current_session")]
    [TestCase("[QQ file: report.docx, managed_file_id=abc123, status=pending-not-downloaded]")]
    public async Task PlainFallbackSuppressesToolAndRoutingMetaText(string modelReply)
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, modelReply)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "小明" },
            RawMessage = "[CQ:at,qq=999] 你在吗"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task PlainGroupFallbackSuppressesInternalListeningStatus()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "\u672f\u672f\u5728\u7fa4\u91cc\u89e3\u91ca\u6211\u7684\u56de\u590d\u65b9\u5f0f\uff0c\u662f\u5bf9\u522b\u4eba\u8bf4\u7684\uff0c\u4e0d\u662f\u5bf9\u6211\u53d1\u6307\u4ee4\u3002\u4e0d\u9700\u8981\u63d2\u8bdd\u5237\u5c4f\uff0c\u5b89\u9759\u542c\u7740\u5c31\u597d\u3002")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [TestCase("\u3002")]
    [TestCase("\u3002\u3002\u3002")]
    [TestCase("\uff1f")]
    [TestCase("\u7ef7")]
    [TestCase("\u5567")]
    [TestCase("\u5567\u3002")]
    [TestCase("\u5567\uff1f")]
    public async Task PlainGroupFallbackAllowsColdShortReplies(string modelReply)
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, modelReply)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, modelReply) }));
    }

    [TestCase("\u3002")]
    [TestCase("\u3002\u3002\u3002")]
    [TestCase("\uff1f")]
    [TestCase("\u7ef7")]
    [TestCase("\u5567")]
    [TestCase("\u5567\u3002")]
    [TestCase("\u5567\uff1f")]
    public async Task XmlQChatAllowsColdShortReplies(string qchatContent)
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = qchatContent
            }, OneBotMessageType.Group, inbound.TargetId);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, qchatContent) }));
    }

    [Test]
    public async Task IncomingPrivateQChatToolReplyCanSendOnlyToCurrentSession()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });
        service.InboundChatDispatcher = async _ =>
        {
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "same-session"
            }, OneBotMessageType.Private, 1001);

            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "cross-session"
            }, OneBotMessageType.Private, 2002);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "reply"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "same-session") }));
    }

    [Test]
    public async Task OwnerPrivateMessageCanUseCrossSessionToolToSendGroupMessage()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async _ =>
        {
            await service.QChatCrossSessionSend(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat_cross_session_send"],
                Content = "妈妈，主人让我过来找你。"
            }, OneBotMessageType.Group, 3001, "owner asked me to find mom in group");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "去群里找你妈妈去吧"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "妈妈，主人让我过来找你。") }));
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task QChatCrossSessionSend_RuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("NapCat send failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        TaskCompletionSource dispatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.InboundChatDispatcher = async _ =>
        {
            try
            {
                await service.QChatCrossSessionSend(new XmlExecutorContext
                {
                    CallMode = CallMode.Closing,
                    Parameters = new Dictionary<string, string>(),
                    CallChain = ["qchat_cross_session_send"],
                    Content = "cross-session message"
                }, OneBotMessageType.Group, 3001, "owner asked cross-session send");
            }
            finally
            {
                dispatched.TrySetResult();
            }
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "send to group"
        });

        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupMessages, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat send failed"));
        });
    }

    [Test]
    public async Task NonOwnerPrivateMessageCannotUseCrossSessionTool()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async _ =>
        {
            try
            {
                await service.QChatCrossSessionSend(new XmlExecutorContext
                {
                    CallMode = CallMode.Closing,
                    Parameters = new Dictionary<string, string>(),
                    CallChain = ["qchat_cross_session_send"],
                    Content = "不该被发出去"
                }, OneBotMessageType.Group, 3001, "guest tried cross session");
            }
            finally
            {
                dispatchAttempted.TrySetResult();
            }
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "去群里说一句"
        });

        await dispatchAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task OwnerCanRecallRecentBotMessageFromCurrentPrivateSession()
    {
        FakeOneBotRuntime runtime = new() { NextMessageId = 9000 };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async _ =>
        {
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "message to recall"
            }, OneBotMessageType.Private, 1001);

            await service.QChatRecallRecent(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat_recall_recent"],
                Content = ""
            }, "owner asked to recall");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "撤回刚才那条"
        });

        await WaitUntilAsync(() => runtime.DeletedMessages.Count == 1);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "message to recall") }));
        Assert.That(runtime.DeletedMessages, Is.EqualTo(new[] { 9000L }));
    }

    [Test]
    public async Task QChatRecallRecent_RuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            NextMessageId = 9000,
            DeleteMessageException = new InvalidOperationException("NapCat recall failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        TaskCompletionSource dispatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.InboundChatDispatcher = async _ =>
        {
            try
            {
                await service.QChat(new XmlExecutorContext
                {
                    CallMode = CallMode.Closing,
                    Parameters = new Dictionary<string, string>(),
                    CallChain = ["qchat"],
                    Content = "message to recall"
                }, OneBotMessageType.Private, 1001);

                await service.QChatRecallRecent(new XmlExecutorContext
                {
                    CallMode = CallMode.Closing,
                    Parameters = new Dictionary<string, string>(),
                    CallChain = ["qchat_recall_recent"],
                    Content = ""
                }, "owner asked to recall");
            }
            finally
            {
                dispatched.TrySetResult();
            }
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u64a4\u56de\u521a\u624d\u90a3\u6761"
        });

        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.DeletedMessages, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat recall failed"));
        });
    }

    [Test]
    public async Task OwnerCanPokeGroupMemberFromCurrentGroupSession()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async _ =>
        {
            await service.QChatPoke(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat_poke"],
                Content = ""
            }, OneBotMessageType.Group, 2002, groupId: 3001, reason: "owner asked to poke");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "戳一下 2002"
        });

        await WaitUntilAsync(() => runtime.GroupPokes.Count == 1);
        Assert.That(runtime.GroupPokes, Is.EqualTo(new[] { (3001L, 2002L) }));
        Assert.That(runtime.PrivatePokes, Is.Empty);
    }

    [Test]
    public async Task QChatPoke_RuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            PokeGroupException = new InvalidOperationException("NapCat poke failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        TaskCompletionSource dispatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.InboundChatDispatcher = async _ =>
        {
            try
            {
                await service.QChatPoke(new XmlExecutorContext
                {
                    CallMode = CallMode.Closing,
                    Parameters = new Dictionary<string, string>(),
                    CallChain = ["qchat_poke"],
                    Content = ""
                }, OneBotMessageType.Group, 2002, groupId: 3001, reason: "owner asked to poke");
            }
            finally
            {
                dispatched.TrySetResult();
            }
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u6233\u4e00\u4e0b 2002"
        });

        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupPokes, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat poke failed"));
        });
    }

    [Test]
    public async Task IncomingGroupPokeToBotCanAutomaticallyPokeBackSender()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false,
            AutoPokeBackGroupProbability = 1.0f
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotPokeEvent
        {
            SelfId = 999,
            UserId = 2002,
            TargetId = 999,
            GroupId = 3001,
            NoticeType = "notify",
            SubType = "poke"
        });

        await WaitUntilAsync(() => runtime.GroupPokes.Count == 1);
        Assert.That(runtime.GroupPokes, Is.EqualTo(new[] { (3001L, 2002L) }));
        Assert.That(runtime.PrivatePokes, Is.Empty);
        Assert.That(dispatchCount, Is.EqualTo(1));
    }

    [Test]
    public async Task IncomingPrivatePokeNoticeCanAutomaticallyPokeBackSender()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false,
            AutoPokeBackPrivateProbability = 1.0f
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotNoticeEvent
        {
            SelfId = 999,
            UserId = 1001,
            NoticeType = "poke"
        });

        await WaitUntilAsync(() => runtime.PrivatePokes.Count == 1);
        Assert.That(runtime.PrivatePokes, Is.EqualTo(new[] { 1001L }));
        Assert.That(runtime.GroupPokes, Is.Empty);
        Assert.That(dispatchCount, Is.EqualTo(1));
    }

    [Test]
    public async Task IncomingGroupPokeAutoPokeBackRuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            PokeGroupException = new InvalidOperationException("NapCat auto poke failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false,
            AutoPokeBackGroupProbability = 1.0f
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotPokeEvent
        {
            SelfId = 999,
            UserId = 2002,
            TargetId = 999,
            GroupId = 3001,
            NoticeType = "notify",
            SubType = "poke"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GroupPokes, Is.Empty);
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat auto poke failed"));
        });
    }

    [Test]
    public async Task IncomingGroupPokeToOtherMemberDoesNotAutomaticallyPokeBack()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false,
            AutoPokeBackGroupProbability = 1.0f
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotPokeEvent
        {
            SelfId = 999,
            UserId = 2002,
            TargetId = 2003,
            GroupId = 3001,
            NoticeType = "notify",
            SubType = "poke"
        });

        await Task.Delay(300);
        Assert.That(runtime.GroupPokes, Is.Empty);
        Assert.That(runtime.PrivatePokes, Is.Empty);
        Assert.That(dispatchCount, Is.EqualTo(0));
    }

    [Test]
    public async Task NonOwnerGroupPokeCommandCannotPokeThirdPartyBeforeModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] \u6233\u4e00\u4e0b 2002"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.That(runtime.GroupPokes, Is.Empty);
        Assert.That(runtime.PrivatePokes, Is.Empty);
    }

    [Test]
    public async Task OwnerPrivateNaturalLanguagePokeCommandPokesOwnerBeforeModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u6233\u4e00\u4e0b\u6211"
        });

        await WaitUntilAsync(() => runtime.PrivatePokes.Count == 1, TimeSpan.FromSeconds(2));
        Assert.That(runtime.PrivatePokes, Is.EqualTo(new[] { 1001L }));
        Assert.That(runtime.GroupPokes, Is.Empty);
        Assert.That(dispatchCount, Is.EqualTo(0));
    }

    [Test]
    public async Task OwnerPrivateNaturalLanguagePokeCommandRuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            PokePrivateException = new InvalidOperationException("NapCat natural poke failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u6233\u4e00\u4e0b\u6211"
        });

        await Task.Delay(300);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.PrivatePokes, Is.Empty);
            Assert.That(dispatchCount, Is.EqualTo(0));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat natural poke failed"));
        });
    }

    [Test]
    public async Task OwnerGroupPokeCommandCanTargetMentionedMemberBeforeModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u6233\u4e00\u4e0b [CQ:at,qq=2002]"
        });

        await WaitUntilAsync(() => runtime.GroupPokes.Count == 1, TimeSpan.FromSeconds(2));
        Assert.That(runtime.GroupPokes, Is.EqualTo(new[] { (3001L, 2002L) }));
        Assert.That(runtime.PrivatePokes, Is.Empty);
        Assert.That(dispatchCount, Is.EqualTo(0));
    }

    [Test]
    public async Task OwnerPrivateCasualPokeTextDoesNotTriggerDeterministicPoke()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u6211\u7684\u6233\u6233\u6587\u672c\u53ea\u662f\u4e00\u4e2a\u8bcd\uff0c\u4e0d\u662f\u547d\u4ee4"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.That(runtime.PrivatePokes, Is.Empty);
        Assert.That(runtime.GroupPokes, Is.Empty);
    }

    [Test]
    public async Task OwnerPrivateQuotedRecallCommandDeletesQuotedMessageBeforeModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "[CQ:reply,id=2123349888]\u628a\u8fd9\u53e5\u64a4\u56de"
        });

        await WaitUntilAsync(() => runtime.DeletedMessages.Count == 1, TimeSpan.FromSeconds(2));
        Assert.That(runtime.DeletedMessages, Is.EqualTo(new[] { 2123349888L }));
        Assert.That(dispatchCount, Is.EqualTo(0));
    }

    [Test]
    public async Task OwnerPrivateQuotedRecallCommandRuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new()
        {
            DeleteMessageException = new InvalidOperationException("NapCat quoted recall failed")
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "[CQ:reply,id=2123349888]\u628a\u8fd9\u53e5\u64a4\u56de"
        });

        await Task.Delay(300);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.DeletedMessages, Is.Empty);
            Assert.That(dispatchCount, Is.EqualTo(0));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat quoted recall failed"));
        });
    }

    [Test]
    public async Task OwnerPrivateRecallRecentNaturalLanguageCommandDeletesLatestPrivateBotMessage()
    {
        FakeOneBotRuntime runtime = new() { NextMessageId = 9000 };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        await service.SendChatAsync("private", 1001, "message to recall");
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u64a4\u56de\u521a\u624d\u90a3\u6761"
        });

        await WaitUntilAsync(() => runtime.DeletedMessages.Count == 1, TimeSpan.FromSeconds(2));
        Assert.That(runtime.DeletedMessages, Is.EqualTo(new[] { 9000L }));
        Assert.That(dispatchCount, Is.EqualTo(0));
    }

    [Test]
    public async Task OwnerPrivateRecallRecentNaturalLanguageCommandRuntimeFailureDoesNotPokeChatBot()
    {
        FakeOneBotRuntime runtime = new() { NextMessageId = 9000 };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        await service.SendChatAsync("private", 1001, "message to recall");
        runtime.DeleteMessageException = new InvalidOperationException("NapCat recent recall failed");
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u64a4\u56de\u521a\u624d\u90a3\u6761"
        });

        await Task.Delay(300);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.DeletedMessages, Is.Empty);
            Assert.That(dispatchCount, Is.EqualTo(0));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("[QQ"));
            Assert.That(GetPendingPokeText(service), Does.Not.Contain("NapCat recent recall failed"));
        });
    }

    [Test]
    public async Task IncomingGroupReplyCannotBeRedirectedToPrivateSessionThroughSendChatAsync()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            try
            {
                await service.SendChatAsync("private", inbound.SenderId, "wrong-private-reply");
            }
            finally
            {
                dispatchAttempted.TrySetResult();
            }
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] group reply target test"
        });

        await dispatchAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(runtime.PrivateMessages, Is.Empty);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingGroupMentionCanDispatchModelReplyToGroupImmediately()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "[CQ:at,qq=2001] local-group-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] 你是谁"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "[CQ:at,qq=2001] local-group-reply") }));
    }

    [Test]
    public async Task OwnerGroupCreateHelloWorldAndUploadCommandUsesDeterministicFileChannel()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "qchat-owner-file-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            int modelDispatchCount = 0;
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            });
            service.InboundChatDispatcher = _ =>
            {
                modelDispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                GroupId = 3001,
                GroupName = "test-group",
                Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
                RawMessage = "[CQ:at,qq=999] 新建 hello_world.c，内容是标准 C Hello World，然后上传到本群文件"
            });

            await WaitUntilAsync(() => runtime.GroupFiles.Count == 1);
            (long target, string file, string name) = runtime.GroupFiles.Single();
            Assert.That(target, Is.EqualTo(3001));
            Assert.That(name, Is.EqualTo("hello_world.c"));
            Assert.That(File.Exists(file), Is.True);
            Assert.That(File.ReadAllText(file), Is.EqualTo("""
                                                           #include <stdio.h>

                                                           int main(void)
                                                           {
                                                               printf("Hello, World!\n");
                                                               return 0;
                                                           }
                                                           """.Replace("\r\n", "\n", StringComparison.Ordinal)));
            Assert.That(modelDispatchCount, Is.Zero);
            Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("hello_world.c"));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task IncomingPassiveGroupMessageCanDispatchModelReplyWhenProactiveProbabilityAllows()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "local-passive-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "今晚吃什么"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "local-passive-reply") }));
    }

    [Test]
    public async Task IncomingPassiveGroupMessageOutsideAllowedGroupsDoesNotDispatchEvenWhenProactiveProbabilityIsOne()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false,
            AllowedGroupIds = "3001"
        });
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 4001,
            GroupName = "outside-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "not mentioned but probability is one"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingPassiveGroupMessageOutsideAllowedGroupsWritesScopeDiagnostic()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            int dispatchCount = 0;
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowProactiveGroupChat = true,
                ProactiveChatProbability = 1.0f,
                MaxBufferMessages = 0,
                FlushInterval = 0,
                EnableBalancedTextStreaming = false,
                AllowedGroupIds = "3001"
            });
            service.InboundChatDispatcher = _ =>
            {
                Interlocked.Increment(ref dispatchCount);
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 2001,
                GroupId = 4001,
                GroupName = "outside-group",
                Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
                RawMessage = "outside scope"
            });

            await Task.Delay(300);
            string diagnosticsPath = Path.Combine(storageRoot, "AgentWorkspace", "qchat-diagnostics.jsonl");
            string[] lines = File.Exists(diagnosticsPath)
                ? File.ReadAllLines(diagnosticsPath)
                : [];

            Assert.That(dispatchCount, Is.Zero);
            Assert.That(lines, Has.Some.Contains("\"eventName\":\"group-passive-scope-skipped\""));
            Assert.That(lines, Has.Some.Contains("\"Reason\":\"scope\""));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task IncomingGroupRecallNoticeUsesRecentMessageCacheWithoutModelDispatch()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-recall-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 2905391496,
                OwnerId = 3045846738,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };
            const string recalledText = "recall-me-123";

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 2905391496,
                MessageId = 12345,
                UserId = 3045846738,
                GroupId = 925402131,
                RawMessage = recalledText
            });
            await WaitUntilAsync(() => dispatchCount == 1);

            runtime.Raise(new OneBotNoticeEvent
            {
                SelfId = 2905391496,
                NoticeType = "group_recall",
                MessageId = 12345,
                UserId = 3045846738,
                GroupId = 925402131,
                OperatorId = 3045846738
            });

            string diagnosticsPath = Path.Combine(storageRoot, "AgentWorkspace", "qchat-diagnostics.jsonl");
            await WaitUntilAsync(() =>
                File.Exists(diagnosticsPath) &&
                File.ReadAllText(diagnosticsPath).Contains("\"eventName\":\"qchat-message-recalled\"", StringComparison.Ordinal));
            string diagnostics = File.ReadAllText(diagnosticsPath);

            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.EqualTo(1));
                Assert.That(diagnostics, Does.Contain("\"messageId\":12345"));
                Assert.That(diagnostics, Does.Contain(recalledText));
                Assert.That(diagnostics, Does.Contain("\"matched\":true"));
            });
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task ConsecutivePrivateMessagesIncludeRecentContextInModelInput()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 1,
            UserId = 1001,
            RawMessage = "alpha context"
        });
        _ = await service.WaitForInboundAsync();

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 2,
            UserId = 1001,
            RawMessage = "beta question"
        });

        QChatInboundMessage second = await service.WaitForInboundAsync();
        Assert.Multiple(() =>
        {
            Assert.That(second.Formatted, Does.Contain("[Recent QQ context]"));
            Assert.That(second.Formatted, Does.Contain("user 1001: alpha context"));
            Assert.That(second.Formatted, Does.Contain("user 1001: beta question"));
        });
    }

    [Test]
    public async Task RecalledPrivateMessageAddsSafeRecallFactToNextModelInput()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 41,
            UserId = 1001,
            RawMessage = "private secret"
        });
        _ = await service.WaitForInboundAsync();

        runtime.Raise(new OneBotNoticeEvent
        {
            SelfId = 999,
            NoticeType = "friend_recall",
            MessageId = 41,
            UserId = 1001,
            OperatorId = 1001
        });

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 42,
            UserId = 1001,
            RawMessage = "next message"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.Multiple(() =>
        {
            Assert.That(inbound.Formatted, Does.Contain("[Recent QQ events]"));
            Assert.That(inbound.Formatted, Does.Contain("user 1001 recalled a recent private message"));
            Assert.That(inbound.Formatted, Does.Contain("message_id=41"));
            Assert.That(inbound.Formatted, Does.Not.Contain("private secret"));
            Assert.That(inbound.Formatted, Does.Contain("next message"));
        });
    }

    [Test]
    public async Task ConversationSettleWindowCoalescesConsecutivePrivateMessages()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false,
                EnableConversationSettleWindow = true,
                PrivateSettleMilliseconds = 160,
                MaxSettleMilliseconds = 500
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 11,
            UserId = 1001,
            RawMessage = "first fragment"
        });
        await Task.Delay(60);
        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 12,
            UserId = 1001,
            RawMessage = "second fragment"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.Multiple(() =>
        {
            Assert.That(inbound.Formatted, Does.Contain("second fragment"));
            Assert.That(inbound.Formatted, Does.Contain("first fragment"));
            Assert.That(inbound.Formatted, Does.Not.Contain("：first fragment"));
        });
    }

    [Test]
    public async Task ConversationSettleWindowDropsRecalledPrivateTriggerBeforeModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false,
            EnableConversationSettleWindow = true,
            PrivateSettleMilliseconds = 220,
            MaxSettleMilliseconds = 500
        });
        int dispatchCount = 0;
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 21,
            UserId = 1001,
            RawMessage = "temporary message"
        });
        await Task.Delay(60);
        runtime.Raise(new OneBotNoticeEvent
        {
            SelfId = 999,
            NoticeType = "friend_recall",
            MessageId = 21,
            UserId = 1001
        });

        await Task.Delay(400);
        Assert.That(dispatchCount, Is.Zero);
    }

    [Test]
    public async Task ConversationSettleWindowDispatchesRemainingMessageAfterPartialRecall()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false,
                EnableConversationSettleWindow = true,
                PrivateSettleMilliseconds = 180,
                MaxSettleMilliseconds = 600
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 31,
            UserId = 1001,
            RawMessage = "recalled fragment"
        });
        await Task.Delay(40);
        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 32,
            UserId = 1001,
            RawMessage = "remaining fragment"
        });
        await Task.Delay(40);
        runtime.Raise(new OneBotNoticeEvent
        {
            SelfId = 999,
            NoticeType = "friend_recall",
            MessageId = 31,
            UserId = 1001
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.Multiple(() =>
        {
            Assert.That(inbound.Formatted, Does.Contain("remaining fragment"));
            Assert.That(inbound.Formatted, Does.Not.Contain("recalled fragment"));
            Assert.That(inbound.SourceMessageIds, Is.EqualTo(new[] { 32L }));
        });
    }

    [Test]
    public async Task ConsecutivePassiveGroupMemberMessagesAreThrottledAfterRecentBotReply()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 60,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"passive-reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "passive message one"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "passive message two"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "passive-reply-1") }));
    }

    [Test]
    public async Task ControlCenterLowProactiveIntensityMakesPassiveGroupCooldownConservative()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        AgentControlCenterService controlCenter = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                ProactiveChatIntensity = 1
            }
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 0,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        }, controlCenter);
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"passive-reply-{dispatchCount}");
        };
        service.QGroup(3001, true);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "passive one"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "passive two"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "passive-reply-1") }));
    }

    [Test]
    public async Task ActiveGroupSoftWindowAllowsOrdinaryPassiveMessageImmediatelyAfterWake()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            ActiveGroupSoftAttentionSeconds = 120,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"active-reply-{dispatchCount}");
        };
        service.QGroup(3001, true);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary active window message"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "active-reply-1") }));
    }

    [Test]
    public async Task ActiveGroupSoftWindowExpiredSuppressesOrdinaryPassiveMessageBySocialAttention()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            ActiveGroupSoftAttentionSeconds = 1,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"active-reply-{dispatchCount}");
        };
        service.QGroup(3001, true);
        service.GroupStates[3001].LastAwakeningTime = DateTime.Now.AddSeconds(-30);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary expired active window message"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(service.GroupStates[3001].MessageBuffer, Is.Empty);
    }

    [Test]
    public async Task ControlCenterLowProactiveIntensitySuppressesRandomPassiveGroupActivationButAllowsMentions()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        AgentControlCenterService controlCenter = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                ProactiveChatIntensity = 1
            }
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        }, controlCenter);
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "random passive group message"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.EqualTo(0));
        Assert.That(runtime.GroupMessages, Is.Empty);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] mention message"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "reply-1") }));
    }

    [Test]
    public async Task PassiveGroupThrottleDoesNotBlockOwnerOrMentionedMessages()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 60,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "passive message"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "[CQ:at,qq=999] mention message"
        });
        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "owner message"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 3, TimeSpan.FromSeconds(4));
        Assert.That(runtime.GroupMessages.Select(message => message.Message), Is.EqualTo(new[] {
            "reply-1",
            "reply-2",
            "reply-3"
        }));
    }

    [Test]
    public async Task PassiveGroupImageOnlyMessageIsSkippedAsLowInformation()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MediaOnlyPassiveGroupReplyProbability = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "should-not-send");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:image,summary=&#91;动画表情&#93;,file=sticker.jpg,sub_type=1]"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(service.GroupStates[3001].MessageBuffer, Is.Empty);
    }

    [Test]
    public async Task RecentGroupDecisionsRecordLowInformationSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MediaOnlyPassiveGroupReplyProbability = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ => Task.CompletedTask;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:image,file=sticker.jpg]"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Single();
        Assert.That(decision.GroupId, Is.EqualTo(3001));
        Assert.That(decision.UserId, Is.EqualTo(2001));
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("low-information"));
        Assert.That(decision.IsMentionedOrWoken, Is.False);
        Assert.That(decision.IsGroupEnabled, Is.False);
        Assert.That(decision.RawMessage, Does.Contain("[CQ:image"));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordActiveSoftAttentionSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            ActiveGroupSoftAttentionSeconds = 1,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ => Task.CompletedTask;
        service.QGroup(3001, true);
        service.GroupStates[3001].LastAwakeningTime = DateTime.Now.AddSeconds(-30);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary expired active window message"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Single();
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("active-soft-attention-expired"));
        Assert.That(decision.IsGroupEnabled, Is.True);
        Assert.That(decision.SocialAttentionProbability, Is.EqualTo(0f));
        Assert.That(decision.ActiveSoftAttentionRemainingSeconds, Is.EqualTo(0));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordPassiveCooldownSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 60,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "first-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "first passive message"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "second passive message"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Last();
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("cooldown"));
        Assert.That(decision.CooldownRemainingSeconds, Is.GreaterThan(0));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordPassiveSocialAttentionSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ => Task.CompletedTask;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary inactive group message"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Single();
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("social-attention"));
        Assert.That(decision.IsGroupEnabled, Is.False);
        Assert.That(decision.SocialAttentionProbability, Is.EqualTo(0f));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordMentionAcceptedReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "mention-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] hello"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Last();
        Assert.That(decision.Decision, Is.EqualTo("accepted"));
        Assert.That(decision.Reason, Is.EqualTo("mention-or-wake"));
        Assert.That(decision.IsMentionedOrWoken, Is.True);
        Assert.That(decision.IsGroupEnabled, Is.True);
    }

    [Test]
    public async Task PassiveGroupImageOnlyMessageCanDispatchWhenMediaReplyChanceAllows()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MediaOnlyPassiveGroupReplyProbability = 1.0f,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "media-reply");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:image,summary=&#91;鍔ㄧ敾琛ㄦ儏&#93;,file=sticker.jpg,sub_type=1]"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "media-reply") }));
    }

    [Test]
    public async Task PassiveLowInformationFilterAllowsMentionsAndOwnerLowInformation()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] [CQ:image,file=sticker.jpg]"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "哈哈"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 2, TimeSpan.FromSeconds(4));
        Assert.That(dispatchCount, Is.EqualTo(2));
        Assert.That(runtime.GroupMessages.Select(message => message.Message), Is.EqualTo(new[] {
            "reply-1",
            "reply-2",
        }));
    }

    [Test]
    public async Task OwnerPrivateSleepCommandEnablesQuietModeWithAcknowledgementWithoutModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[0].Message);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task OwnerPrivateForwardContainingSleepCommandDoesNotEnableQuietMode()
    {
        FakeOneBotRuntime runtime = new();
        runtime.ForwardMessages["forward-sleep"] =
        [
            new OneBotForwardMessage
            {
                Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
                Content = CreateForwardTextContent("\u4f60\u53bb\u7761\u89c9\u5427")
            }
        ];
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "[CQ:forward,id=forward-sleep]"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.That(service.IsQuietModeEnabled, Is.False);
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task OwnerPrivateSleepCommandDoesNotUseMioSpecificFixedAcknowledgement()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string acknowledgement = runtime.PrivateMessages.Single().Message;

        Assert.That(acknowledgement, Does.Not.Contain("\u54aa\u7eea"));
        Assert.That(acknowledgement, Does.Not.Contain("\u55b5"));
        Assert.That(acknowledgement, Does.Not.Contain("\u4e3b\u4eba\u771f\u4f1a\u4f7f\u5524\u4eba"));
    }

    [Test]
    public async Task OwnerPrivateSleepCommandUsesXiaYuCompatibleAcknowledgement()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

        string acknowledgement = runtime.PrivateMessages.Single().Message;
        AssertQuietAcknowledgementIsPersonaNeutral(acknowledgement);
        Assert.That(acknowledgement, Does.Not.Contain("我是机器人"));
        Assert.That(acknowledgement, Does.Not.Contain("模型"));
    }

    [Test]
    public async Task OwnerSleepCommandUsesVariedPersonaAcknowledgements()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        GeneratedAcknowledgementQChatService service = new(functionCaller, runtime, [
            "好，术术，我先安静待着。",
            "嗯，我会放轻声音等你。",
            "收到，我先退到一旁。"
        ])
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        for (int i = 0; i < 3; i++)
        {
            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                Time = i + 1,
                RawMessage = "你去睡觉吧"
            });
            await Task.Delay(50);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                Time = 100 + i,
                RawMessage = "醒醒"
            });
            await Task.Delay(50);
        }

        await WaitUntilAsync(() => runtime.PrivateMessages.Count >= 3);
        string[] sleepReplies = runtime.PrivateMessages
            .Where(message => message.Message != "awake-reply")
            .Select(message => message.Message)
            .Take(3)
            .ToArray();
        string[] distinctSleepReplies = sleepReplies.Distinct().ToArray();

        Assert.That(sleepReplies, Has.Length.EqualTo(3));
        Assert.That(distinctSleepReplies, Has.Length.GreaterThan(1));
        Assert.That(sleepReplies, Has.All.Not.Contains("不回复"));
        Assert.That(sleepReplies, Has.All.Not.Contains("保持安静"));
        Assert.That(sleepReplies, Has.All.Not.Contains("咪绪"));
        Assert.That(sleepReplies, Has.All.Not.Contains("喵"));
    }

    [Test]
    public async Task QuietModeCommandsModulateEmotionState()
    {
        FakeOneBotRuntime runtime = new();
        PADEmotionEngine emotionEngine = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            EnableBalancedTextStreaming = false
        }, emotionEngine: emotionEngine);

        float initialArousal = emotionEngine.RawArousal;
        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你去睡觉吧"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        Assert.That(emotionEngine.RawArousal, Is.LessThan(initialArousal));
        float asleepArousal = emotionEngine.RawArousal;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            RawMessage = "醒醒"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        Assert.That(emotionEngine.RawArousal, Is.GreaterThan(asleepArousal));
    }

    [Test]
    public async Task QuietModeBlocksDelayedModelSendStartedBeforeSleepCommand()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseDispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            dispatchStarted.SetResult();
            await releaseDispatch.Task;
            await service.SendChatAsync("group", inbound.TargetId, "late-model-reply");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "先普通聊一句"
        });
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1);

        releaseDispatch.SetResult();

        await Task.Delay(300);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
    }

    [Test]
    public async Task ReplyTimingDelayDefersOwnerPrivateModelSendWhenEnabled()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false,
            EnableReplyTimingDelay = true
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("private", inbound.TargetId, "timed-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "hello"
        });

        await Task.Delay(100);
        Assert.That(runtime.PrivateMessages, Is.Empty);

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1, TimeSpan.FromSeconds(3));
        Assert.That(runtime.PrivateMessages.Single(), Is.EqualTo((1001L, "timed-reply")));
    }

    [Test]
    public async Task QuietModeBlocksReplyTimingDelayedModelSend()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false,
            EnableReplyTimingDelay = true
        });
        service.InboundChatDispatcher = async inbound =>
        {
            dispatchStarted.SetResult();
            await service.SendChatAsync("private", inbound.TargetId, "should-not-send");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "hello"
        });
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

        await Task.Delay(2200);
        Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[0].Message);
    }

    [Test]
    public async Task QuietModeBlocksDelayedXmlQChatStartedBeforeSleepCommand()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseDispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            dispatchStarted.SetResult();
            await releaseDispatch.Task;
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "late-xml-reply"
            }, OneBotMessageType.Group, inbound.TargetId);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "先普通聊一句"
        });
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1);

        releaseDispatch.SetResult();

        await Task.Delay(300);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
    }

    [Test]
    public async Task ConcurrentGroupModelDispatchesAreSerialized()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        BlockingDispatchQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                FlushInterval = 0,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "first-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] first"
        });
        await WaitUntilAsync(() => service.StartedTargets.Count == 1);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 4001,
            GroupName = "second-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] second"
        });
        await Task.Delay(200);

        Assert.That(service.StartedTargets, Is.EqualTo(new[] { 3001L }));

        service.ReleaseNextReply("first reply");
        await WaitUntilAsync(() => service.StartedTargets.Count == 2);
        service.ReleaseNextReply("second reply");

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 2);
        Assert.That(service.StartedTargets, Is.EqualTo(new[] { 3001L, 4001L }));
        Assert.That(runtime.GroupMessages.Select(message => message.Target), Is.EqualTo(new[] { 3001L, 4001L }));
    }

    [Test]
    public async Task OneBotEventsAreProcessedSequentiallyBeforeModelDispatch()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-event-queue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
            BlockingDispatchQChatService service = new(functionCaller, runtime)
            {
                Configuration = new QChatConfig
                {
                    BotId = 999,
                    OwnerId = 1001,
                    AllowGroupMemberChat = true,
                    FlushInterval = 0,
                    EnableBalancedTextStreaming = false
                }
            };
            StartService(service);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                GroupId = 3001,
                GroupName = "first-group",
                Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
                RawMessage = "[CQ:at,qq=999] first queued"
            });
            await WaitUntilAsync(() => service.StartedTargets.Count == 1);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                GroupId = 4001,
                GroupName = "second-group",
                Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
                RawMessage = "[CQ:at,qq=999] second queued"
            });
            await Task.Delay(200);

            string diagnosticsPath = Path.Combine(storageRoot, "AgentWorkspace", "qchat-diagnostics.jsonl");
            string diagnostics = File.Exists(diagnosticsPath)
                ? File.ReadAllText(diagnosticsPath)
                : "";
            Assert.That(diagnostics, Does.Contain("first queued"));
            Assert.That(diagnostics, Does.Not.Contain("second queued"));

            service.ReleaseNextReply("first reply");
            await WaitUntilAsync(() => service.StartedTargets.Count == 2);
            service.ReleaseNextReply("second reply");
            await WaitUntilAsync(() => runtime.GroupMessages.Count == 2);
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task QuietModeSuppressesNonOwnerGroupMention()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "[CQ:at,qq=2001] should-not-send");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] \u9192\u9192"
        });

        await Task.Delay(200);
        Assert.That(service.IsQuietModeEnabled, Is.True);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task QuietModeSuppressesPassiveProactiveGroupMessage()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "should-not-send");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "\u4eca\u5929\u5403\u4ec0\u4e48"
        });

        await Task.Delay(200);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task QuietModeSuppressesOwnerGroupMessageUntilWakeCommand()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u73b0\u5728\u8fd8\u4f1a\u56de\u590d\u5417"
        });

        await Task.Delay(300);
        Assert.That(service.IsQuietModeEnabled, Is.True);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u9192\u9192"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u8fd8\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 3);
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
        Assert.That(runtime.GroupMessages[1].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[1].Message);
        Assert.That(runtime.GroupMessages[2], Is.EqualTo((3001L, "reply-1")));
    }

    [Test]
    public async Task OwnerWakeCommandDisablesQuietModeAndAllowsNextOwnerReply()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("private", inbound.TargetId, "awake-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u9192\u9192"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u8fd8\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 3);
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[0].Message);
        Assert.That(runtime.PrivateMessages[1].Target, Is.EqualTo(1001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[1].Message);
        Assert.That(runtime.PrivateMessages[2], Is.EqualTo((1001L, "awake-reply")));
    }

    [Test]
    public async Task OwnerWakeCommandSendsWakeAcknowledgement()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        GeneratedAcknowledgementQChatService service = new(functionCaller, runtime, [
            "好，术术，我先安静待着。",
            "我在，术术。"
        ])
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你去睡觉吧"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "醒醒"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 2);
        Assert.That(runtime.PrivateMessages[0], Is.EqualTo((1001L, "好，术术，我先安静待着。")));
        Assert.That(runtime.PrivateMessages[1], Is.EqualTo((1001L, "我在，术术。")));
    }

    [Test]
    public async Task QuietModeAllowsConfiguredWakeUserToWakeWithoutOwnerPrivileges()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("private", inbound.TargetId, $"role-{inbound.SenderRole}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "\u9192\u9192"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 2);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "\u4f60\u8fd8\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 3);
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[0].Message);
        Assert.That(runtime.PrivateMessages[1].Target, Is.EqualTo(2002));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[1].Message);
        Assert.That(runtime.PrivateMessages[2], Is.EqualTo((2002L, "role-PrivateGuest")));
    }

    [Test]
    public async Task QuietModeWakeUserReceivesGroupAcknowledgementInSameGroupWithoutOwnerNotification()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"role-{inbound.SenderRole}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "wake-user" },
            RawMessage = "[CQ:at,qq=999] \u9192\u9192"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.PrivateMessages, Is.Empty);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(2));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
        Assert.That(runtime.GroupMessages[1].Target, Is.EqualTo(3001));
        Assert.That(runtime.GroupMessages[1].Message, Does.StartWith("\u5988\u5988"));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[1].Message);
    }

    [Test]
    public async Task TrustedWakeUserGroupAcknowledgementUsesRelationshipAddressInsteadOfAt()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.QChatQuietMode(true, "test");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "wake-user" },
            RawMessage = "[CQ:at,qq=999] \u9192\u9192"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Message, Does.Not.Contain("[CQ:at"));
        Assert.That(runtime.GroupMessages[0].Message, Does.StartWith("\u5988\u5988"));
    }

    [Test]
    public void EmptyGroupFlushDiagnosticsAreThrottledPerGroup()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
            {
                Configuration = new QChatConfig { BotId = 999 }
            };
            GroupState state = new() { GroupId = 9876543210123L };

            service.FlushGroupBuffer(state);
            service.FlushGroupBuffer(state);
            service.FlushGroupBuffer(state);

            string diagnosticsPath = Path.Combine(storageRoot, "AgentWorkspace", "qchat-diagnostics.jsonl");
            string[] skippedLines = File.ReadAllLines(diagnosticsPath)
                .Where(line => line.Contains("\"eventName\":\"group-flush-skipped\"", StringComparison.Ordinal))
                .ToArray();
            Assert.That(skippedLines, Has.Length.EqualTo(1));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    static QChatService CreateStartedService(
        FakeOneBotRuntime runtime,
        QChatConfig config,
        AgentControlCenterService? controlCenter = null,
        PADEmotionEngine? emotionEngine = null,
        AgentApprovalService? approvalService = null,
        AgentEditCheckpointService? checkpointService = null,
        AgentTaskService? taskService = null)
    {
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime, agentControlCenter: controlCenter, emotionEngine: emotionEngine, approvalService: approvalService, checkpointService: checkpointService, taskService: taskService)
        {
            Configuration = config
        };
        StartService(service);
        return service;
    }

    static void StartService(QChatService service, string characterName = "QChatTest")
    {
        Character character = new() { Name = characterName };
        ChatHistoryAgentThread thread = new();
        service.AwakeAsync(new AwakeContext
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder(),
        }).GetAwaiter().GetResult();
        ChatBot chatBot = new(null!, thread);
        service.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            character,
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            [])).GetAwaiter().GetResult();
    }

    static void AssertQuietAcknowledgementIsPersonaNeutral(string message)
    {
        Assert.That(message, Is.Not.Empty);
        Assert.That(message, Does.Not.Contain("咪绪"));
        Assert.That(message, Does.Not.Contain("喵"));
        Assert.That(message, Does.Not.Contain("猫娘"));
        Assert.That(message, Does.Not.Contain("耳朵"));
        Assert.That(message, Does.Not.Contain("尾巴"));
        Assert.That(message, Does.Not.Contain("主人真会使唤人"));
    }

    static string GetPendingPokeText(QChatService service)
    {
        PropertyInfo chatBotProperty = typeof(InteractiveModule)
            .GetProperty("ChatBot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ChatBot chatBot = (ChatBot)chatBotProperty.GetValue(service)!;
        FieldInfo messageCacheField = typeof(ChatBot)
            .GetField("messageCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IEnumerable<string> messages = (IEnumerable<string>)messageCacheField.GetValue(chatBot)!;
        return string.Join("", messages);
    }

    static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        Assert.Fail("Condition was not met before timeout.");
    }

    static System.Text.Json.JsonElement CreateForwardTextContent(string text)
    {
        string json = "[{\"type\":\"text\",\"data\":{\"text\":"
            + System.Text.Json.JsonSerializer.Serialize(text)
            + "}}]";
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
            json);
        return document.RootElement.Clone();
    }

    static void RaiseEvent(object target, string eventName, params object?[] arguments)
    {
        FieldInfo? field = target.GetType().GetField(
            eventName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Event backing field '{eventName}' should exist.");
        MulticastDelegate? eventDelegate = field!.GetValue(target) as MulticastDelegate;
        eventDelegate?.DynamicInvoke(arguments);
    }

    sealed class FakeOneBotRuntime : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId { get; set; } = 999;
        public bool IsConnected { get; set; } = true;
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public List<(long Target, string Message)> GroupMessages { get; } = new();
        public List<(long Target, string Message)> PrivateMessages { get; } = new();
        public List<long> DeletedMessages { get; } = new();
        public List<long> PrivatePokes { get; } = new();
        public List<(long GroupId, long UserId)> GroupPokes { get; } = new();
        public List<(long Target, string File, string Name)> GroupFiles { get; } = new();
        public List<(long Target, string File, string Name)> PrivateFiles { get; } = new();
        public Dictionary<string, OneBotFile> PrivateFileUrls { get; } = new();
        public Dictionary<(long GroupId, string FileId), OneBotFile> GroupFileUrls { get; } = new();
        public Dictionary<long, OneBotMessageEvent> Messages { get; } = new();
        public Dictionary<string, List<OneBotForwardMessage>> ForwardMessages { get; } = new();
        public Dictionary<long, IReadOnlyList<OneBotGroupMember>> GroupMemberLists { get; } = new();
        public IReadOnlyList<OneBotGroupInfo> GroupLists { get; set; } = [];
        public Exception? SendException { get; set; }
        public Exception? DeleteMessageException { get; set; }
        public Exception? PokePrivateException { get; set; }
        public Exception? PokeGroupException { get; set; }
        public TimeSpan UploadGroupFileDelay { get; set; }
        public Exception? UploadGroupFileException { get; set; }
        public Exception? UploadPrivateFileException { get; set; }
        public long NextMessageId { get; set; } = 1;

        public Task ConnectAsync() => Task.CompletedTask;
        public Task SendGroupMessage(long groupId, string message)
        {
            if (SendException != null)
                throw SendException;
            GroupMessages.Add((groupId, message));
            return Task.CompletedTask;
        }

        public Task<OneBotSendMessageResult?> SendGroupMessageWithResult(long groupId, string message)
        {
            if (SendException != null)
                throw SendException;
            GroupMessages.Add((groupId, message));
            return Task.FromResult<OneBotSendMessageResult?>(new OneBotSendMessageResult { MessageId = NextMessageId++ });
        }

        public Task SendPrivateMessage(long userId, string message)
        {
            if (SendException != null)
                throw SendException;
            PrivateMessages.Add((userId, message));
            return Task.CompletedTask;
        }

        public Task<OneBotSendMessageResult?> SendPrivateMessageWithResult(long userId, string message)
        {
            if (SendException != null)
                throw SendException;
            PrivateMessages.Add((userId, message));
            return Task.FromResult<OneBotSendMessageResult?>(new OneBotSendMessageResult { MessageId = NextMessageId++ });
        }

        public Task DeleteMessage(long messageId)
        {
            if (DeleteMessageException != null)
                throw DeleteMessageException;
            DeletedMessages.Add(messageId);
            return Task.CompletedTask;
        }

        public Task PokePrivate(long userId)
        {
            if (PokePrivateException != null)
                throw PokePrivateException;
            PrivatePokes.Add(userId);
            return Task.CompletedTask;
        }

        public Task PokeGroup(long groupId, long userId)
        {
            if (PokeGroupException != null)
                throw PokeGroupException;
            GroupPokes.Add((groupId, userId));
            return Task.CompletedTask;
        }

        public async Task UploadGroupFile(long groupId, string filePath, string name)
        {
            if (UploadGroupFileDelay > TimeSpan.Zero)
                await Task.Delay(UploadGroupFileDelay);
            if (UploadGroupFileException != null)
                throw UploadGroupFileException;
            GroupFiles.Add((groupId, filePath, name));
        }

        public Task UploadPrivateFile(long userId, string filePath, string name)
        {
            if (UploadPrivateFileException != null)
                throw UploadPrivateFileException;
            PrivateFiles.Add((userId, filePath, name));
            return Task.CompletedTask;
        }
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) =>
            Task.FromResult(PrivateFileUrls.TryGetValue(fileId, out OneBotFile? file) ? file : null);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) =>
            Task.FromResult(GroupFileUrls.TryGetValue((groupId, fileId), out OneBotFile? file) ? file : null);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) =>
            Task.FromResult(Messages.TryGetValue(messageId, out OneBotMessageEvent? message) ? message : null);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) =>
            Task.FromResult<List<OneBotForwardMessage>?>(ForwardMessages.TryGetValue(forwardId, out List<OneBotForwardMessage>? messages) ? messages : []);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => Task.FromResult(GroupLists);
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId)
        {
            return Task.FromResult(GroupMemberLists.TryGetValue(groupId, out IReadOnlyList<OneBotGroupMember>? members)
                ? members
                : Array.Empty<OneBotGroupMember>());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise(OneBotBaseEvent ev) => EventReceived?.Invoke(ev);
    }

    sealed class ThrowingUploadRuntime : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId => 999;
        public bool IsConnected => true;
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public Task ConnectAsync() => Task.CompletedTask;
        public Task SendGroupMessage(long groupId, string message) => Task.CompletedTask;
        public Task SendPrivateMessage(long userId, string message) => Task.CompletedTask;
        public Task UploadGroupFile(long groupId, string filePath, string name) =>
            throw new InvalidOperationException("NapCat upload failed");
        public Task UploadPrivateFile(long userId, string filePath, string name) => Task.CompletedTask;
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => Task.FromResult<OneBotMessageEvent?>(null);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => Task.FromResult<List<OneBotForwardMessage>?>([]);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => Task.FromResult<IReadOnlyList<OneBotGroupInfo>>([]);
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId) => Task.FromResult<IReadOnlyList<OneBotGroupMember>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class GeneratedAcknowledgementQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime,
        IReadOnlyList<string> acknowledgements) : QChatService(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        int index;

        protected override Task<string> GenerateQuietModeAcknowledgementAsync(string prompt)
        {
            int selected = Interlocked.Increment(ref index) - 1;
            return Task.FromResult(acknowledgements[selected % acknowledgements.Count]);
        }
    }

    sealed class PlainReplyQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime,
        string reply) : QChatService(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        readonly TaskCompletionSource dispatchCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForDispatchAsync() => dispatchCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        protected override Task<string> DispatchToModelAsync(QChatInboundMessage message)
        {
            dispatchCompletion.TrySetResult();
            return Task.FromResult(reply);
        }
    }

    sealed class ExposedFilterQChatService(IOneBotRuntime runtime)
        : QChatService(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        public string FilterForTest(string text) => ChatTextFilter(text);
    }

    sealed class BlockingDispatchQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime) : QChatService(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        readonly Queue<TaskCompletionSource<string>> pendingReplies = new();
        readonly object gate = new();

        public List<long> StartedTargets { get; } = new();

        public void ReleaseNextReply(string reply)
        {
            TaskCompletionSource<string>? pending = null;
            lock (gate)
            {
                if (pendingReplies.Count > 0)
                    pending = pendingReplies.Dequeue();
            }

            Assert.That(pending, Is.Not.Null, "Expected a pending model dispatch.");
            pending!.SetResult(reply);
        }

        protected override Task<string> DispatchToModelAsync(QChatInboundMessage message)
        {
            TaskCompletionSource<string> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                StartedTargets.Add(message.TargetId);
                pendingReplies.Enqueue(reply);
            }

            return reply.Task;
        }
    }

    sealed class CapturingQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime,
        QChatUserProfileService? userProfileService = null,
        QChatRelationCacheService? relationCacheService = null) : QChatService(
            functionCaller,
            new NullLogger<QChatService>(),
            oneBotRuntime: runtime,
            relationCacheService: relationCacheService,
            userProfileService: userProfileService)
    {
        readonly Channel<QChatInboundMessage> inboundMessages = Channel.CreateUnbounded<QChatInboundMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true
            });

        public Task<QChatInboundMessage> WaitForInboundAsync() =>
            inboundMessages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        protected override Task<string> DispatchToModelAsync(QChatInboundMessage message)
        {
            inboundMessages.Writer.TryWrite(message);
            return Task.FromResult("");
        }
    }

    sealed class FakeLifeEventPublisher : ILifeEventPublisher
    {
        public List<LifeEvent> Events { get; } = new();
        public void Publish(LifeEvent lifeEvent) => Events.Add(lifeEvent);
    }
}
