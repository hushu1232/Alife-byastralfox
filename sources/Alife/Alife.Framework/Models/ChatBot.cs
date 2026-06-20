using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using ChatMessageContent=Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Framework;

public sealed record ChatRuntimeEvent(DateTimeOffset Timestamp, string Kind, string Detail);

public sealed record ChatLatencySnapshot(
    DateTimeOffset? LastChatStartedAt,
    DateTimeOffset? LastFirstContentAt,
    DateTimeOffset? LastChatEndedAt,
    TimeSpan? LastFirstContentLatency,
    TimeSpan? LastChatDuration)
{
    public static ChatLatencySnapshot Empty { get; } = new(null, null, null, null, null);
}

public sealed record ChatRuntimeState(
    bool IsChatting,
    int PendingPokeCount,
    int ChatHistoryCount,
    string? LastError,
    IReadOnlyList<ChatRuntimeEvent> RecentEvents)
{
    public ChatLatencySnapshot Latency { get; init; } = ChatLatencySnapshot.Empty;
}

public class ChatBot : IAsyncDisposable
{
    public const string ThinkContentPrefix = "__THINK__";
    public const string PokeMessageTag = "[来自系统的杂项消息推送]";

    public event Func<string, string>? PokeSend;//Poke消息过滤
    public event Func<string, string>? ChatSend;//消息过滤
    public event Action<string>? ChatSent;//消息发送前
    public event Action<string>? ChatReceived;//消息接收到
    public event Action<string>? ReasoningReceived;//思考消息接收到
    public event Action? ChatOver;//消息结束

    public event Action<ChatMessageContent>? ChatHistoryAdd;
    public event Action<ChatTokenUsage>? TokenUsed;
    public ChatHistory ChatHistory => llmAgentThread.ChatHistory;
    public bool IsChatting => chatSemaphore.CurrentCount == 0;
    public CancellationTokenSource ChatBreakTokenSource => chatBreakSource;

    public async Task RequestChatAsync(CancellationToken cancellationToken = default)
    {
        RecordRuntimeEvent("ChatLockWait", "Waiting for chat lock.");
        await chatSemaphore.WaitAsync(cancellationToken);
        RecordRuntimeEvent("ChatLockAcquired", "Chat lock acquired.");
    }

    public void ReleaseChat()
    {
        chatSemaphore.Release();
        RecordRuntimeEvent("ChatLockReleased", "Chat lock released.");
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(string message, AuthorRole? role = null)
    {
        if (IsChatting)//打断上一次的聊天
        {
            await chatBreakSource.CancelAsync();
        }

        await RequestChatAsync();
        try
        {
            MarkChatStart();
            RecordRuntimeEvent("ChatStart", "Chat streaming started.");
            chatBreakSource = new CancellationTokenSource();
            if (llmAgent == null)
            {
                InvalidOperationException exception = new("Chat completion agent is unavailable.");
                RecordError(exception.ToString());
                throw exception;
            }

            if (ChatSend != null)
            {
                foreach (Delegate @delegate in ChatSend.GetInvocationList())
                {
                    Func<string, string> chatSend = (Func<string, string>)@delegate;
                    message = chatSend.Invoke(message);
                }
            }

            message = message.Trim();
            llmAgentThread.ChatHistory.AddMessage(role ?? AuthorRole.User, message);

            ChaseChatHistory();

            ChatSent?.Invoke(message);
            string? error = null;
            StringBuilder cleanResponseBuilder = new();// 用于存储不含思考过程的最终回复

            await using IAsyncEnumerator<AgentResponseItem<StreamingChatMessageContent>> enumerator = llmAgent
                .InvokeStreamingAsync(llmAgentThread, cancellationToken: chatBreakSource.Token)
                .GetAsyncEnumerator();
            while (true)
            {
                try
                {
                    if (await enumerator.MoveNextAsync() == false)
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    error = e.ToString();
                    break;
                }

                string? content = enumerator.Current.Message.Content;
                if (content != null)
                {
                    //前置报文会对思考内容进行特殊处理，以便兼容思考模式
                    if (content.StartsWith(ThinkContentPrefix))
                    {
                        string reasoningPart = content.Substring(ThinkContentPrefix.Length);
                        if (!string.IsNullOrEmpty(reasoningPart))
                        {
                            ReasoningReceived?.Invoke(reasoningPart);
                        }
                    }
                    else
                    {
                        MarkFirstContent();
                        yield return content;
                        ChatReceived?.Invoke(content);
                        cleanResponseBuilder.Append(content);
                    }
                }

                IReadOnlyDictionary<string, object?>? metaData = enumerator.Current.Message.Metadata;
                if (metaData != null)
                {
                    // 尝试从元数据中提取思考过程 (支持原生支持此字段的 SDK)
                    if (metaData.TryGetValue("ReasoningContent", out object? reasoning) ||
                        metaData.TryGetValue("reasoning_content", out reasoning))
                    {
                        string? reasoningStr = reasoning?.ToString();
                        if (!string.IsNullOrEmpty(reasoningStr))
                        {
                            ReasoningReceived?.Invoke(reasoningStr);
                        }
                    }

                    if (metaData.TryGetValue("Usage", out object? usage))
                    {
                        if (usage is ChatTokenUsage chatTokenUsage)
                        {
                            AlifeTerminal.LogInfo("[ChatBot]" + KernelPrinter.ToTokenLog(metaData));
                            TokenUsed?.Invoke(chatTokenUsage);
                        }
                    }
                }
            }

            // 在同步历史记录前，清洗掉可能存入 ChatHistory 的思考内容（防止污染上下文）
            if (llmAgentThread.ChatHistory.Count > 0)
            {
                ChatMessageContent lastMsg = llmAgentThread.ChatHistory[^1];
                if (lastMsg.Role == AuthorRole.Assistant && (lastMsg.Content?.Contains(ThinkContentPrefix) ?? false))
                    lastMsg.Content = cleanResponseBuilder.ToString();
            }

            ChatOver?.Invoke();

            ChaseChatHistory();

            if (error != null)
            {
                RecordError(error);
                llmAgentThread.ChatHistory.AddMessage(AuthorRole.System, error);
                yield return error;
            }
        }
        finally
        {
            MarkChatEnd();
            RecordRuntimeEvent("ChatEnd", "Chat streaming ended.");
            ReleaseChat();
        }
    }

    public async Task<string> ChatAsync(string message, AuthorRole? role = null)
    {
        StringBuilder stringBuilder = new StringBuilder();
        await foreach (string content in ChatStreamingAsync(message, role))
            stringBuilder.Append(content);
        return stringBuilder.ToString();
    }

    public void Chat(string content, AuthorRole? role = null)
    {
        _ = ChatFireAndForgetAsync(content, role);
    }

    async Task ChatFireAndForgetAsync(string content, AuthorRole? role = null)
    {
        try
        {
            await ChatAsync(content, role);
        }
        catch (Exception e)
        {
            RecordError(e.ToString());
            AlifeTerminal.LogError(e.ToString());
        }
    }

    public void Poke(string message)
    {
        while (messageCache.Count > 11)
            messageCache.TryDequeue(out _);
        messageCache.Enqueue($"{message}\n");
        RecordRuntimeEvent("PokeQueued", $"Pending poke messages: {messageCache.Count}.");
        lastAutoFlushTime = 0;//重新计时，防止后续还有Poke
    }

    public async Task ImplicitChatAsync(string message)
    {
        await RequestChatAsync();
        ChatHistory.AddUserMessage(message);
        ReleaseChat();
    }

    public void UpdateHistoryEndIndex()
    {
        lastContentIndex = ChatHistory.Count;
    }

    public ChatRuntimeState GetRuntimeState()
    {
        return new ChatRuntimeState(
            IsChatting,
            messageCache.Count,
            llmAgentThread?.ChatHistory.Count ?? 0,
            lastError,
            runtimeEvents.ToArray())
        {
            Latency = BuildLatencySnapshot()
        };
    }

    readonly ChatCompletionAgent llmAgent;
    readonly ChatHistoryAgentThread llmAgentThread;
    readonly ConcurrentQueue<string> messageCache;
    readonly ConcurrentQueue<ChatRuntimeEvent> runtimeEvents = new();
    readonly SemaphoreSlim chatSemaphore;
    CancellationTokenSource chatBreakSource = new();
    string? lastError;
    DateTimeOffset? lastChatStartedAt;
    DateTimeOffset? lastFirstContentAt;
    DateTimeOffset? lastChatEndedAt;

    int lastContentIndex;

    //计时器
    readonly CancellationTokenSource timerCancellationSource = new();
    readonly Task updateTask;
    int currentTime;
    int lastAutoFlushTime;
    const int DeltaTime = 1;


    public ChatBot(ChatCompletionAgent llmAgent, ChatHistoryAgentThread llmAgentThread)
    {
        this.llmAgent = llmAgent;
        this.llmAgentThread = llmAgentThread;
        messageCache = new ConcurrentQueue<string>();
        chatSemaphore = new SemaphoreSlim(1, 1);

        updateTask = UpdateAsync(timerCancellationSource.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await timerCancellationSource.CancelAsync();
        await updateTask;

        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(5));
        while (!timeoutSource.IsCancellationRequested && (IsChatting || !messageCache.IsEmpty))
        {
            try
            {
                await TryFlushMessageCache(timeoutSource.Token);
                await Task.Delay(100, timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        timerCancellationSource.Dispose();
    }

    async Task UpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer periodicTimer = new(TimeSpan.FromSeconds(DeltaTime));
            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                currentTime += DeltaTime;
                if (currentTime - lastAutoFlushTime > 2)
                {
                    await TryFlushMessageCache(cancellationToken);
                    lastAutoFlushTime = currentTime;
                }
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            RecordError(e.ToString());
            AlifeTerminal.LogError(e.ToString());
        }
    }

    async Task TryFlushMessageCache(CancellationToken cancellationToken = default)
    {
        int pendingCount = messageCache.Count;
        if (pendingCount == 0)
            return;

        RecordRuntimeEvent("PokeFlushStarted", $"Flushing {pendingCount} pending poke message(s).");
        await RequestChatAsync(cancellationToken);
        try
        {
            //组合消息
            StringBuilder stringBuilder = new();
            foreach (string message in messageCache.Distinct())
                stringBuilder.AppendLine(message);
            string poke = stringBuilder.ToString();
            messageCache.Clear();

            if (PokeSend != null)
            {
                foreach (Delegate @delegate in PokeSend.GetInvocationList())
                {
                    Func<string, string> pokeSend = (Func<string, string>)@delegate;
                    poke = pokeSend.Invoke(poke);
                }
            }

            //发送消息
            RecordRuntimeEvent("PokeFlushDispatched", "Pending poke messages were dispatched into chat.");
            Chat($"{PokeMessageTag}\n{poke}");
        }
        finally
        {
            ReleaseChat();
        }
    }

    void ChaseChatHistory()
    {
        for (; lastContentIndex < ChatHistory.Count; lastContentIndex++)
            ChatHistoryAdd?.Invoke(ChatHistory[lastContentIndex]);
    }

    void RecordError(string error)
    {
        lastError = error;
        RecordRuntimeEvent("Error", error);
    }

    void MarkChatStart()
    {
        lastChatStartedAt = DateTimeOffset.Now;
        lastFirstContentAt = null;
        lastChatEndedAt = null;
    }

    void MarkFirstContent()
    {
        lastFirstContentAt ??= DateTimeOffset.Now;
    }

    void MarkChatEnd()
    {
        lastChatEndedAt = DateTimeOffset.Now;
    }

    ChatLatencySnapshot BuildLatencySnapshot()
    {
        TimeSpan? firstContentLatency = lastChatStartedAt != null && lastFirstContentAt != null
            ? lastFirstContentAt.Value - lastChatStartedAt.Value
            : null;
        TimeSpan? chatDuration = lastChatStartedAt != null && lastChatEndedAt != null
            ? lastChatEndedAt.Value - lastChatStartedAt.Value
            : null;

        return new ChatLatencySnapshot(
            lastChatStartedAt,
            lastFirstContentAt,
            lastChatEndedAt,
            firstContentLatency,
            chatDuration);
    }

    void RecordRuntimeEvent(string kind, string detail)
    {
        runtimeEvents.Enqueue(new ChatRuntimeEvent(DateTimeOffset.Now, kind, detail));
        while (runtimeEvents.Count > 32)
            runtimeEvents.TryDequeue(out _);
    }
}
