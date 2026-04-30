using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Framework;

public abstract class InteractivePlugin : Plugin
{
    protected ChatBot ChatBot { get; private set; } = null!;
    protected ChatHistory ChatHistory { get; private set; } = null!;

    public override Task AwakeAsync(AwakeContext context)
    {
        ChatHistory = context.contextBuilder.ChatHistory;

        return Task.CompletedTask;
    }
    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        ChatBot = chatActivity.ChatBot;

        if (this is ITimeIterative interactivePlugin)
        {
            updateCancellation = new CancellationTokenSource();
            StartUpdate(interactivePlugin, updateCancellation.Token);
        }

        return Task.CompletedTask;
    }
    public override Task DestroyAsync()
    {
        if (updateCancellation != null)
            return updateCancellation.CancelAsync();
        return base.DestroyAsync();
    }

    CancellationTokenSource? updateCancellation;

    static async void StartUpdate(ITimeIterative handler, CancellationToken token)
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
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
public class InteractivePlugin<T> : InteractivePlugin
{
    protected string ChatPrefixPrompt => $"[这是来自{typeof(T).Name}的消息]";
    protected void Prompt(string prompt)
    {
        ChatHistory.AddSystemMessage($"[{typeof(T).Name}] {prompt}");
    }
    protected void Throw(string error)
    {
        throw new Exception($"[{typeof(T).Name}] 发生错误\n{error}");
    }
    protected void Poke(string message)
    {
        ChatBot.Poke($"{ChatPrefixPrompt}{message}");
    }
    protected void Chat(string message)
    {
        ChatBot.Chat($"{ChatPrefixPrompt}{message}");
    }
    protected Task ChatAsync(string message)
    {
        return ChatBot.ChatAsync($"{ChatPrefixPrompt}{message}");
    }
}
