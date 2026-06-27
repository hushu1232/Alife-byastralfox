using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Alife.Function.Agent;

public static class AgentBrowserActionPolicy
{
    static readonly HashSet<AgentBrowserAutomationActionKind> AllowedActions =
    [
        AgentBrowserAutomationActionKind.SearchPublicWeb,
        AgentBrowserAutomationActionKind.NavigatePublicUrl,
        AgentBrowserAutomationActionKind.CaptureSnapshot,
        AgentBrowserAutomationActionKind.Scroll,
        AgentBrowserAutomationActionKind.ClickPublicLink,
        AgentBrowserAutomationActionKind.ClickSamePageNavigation,
        AgentBrowserAutomationActionKind.GoBack,
        AgentBrowserAutomationActionKind.Stop,
        AgentBrowserAutomationActionKind.DownloadPublicImage,
        AgentBrowserAutomationActionKind.ReturnPublicVideoLink
    ];

    static readonly HashSet<AgentBrowserAutomationActionKind> PublicUrlActions =
    [
        AgentBrowserAutomationActionKind.NavigatePublicUrl,
        AgentBrowserAutomationActionKind.ClickPublicLink,
        AgentBrowserAutomationActionKind.ClickSamePageNavigation,
        AgentBrowserAutomationActionKind.DownloadPublicImage,
        AgentBrowserAutomationActionKind.ReturnPublicVideoLink
    ];

    public static AgentBrowserActionDecision Evaluate(AgentBrowserActionPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        AgentBrowserAutomationConfig config = request.Config ?? new AgentBrowserAutomationConfig();

        if (config.Enabled == false)
            return Deny("browser_agent_disabled");
        if (request.ActorRole != AgentWebAccessActorRole.Owner)
            return Deny("browser_agent_owner_required");
        if (request.StepIndex >= Math.Max(config.MaxSteps, 1))
            return Deny("browser_agent_step_limit");
        if (request.OpenedPageCount >= Math.Max(config.MaxPages, 1) && OpensPage(request.Action.Kind))
            return Deny("browser_agent_page_limit");
        if (AllowedActions.Contains(request.Action.Kind) == false)
            return Deny("browser_agent_action_denied");
        if (PublicUrlActions.Contains(request.Action.Kind) && IsPublicHttpUrl(request.Action.Target) == false)
            return Deny("browser_agent_unsafe_url");

        return new AgentBrowserActionDecision(true, "allowed");
    }

    public static bool IsPublicHttpUrl(string? value)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) == false)
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;
        if (uri.IsLoopback)
            return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return false;
        if (IPAddress.TryParse(uri.Host, out IPAddress? address) && IsPublicAddress(address) == false)
            return false;

        return true;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return IsPublicAddress(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return false;
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                return false;
            if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
                return false;
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                return false;
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                return false;

            return bytes[0] switch
            {
                0 => false,
                10 => false,
                127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
                192 when bytes[1] == 168 => false,
                >= 224 => false,
                _ => true
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = address.GetAddressBytes();
            bool uniqueLocal = (bytes[0] & 0xfe) == 0xfc;
            return uniqueLocal == false
                && address.IsIPv6LinkLocal == false
                && address.IsIPv6SiteLocal == false
                && address.IsIPv6Multicast == false;
        }

        return false;
    }

    static bool OpensPage(AgentBrowserAutomationActionKind kind) =>
        kind is AgentBrowserAutomationActionKind.NavigatePublicUrl
            or AgentBrowserAutomationActionKind.ClickPublicLink
            or AgentBrowserAutomationActionKind.ClickSamePageNavigation;

    static AgentBrowserActionDecision Deny(string reason) => new(false, reason);
}
