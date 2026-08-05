using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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
    public const string DefaultConversationId = "default";
    public const string LocalConversationId = "local-desktop";

    public event Func<string, string>? PokeSend;//Poke消息过滤
    public event Func<string, string>? ChatSend;//消息过滤
    public event Action<string>? ChatSent;//消息发送前
    public event Action<string>? ChatReceived;//消息接收到
    public event Action<string>? ReasoningReceived;//思考消息接收到
    public event Action<string, string>? ChatFinished;
    public event Action? ChatOver;//消息结束

    public event Action<ChatMessageContent>? ChatHistoryAdd;
    public event Action<ChatTokenUsage>? TokenUsed;
    public ChatCompletionAgent? ChatCompletionAgent => llmAgent;
    public ChatHistoryAgentThread ChatHistoryAgentThread => llmAgentThread;
    public ChatHistory ChatHistory => llmAgentThread.ChatHistory;
    public string CurrentConversationId => activeConversationId.Value ?? DefaultConversationId;
    public string? CurrentInputMessage => activeInputMessage.Value;
    public bool IsChatting => chatSemaphore.CurrentCount == 0;
    public CancellationTokenSource ChatBreakTokenSource => chatBreakSource;

    public ChatHistoryAgentThread CreateConversation(string conversationId)
    {
        string id = NormalizeConversationId(conversationId);
        if (id == DefaultConversationId)
            return llmAgentThread;

        return conversationThreads.GetOrAdd(id, _ => {
            ChatHistoryAgentThread thread = new();
            foreach ((AuthorRole role, string? content) in conversationSeed)
                thread.ChatHistory.Add(new ChatMessageContent(role, content));
            conversationLastContentIndexes[id] = thread.ChatHistory.Count;
            return thread;
        });
    }

    public ChatHistory GetConversationHistory(string conversationId) =>
        CreateConversation(conversationId).ChatHistory;

    public IDisposable UseConversation(string conversationId)
    {
        string id = NormalizeConversationId(conversationId);
        _ = CreateConversation(id);
        string? previous = activeConversationId.Value;
        activeConversationId.Value = id;
        return new ConversationScope(activeConversationId, previous);
    }

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

    public IAsyncEnumerable<string> ChatStreamingAsync(
        string message,
        AuthorRole? role = null,
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null) =>
        ChatStreamingCoreAsync(
            message,
            role,
            cancellationToken,
            reasoningEffort,
            ResolveConversationId());

    public IAsyncEnumerable<string> ChatInConversationStreamingAsync(
        string conversationId,
        string message,
        AuthorRole? role = null,
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null) =>
        ChatStreamingCoreAsync(
            message,
            role,
            cancellationToken,
            reasoningEffort,
            NormalizeConversationId(conversationId));

    async IAsyncEnumerable<string> ChatStreamingCoreAsync(
        string message,
        AuthorRole? role,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? reasoningEffort,
        string conversationId)
    {
        ChatHistoryAgentThread conversationThread = CreateConversation(conversationId);
        using IDisposable conversationScope = UseConversation(conversationId);
        string? previousInputMessage = activeInputMessage.Value;
        activeInputMessage.Value = message;
        long generation = conversationGenerations.AddOrUpdate(
            conversationId,
            1,
            (_, current) => current + 1);
        try
        {
            if (IsChatting && string.Equals(
                    Volatile.Read(ref activeChatConversationId),
                    conversationId,
                    StringComparison.Ordinal))//只打断同一会话的上一次聊天
            {
                await chatBreakSource.CancelAsync();
            }

            await RequestChatAsync(cancellationToken);
            try
            {
                if (IsCurrentGeneration(conversationId, generation) == false)
                    yield break;

                Volatile.Write(ref activeChatConversationId, conversationId);
                MarkChatStart();
                RecordRuntimeEvent("ChatStart", $"Chat streaming started ({conversationId}).");
                chatBreakSource = new CancellationTokenSource();
                using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    chatBreakSource.Token,
                    cancellationToken);
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
                conversationThread.ChatHistory.AddMessage(role ?? AuthorRole.User, message);
                ChaseChatHistory(conversationId, conversationThread);
                InvokeConversationEvent(conversationId, () => ChatSent?.Invoke(message));

                string? error = null;
                bool cancelled = false;
                StringBuilder cleanResponseBuilder = new();// 用于存储不含思考过程的最终回复
                ChatStreamChunkClassifier chunkClassifier = new(ThinkContentPrefix);
                int generatedHistoryStartIndex = conversationThread.ChatHistory.Count;

                AgentInvokeOptions? invokeOptions = CreateInvokeOptions(reasoningEffort);
                IAsyncEnumerable<AgentResponseItem<StreamingChatMessageContent>> stream = invokeOptions == null
                    ? llmAgent.InvokeStreamingAsync(conversationThread, cancellationToken: linkedCancellation.Token)
                    : llmAgent.InvokeStreamingAsync(conversationThread, invokeOptions, linkedCancellation.Token);
                await using IAsyncEnumerator<AgentResponseItem<StreamingChatMessageContent>> enumerator = stream.GetAsyncEnumerator();
                while (true)
                {
                    try
                    {
                        if (await enumerator.MoveNextAsync() == false)
                            break;
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception e)
                    {
                        error = e.ToString();
                        break;
                    }

                    string? content = enumerator.Current.Message.Content;
                    if (content != null && IsCurrentGeneration(conversationId, generation))
                    {
                        ChatStreamChunkClassification classified = chunkClassifier.Push(content);
                        if (string.IsNullOrEmpty(classified.ReasoningText) == false)
                            InvokeConversationEvent(
                                conversationId,
                                () => ReasoningReceived?.Invoke(classified.ReasoningText));
                        if (string.IsNullOrEmpty(classified.VisibleText) == false)
                        {
                            MarkFirstContent();
                            yield return classified.VisibleText;
                            InvokeConversationEvent(
                                conversationId,
                                () => ChatReceived?.Invoke(classified.VisibleText));
                            cleanResponseBuilder.Append(classified.VisibleText);
                        }
                    }

                    IReadOnlyDictionary<string, object?>? metaData = enumerator.Current.Message.Metadata;
                    if (metaData != null && IsCurrentGeneration(conversationId, generation))
                    {
                        // 尝试从元数据中提取思考过程 (支持原生支持此字段的 SDK)
                        if (metaData.TryGetValue("ReasoningContent", out object? reasoning) ||
                            metaData.TryGetValue("reasoning_content", out reasoning))
                        {
                            string? reasoningStr = reasoning?.ToString();
                            if (!string.IsNullOrEmpty(reasoningStr))
                                InvokeConversationEvent(
                                    conversationId,
                                    () => ReasoningReceived?.Invoke(reasoningStr));
                        }

                        if (metaData.TryGetValue("Usage", out object? usage) && usage is ChatTokenUsage chatTokenUsage)
                        {
                            AlifeTerminal.LogInfo("[ChatBot]" + KernelPrinter.ToTokenLog(metaData));
                            TokenUsed?.Invoke(chatTokenUsage);
                        }
                    }
                }

                if (IsCurrentGeneration(conversationId, generation))
                {
                    ChatStreamChunkClassification trailing = chunkClassifier.Flush();
                    if (string.IsNullOrEmpty(trailing.VisibleText) == false)
                    {
                        MarkFirstContent();
                        yield return trailing.VisibleText;
                        InvokeConversationEvent(
                            conversationId,
                            () => ChatReceived?.Invoke(trailing.VisibleText));
                        cleanResponseBuilder.Append(trailing.VisibleText);
                    }

                    if (cancelled)
                    {
                        RemoveGeneratedHistory(conversationThread, generatedHistoryStartIndex);
                        InvokeConversationEvent(conversationId, () => ChatOver?.Invoke());
                    }
                    else
                    {
                        // 在同步历史记录前，清洗掉可能存入 ChatHistory 的思考内容（防止污染上下文）
                        string aiMessage = cleanResponseBuilder.ToString();
                        if (conversationThread.ChatHistory.Count > 0)
                        {
                            ChatMessageContent lastMsg = conversationThread.ChatHistory[^1];
                            if (lastMsg.Role == AuthorRole.Assistant && (lastMsg.Content?.Contains(ThinkContentPrefix) ?? false))
                                lastMsg.Content = aiMessage;
                        }

                        InvokeConversationEvent(
                            conversationId,
                            () => ChatFinished?.Invoke(message, aiMessage));
                        InvokeConversationEvent(conversationId, () => ChatOver?.Invoke());
                        ChaseChatHistory(conversationId, conversationThread);
                    }

                    if (error != null)
                        RecordError(error);
                }
                else
                {
                    RemoveGeneratedHistory(conversationThread, generatedHistoryStartIndex);
                }
            }
            finally
            {
                if (IsCurrentGeneration(conversationId, generation))
                {
                    MarkChatEnd();
                    RecordRuntimeEvent("ChatEnd", $"Chat streaming ended ({conversationId}).");
                    Volatile.Write(ref activeChatConversationId, null);
                }
                ReleaseChat();
            }
        }
        finally
        {
            activeInputMessage.Value = previousInputMessage;
        }
    }

    public async Task<string> ChatAsync(
        string message,
        AuthorRole? role = null,
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null)
    {
        StringBuilder stringBuilder = new StringBuilder();
        await foreach (string content in ChatStreamingAsync(message, role, cancellationToken, reasoningEffort))
            stringBuilder.Append(content);
        return stringBuilder.ToString();
    }

    public async Task<string> ChatInConversationAsync(
        string conversationId,
        string message,
        AuthorRole? role = null,
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null)
    {
        StringBuilder stringBuilder = new();
        await foreach (string content in ChatInConversationStreamingAsync(
                           conversationId,
                           message,
                           role,
                           cancellationToken,
                           reasoningEffort))
        {
            stringBuilder.Append(content);
        }
        return stringBuilder.ToString();
    }

    static AgentInvokeOptions? CreateInvokeOptions(string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
            return null;

        return new AgentInvokeOptions
        {
            KernelArguments = new KernelArguments(new OpenAIPromptExecutionSettings
            {
                ReasoningEffort = reasoningEffort.Trim()
            })
        };
    }

    public void Chat(string content, AuthorRole? role = null)
    {
        _ = ChatFireAndForgetAsync(ResolveConversationId(), content, role);
    }

    public void ChatInConversation(string conversationId, string content, AuthorRole? role = null)
    {
        _ = ChatFireAndForgetAsync(NormalizeConversationId(conversationId), content, role);
    }

    async Task ChatFireAndForgetAsync(string conversationId, string content, AuthorRole? role = null)
    {
        try
        {
            await ChatInConversationAsync(conversationId, content, role);
        }
        catch (Exception e)
        {
            RecordError(e.ToString());
            AlifeTerminal.LogError(e.ToString());
        }
    }

    public void Poke(string message)
    {
        PokeInConversation(ResolveConversationId(), message);
    }

    public void PokeInConversation(string conversationId, string message)
    {
        string id = NormalizeConversationId(conversationId);
        ConcurrentQueue<string> queue = GetMessageCache(id);
        while (queue.Count > 11)
            queue.TryDequeue(out _);
        queue.Enqueue($"{message}\n");
        RecordRuntimeEvent("PokeQueued", $"Pending poke messages for {id}: {queue.Count}.");
        lastAutoFlushTime = currentTime;//重新计时，防止后续还有Poke
    }

    public Task<string?> FlushPendingPokesAsync(
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null)
    {
        return TryFlushConversationMessageCache(
            ResolveConversationId(),
            cancellationToken,
            waitForResponse: true,
            reasoningEffort: reasoningEffort);
    }

    public Task<string?> FlushPendingPokesInConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null)
    {
        return TryFlushConversationMessageCache(
            NormalizeConversationId(conversationId),
            cancellationToken,
            waitForResponse: true,
            reasoningEffort: reasoningEffort);
    }

    public async Task ImplicitChatAsync(string message)
    {
        string conversationId = ResolveConversationId();
        ChatHistoryAgentThread conversationThread = CreateConversation(conversationId);
        using IDisposable conversationScope = UseConversation(conversationId);
        await RequestChatAsync();
        try
        {
            conversationThread.ChatHistory.AddUserMessage(message);
        }
        finally
        {
            ReleaseChat();
        }
    }

    public void UpdateHistoryEndIndex()
    {
        lastContentIndex = ChatHistory.Count;
    }

    public ChatRuntimeState GetRuntimeState()
    {
        return new ChatRuntimeState(
            IsChatting,
            GetPendingPokeCount(),
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
    readonly ConcurrentDictionary<string, ConcurrentQueue<string>> conversationMessageCaches = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, ChatHistoryAgentThread> conversationThreads = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, int> conversationLastContentIndexes = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, long> conversationGenerations = new(StringComparer.Ordinal);
    readonly (AuthorRole Role, string? Content)[] conversationSeed;
    readonly AsyncLocal<string?> activeConversationId = new();
    readonly AsyncLocal<string?> activeInputMessage = new();
    readonly ConcurrentQueue<ChatRuntimeEvent> runtimeEvents = new();
    readonly SemaphoreSlim chatSemaphore;
    CancellationTokenSource chatBreakSource = new();
    string? activeChatConversationId;
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
        conversationSeed = llmAgentThread?.ChatHistory
            .Select(message => (message.Role, message.Content))
            .ToArray() ?? [];
        chatSemaphore = new SemaphoreSlim(1, 1);

        updateTask = UpdateAsync(timerCancellationSource.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await timerCancellationSource.CancelAsync();
        await updateTask;

        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(5));
        while (!timeoutSource.IsCancellationRequested && (IsChatting || HasPendingPokes()))
        {
            try
            {
                await FlushAllPendingPokesAsync(timeoutSource.Token);
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
                    await FlushAllPendingPokesAsync(cancellationToken);
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

    Task<string?> TryFlushMessageCache(
        CancellationToken cancellationToken = default,
        bool waitForResponse = false,
        string? reasoningEffort = null)
    {
        return TryFlushConversationMessageCache(
            ResolveConversationId(),
            cancellationToken,
            waitForResponse,
            reasoningEffort);
    }

    async Task<string?> TryFlushConversationMessageCache(
        string conversationId,
        CancellationToken cancellationToken = default,
        bool waitForResponse = false,
        string? reasoningEffort = null)
    {
        string id = NormalizeConversationId(conversationId);
        ConcurrentQueue<string> queue = GetMessageCache(id);
        if (queue.IsEmpty)
            return null;

        using IDisposable conversationScope = UseConversation(id);
        await RequestChatAsync(cancellationToken);
        bool lockHeld = true;
        string[] pendingMessages = [];
        bool pendingMessagesRemoved = false;
        try
        {
            if (queue.IsEmpty)
                return null;

            RecordRuntimeEvent("PokeFlushStarted", $"Flushing {queue.Count} pending poke message(s) for {id}.");
            //组合消息
            pendingMessages = queue.Distinct().ToArray();
            StringBuilder stringBuilder = new();
            foreach (string message in pendingMessages)
                stringBuilder.AppendLine(message);
            string poke = stringBuilder.ToString();
            queue.Clear();
            pendingMessagesRemoved = true;

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
            string prompt = $"{PokeMessageTag}\n{poke}";
            if (waitForResponse)
            {
                ReleaseChat();
                lockHeld = false;
                return await ChatInConversationAsync(
                    id,
                    prompt,
                    cancellationToken: cancellationToken,
                    reasoningEffort: reasoningEffort);
            }

            ChatInConversation(id, prompt);
            return null;
        }
        catch
        {
            if (pendingMessagesRemoved)
            {
                foreach (string pendingMessage in pendingMessages)
                    queue.Enqueue(pendingMessage);
            }
            throw;
        }
        finally
        {
            if (lockHeld)
                ReleaseChat();
        }
    }

    async Task FlushAllPendingPokesAsync(CancellationToken cancellationToken)
    {
        await TryFlushConversationMessageCache(DefaultConversationId, cancellationToken);
        foreach (string conversationId in conversationMessageCaches.Keys)
            await TryFlushConversationMessageCache(conversationId, cancellationToken);
    }

    ConcurrentQueue<string> GetMessageCache(string conversationId)
    {
        return conversationId == DefaultConversationId
            ? messageCache
            : conversationMessageCaches.GetOrAdd(conversationId, _ => new ConcurrentQueue<string>());
    }

    int GetPendingPokeCount() =>
        messageCache.Count + conversationMessageCaches.Values.Sum(queue => queue.Count);

    bool HasPendingPokes() =>
        !messageCache.IsEmpty || conversationMessageCaches.Values.Any(queue => !queue.IsEmpty);

    void ChaseChatHistory(string conversationId, ChatHistoryAgentThread conversationThread)
    {
        using IDisposable conversationScope = UseConversation(conversationId);
        if (conversationId == DefaultConversationId)
        {
            for (; lastContentIndex < conversationThread.ChatHistory.Count; lastContentIndex++)
                ChatHistoryAdd?.Invoke(conversationThread.ChatHistory[lastContentIndex]);
            return;
        }

        int index = conversationLastContentIndexes.GetValueOrDefault(conversationId);
        for (; index < conversationThread.ChatHistory.Count; index++)
            ChatHistoryAdd?.Invoke(conversationThread.ChatHistory[index]);
        conversationLastContentIndexes[conversationId] = index;
    }

    void InvokeConversationEvent(string conversationId, Action callback)
    {
        using IDisposable conversationScope = UseConversation(conversationId);
        callback();
    }

    bool IsCurrentGeneration(string conversationId, long generation) =>
        conversationGenerations.GetValueOrDefault(conversationId) == generation;

    static void RemoveGeneratedHistory(
        ChatHistoryAgentThread conversationThread,
        int generatedHistoryStartIndex)
    {
        for (int index = conversationThread.ChatHistory.Count - 1; index >= generatedHistoryStartIndex; index--)
        {
            AuthorRole role = conversationThread.ChatHistory[index].Role;
            if (role == AuthorRole.Assistant || role == AuthorRole.Tool)
                conversationThread.ChatHistory.RemoveAt(index);
        }
    }

    string ResolveConversationId() =>
        activeConversationId.Value ?? DefaultConversationId;

    static string NormalizeConversationId(string? conversationId)
    {
        return string.IsNullOrWhiteSpace(conversationId)
            ? DefaultConversationId
            : conversationId.Trim();
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

    sealed class ConversationScope(AsyncLocal<string?> target, string? previous) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            target.Value = previous;
            disposed = true;
        }
    }
}
