using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Alife.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alife.Test.Framework;

public class ChatBotLifecycleTests
{
    [Test]
    public void ChatBotTimerLoopIsTaskBased()
    {
        string[] asyncVoidMethods = typeof(ChatBot)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.ReturnType == typeof(void))
            .Where(method => method.GetCustomAttribute<AsyncStateMachineAttribute>() != null)
            .Select(method => method.Name)
            .ToArray();
        MethodInfo? legacyUpdate = typeof(ChatBot).GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? updateAsync = typeof(ChatBot).GetMethod(
            "UpdateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(asyncVoidMethods, Is.Empty, "ChatBot should not contain async void methods.");
        Assert.That(legacyUpdate, Is.Null, "ChatBot must not use an async void timer loop.");
        Assert.That(updateAsync, Is.Not.Null, "ChatBot should expose its timer loop as a private Task-returning method.");
        Assert.That(typeof(Task).IsAssignableFrom(updateAsync!.ReturnType), Is.True);
    }

    [Test]
    public void InteractiveModuleUpdateLoopIsTaskBased()
    {
        string[] asyncVoidMethods = typeof(InteractiveModule)
            .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.ReturnType == typeof(void))
            .Where(method => method.GetCustomAttribute<AsyncStateMachineAttribute>() != null)
            .Select(method => method.Name)
            .ToArray();
        MethodInfo? startUpdate = typeof(InteractiveModule).GetMethod(
            "StartUpdate",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(asyncVoidMethods, Is.Empty, "InteractiveModule update loop should not use async void.");
        Assert.That(startUpdate, Is.Not.Null);
        Assert.That(typeof(Task).IsAssignableFrom(startUpdate!.ReturnType), Is.True);
    }

    [Test]
    public async Task DisposeAsyncCompletesWhenChatLockIsHeld()
    {
        ChatBot chatBot = new(null!, null!);
        await chatBot.RequestChatAsync();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Task disposeTask = chatBot.DisposeAsync().AsTask();
        Task completedTask = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(6.5)));
        stopwatch.Stop();

        if (completedTask != disposeTask)
        {
            chatBot.ReleaseChat();
            await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(1)));
        }

        Assert.That(completedTask, Is.SameAs(disposeTask), "DisposeAsync should stop waiting after its bounded timeout.");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(6)));
    }

    [Test]
    public async Task GetRuntimeStateReportsPendingPokeCountAndRecentEvents()
    {
        await using ChatBot chatBot = new(null!, new ChatHistoryAgentThread());

        chatBot.Poke("first");
        chatBot.Poke("second");

        ChatRuntimeState state = chatBot.GetRuntimeState();

        Assert.That(state.PendingPokeCount, Is.EqualTo(2));
        Assert.That(state.RecentEvents.Any(runtimeEvent => runtimeEvent.Kind == "PokeQueued"), Is.True);
    }

    [Test]
    public async Task ExposesUnderlyingAgentThreadForAdvancedRuntimeServices()
    {
        ChatHistoryAgentThread thread = new();
        await using ChatBot chatBot = new(null!, thread);

        Assert.That(chatBot.ChatCompletionAgent, Is.Null);
        Assert.That(chatBot.ChatHistoryAgentThread, Is.SameAs(thread));
        Assert.That(chatBot.ChatHistory, Is.SameAs(thread.ChatHistory));
    }

    [Test]
    public async Task GetRuntimeStateReportsLastError()
    {
        await using ChatBot chatBot = new(null!, new ChatHistoryAgentThread());

        try
        {
            await chatBot.ChatAsync("hello");
        }
        catch
        {
            // Expected: this test intentionally uses a null agent to exercise runtime error recording.
        }

        ChatRuntimeState state = chatBot.GetRuntimeState();

        Assert.That(state.LastError, Is.Not.Null);
        Assert.That(state.RecentEvents.Any(runtimeEvent => runtimeEvent.Kind == "Error"), Is.True);
    }

    [Test]
    public async Task GetRuntimeStateReportsChatLatencyMetricsWhenChatFails()
    {
        await using ChatBot chatBot = new(null!, new ChatHistoryAgentThread());

        try
        {
            await chatBot.ChatAsync("hello");
        }
        catch
        {
            // Expected: this test intentionally uses a null agent to exercise runtime metrics on failure.
        }

        ChatRuntimeState state = chatBot.GetRuntimeState();

        Assert.That(state.Latency.LastChatStartedAt, Is.Not.Null);
        Assert.That(state.Latency.LastChatEndedAt, Is.Not.Null);
        Assert.That(state.Latency.LastChatDuration, Is.Not.Null);
        Assert.That(state.Latency.LastFirstContentLatency, Is.Null);
    }

    [Test]
    public async Task GetRuntimeStateReportsPokeFlushSchedulingEvents()
    {
        await using ChatBot chatBot = new(null!, new ChatHistoryAgentThread());
        chatBot.Poke("scheduled");

        MethodInfo method = typeof(ChatBot).GetMethod(
            "TryFlushMessageCache",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TryFlushMessageCache was not found.");
        Task flushTask = (Task)method.Invoke(chatBot, [CancellationToken.None])!;

        await flushTask;
        ChatRuntimeState state = chatBot.GetRuntimeState();

        Assert.That(state.PendingPokeCount, Is.EqualTo(0));
        Assert.That(state.RecentEvents.Any(runtimeEvent => runtimeEvent.Kind == "PokeFlushStarted"), Is.True);
        Assert.That(state.RecentEvents.Any(runtimeEvent => runtimeEvent.Kind == "PokeFlushDispatched"), Is.True);
    }

    [Test]
    public void StreamChunkClassifierDoesNotLeakThinkPrefixWhenItArrivesAcrossChunks()
    {
        ChatStreamChunkClassifier classifier = new(ChatBot.ThinkContentPrefix);

        ChatStreamChunkClassification first = classifier.Push("__TH");
        ChatStreamChunkClassification second = classifier.Push("INK__隐藏内容");
        ChatStreamChunkClassification third = classifier.Push("可见回复");

        Assert.Multiple(() =>
        {
            Assert.That(first.VisibleText, Is.Empty);
            Assert.That(first.ReasoningText, Is.Empty);
            Assert.That(second.VisibleText, Is.Empty);
            Assert.That(second.ReasoningText, Is.EqualTo("隐藏内容"));
            Assert.That(third.VisibleText, Is.EqualTo("可见回复"));
            Assert.That(third.ReasoningText, Is.Empty);
        });
    }

    [Test]
    public async Task CancelledGenerationCannotPublishLateChunkOrFinishAfterNewGenerationStarts()
    {
        ControlledStreamingCompletionService completion = new();
        await using ChatBot chatBot = CreateChatBot(completion);
        List<string> received = [];
        List<string> finished = [];
        int over = 0;
        chatBot.ChatReceived += received.Add;
        chatBot.ChatFinished += (input, _) => finished.Add(input);
        chatBot.ChatOver += () => over++;

        Task<string> first = chatBot.ChatAsync("first");
        await completion.FirstInvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<string> second = chatBot.ChatAsync("second");
        completion.ReleaseFirstInvocation.TrySetResult();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo(["new"]));
            Assert.That(finished, Is.EqualTo(["second"]));
            Assert.That(over, Is.EqualTo(1));
            Assert.That(chatBot.ChatHistory.Any(message => message.Role == AuthorRole.Assistant && message.Content == "old"), Is.False);
        });
    }

    [Test]
    public async Task ChatAsyncPassesPerCallReasoningEffortToProvider()
    {
        ReasoningEffortRecordingCompletionService completion = new();
        await using ChatBot chatBot = CreateChatBot(completion);
        MethodInfo? method = typeof(ChatBot).GetMethod(
            "ChatAsync",
            [typeof(string), typeof(AuthorRole?), typeof(CancellationToken), typeof(string)]);

        Assert.That(method, Is.Not.Null, "ChatBot must support a per-call reasoning-effort override.");
        Task<string> response = (Task<string>)method!.Invoke(
            chatBot,
            ["analyze this", null, CancellationToken.None, "high"])!;
        string responseText = await response;

        Assert.Multiple(() =>
        {
            Assert.That(responseText, Is.EqualTo("reasoned"));
            Assert.That(completion.ReasoningEfforts, Is.EqualTo(["high"]));
        });
    }

    static ChatBot CreateChatBot(IChatCompletionService completion)
    {
        IKernelBuilder builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(completion);
        Kernel kernel = builder.Build();
        ChatCompletionAgent agent = new()
        {
            Name = "test",
            Instructions = "test",
            Kernel = kernel
        };
        return new ChatBot(agent, new ChatHistoryAgentThread());
    }

    sealed class ControlledStreamingCompletionService : IChatCompletionService
    {
        int invocation;
        public TaskCompletionSource FirstInvocationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstInvocation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessageContent>>([]);

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int current = Interlocked.Increment(ref invocation);
            if (current == 1)
            {
                FirstInvocationStarted.TrySetResult();
                await ReleaseFirstInvocation.Task;
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, "old");
                yield break;
            }

            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "new");
        }
    }

    sealed class ReasoningEffortRecordingCompletionService : IChatCompletionService
    {
        public List<string?> ReasoningEfforts { get; } = [];
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessageContent>>([]);

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReasoningEfforts.Add((executionSettings as OpenAIPromptExecutionSettings)?.ReasoningEffort?.ToString());
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "reasoned");
            await Task.CompletedTask;
        }
    }
}
