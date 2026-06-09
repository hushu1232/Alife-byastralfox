using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Framework;

public abstract class InteractiveModule : ISystemEvent
{
    protected Character Character { get; private set; } = null!;
    protected ChatActivity ChatActivity { get; private set; } = null!;
    protected ChatBot ChatBot { get; private set; } = null!;
    protected ChatHistory ChatHistory { get; private set; } = null!;

    public virtual Task AwakeAsync(AwakeContext context)
    {
        Character = context.Character;
        ChatHistory = context.ContextBuilder.ChatHistory;

        return Task.CompletedTask;
    }
    public virtual Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        ChatActivity = chatActivity;
        ChatBot = chatActivity.ChatBot;

        if (this is ITimeIterative interactiveModule)
        {
            updateCancellation = new CancellationTokenSource();
            updateTask = StartUpdate(interactiveModule, updateCancellation.Token);
        }

        return Task.CompletedTask;
    }
    public virtual async Task DestroyAsync()
    {
        if (updateCancellation != null)
            await updateCancellation.CancelAsync();
        if (updateTask != null)
            await updateTask;
        updateCancellation?.Dispose();
        updateCancellation = null;
        updateTask = null;
    }

    CancellationTokenSource? updateCancellation;
    Task? updateTask;

    static async Task StartUpdate(ITimeIterative handler, CancellationToken token)
    {
        try
        {
            DateTime startTime = DateTime.Now;
            while (!token.IsCancellationRequested)
            {
                await Task.Delay((int)(handler.DeltaTime * 1000), token);
                float seconds = (float)(DateTime.Now - startTime).TotalSeconds;
                handler.OnUpdate(ref seconds);
                startTime = DateTime.Now - TimeSpan.FromSeconds(seconds);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            AlifeTerminal.LogError(e.ToString());
        }
    }
}

public class InteractiveModule<T> : InteractiveModule
{
    protected virtual string ChatTextFilter(string text)
    {
        return $"[{typeof(T).Name}]{text}";
    }

    protected void Prompt(string prompt)
    {
        ChatHistory.AddSystemMessage($"# {typeof(T).Name}功能介绍\n{prompt}");
    }

    protected void Throw(string error)
    {
        throw new Exception($"[{typeof(T).Name}] 发生错误\n{error}");
    }

    protected void Poke(string message)
    {
        ChatBot.Poke(ChatTextFilter(message));
    }

    protected void Chat(string message)
    {
        ChatBot.Chat(ChatTextFilter(message));
    }

    protected Task ChatAsync(string message)
    {
        return ChatBot.ChatAsync(ChatTextFilter(message));
    }

    protected Task ImplicitChatAsync(string message)
    {
        return ChatBot.ImplicitChatAsync(ChatTextFilter(message));
    }
}
