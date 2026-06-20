using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Alife.Framework;
using Microsoft.SemanticKernel.Agents;

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
}
