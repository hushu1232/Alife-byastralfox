using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed record AgentPublicSearchCandidate(
    string ProviderId,
    int ProviderOrder,
    int ResultOrder,
    AgentPublicSearchResult Result);

public static class AgentPublicSearchResultMerger
{
    public static IReadOnlyList<AgentPublicSearchResult> Merge(
        IEnumerable<AgentPublicSearchCandidate> candidates,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        int limit = Math.Clamp(maxResults, 1, 5);
        HashSet<string> seenUrls = new(StringComparer.Ordinal);
        List<string> acceptedTitles = [];
        List<AgentPublicSearchResult> results = [];

        foreach (AgentPublicSearchCandidate candidate in candidates
                     .Where(static item => item is { Result: not null })
                     .OrderBy(item => item.ProviderOrder)
                     .ThenBy(item => item.ResultOrder)
                     .ThenBy(item => NormalizeTitle(item.Result.Title), StringComparer.Ordinal)
                     .ThenBy(item => item.Result.Url, StringComparer.Ordinal))
        {
            string title = NormalizeTitle(candidate.Result.Title);
            if (TryNormalizeUrl(candidate.Result.Url, out string url) == false ||
                seenUrls.Contains(url) ||
                acceptedTitles.Any(accepted => IsNearDuplicateTitle(accepted, title)))
                continue;

            seenUrls.Add(url);
            results.Add(candidate.Result with { Url = url });
            acceptedTitles.Add(title);
            if (results.Count == limit)
                break;
        }

        return results;
    }

    internal static bool TryNormalizeUrl(string? value, out string normalized)
    {
        normalized = "";
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        UriBuilder builder = new(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = "",
            Port = uri.IsDefaultPort ? -1 : uri.Port
        };
        if (builder.Path.Length > 1)
            builder.Path = builder.Path.TrimEnd('/');

        normalized = builder.Uri.AbsoluteUri;
        if (builder.Path == "/" && string.IsNullOrEmpty(builder.Query))
            normalized = normalized.TrimEnd('/');
        return true;
    }

    internal static string NormalizeTitle(string? value) => string.Concat((value ?? "")
        .Trim()
        .ToLowerInvariant()
        .Where(char.IsLetterOrDigit));

    internal static bool IsNearDuplicateTitle(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return false;
        if (left == right)
            return true;

        int length = Math.Max(left.Length, right.Length);
        return length >= 5 && LevenshteinDistance(left, right) <= Math.Max(1, length / 7);
    }

    static int LevenshteinDistance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int row = 1; row <= left.Length; row++)
        {
            int[] current = new int[right.Length + 1];
            current[0] = row;
            for (int column = 1; column <= right.Length; column++)
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            previous = current;
        }

        return previous[right.Length];
    }
}

public sealed class ParallelPublicSearchProvider : IAgentPublicSearchProvider
{
    readonly ProviderSlot[] slots;
    readonly AgentMultiSourceSearchConfig config;
    readonly TimeProvider timeProvider;
    readonly ConcurrentDictionary<string, ProviderCircuit> circuits = new(StringComparer.Ordinal);

    public ParallelPublicSearchProvider(
        IAgentPublicSearchProvider duckDuckGo,
        IAgentPublicSearchProvider bing,
        AgentMultiSourceSearchConfig config,
        TimeProvider? timeProvider = null)
    {
        slots =
        [
            new ProviderSlot("duckduckgo", 0, duckDuckGo ?? throw new ArgumentNullException(nameof(duckDuckGo))),
            new ProviderSlot("bing", 1, bing ?? throw new ArgumentNullException(nameof(bing)))
        ];
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderSlot[] available = slots.Where(slot => IsCircuitClosed(slot.Id)).ToArray();
        if (available.Length == 0)
            throw new InvalidOperationException("public_search_all_providers_failed");

        using CancellationTokenSource stopPeers = new();
        List<Task<ProviderAttempt>> pending = available
            .Select(slot => ExecuteSlotAsync(slot, query, maxResults, cancellationToken, stopPeers.Token))
            .ToList();
        List<ProviderAttempt> completed = [];
        try
        {
            while (pending.Count > 0)
            {
                Task<ProviderAttempt> next = await Task.WhenAny(pending).WaitAsync(cancellationToken);
                pending.Remove(next);
                ProviderAttempt attempt = await next;
                completed.Add(attempt);
                if (attempt.Results.Count == 0)
                    continue;

                completed.AddRange(pending.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result));
                IReadOnlyList<AgentPublicSearchResult> merged = AgentPublicSearchResultMerger.Merge(
                    completed.Where(item => item.Results.Count > 0)
                        .SelectMany(item => item.Results.Select((result, index) => new AgentPublicSearchCandidate(
                            item.ProviderId,
                            item.ProviderOrder,
                            index,
                            result))),
                    GetMaxResults(maxResults));
                if (merged.Count == 0)
                    continue;

                stopPeers.Cancel();
                return merged;
            }
        }
        finally
        {
            stopPeers.Cancel();
        }

        if (completed.Any(item => item.CompletedNormally))
            return [];
        throw new InvalidOperationException("public_search_all_providers_failed");
    }

    async Task<ProviderAttempt> ExecuteSlotAsync(
        ProviderSlot slot,
        string query,
        int maxResults,
        CancellationToken callerCancellationToken,
        CancellationToken peerStopToken)
    {
        using CancellationTokenSource deadlineCancellation = new();
        deadlineCancellation.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, config.PerProviderTimeoutMilliseconds)));
        using CancellationTokenSource requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            peerStopToken,
            deadlineCancellation.Token);
        try
        {
            IReadOnlyList<AgentPublicSearchResult> results = await slot.Provider.SearchAsync(
                query,
                GetMaxResults(maxResults),
                requestCancellation.Token);
            callerCancellationToken.ThrowIfCancellationRequested();
            if (peerStopToken.IsCancellationRequested)
                return ProviderAttempt.Cancelled(slot.Id, slot.Order);
            if (deadlineCancellation.IsCancellationRequested)
            {
                RecordFailure(slot.Id);
                return ProviderAttempt.Failed(slot.Id, slot.Order);
            }

            RecordSuccess(slot.Id);
            return new ProviderAttempt(slot.Id, slot.Order, results ?? [], true);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (peerStopToken.IsCancellationRequested && deadlineCancellation.IsCancellationRequested == false)
        {
            return ProviderAttempt.Cancelled(slot.Id, slot.Order);
        }
        catch (Exception)
        {
            RecordFailure(slot.Id);
            return ProviderAttempt.Failed(slot.Id, slot.Order);
        }
    }

    bool IsCircuitClosed(string providerId)
    {
        ProviderCircuit circuit = circuits.GetOrAdd(providerId, _ => new ProviderCircuit());
        lock (circuit.Gate)
        {
            if (circuit.OpenUntil is not { } openUntil)
                return true;
            if (openUntil > timeProvider.GetUtcNow())
                return false;

            circuit.OpenUntil = null;
            circuit.ConsecutiveFailures = 0;
            return true;
        }
    }

    void RecordSuccess(string providerId)
    {
        ProviderCircuit circuit = circuits.GetOrAdd(providerId, _ => new ProviderCircuit());
        lock (circuit.Gate)
        {
            circuit.ConsecutiveFailures = 0;
            circuit.OpenUntil = null;
        }
    }

    void RecordFailure(string providerId)
    {
        ProviderCircuit circuit = circuits.GetOrAdd(providerId, _ => new ProviderCircuit());
        lock (circuit.Gate)
        {
            circuit.ConsecutiveFailures++;
            if (circuit.ConsecutiveFailures >= Math.Max(1, config.FailureThreshold))
            {
                circuit.OpenUntil = timeProvider.GetUtcNow().AddSeconds(Math.Max(1, config.CircuitBreakSeconds));
            }
        }
    }

    int GetMaxResults(int maxResults) => Math.Min(
        Math.Max(1, maxResults),
        Math.Clamp(config.MaxMergedResults, 1, 5));

    sealed record ProviderSlot(string Id, int Order, IAgentPublicSearchProvider Provider);

    sealed class ProviderCircuit
    {
        public object Gate { get; } = new();
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? OpenUntil { get; set; }
    }

    sealed record ProviderAttempt(
        string ProviderId,
        int ProviderOrder,
        IReadOnlyList<AgentPublicSearchResult> Results,
        bool CompletedNormally)
    {
        public static ProviderAttempt Failed(string id, int order) => new(id, order, [], false);
        public static ProviderAttempt Cancelled(string id, int order) => new(id, order, [], false);
    }
}
