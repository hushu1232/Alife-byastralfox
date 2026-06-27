using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Alife.Function.Agent;

public sealed class AgentWebResearchControlState
{
    readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, DateTimeOffset> cooldowns = new(StringComparer.Ordinal);
    long activeResearches;
    long searchCount;
    long readCount;
    long pageBytes;
    long totalLatencyMilliseconds;
    long approximateSummaryTokens;
    long cacheHits;
    long rateLimitedCount;
    long concurrentRejectedCount;

    public bool TryGetCachedResult(
        AgentWebResearchRequest request,
        string normalizedQuery,
        int maxSources,
        out AgentWebResearchResult result)
    {
        result = default!;
        int ttlSeconds = Math.Max(request.Config.WebResearchCacheSeconds, 0);
        if (ttlSeconds <= 0)
            return false;

        string key = BuildCacheKey(request, normalizedQuery, maxSources);
        if (cache.TryGetValue(key, out CacheEntry? entry) == false)
            return false;

        if (DateTimeOffset.UtcNow - entry.CreatedAt > TimeSpan.FromSeconds(ttlSeconds))
        {
            cache.TryRemove(key, out _);
            return false;
        }

        Interlocked.Increment(ref cacheHits);
        result = entry.Result;
        return true;
    }

    public void StoreCachedResult(
        AgentWebResearchRequest request,
        string normalizedQuery,
        int maxSources,
        AgentWebResearchResult result)
    {
        if (result.Success == false || request.Config.WebResearchCacheSeconds <= 0)
            return;

        string key = BuildCacheKey(request, normalizedQuery, maxSources);
        cache[key] = new CacheEntry(DateTimeOffset.UtcNow, result);
    }

    public bool TryEnterCooldown(
        AgentWebResearchRequest request,
        out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (request.ActorRole != AgentWebAccessActorRole.GroupMember)
            return true;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string[] keys = BuildCooldownKeys(request);
        foreach (string key in keys)
        {
            if (cooldowns.TryGetValue(key, out DateTimeOffset lastSeenAt) == false)
                continue;

            int cooldownSeconds = key.StartsWith("group:", StringComparison.Ordinal)
                ? Math.Max(request.Config.WebResearchGroupCooldownSeconds, 0)
                : Math.Max(request.Config.WebResearchUserCooldownSeconds, 0);
            if (cooldownSeconds <= 0)
                continue;

            TimeSpan elapsed = now - lastSeenAt;
            TimeSpan configured = TimeSpan.FromSeconds(cooldownSeconds);
            if (elapsed < configured)
            {
                remaining = configured - elapsed;
                Interlocked.Increment(ref rateLimitedCount);
                return false;
            }
        }

        foreach (string key in keys)
            cooldowns[key] = now;
        return true;
    }

    public bool TryAcquireConcurrency(int maxConcurrent, out IDisposable lease)
    {
        lease = NoopLease.Instance;
        if (maxConcurrent <= 0)
            return true;

        while (true)
        {
            long current = Interlocked.Read(ref activeResearches);
            if (current >= maxConcurrent)
            {
                Interlocked.Increment(ref concurrentRejectedCount);
                return false;
            }

            if (Interlocked.CompareExchange(ref activeResearches, current + 1, current) == current)
            {
                lease = new ConcurrencyLease(this);
                return true;
            }
        }
    }

    public void RecordSearch() => Interlocked.Increment(ref searchCount);

    public void RecordRead(string? content)
    {
        Interlocked.Increment(ref readCount);
        Interlocked.Add(ref pageBytes, System.Text.Encoding.UTF8.GetByteCount(content ?? ""));
    }

    public void RecordSummaryText(string? text)
    {
        string value = text ?? "";
        if (value.Length == 0)
            return;

        Interlocked.Add(ref approximateSummaryTokens, Math.Max(1, (value.Length + 3) / 4));
    }

    public void RecordLatency(TimeSpan latency)
    {
        long milliseconds = Math.Max(0, (long)latency.TotalMilliseconds);
        Interlocked.Add(ref totalLatencyMilliseconds, milliseconds);
    }

    public AgentWebResearchMetricsSnapshot GetMetricsSnapshot() => new(
        Interlocked.Read(ref searchCount),
        Interlocked.Read(ref readCount),
        Interlocked.Read(ref pageBytes),
        Interlocked.Read(ref totalLatencyMilliseconds),
        Interlocked.Read(ref approximateSummaryTokens),
        Interlocked.Read(ref cacheHits),
        Interlocked.Read(ref rateLimitedCount),
        Interlocked.Read(ref concurrentRejectedCount));

    static string BuildCacheKey(
        AgentWebResearchRequest request,
        string normalizedQuery,
        int maxSources)
    {
        return string.Join("|", [
            request.ActorRole.ToString(),
            request.Config.EnableAutoRead ? "read" : "snippet",
            maxSources.ToString(System.Globalization.CultureInfo.InvariantCulture),
            normalizedQuery.ToUpperInvariant()
        ]);
    }

    static string[] BuildCooldownKeys(AgentWebResearchRequest request)
    {
        System.Collections.Generic.List<string> keys = [];
        if (request.GroupId is { } groupId && request.Config.WebResearchGroupCooldownSeconds > 0)
            keys.Add($"group:{groupId}");
        if (request.ActorUserId is { } actorUserId && request.Config.WebResearchUserCooldownSeconds > 0)
            keys.Add($"user:{actorUserId}");
        return keys.ToArray();
    }

    void ReleaseConcurrency() => Interlocked.Decrement(ref activeResearches);

    sealed record CacheEntry(DateTimeOffset CreatedAt, AgentWebResearchResult Result);

    sealed class ConcurrencyLease(AgentWebResearchControlState owner) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.ReleaseConcurrency();
        }
    }

    sealed class NoopLease : IDisposable
    {
        public static readonly NoopLease Instance = new();
        public void Dispose()
        {
        }
    }
}
