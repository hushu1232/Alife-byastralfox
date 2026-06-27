using System;
using System.Linq;
using System.Net;

namespace Alife.Function.Agent;

public sealed class AgentInternetConfig
{
    public bool EnableInternetAccess { get; set; } = false;
    public string AllowedSchemes { get; set; } = "http,https";
    public string BlockedHosts { get; set; } = "localhost,127.0.0.1,0.0.0.0";
    public int TimeoutMilliseconds { get; set; } = 12000;
    public int MaxResponseBytes { get; set; } = 512000;
    public int MaxExtractedChars { get; set; } = 8000;
    public string UserAgent { get; set; } = "astralfox-alife-AgentInternet/1.0";

    public static AgentInternetConfig CreateDefault() => new();
}

public sealed record AgentInternetUrlPolicyDecision(bool Allowed, string Reason, Uri? Uri);

public static class AgentInternetUrlPolicy
{
    public static AgentInternetUrlPolicyDecision Evaluate(string? url, AgentInternetConfig? config = null)
    {
        config ??= AgentInternetConfig.CreateDefault();

        if (string.IsNullOrWhiteSpace(url))
            return Deny("empty_url");

        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) == false)
            return Deny("invalid_url");

        if (ContainsToken(config.AllowedSchemes, uri.Scheme) == false)
            return Deny("scheme_not_allowed");

        string host = uri.Host.Trim().ToLowerInvariant();
        if (ContainsToken(config.BlockedHosts, host))
            return Deny("blocked_host");

        if (IPAddress.TryParse(host, out IPAddress? address) && IsPrivateOrLoopback(address))
            return Deny("private_or_loopback_address");

        return new AgentInternetUrlPolicyDecision(true, "allowed", uri);
    }

    static AgentInternetUrlPolicyDecision Deny(string reason) =>
        new(false, reason, null);

    static bool ContainsToken(string? csv, string value)
    {
        string normalized = (value ?? "").Trim();
        return (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
    }

    static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        return bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }
}
