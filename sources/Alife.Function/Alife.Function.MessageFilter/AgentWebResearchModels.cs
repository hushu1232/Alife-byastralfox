using System.Collections.Generic;

namespace Alife.Function.Agent;

public sealed record AgentWebResearchRequest(
    string Query,
    AgentWebAccessActorRole ActorRole,
    AgentWebAccessConfig Config,
    int MaxSources = 3,
    long? ActorUserId = null,
    long? GroupId = null);

public sealed record AgentWebResearchEvidence(
    string Title,
    string Url,
    string Summary,
    string SourceType);

public sealed record AgentWebResearchResult(
    bool Success,
    string Reason,
    string Query,
    string Answer,
    IReadOnlyList<AgentWebResearchEvidence> Evidence);

public sealed record AgentWebResearchMetricsSnapshot(
    long SearchCount,
    long ReadCount,
    long PageBytes,
    long TotalLatencyMilliseconds,
    long ApproximateSummaryTokens,
    long CacheHits,
    long RateLimitedCount,
    long ConcurrentRejectedCount);
