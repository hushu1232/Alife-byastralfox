namespace Alife.Function.Agent;

public sealed record AgentWebStrategyDecision(
    bool Allowed,
    string Host,
    AgentBrowserSiteStrategy Strategy,
    AgentWebAccessCapability? Capability,
    string Reason);

public static class AgentWebStrategyRouter
{
    public static AgentWebStrategyDecision Evaluate(
        string url,
        AgentBrowserSiteExperienceStore? siteExperienceStore)
    {
        if (AgentBrowserSiteExperienceStore.TryNormalizeHttpHost(url, out string host) == false)
            return Denied("", AgentBrowserSiteStrategy.Blocked, "unsafe_url");

        AgentBrowserSiteExperience? experience = siteExperienceStore?.Get(host);
        if (experience == null)
        {
            return new AgentWebStrategyDecision(
                true,
                host,
                AgentBrowserSiteStrategy.PublicFetch,
                AgentWebAccessCapability.PublicFetch,
                "default_public_fetch");
        }

        if (experience.NeedsLogin || experience.PreferredStrategy == AgentBrowserSiteStrategy.Blocked)
        {
            return Denied(
                host,
                AgentBrowserSiteStrategy.Blocked,
                "site_requires_login_or_owner_assistance");
        }

        if (experience.PreferredStrategy == AgentBrowserSiteStrategy.DynamicBrowser)
        {
            return new AgentWebStrategyDecision(
                true,
                host,
                AgentBrowserSiteStrategy.DynamicBrowser,
                AgentWebAccessCapability.BrowserSnapshot,
                "dynamic_browser_not_implemented_snapshot_only");
        }

        if (experience.PreferredStrategy == AgentBrowserSiteStrategy.BrowserSnapshot || experience.NeedsBrowser)
        {
            return new AgentWebStrategyDecision(
                true,
                host,
                AgentBrowserSiteStrategy.BrowserSnapshot,
                AgentWebAccessCapability.BrowserSnapshot,
                "site_prefers_browser_snapshot");
        }

        return new AgentWebStrategyDecision(
            true,
            host,
            AgentBrowserSiteStrategy.PublicFetch,
            AgentWebAccessCapability.PublicFetch,
            "site_prefers_public_fetch");
    }

    static AgentWebStrategyDecision Denied(
        string host,
        AgentBrowserSiteStrategy strategy,
        string reason)
    {
        return new AgentWebStrategyDecision(false, host, strategy, null, reason);
    }
}
