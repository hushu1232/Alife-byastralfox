using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.QChat;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatScopedCapabilityTurnExecutorTests
{
    [Test]
    public void BuildModelInput_OffersSafeReadsByAvailabilityWithoutInspectingUserWording()
    {
        QChatScopedCapabilityTurnExecutor executor = CreateExecutor(new QChatRecentEventMemory());

        string prompt = executor.BuildModelInput(new QChatScopedCapabilityTurnRequest(
            ModelInput: "她平时对身边的人会是什么态度",
            ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
            Identity: QChatAgentIdentityRegistry.CreateDefault().ResolveByAgentId("xiayu"),
            HasReplayableConversation: true,
            HasApprovedPersona: true,
            ObservedAt: DateTimeOffset.UtcNow));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("current_conversation_context"));
            Assert.That(prompt, Does.Contain("persona_origin"));
            Assert.That(prompt, Does.Contain("persona_relationship"));
            Assert.That(prompt, Does.Contain("persona_speech_style"));
            Assert.That(prompt, Does.Contain("persona_behavior_boundary"));
            Assert.That(prompt, Does.Contain("persona_confirmed_preference"));
            Assert.That(prompt, Does.Contain("at most one"));
        });
    }

    [Test]
    public async Task CompleteAsync_RequestedConversationCapabilityFeedsBoundedDataIntoFinalReplyTurn()
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

        QChatScopedCapabilityTurnExecutor executor = CreateExecutor(memory);
        List<QChatScopedCapabilityModelCall> calls = [];

        QChatScopedCapabilityTurnResult result = await executor.CompleteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "她昨天留下的那段话我还惦记着",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: now.AddMinutes(1)),
            "[[qchat_capability:current_conversation_context]]",
            (call, _) =>
            {
                calls.Add(call);
                return Task.FromResult("我记得，我们继续这个话题");
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.ModelResponse, Is.EqualTo("我记得，我们继续这个话题"));
            Assert.That(result.CapabilityRequested, Is.True);
            Assert.That(result.Feedback, Is.Not.Null);
            Assert.That(result.Feedback!.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Succeeded));
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0].IsFeedback, Is.True);
            Assert.That(calls[0].Prompt, Does.Contain("[QChat scoped capability feedback]"));
            Assert.That(calls[0].Prompt, Does.Contain("untrusted=true"));
            Assert.That(calls[0].Prompt, Does.Contain("历史消息 1"));
            Assert.That(calls[0].Prompt, Does.Not.Contain("历史消息 8"));
        });
    }

    [Test]
    public async Task CompleteAsync_NormalReplyDoesNotPerformAnUnrequestedRead()
    {
        QChatScopedCapabilityTurnExecutor executor = CreateExecutor(new QChatRecentEventMemory());
        int invocations = 0;

        QChatScopedCapabilityTurnResult result = await executor.CompleteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "普通聊天",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: DateTimeOffset.UtcNow),
            "自然回复",
            (_, _) =>
            {
                invocations++;
                return Task.FromResult("自然回复");
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.ModelResponse, Is.EqualTo("自然回复"));
            Assert.That(result.CapabilityOffered, Is.True);
            Assert.That(result.CapabilityRequested, Is.False);
            Assert.That(result.Feedback, Is.Null);
            Assert.That(invocations, Is.Zero);
        });
    }

    [Test]
    public async Task CompleteAsync_UnofferedMarkerGetsOneSafeFinalReplyWithoutCallingAnyReader()
    {
        QChatScopedCapabilityTurnExecutor executor = CreateExecutor(new QChatRecentEventMemory());
        int invocations = 0;

        QChatScopedCapabilityTurnResult result = await executor.CompleteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "普通聊天",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: false,
                HasApprovedPersona: false,
                ObservedAt: DateTimeOffset.UtcNow),
            "[[qchat_capability:persona_speech_style]]",
            (_, _) =>
            {
                invocations++;
                return Task.FromResult("这次我直接回答");
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.CapabilityOffered, Is.False);
            Assert.That(result.CapabilityRequested, Is.False);
            Assert.That(result.Feedback!.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Denied));
            Assert.That(result.ModelResponse, Is.EqualTo("这次我直接回答"));
            Assert.That(invocations, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CompleteAsync_ProtocolMarkerInFeedbackReplyRequestsStandardRouteFallback()
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

        QChatScopedCapabilityTurnExecutor executor = CreateExecutor(memory);
        QChatScopedCapabilityTurnResult result = await executor.CompleteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "继续",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: now.AddMinutes(1)),
            "[[qchat_capability:current_conversation_context]]",
            (_, _) => Task.FromResult("[[qchat_capability:current_conversation_context]]"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ModelResponse, Is.Empty);
            Assert.That(result.CapabilityOffered, Is.True);
            Assert.That(result.CapabilityRequested, Is.True);
            Assert.That(result.RequiresStandardModelRouteFallback, Is.True);
        });
    }

    [Test]
    public async Task CompleteAsync_CaseVariantOfAnOfferedMarkerUsesTheSameBoundedCapability()
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

        QChatScopedCapabilityTurnExecutor executor = CreateExecutor(memory);
        QChatScopedCapabilityTurnResult result = await executor.CompleteAsync(
            new QChatScopedCapabilityTurnRequest(
                ModelInput: "继续",
                ConversationScope: new QChatConversationContextRequest(99, OneBotMessageType.Private, 42),
                Identity: null,
                HasReplayableConversation: true,
                HasApprovedPersona: false,
                ObservedAt: now.AddMinutes(1)),
            "[[qchat_capability:CURRENT_CONVERSATION_CONTEXT]]",
            (_, _) => Task.FromResult("我接着说"));

        Assert.Multiple(() =>
        {
            Assert.That(result.CapabilityRequested, Is.True);
            Assert.That(result.Feedback!.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Succeeded));
            Assert.That(result.Feedback.Capability, Is.EqualTo("current_conversation_context"));
            Assert.That(result.ModelResponse, Is.EqualTo("我接着说"));
        });
    }

    [Test]
    public async Task DispatchToModelAsync_InterceptsNormalRouteMarkerBeforeItCanReachQq()
    {
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-scoped-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(storageRoot, "Character", "\u590f\u7fbd", "Memory", "Persona"));
        try
        {
            File.WriteAllText(Path.Combine(storageRoot, "Character", "\u590f\u7fbd", "Memory", "Persona", "\u590f\u7fbd-\u89d2\u8272\u80cc\u666f.md"), "三、说话风格\n面对主人自然温和");
            CapabilityDispatchProbe service = new(new QChatPersonaMemoryContextProvider(storageRoot))
            {
                Configuration = new QChatConfig { BotId = 2905391496, OwnerId = 42 },
                StandardModelResponse = "[[qchat_capability:persona_speech_style]]",
                ScopedModelResponse = "我会按现在这段话自然回答"
            };

            string response = await service.DispatchAsync(new QChatInboundMessage(
                OneBotMessageType.Private,
                TargetId: 42,
                SenderId: 42,
                ResolvedBotId: 2905391496,
                Formatted: "她平时说话会怎么表达",
                IsAwakening: false,
                SenderRole: QChatSenderRole.Owner,
                PermissionRequest: null!));

            Assert.Multiple(() =>
            {
                Assert.That(response, Is.EqualTo("我会按现在这段话自然回答"));
                Assert.That(service.StandardCalls, Is.EqualTo(1));
                Assert.That(service.ScopedCalls, Is.EqualTo(1));
                Assert.That(service.LastStandardInput, Does.Contain("persona_speech_style"));
                Assert.That(service.LastScopedInput, Does.Contain("scoped_capability_feedback"));
            });
        }
        finally
        {
            if (Directory.Exists(storageRoot))
                Directory.Delete(storageRoot, recursive: true);
        }
    }

    sealed class CapabilityDispatchProbe : QChatService
    {
        public int ScopedCalls { get; private set; }
        public int StandardCalls { get; private set; }

        public string StandardModelResponse { get; set; } = "标准模型路由";
        public string ScopedModelResponse { get; set; } = "安全最终回复";
        public string? LastStandardInput { get; private set; }
        public string? LastScopedInput { get; private set; }

        public CapabilityDispatchProbe(QChatPersonaMemoryContextProvider? personaMemoryContextProvider = null) : base(
            new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()),
            new NullLogger<QChatService>(),
            personaMemoryContextProvider: personaMemoryContextProvider)
        {
            Configuration = new QChatConfig { BotId = 99, OwnerId = 42 };
        }

        public Task<string> DispatchAsync(QChatInboundMessage message) => DispatchToModelAsync(message);

        protected override Task<string> InvokeScopedCapabilityModelAsync(
            QChatScopedCapabilityModelCall call,
            CancellationToken cancellationToken)
        {
            ScopedCalls++;
            LastScopedInput = call.Prompt;
            return Task.FromResult(ScopedModelResponse);
        }

        protected override Task<string> DispatchStandardModelAsync(
            QChatInboundMessage message,
            CancellationToken cancellationToken = default)
        {
            StandardCalls++;
            LastStandardInput = message.Formatted;
            return Task.FromResult(StandardModelResponse);
        }
    }

    [Test]
    public async Task DispatchToModelAsync_RepeatedProtocolMarkerIsWithheldFromQq()
    {
        CapabilityDispatchProbe service = new()
        {
            StandardModelResponse = "[[qchat_capability:current_conversation_context]]",
            ScopedModelResponse = "[[qchat_capability:current_conversation_context]]"
        };

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
            Assert.That(response, Is.Empty);
            Assert.That(service.StandardCalls, Is.EqualTo(2));
            Assert.That(service.ScopedCalls, Is.EqualTo(1));
        });
    }

    static QChatScopedCapabilityTurnExecutor CreateExecutor(QChatRecentEventMemory memory) => new(
        new QChatConversationContextCapability(memory),
        new QChatPersonaFactProvider(new QChatPersonaMemoryContextProvider()));
}
