using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using NUnit.Framework;

#pragma warning disable SKEXP0010

namespace Alife.Test.QChat;

[TestFixture]
[Category("Integration")]
public class QChatModelReplyLoopLiveTests
{
    [Test]
    public async Task LiveModelReplyLoopHandlesPrivateMentionAndPassiveGroupEvents()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_MODEL_LOOP") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_MODEL_LOOP=1 to run real model reply-loop validation.");

        FakeOneBotRuntime runtime = new() { BotId = 3340947887 };
        await using ChatBot chatBot = await StartModelBackedQChatAsync(runtime);
        List<string> sentMessages = [];
        List<string> receivedChunks = [];
        chatBot.ChatSent += sentMessages.Add;
        chatBot.ChatReceived += receivedChunks.Add;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = runtime.BotId,
            UserId = 3045846738,
            RawMessage = "闭环测试：私聊回复 private-ok"
        });
        bool privateOk = await WaitUntilAsync(() => runtime.PrivateMessages.Count >= 1, TimeSpan.FromSeconds(60));
        Assert.That(privateOk, Is.True, BuildDiagnostics(chatBot, sentMessages, receivedChunks, runtime));

        int beforePassive = runtime.GroupMessages.Count;
        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = runtime.BotId,
            UserId = 3425085583,
            GroupId = 867165927,
            GroupName = "AstralFoxTest",
            Sender = new OneBotSender { UserId = 3425085583, Nickname = "test-user" },
            RawMessage = "闭环测试：非@主动回复 passive-ok"
        });
        bool passiveOk = await WaitUntilAsync(() => runtime.GroupMessages.Count > beforePassive, TimeSpan.FromSeconds(60));
        Assert.That(passiveOk, Is.True, BuildDiagnostics(chatBot, sentMessages, receivedChunks, runtime));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = runtime.BotId,
            UserId = 3425085583,
            GroupId = 867165927,
            GroupName = "AstralFoxTest",
            Sender = new OneBotSender { UserId = 3425085583, Nickname = "test-user" },
            RawMessage = $"[CQ:at,qq={runtime.BotId}] 闭环测试：群聊@回复 mention-ok"
        });
        bool mentionOk = await WaitUntilAsync(() => runtime.GroupMessages.Count > beforePassive + 1, TimeSpan.FromSeconds(60));
        Assert.That(mentionOk, Is.True, BuildDiagnostics(chatBot, sentMessages, receivedChunks, runtime));

        TestContext.Out.WriteLine($"Private replies: {runtime.PrivateMessages.Count}");
        TestContext.Out.WriteLine($"Group replies: {runtime.GroupMessages.Count}");
        Assert.That(runtime.PrivateMessages.Any(message => message.Target == 3045846738), Is.True);
        Assert.That(runtime.GroupMessages.Any(message => message.Target == 867165927), Is.True);
    }

    [Test]
    public async Task LiveRealOneBotIncomingMessagesTriggerModelReplies()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_REAL_MODEL_LOOP") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_REAL_MODEL_LOOP=1 and send real QQ trigger messages.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long botId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_BOT_ID", 3340947887);
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);
        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");

        OneBotRuntime realRuntime = new(new OneBotClient(url, token));
        TrackingOneBotRuntime runtime = new(realRuntime) { BotIdOverride = botId };
        await using ChatBot chatBot = await StartModelBackedQChatAsync(runtime, botId, ownerId, groupId);
        List<string> sentMessages = [];
        List<string> receivedChunks = [];
        chatBot.ChatSent += sentMessages.Add;
        chatBot.ChatReceived += receivedChunks.Add;

        TestContext.Out.WriteLine($"Connected OneBot bot id: {runtime.BotId}");
        TestContext.Out.WriteLine($"Send now:");
        TestContext.Out.WriteLine($"1. Private to {botId}: AstralFox real-private-ok {marker}");
        TestContext.Out.WriteLine($"2. Group {groupId} @{botId}: AstralFox real-mention-ok {marker}");
        TestContext.Out.WriteLine($"3. Group {groupId} without @: AstralFox real-passive-ok {marker}");

        bool privateOk = await WaitUntilAsync(
            () => runtime.PrivateMessages.Any(message => message.Target == ownerId && message.Message.Contains("real-private-ok")),
            TimeSpan.FromSeconds(120));
        Assert.That(privateOk, Is.True, BuildRealDiagnostics(chatBot, sentMessages, receivedChunks, runtime));

        bool mentionOk = await WaitUntilAsync(
            () => runtime.GroupMessages.Any(message => message.Target == groupId && message.Message.Contains("real-mention-ok")),
            TimeSpan.FromSeconds(120));
        Assert.That(mentionOk, Is.True, BuildRealDiagnostics(chatBot, sentMessages, receivedChunks, runtime));

        bool passiveOk = await WaitUntilAsync(
            () => runtime.GroupMessages.Any(message => message.Target == groupId && message.Message.Contains("real-passive-ok")),
            TimeSpan.FromSeconds(120));
        Assert.That(passiveOk, Is.True, BuildRealDiagnostics(chatBot, sentMessages, receivedChunks, runtime));

        TestContext.Out.WriteLine($"Private replies: {runtime.PrivateMessages.Count}");
        TestContext.Out.WriteLine($"Group replies: {runtime.GroupMessages.Count}");
    }

    static async Task<ChatBot> StartModelBackedQChatAsync(
        IOneBotRuntime runtime,
        long botId,
        long ownerId,
        long groupId)
    {
        OpenAILanguageModelConfig? modelConfig = new ConfigurationSystem(new StorageSystem())
            .GetConfiguration(typeof(OpenAILanguageModel)) as OpenAILanguageModelConfig;
        Assert.That(modelConfig, Is.Not.Null, "OpenAI-compatible model configuration was not found.");
        Assert.That(string.IsNullOrWhiteSpace(modelConfig!.apiKey), Is.False, "OpenAI-compatible model apiKey is empty.");

        Character character = new()
        {
            Name = "AstralFoxLiveLoop",
            Prompt = """
                     You are validating the QQ reply loop.
                     When the user message contains "private-ok", answer by sending a short QQ private message with exactly the token from the user message that ends in "private-ok".
                     When the user message contains "mention-ok", answer by sending a short QQ group message with exactly the token from the user message that ends in "mention-ok".
                     When the user message contains "passive-ok", answer by sending a short QQ group message with exactly the token from the user message that ends in "passive-ok".
                     Use the qchat XML tool directly. Do not only explain what you would do.
                     """
        };
        ChatHistoryAgentThread thread = new();
        IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
        OpenAILanguageModel languageModel = new(new NullLogger<OpenAILanguageModel>())
        {
            Configuration = modelConfig
        };
        languageModel.RegisterChatCompletion(kernelBuilder);
        Kernel kernel = kernelBuilder.Build();

        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService qchat = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = botId,
                OwnerId = ownerId,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                AllowProactiveGroupChat = true,
                ProactiveChatProbability = 1,
                PassiveGroupReplyCooldownSeconds = 0,
                FlushInterval = 0,
                EnableBalancedTextStreaming = false,
                AppendChatPrompt = """
                                   For this validation, reply quickly through the QQ qchat XML tool.
                                   Reply only to the current QQ session.
                                   """
            }
        };

        AwakeContext awakeContext = new()
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = kernelBuilder
        };
        await functionCaller.AwakeAsync(awakeContext);
        await qchat.AwakeAsync(awakeContext);

        ChatCompletionAgent agent = new()
        {
            Name = character.Name,
            Instructions = character.Prompt,
            InstructionsRole = AuthorRole.System,
            Kernel = kernel,
            Arguments = new KernelArguments(languageModel.ProvidePromptExecutionSettings())
        };
        ChatBot chatBot = new(agent, thread);
        ChatActivity activity = new(character, kernel, null!, chatBot, []);
        await functionCaller.StartAsync(kernel, activity);
        await qchat.StartAsync(kernel, activity);
        return chatBot;
    }

    static Task<ChatBot> StartModelBackedQChatAsync(FakeOneBotRuntime runtime)
    {
        return StartModelBackedQChatAsync(runtime, runtime.BotId, 3045846738, 867165927);
    }

    static string BuildDiagnostics(
        ChatBot chatBot,
        IReadOnlyList<string> sentMessages,
        IReadOnlyList<string> receivedChunks,
        FakeOneBotRuntime runtime)
    {
        ChatRuntimeState state = chatBot.GetRuntimeState();
        string recentEvents = string.Join(" | ", state.RecentEvents.TakeLast(8).Select(item => $"{item.Kind}:{item.Detail}"));
        string receivedPreview = string.Join("", receivedChunks).Trim();
        if (receivedPreview.Length > 600)
            receivedPreview = receivedPreview[..600];

        return $"""
                Model reply loop did not produce expected qchat output.
                SentToModel={sentMessages.Count}; ReceivedChunks={receivedChunks.Count}; PrivateOut={runtime.PrivateMessages.Count}; GroupOut={runtime.GroupMessages.Count}
                IsChatting={state.IsChatting}; PendingPokes={state.PendingPokeCount}; LastError={state.LastError}
                RecentEvents={recentEvents}
                ReceivedPreview={receivedPreview}
                """;
    }

    static string BuildRealDiagnostics(
        ChatBot chatBot,
        IReadOnlyList<string> sentMessages,
        IReadOnlyList<string> receivedChunks,
        TrackingOneBotRuntime runtime)
    {
        ChatRuntimeState state = chatBot.GetRuntimeState();
        string recentEvents = string.Join(" | ", state.RecentEvents.TakeLast(8).Select(item => $"{item.Kind}:{item.Detail}"));
        string receivedPreview = string.Join("", receivedChunks).Trim();
        if (receivedPreview.Length > 600)
            receivedPreview = receivedPreview[..600];

        return $"""
                Real OneBot model reply loop did not produce expected qchat output.
                SentToModel={sentMessages.Count}; ReceivedChunks={receivedChunks.Count}; PrivateOut={runtime.PrivateMessages.Count}; GroupOut={runtime.GroupMessages.Count}
                IsConnected={runtime.IsConnected}; BotId={runtime.BotId}; IsChatting={state.IsChatting}; PendingPokes={state.PendingPokeCount}; LastError={state.LastError}
                RecentEvents={recentEvents}
                ReceivedPreview={receivedPreview}
                """;
    }

    static long ReadLongEnvironment(string name, long fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return long.TryParse(value, out long parsed) && parsed > 0 ? parsed : fallback;
    }

    static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(250);
        }

        return false;
    }

    sealed class TrackingOneBotRuntime(IOneBotRuntime inner) : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived
        {
            add => inner.EventReceived += value;
            remove => inner.EventReceived -= value;
        }

        public long BotId => inner.BotId == 0 ? BotIdOverride : inner.BotId;
        public long BotIdOverride { get; init; }
        public bool IsConnected => inner.IsConnected;
        public string Url { get => inner.Url; set => inner.Url = value; }
        public string Token { get => inner.Token; set => inner.Token = value; }
        public List<(long Target, string Message)> GroupMessages { get; } = [];
        public List<(long Target, string Message)> PrivateMessages { get; } = [];

        public Task ConnectAsync() => inner.ConnectAsync();

        public async Task SendGroupMessage(long groupId, string message)
        {
            GroupMessages.Add((groupId, message));
            await inner.SendGroupMessage(groupId, message);
        }

        public async Task SendPrivateMessage(long userId, string message)
        {
            PrivateMessages.Add((userId, message));
            await inner.SendPrivateMessage(userId, message);
        }

        public Task UploadGroupFile(long groupId, string filePath, string name) => inner.UploadGroupFile(groupId, filePath, name);
        public Task UploadPrivateFile(long userId, string filePath, string name) => inner.UploadPrivateFile(userId, filePath, name);
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => inner.GetPrivateFileUrl(fileId);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => inner.GetGroupFileUrl(groupId, fileId);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => inner.GetMessage(messageId);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => inner.GetForwardMessage(forwardId);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => inner.GetGroupList();
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId) => inner.GetGroupMemberList(groupId);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    sealed class FakeOneBotRuntime : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId { get; init; }
        public bool IsConnected { get; private set; }
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public List<(long Target, string Message)> GroupMessages { get; } = [];
        public List<(long Target, string Message)> PrivateMessages { get; } = [];

        public Task ConnectAsync()
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task SendGroupMessage(long groupId, string message)
        {
            GroupMessages.Add((groupId, message));
            return Task.CompletedTask;
        }

        public Task SendPrivateMessage(long userId, string message)
        {
            PrivateMessages.Add((userId, message));
            return Task.CompletedTask;
        }

        public Task UploadGroupFile(long groupId, string filePath, string name) => Task.CompletedTask;
        public Task UploadPrivateFile(long userId, string filePath, string name) => Task.CompletedTask;
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => Task.FromResult<OneBotMessageEvent?>(null);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => Task.FromResult<List<OneBotForwardMessage>?>([]);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => Task.FromResult<IReadOnlyList<OneBotGroupInfo>>([]);
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId) => Task.FromResult<IReadOnlyList<OneBotGroupMember>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise(OneBotBaseEvent ev) => EventReceived?.Invoke(ev);
    }
}
