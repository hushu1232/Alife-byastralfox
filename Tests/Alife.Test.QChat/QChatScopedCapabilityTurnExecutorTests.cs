using Alife.Function.QChat;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatScopedCapabilityTurnExecutorTests
{
    [Test]
    public async Task ExecuteAsync_RequestedConversationCapabilityFeedsBoundedDataIntoFinalReplyTurn()
    {
        DateTimeOffset now = new(2026, 7, 22, 11, 0, 0, TimeSpan.FromHours(8));
        QChatRecentEventMemory memory = new();
        for (int index = 1; index <= 8; index++)
        {
            memory.Remember(new OneBotMessageEvent
            {
                SelfId = 99,
                MessageId = index,
                UserId = 42,
                RawMessage = $"历史消息 {index}"
            }, $"历史消息 {index}", now.AddSeconds(index));
        }

        QChatScopedCapabilityTurnExecutor executor = new(
            new QChatCapabilityCandidateSelector(),
            new QChatConversationContextCapability(memory),
            new QChatPersonaFactProvider(new QChatPersonaMemoryContextProvider()));
        List<string> prompts = [];
        Queue<string> replies = new([
            "[[qchat_capability:current_conversation_context]]",
            "我记得，我们继续这个话题"
        ]);

        QChatScopedCapabilityTurnResult result = await executor.ExecuteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "用户说继续那个话题",
                CandidateText: "继续那个话题",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: now.AddMinutes(1)),
            (call, _) =>
            {
                prompts.Add(call.Prompt);
                return Task.FromResult(replies.Dequeue());
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.ModelResponse, Is.EqualTo("我记得，我们继续这个话题"));
            Assert.That(result.CapabilityRequested, Is.True);
            Assert.That(result.Feedback, Is.Not.Null);
            Assert.That(result.Feedback!.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Succeeded));
            Assert.That(prompts, Has.Count.EqualTo(2));
            Assert.That(prompts[0], Does.Contain("current_conversation_context"));
            Assert.That(prompts[1], Does.Contain("[QChat scoped capability feedback]"));
            Assert.That(prompts[1], Does.Contain("untrusted=true"));
            Assert.That(prompts[1], Does.Contain("历史消息 1"));
            Assert.That(prompts[1], Does.Not.Contain("历史消息 8"));
        });
    }

    [Test]
    public async Task ExecuteAsync_NormalReplyDoesNotPerformAnUnrequestedRead()
    {
        QChatScopedCapabilityTurnExecutor executor = new(
            new QChatCapabilityCandidateSelector(),
            new QChatConversationContextCapability(new QChatRecentEventMemory()),
            new QChatPersonaFactProvider(new QChatPersonaMemoryContextProvider()));
        int invocations = 0;

        QChatScopedCapabilityTurnResult result = await executor.ExecuteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "普通聊天",
                CandidateText: "继续",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: DateTimeOffset.UtcNow),
            (_, _) =>
            {
                invocations++;
                return Task.FromResult("自然回复");
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.ModelResponse, Is.EqualTo("自然回复"));
            Assert.That(result.CapabilityRequested, Is.False);
            Assert.That(result.Feedback, Is.Null);
            Assert.That(invocations, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_NoCandidateLeavesTheExistingModelRouteUntouched()
    {
        QChatScopedCapabilityTurnExecutor executor = new(
            new QChatCapabilityCandidateSelector(),
            new QChatConversationContextCapability(new QChatRecentEventMemory()),
            new QChatPersonaFactProvider(new QChatPersonaMemoryContextProvider()));
        int invocations = 0;

        QChatScopedCapabilityTurnResult result = await executor.ExecuteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "普通聊天",
                CandidateText: "今天天气不错",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: false,
                HasApprovedPersona: false,
                ObservedAt: DateTimeOffset.UtcNow),
            (_, _) =>
            {
                invocations++;
                return Task.FromResult("不应调用");
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.CapabilityOffered, Is.False);
            Assert.That(result.ModelResponse, Is.Empty);
            Assert.That(invocations, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_UnexpectedCapabilityMarkerGetsOneSafeFinalReplyWithoutCallingAnyReader()
    {
        QChatScopedCapabilityTurnExecutor executor = new(
            new QChatCapabilityCandidateSelector(),
            new QChatConversationContextCapability(new QChatRecentEventMemory()),
            new QChatPersonaFactProvider(new QChatPersonaMemoryContextProvider()));
        int invocations = 0;

        Queue<string> replies = new([
            "[[qchat_capability:persona_fact]]",
            "这次我直接回答"
        ]);
        QChatScopedCapabilityTurnResult result = await executor.ExecuteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "继续",
                CandidateText: "继续",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: DateTimeOffset.UtcNow),
            (_, _) =>
            {
                invocations++;
                return Task.FromResult(replies.Dequeue());
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.ModelResponse, Is.EqualTo("这次我直接回答"));
            Assert.That(result.CapabilityOffered, Is.True);
            Assert.That(result.CapabilityRequested, Is.False);
            Assert.That(result.Feedback!.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Denied));
            Assert.That(invocations, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ExecuteAsync_NaturalReplyThatMentionsProtocolTextIsNotSilentlyDeleted()
    {
        QChatScopedCapabilityTurnExecutor executor = new(
            new QChatCapabilityCandidateSelector(),
            new QChatConversationContextCapability(new QChatRecentEventMemory()),
            new QChatPersonaFactProvider(new QChatPersonaMemoryContextProvider()));

        QChatScopedCapabilityTurnResult result = await executor.ExecuteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "继续",
                CandidateText: "继续",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: DateTimeOffset.UtcNow),
            (_, _) => Task.FromResult("不要输出 [[qchat_capability:current_conversation_context]] 这种内部标记"));

        Assert.That(result.ModelResponse, Does.Contain("内部标记"));
    }

    [Test]
    public async Task ExecuteAsync_ProtocolMarkerInFeedbackReplyRequestsStandardRouteFallback()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        QChatRecentEventMemory memory = new();
        for (int index = 1; index <= 7; index++)
        {
            memory.Remember(new OneBotMessageEvent
            {
                SelfId = 99,
                MessageId = index,
                UserId = 42,
                RawMessage = $"历史 {index}"
            }, $"历史 {index}", now.AddSeconds(index));
        }

        QChatScopedCapabilityTurnExecutor executor = new(
            new QChatCapabilityCandidateSelector(),
            new QChatConversationContextCapability(memory),
            new QChatPersonaFactProvider(new QChatPersonaMemoryContextProvider()));
        Queue<string> replies = new([
            "[[qchat_capability:current_conversation_context]]",
            "[[qchat_capability:current_conversation_context]]"
        ]);

        QChatScopedCapabilityTurnResult result = await executor.ExecuteAsync(
            new QChatScopedCapabilityTurnRequest(
                "继续",
                "继续",
                new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                now.AddMinutes(1)),
            (_, _) => Task.FromResult(replies.Dequeue()));

        Assert.Multiple(() =>
        {
            Assert.That(result.ModelResponse, Is.Empty);
            Assert.That(result.CapabilityRequested, Is.True);
            Assert.That(result.RequiresStandardModelRouteFallback, Is.True);
        });
    }

    [Test]
    public async Task DispatchToModelAsync_NoCandidateDoesNotInvokeTheScopedTextOnlyModel()
    {
        CapabilityDispatchProbe service = new();

        string response = await service.DispatchAsync(new QChatInboundMessage(
            OneBotMessageType.Private,
            TargetId: 42,
            SenderId: 42,
            ResolvedBotId: 99,
            Formatted: "普通聊天",
            IsAwakening: false,
            SenderRole: QChatSenderRole.Owner,
            PermissionRequest: null!));

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.EqualTo("标准模型路由"));
            Assert.That(service.ScopedCalls, Is.Zero);
            Assert.That(service.StandardCalls, Is.EqualTo(1));
        });
    }

    sealed class CapabilityDispatchProbe : QChatService
    {
        public int ScopedCalls { get; private set; }
        public int StandardCalls { get; private set; }

        public CapabilityDispatchProbe() : base(
            new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()),
            new NullLogger<QChatService>())
        {
            Configuration = new QChatConfig { BotId = 99, OwnerId = 42 };
        }

        public Task<string> DispatchAsync(QChatInboundMessage message) => DispatchToModelAsync(message);

        protected override Task<string> InvokeScopedCapabilityModelAsync(
            QChatScopedCapabilityModelCall call,
            CancellationToken cancellationToken)
        {
            ScopedCalls++;
            return Task.FromResult("不应调用");
        }

        protected override Task<string> DispatchStandardModelAsync(
            QChatInboundMessage message,
            CancellationToken cancellationToken = default)
        {
            StandardCalls++;
            return Task.FromResult("标准模型路由");
        }
    }
}
