using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed record AgentWebAccessResponse(
    bool Success,
    string Reason,
    AgentWebAccessCapability Capability,
    string FormattedContent);

public sealed class AgentWebAccessService(
    AgentPublicSearchService? searchService = null,
    AgentInternetService? internetService = null,
    AgentExternalRagService? externalRagService = null,
    IAgentBrowserProvider? browserProvider = null,
    AgentBrowserSiteExperienceStore? browserSiteExperienceStore = null)
{
    readonly AgentPublicSearchService? searchService = searchService;
    readonly AgentInternetService? internetService = internetService;
    readonly AgentExternalRagService? externalRagService = externalRagService;
    readonly IAgentBrowserProvider? browserProvider = browserProvider;
    readonly AgentBrowserSiteExperienceStore? browserSiteExperienceStore = browserSiteExperienceStore;

    public async Task<AgentWebAccessResponse> ExecuteAsync(
        AgentWebAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(request);
        if (decision.Allowed == false)
            return Denied(request.Capability, decision.Reason);

        return request.Capability switch
        {
            AgentWebAccessCapability.PublicSearch => await ExecutePublicSearchAsync(request, cancellationToken),
            AgentWebAccessCapability.AutoRead => await ExecuteAutoReadAsync(request, cancellationToken),
            AgentWebAccessCapability.PublicFetch => await ExecutePublicFetchAsync(request, cancellationToken),
            AgentWebAccessCapability.ExternalRagQuery => ExecuteExternalRagQuery(request),
            AgentWebAccessCapability.BrowserSnapshot => await ExecuteBrowserSnapshotAsync(request, cancellationToken),
            AgentWebAccessCapability.BrowserInteract => Denied(request.Capability, "browser_interact_not_implemented"),
            AgentWebAccessCapability.ExternalRagMutation => Denied(request.Capability, "external_rag_mutation_not_implemented"),
            _ => Denied(request.Capability, "unknown_capability")
        };
    }

    async Task<AgentWebAccessResponse> ExecuteAutoReadAsync(
        AgentWebAccessRequest request,
        CancellationToken cancellationToken)
    {
        AgentWebStrategyDecision strategy = AgentWebStrategyRouter.Evaluate(
            request.Query,
            browserSiteExperienceStore);
        if (strategy.Allowed == false || strategy.Capability == null)
            return Denied(request.Capability, strategy.Reason);

        AgentWebAccessConfig config = request.Config ?? new AgentWebAccessConfig();
        AgentWebAccessConfig delegatedConfig = new()
        {
            EnablePublicSearch = config.EnablePublicSearch,
            EnableAutoRead = config.EnableAutoRead,
            EnablePublicFetch = config.EnablePublicFetch,
            EnableBrowserSnapshot = config.EnableBrowserSnapshot,
            EnableBrowserInteract = false,
            EnableExternalRagQuery = config.EnableExternalRagQuery,
            EnableExternalRagMutation = false,
            AllowGroupMemberPublicSearch = config.AllowGroupMemberPublicSearch,
            AllowGroupMemberExternalRagQuery = config.AllowGroupMemberExternalRagQuery,
            MaxQueryChars = config.MaxQueryChars,
            MaxExternalRagChunks = config.MaxExternalRagChunks
        };

        AgentWebAccessRequest delegatedRequest = new(
            request.ActorRole,
            strategy.Capability.Value,
            request.Query,
            delegatedConfig);
        return await ExecuteAsync(delegatedRequest, cancellationToken);
    }

    async Task<AgentWebAccessResponse> ExecutePublicSearchAsync(
        AgentWebAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (searchService == null)
            return Denied(request.Capability, "public_search_not_configured");

        AgentPublicSearchResponse response = await searchService.SearchAsync(request.Query, cancellationToken);
        return new AgentWebAccessResponse(
            response.Success,
            response.Reason,
            request.Capability,
            response.FormattedContent);
    }

    async Task<AgentWebAccessResponse> ExecutePublicFetchAsync(
        AgentWebAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (internetService == null)
            return Denied(request.Capability, "internet_service_not_configured");

        AgentInternetFetchResult response = await internetService.FetchPublicPageAsync(request.Query, cancellationToken);
        browserSiteExperienceStore?.RecordSnapshotResult(
            request.Query,
            response.Success,
            response.Reason);
        return new AgentWebAccessResponse(
            response.Success,
            response.Reason,
            request.Capability,
            response.Content);
    }

    AgentWebAccessResponse ExecuteExternalRagQuery(AgentWebAccessRequest request)
    {
        if (externalRagService == null)
            return Denied(request.Capability, "external_rag_not_configured");

        AgentExternalRagQueryResponse response = externalRagService.Query(
            request.Query,
            System.Math.Max(request.Config.MaxExternalRagChunks, 1));
        return new AgentWebAccessResponse(
            response.Success,
            response.Reason,
            request.Capability,
            response.FormattedContext);
    }

    async Task<AgentWebAccessResponse> ExecuteBrowserSnapshotAsync(
        AgentWebAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (browserProvider == null)
            return Denied(request.Capability, "browser_provider_not_configured");

        AgentBrowserSnapshot snapshot = await browserProvider.CaptureSnapshotAsync(
            new AgentBrowserSnapshotRequest(request.Query),
            cancellationToken);
        browserSiteExperienceStore?.RecordSnapshotResult(
            string.IsNullOrWhiteSpace(snapshot.Url) ? request.Query : snapshot.Url,
            snapshot.Success,
            snapshot.Reason);
        return new AgentWebAccessResponse(
            snapshot.Success,
            snapshot.Reason,
            request.Capability,
            AgentBrowserSnapshotFormatter.Format(snapshot));
    }

    static AgentWebAccessResponse Denied(AgentWebAccessCapability capability, string reason) =>
        new(false, reason, capability, $"web_access_denied: {reason}");
}
