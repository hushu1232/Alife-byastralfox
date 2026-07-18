using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public sealed record QChatSemanticWebResearchEvidence(
    bool Researched,
    QChatSemanticWebResearchDecision Decision,
    AgentWebResearchResult? Result,
    string ModelPrompt)
{
    public static QChatSemanticWebResearchEvidence Empty { get; } = new(
        false,
        new QChatSemanticWebResearchDecision(
            false,
            false,
            "",
            QChatSemanticWebResearchDepth.Quick,
            1,
            QChatSemanticWebResearchReasonCategory.Unknown,
            "not_researched"),
        null,
        "");
}

public sealed class QChatSemanticWebResearchService(
    IQChatSemanticWebResearchRouter router,
    IAgentWebResearchService researchService)
{
    readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);

    public async Task<QChatSemanticWebResearchEvidence> ExecuteAsync(
        QChatSemanticWebResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (QChatSemanticWebResearchEligibility.IsEligible(
                request.Config,
                request.MessageEvent,
                request.SenderRole,
                request.IsMentionedOrWoken) == false)
        {
            return QChatSemanticWebResearchEvidence.Empty;
        }

        QChatSemanticWebResearchDecision decision = await router.RouteAsync(request, cancellationToken);
        if (decision.ShouldResearch == false)
            return new QChatSemanticWebResearchEvidence(false, decision, null, "");

        string cacheKey = BuildCacheKey(request, decision);
        if (TryGetCached(cacheKey, out QChatSemanticWebResearchEvidence cached))
            return cached;

        AgentWebAccessActorRole actorRole = request.SenderRole == QChatSenderRole.Owner
            ? AgentWebAccessActorRole.Owner
            : AgentWebAccessActorRole.GroupMember;
        AgentWebAccessConfig accessConfig = CreateAccessConfig(request.Config, actorRole, decision.Depth);
        AgentWebResearchResult result = await researchService.ResearchAsync(
            new AgentWebResearchRequest(
                decision.Query,
                actorRole,
                accessConfig,
                GetMaxSources(request.Config, decision.Depth, decision.MaxSources),
                request.MessageEvent.UserId,
                request.MessageEvent.GroupId == 0 ? null : request.MessageEvent.GroupId),
            cancellationToken);

        QChatSemanticWebResearchEvidence evidence = new(
            true,
            decision,
            result,
            FormatModelPrompt(result));
        if (result.Success)
            Store(cacheKey, evidence, request.Config.SessionCacheSeconds);

        return evidence;
    }

    bool TryGetCached(string key, out QChatSemanticWebResearchEvidence evidence)
    {
        evidence = default!;
        if (cache.TryGetValue(key, out CacheEntry? entry) == false)
            return false;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            cache.TryRemove(key, out _);
            return false;
        }

        evidence = entry.Evidence;
        return true;
    }

    void Store(string key, QChatSemanticWebResearchEvidence evidence, int cacheSeconds)
    {
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, cacheSeconds));
        cache[key] = new CacheEntry(evidence, expiresAt);
    }

    static string BuildCacheKey(
        QChatSemanticWebResearchRequest request,
        QChatSemanticWebResearchDecision decision) => string.Join(
        "|",
        request.AgentId.Trim().ToLowerInvariant(),
        request.MessageEvent.MessageType,
        request.MessageEvent.MessageType == OneBotMessageType.Group
            ? request.MessageEvent.GroupId
            : request.MessageEvent.UserId,
        decision.Query.Trim().ToLowerInvariant(),
        decision.Depth,
        GetMaxSources(request.Config, decision.Depth, decision.MaxSources));

    static AgentWebAccessConfig CreateAccessConfig(
        QChatSemanticWebResearchConfig config,
        AgentWebAccessActorRole actorRole,
        QChatSemanticWebResearchDepth depth)
    {
        bool allowPageRead = actorRole == AgentWebAccessActorRole.Owner && depth != QChatSemanticWebResearchDepth.Quick;
        return new AgentWebAccessConfig
        {
            EnablePublicSearch = true,
            EnableAutoRead = allowPageRead,
            EnablePublicFetch = allowPageRead,
            EnableBrowserSnapshot = allowPageRead,
            AllowGroupMemberPublicSearch = true,
            MaxQueryChars = 160
        };
    }

    static int GetMaxSources(
        QChatSemanticWebResearchConfig config,
        QChatSemanticWebResearchDepth depth,
        int requestedMaxSources)
    {
        int configured = depth switch
        {
            QChatSemanticWebResearchDepth.Quick => config.QuickMaxSources,
            QChatSemanticWebResearchDepth.Standard => config.StandardMaxSources,
            _ => config.DeepMaxSources
        };
        return Math.Clamp(Math.Min(Math.Max(1, requestedMaxSources), Math.Max(1, configured)), 1, 5);
    }

    static string FormatModelPrompt(AgentWebResearchResult result)
    {
        if (result.Success == false)
        {
            return ExternalContextFormatter.WrapUntrusted(
                "semantic-web-research",
                $"research_failed reason={result.Reason}");
        }

        IEnumerable<string> sources = (result.Evidence ?? [])
            .Where(item => string.IsNullOrWhiteSpace(item.Url) == false)
            .Select(item => $"title={item.Title}\nurl={item.Url}\nsummary={item.Summary}");
        string content = string.Join(
            Environment.NewLine,
            new[]
            {
                $"query={result.Query}",
                $"answer={result.Answer}",
                string.Join(Environment.NewLine, sources)
            }.Where(value => string.IsNullOrWhiteSpace(value) == false));
        return ExternalContextFormatter.WrapUntrusted("semantic-web-research", content);
    }

    sealed record CacheEntry(QChatSemanticWebResearchEvidence Evidence, DateTimeOffset ExpiresAt);
}
