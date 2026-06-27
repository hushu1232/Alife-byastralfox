using System;

namespace Alife.Function.Agent;

public enum AgentWebAccessActorRole
{
    Unknown,
    Owner,
    GroupMember,
    PrivateGuest
}

public enum AgentWebAccessCapability
{
    PublicSearch,
    AutoRead,
    PublicFetch,
    BrowserSnapshot,
    BrowserInteract,
    ExternalRagQuery,
    ExternalRagMutation
}

public sealed class AgentWebAccessConfig
{
    public bool EnablePublicSearch { get; set; }
    public bool EnableAutoRead { get; set; }
    public bool EnablePublicFetch { get; set; }
    public bool EnableBrowserSnapshot { get; set; }
    public bool EnableBrowserInteract { get; set; }
    public bool EnableExternalRagQuery { get; set; }
    public bool EnableExternalRagMutation { get; set; }
    public bool AllowGroupMemberPublicSearch { get; set; }
    public bool AllowGroupMemberExternalRagQuery { get; set; }
    public int MaxQueryChars { get; set; } = 160;
    public int MaxExternalRagChunks { get; set; } = 4;
    public int WebResearchUserCooldownSeconds { get; set; }
    public int WebResearchGroupCooldownSeconds { get; set; }
    public int WebResearchCacheSeconds { get; set; }
    public int WebResearchMaxConcurrent { get; set; }
}

public sealed record AgentWebAccessRequest(
    AgentWebAccessActorRole ActorRole,
    AgentWebAccessCapability Capability,
    string Query,
    AgentWebAccessConfig Config);

public sealed record AgentWebAccessDecision(
    bool Allowed,
    string Reason,
    AgentWebAccessCapability Capability);

public static class AgentWebAccessRouter
{
    public static AgentWebAccessDecision Evaluate(AgentWebAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        AgentWebAccessConfig config = request.Config ?? new AgentWebAccessConfig();
        string query = request.Query?.Trim() ?? "";
        int maxQueryChars = Math.Max(config.MaxQueryChars, 1);
        if (query.Length > maxQueryChars)
            return Deny(request.Capability, "query_too_long");

        return request.Capability switch
        {
            AgentWebAccessCapability.PublicSearch => EvaluatePublicSearch(request.ActorRole, config),
            AgentWebAccessCapability.AutoRead => EvaluateOwnerOnly(
                request.ActorRole,
                config.EnableAutoRead,
                request.Capability,
                "auto_read_disabled"),
            AgentWebAccessCapability.PublicFetch => EvaluateOwnerOnly(
                request.ActorRole,
                config.EnablePublicFetch,
                request.Capability,
                "public_fetch_disabled"),
            AgentWebAccessCapability.BrowserSnapshot => EvaluateOwnerOnly(
                request.ActorRole,
                config.EnableBrowserSnapshot,
                request.Capability,
                "browser_snapshot_disabled"),
            AgentWebAccessCapability.BrowserInteract => EvaluateOwnerOnly(
                request.ActorRole,
                config.EnableBrowserInteract,
                request.Capability,
                "browser_interact_disabled"),
            AgentWebAccessCapability.ExternalRagQuery => EvaluateExternalRagQuery(request.ActorRole, config),
            AgentWebAccessCapability.ExternalRagMutation => EvaluateOwnerOnly(
                request.ActorRole,
                config.EnableExternalRagMutation,
                request.Capability,
                "external_rag_mutation_disabled"),
            _ => Deny(request.Capability, "unknown_capability")
        };
    }

    static AgentWebAccessDecision EvaluatePublicSearch(
        AgentWebAccessActorRole actorRole,
        AgentWebAccessConfig config)
    {
        if (config.EnablePublicSearch == false)
            return Deny(AgentWebAccessCapability.PublicSearch, "public_search_disabled");

        return actorRole switch
        {
            AgentWebAccessActorRole.Owner => Allow(AgentWebAccessCapability.PublicSearch),
            AgentWebAccessActorRole.GroupMember when config.AllowGroupMemberPublicSearch
                => Allow(AgentWebAccessCapability.PublicSearch),
            AgentWebAccessActorRole.GroupMember => Deny(AgentWebAccessCapability.PublicSearch, "group_member_public_search_disabled"),
            _ => Deny(AgentWebAccessCapability.PublicSearch, "owner_required")
        };
    }

    static AgentWebAccessDecision EvaluateExternalRagQuery(
        AgentWebAccessActorRole actorRole,
        AgentWebAccessConfig config)
    {
        if (config.EnableExternalRagQuery == false)
            return Deny(AgentWebAccessCapability.ExternalRagQuery, "external_rag_query_disabled");

        return actorRole switch
        {
            AgentWebAccessActorRole.Owner => Allow(AgentWebAccessCapability.ExternalRagQuery),
            AgentWebAccessActorRole.GroupMember when config.AllowGroupMemberExternalRagQuery
                => Allow(AgentWebAccessCapability.ExternalRagQuery),
            AgentWebAccessActorRole.GroupMember => Deny(AgentWebAccessCapability.ExternalRagQuery, "group_member_external_rag_query_disabled"),
            _ => Deny(AgentWebAccessCapability.ExternalRagQuery, "owner_required")
        };
    }

    static AgentWebAccessDecision EvaluateOwnerOnly(
        AgentWebAccessActorRole actorRole,
        bool enabled,
        AgentWebAccessCapability capability,
        string disabledReason)
    {
        if (enabled == false)
            return Deny(capability, disabledReason);

        return actorRole == AgentWebAccessActorRole.Owner
            ? Allow(capability)
            : Deny(capability, "owner_required");
    }

    static AgentWebAccessDecision Allow(AgentWebAccessCapability capability) =>
        new(true, "allowed", capability);

    static AgentWebAccessDecision Deny(AgentWebAccessCapability capability, string reason) =>
        new(false, reason, capability);
}
