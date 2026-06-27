using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;

namespace Alife.Function.Agent;

public class AgentPublicSearchService(
    AgentPublicSearchConfig? config = null,
    IAgentPublicSearchProvider? provider = null,
    AgentAuditLogService? auditLog = null)
{
    readonly AgentPublicSearchConfig config = config ?? new AgentPublicSearchConfig();
    readonly IAgentPublicSearchProvider? provider = provider;
    readonly AgentAuditLogService? auditLog = auditLog;

    public virtual async Task<AgentPublicSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (config.EnablePublicSearch == false)
            return Deny("public_search_disabled", query);

        string normalized = NormalizeQuery(query);
        if (normalized.Length == 0)
            return Deny("empty_query", query);

        if (provider == null)
            return Deny("search_provider_not_configured", normalized);

        int maxResults = Math.Max(1, config.MaxResults);
        try
        {
            IReadOnlyList<AgentPublicSearchResult> results = await provider.SearchAsync(
                normalized,
                maxResults,
                cancellationToken);
            AgentPublicSearchResult[] limited = results.Take(maxResults).ToArray();
            string formatted = ExternalContextFormatter.WrapUntrusted(
                "public-search",
                FormatResults(normalized, limited));

            auditLog?.Record(
                "agent.public_search",
                "agent",
                normalized,
                AgentAuditRiskLevel.Low,
                succeeded: true);

            return new AgentPublicSearchResponse(true, "ok", limited, formatted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
        {
            auditLog?.Record(
                "agent.public_search",
                "agent",
                normalized,
                AgentAuditRiskLevel.Low,
                succeeded: false,
                error: "search_failed");

            return new AgentPublicSearchResponse(false, "search_failed", [], "public_search_failed: search_failed");
        }
    }

    AgentPublicSearchResponse Deny(string reason, string detail)
    {
        auditLog?.Record(
            "agent.public_search",
            "agent",
            detail,
            AgentAuditRiskLevel.Low,
            succeeded: false,
            error: reason);

        return new AgentPublicSearchResponse(false, reason, [], $"public_search_denied: {reason}");
    }

    string NormalizeQuery(string? query)
    {
        string normalized = (query ?? "").Trim();
        int max = Math.Clamp(config.MaxQueryChars, 1, 1000);
        return normalized.Length <= max ? normalized : normalized[..max];
    }

    static string FormatResults(string query, IReadOnlyList<AgentPublicSearchResult> results)
    {
        StringBuilder builder = new();
        builder.AppendLine($"query={query}");
        for (int i = 0; i < results.Count; i++)
        {
            AgentPublicSearchResult item = results[i];
            builder.AppendLine($"{i + 1}. {item.Title}");
            builder.AppendLine($"url={item.Url}");
            builder.AppendLine($"snippet={item.Snippet}");
        }

        return builder.ToString().TrimEnd();
    }
}
