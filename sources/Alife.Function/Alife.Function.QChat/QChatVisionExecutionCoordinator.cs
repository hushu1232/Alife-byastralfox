using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed class QChatVisionExecutionCoordinator
{
    readonly IReadOnlyDictionary<string, IQChatImageRecognitionClient> clients;
    readonly ConcurrentDictionary<long, BotState> botStates = new();
    readonly TimeSpan duplicateTtl;
    readonly TimeSpan circuitOpenDuration;
    readonly int maxPendingPerBot;
    readonly int retryableFailureThreshold;
    readonly Func<DateTimeOffset> utcNow;

    public QChatVisionExecutionCoordinator(
        IReadOnlyDictionary<string, IQChatImageRecognitionClient> clients,
        int maxPendingPerBot = 16,
        TimeSpan? duplicateTtl = null,
        int retryableFailureThreshold = 3,
        TimeSpan? circuitOpenDuration = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.clients = clients ?? throw new ArgumentNullException(nameof(clients));
        this.maxPendingPerBot = Math.Max(1, maxPendingPerBot);
        this.duplicateTtl = duplicateTtl.GetValueOrDefault(TimeSpan.FromMinutes(2));
        this.retryableFailureThreshold = Math.Max(1, retryableFailureThreshold);
        this.circuitOpenDuration = circuitOpenDuration.GetValueOrDefault(TimeSpan.FromSeconds(30));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
        long botId,
        bool ownerPriority,
        string imageKey,
        QChatVisionRoutePlan route,
        QChatImageRecognitionProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(request);

        if (HasPublicUrl(request.ImageUrl) == false)
        {
            return Task.FromResult(QChatImageRecognitionProviderResult.Fail(
                route.PrimaryProvider, request.Model, QChatImageRecognitionFailureKind.MissingPublicUrl, "public_url_unavailable"));
        }

        string normalizedImageKey = imageKey?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedImageKey))
        {
            return Task.FromResult(QChatImageRecognitionProviderResult.Fail(
                route.PrimaryProvider, request.Model, QChatImageRecognitionFailureKind.Disabled, "image_key_missing"));
        }

        BotState state = botStates.GetOrAdd(botId, static _ => new BotState());
        bool startProcessor = false;
        Task<QChatImageRecognitionProviderResult> task;
        lock (state.Gate)
        {
            RemoveExpiredDuplicateEntries(state, utcNow());
            if (state.Duplicates.TryGetValue(normalizedImageKey, out DuplicateEntry? duplicate))
                return duplicate.Task;

            if (state.OwnerQueue.Count + state.GuestQueue.Count >= maxPendingPerBot)
            {
                return Task.FromResult(QChatImageRecognitionProviderResult.Fail(
                    route.PrimaryProvider, request.Model, QChatImageRecognitionFailureKind.Disabled, "vision_queue_full"));
            }

            TaskCompletionSource<QChatImageRecognitionProviderResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingRequest pending = new(normalizedImageKey, route, request, completion, cancellationToken);
            if (ownerPriority)
                state.OwnerQueue.Enqueue(pending);
            else
                state.GuestQueue.Enqueue(pending);

            task = completion.Task;
            state.Duplicates[normalizedImageKey] = new DuplicateEntry(task, utcNow().Add(duplicateTtl));
            if (state.Processing == false)
            {
                state.Processing = true;
                startProcessor = true;
            }
        }

        if (startProcessor)
            _ = ProcessBotQueueAsync(state);

        return task;
    }

    async Task ProcessBotQueueAsync(BotState state)
    {
        while (true)
        {
            PendingRequest? pending;
            lock (state.Gate)
            {
                pending = state.OwnerQueue.Count > 0
                    ? state.OwnerQueue.Dequeue()
                    : state.GuestQueue.Count > 0 ? state.GuestQueue.Dequeue() : null;
                if (pending == null)
                {
                    state.Processing = false;
                    return;
                }
            }

            QChatImageRecognitionProviderResult result;
            try
            {
                result = await ExecuteAsync(state, pending.Route, pending.Request, pending.CancellationToken);
            }
            catch (Exception)
            {
                result = QChatImageRecognitionProviderResult.Fail(
                    pending.Route.PrimaryProvider,
                    pending.Request.Model,
                    QChatImageRecognitionFailureKind.HttpError,
                    "vision_execution_failed");
            }

            pending.Completion.TrySetResult(result);
        }
    }

    async Task<QChatImageRecognitionProviderResult> ExecuteAsync(
        BotState state,
        QChatVisionRoutePlan route,
        QChatImageRecognitionProviderRequest request,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(route.TotalTimeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(12)
            : route.TotalTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

        string primaryProvider = route.PrimaryProvider.Trim();
        string? fallbackProvider = string.IsNullOrWhiteSpace(route.FallbackProvider) ? null : route.FallbackProvider.Trim();
        if (IsCircuitOpen(state, primaryProvider))
        {
            if (fallbackProvider == null)
            {
                return QChatImageRecognitionProviderResult.Fail(
                    primaryProvider, request.Model, QChatImageRecognitionFailureKind.Disabled, "provider_circuit_open");
            }

            return await CallProviderAsync(state, fallbackProvider, request, linked.Token);
        }

        QChatImageRecognitionProviderResult primary = await CallProviderAsync(state, primaryProvider, request, linked.Token);
        if (primary.Success || fallbackProvider == null || QChatVisionRoutePlanner.ShouldFallback(primary.FailureKind) == false)
            return primary;

        return await CallProviderAsync(state, fallbackProvider, request, linked.Token);
    }

    async Task<QChatImageRecognitionProviderResult> CallProviderAsync(
        BotState state,
        string providerId,
        QChatImageRecognitionProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (TryFindClient(providerId, out IQChatImageRecognitionClient? client) == false)
        {
            return QChatImageRecognitionProviderResult.Fail(
                providerId, request.Model, QChatImageRecognitionFailureKind.Disabled, "provider_unavailable");
        }

        QChatImageRecognitionProviderResult result;
        try
        {
            result = await client!.AnalyzeAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result = QChatImageRecognitionProviderResult.Fail(
                providerId, request.Model, QChatImageRecognitionFailureKind.Timeout, "timeout");
        }
        catch (HttpRequestException)
        {
            result = QChatImageRecognitionProviderResult.Fail(
                providerId, request.Model, QChatImageRecognitionFailureKind.HttpError, "http_request_failed");
        }
        catch (Exception)
        {
            result = QChatImageRecognitionProviderResult.Fail(
                providerId, request.Model, QChatImageRecognitionFailureKind.HttpError, "provider_execution_failed");
        }

        RecordCircuitOutcome(state, providerId, result);
        return result;
    }

    bool TryFindClient(string providerId, out IQChatImageRecognitionClient? client)
    {
        foreach ((string configuredId, IQChatImageRecognitionClient configuredClient) in clients)
        {
            if (string.Equals(configuredId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                client = configuredClient;
                return true;
            }
        }

        client = null;
        return false;
    }

    bool IsCircuitOpen(BotState state, string providerId)
    {
        return state.Circuits.TryGetValue(providerId, out CircuitState? circuit) &&
               circuit.OpenUntil > utcNow();
    }

    void RecordCircuitOutcome(BotState state, string providerId, QChatImageRecognitionProviderResult result)
    {
        if (result.Success)
        {
            state.Circuits.Remove(providerId);
            return;
        }

        if (QChatVisionRoutePlanner.ShouldFallback(result.FailureKind) == false)
            return;

        if (state.Circuits.TryGetValue(providerId, out CircuitState? current) == false)
            current = new CircuitState();

        current.ConsecutiveRetryableFailures++;
        if (current.ConsecutiveRetryableFailures >= retryableFailureThreshold)
        {
            current.OpenUntil = utcNow().Add(circuitOpenDuration);
            current.ConsecutiveRetryableFailures = 0;
        }

        state.Circuits[providerId] = current;
    }

    static void RemoveExpiredDuplicateEntries(BotState state, DateTimeOffset now)
    {
        List<string>? expired = null;
        foreach ((string key, DuplicateEntry entry) in state.Duplicates)
        {
            if (entry.ExpiresAt <= now)
                (expired ??= []).Add(key);
        }

        if (expired == null)
            return;

        foreach (string key in expired)
            state.Duplicates.Remove(key);
    }

    static bool HasPublicUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    sealed class BotState
    {
        public object Gate { get; } = new();
        public Queue<PendingRequest> OwnerQueue { get; } = new();
        public Queue<PendingRequest> GuestQueue { get; } = new();
        public Dictionary<string, DuplicateEntry> Duplicates { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CircuitState> Circuits { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Processing { get; set; }
    }

    sealed record PendingRequest(
        string ImageKey,
        QChatVisionRoutePlan Route,
        QChatImageRecognitionProviderRequest Request,
        TaskCompletionSource<QChatImageRecognitionProviderResult> Completion,
        CancellationToken CancellationToken);

    sealed record DuplicateEntry(Task<QChatImageRecognitionProviderResult> Task, DateTimeOffset ExpiresAt);

    sealed class CircuitState
    {
        public int ConsecutiveRetryableFailures { get; set; }
        public DateTimeOffset OpenUntil { get; set; }
    }
}
