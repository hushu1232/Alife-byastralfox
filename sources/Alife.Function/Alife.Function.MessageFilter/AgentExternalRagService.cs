using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public class AgentExternalRagService(
    AgentExternalRagStore store,
    AgentInternetService internetService,
    AgentAuditLogService? auditLog = null)
{
    public virtual async Task<AgentExternalRagSource> AddPublicUrlAsync(
        string url,
        string title,
        bool addedByOwner,
        CancellationToken cancellationToken = default)
    {
        if (addedByOwner == false)
        {
            auditLog?.Record(
                "agent.external_rag.add",
                "non_owner",
                url,
                AgentAuditRiskLevel.Medium,
                succeeded: false,
                error: "external_rag_owner_required");
            throw new InvalidOperationException("external_rag_owner_required");
        }

        AgentInternetUrlPolicyDecision policy = AgentInternetUrlPolicy.Evaluate(
            url,
            AgentInternetConfig.CreateDefault());
        if (policy.Allowed == false || policy.Uri == null)
        {
            auditLog?.Record(
                "agent.external_rag.add",
                "owner",
                url,
                AgentAuditRiskLevel.Medium,
                succeeded: false,
                error: policy.Reason);
            throw new InvalidOperationException($"external_rag_url_denied:{policy.Reason}");
        }

        string normalizedUrl = policy.Uri.ToString();
        AgentInternetFetchResult fetch = await internetService.FetchPublicPageAsync(normalizedUrl, cancellationToken);
        if (fetch.Success == false)
        {
            auditLog?.Record(
                "agent.external_rag.add",
                "owner",
                normalizedUrl,
                AgentAuditRiskLevel.Medium,
                succeeded: false,
                error: fetch.Reason);
            throw new InvalidOperationException($"external_rag_fetch_failed:{fetch.Reason}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        AgentExternalRagSource source;
        try
        {
            source = store.AddOrReplaceSource(
                normalizedUrl,
                title,
                fetch.Content,
                addedByOwner: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            auditLog?.Record(
                "agent.external_rag.add",
                "owner",
                normalizedUrl,
                AgentAuditRiskLevel.Medium,
                succeeded: false,
                error: "external_rag_store_failed");
            throw;
        }

        auditLog?.Record(
            "agent.external_rag.add",
            "owner",
            normalizedUrl,
            AgentAuditRiskLevel.Medium,
            succeeded: true);
        return source;
    }

    public virtual AgentExternalRagQueryResponse Query(string query, int maxChunks)
    {
        IReadOnlyList<AgentExternalRagChunk> chunks = store.Query(query, maxChunks);
        if (chunks.Count == 0)
            return new AgentExternalRagQueryResponse(
                false,
                "no_match",
                [],
                "external_rag=no_match");

        return new AgentExternalRagQueryResponse(
            true,
            "ok",
            chunks,
            AgentExternalRagStore.FormatQueryContext(chunks));
    }

    public virtual IReadOnlyList<AgentExternalRagSource> ListSources(int limit)
    {
        return store.ListSources(limit);
    }

    public virtual bool DeleteSource(string urlOrId, bool deletedByOwner)
    {
        string actor = deletedByOwner ? "owner" : "non_owner";
        try
        {
            bool deleted = store.DeleteSource(urlOrId, deletedByOwner);
            auditLog?.Record(
                "agent.external_rag.delete",
                actor,
                urlOrId,
                AgentAuditRiskLevel.Medium,
                succeeded: deleted,
                error: deleted ? null : "external_rag_source_not_found");
            return deleted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            auditLog?.Record(
                "agent.external_rag.delete",
                actor,
                urlOrId,
                AgentAuditRiskLevel.Medium,
                succeeded: false,
                error: ex.Message);
            throw;
        }
    }
}
