using System;
using System.Linq;
using System.Net;

namespace Alife.Function.QChat;

public sealed record QChatVisionMediaDecision(bool Allowed, string Reason);

public static class QChatVisionMediaPolicy
{
    const string DeniedReason = "image_url_not_allowed";

    public static QChatVisionMediaDecision CheckImageUrl(string? value, string? allowedHosts)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) == false ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.UserInfo) == false ||
            (uri.IsDefaultPort == false && uri.Port != 443) ||
            IsPublicHost(uri.Host) == false ||
            IsAllowedHost(uri.Host, allowedHosts) == false)
        {
            return new QChatVisionMediaDecision(false, DeniedReason);
        }

        return new QChatVisionMediaDecision(true, "allowed");
    }

    static bool IsPublicHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out IPAddress? address) == false)
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] != 10 &&
                   (bytes[0] != 172 || bytes[1] is < 16 or > 31) &&
                   (bytes[0] != 192 || bytes[1] != 168) &&
                   (bytes[0] != 169 || bytes[1] != 254) &&
                   bytes[0] != 127 &&
                   bytes[0] != 0;
        }

        return address.IsIPv6LinkLocal == false &&
               address.IsIPv6SiteLocal == false &&
               address.IsIPv6Multicast == false &&
               (bytes[0] & 0xFE) != 0xFC;
    }

    static bool IsAllowedHost(string host, string? allowedHosts)
    {
        string[] entries = (allowedHosts ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
            return true;

        return entries.Any(entry =>
            host.Equals(entry.TrimStart('*', '.'), StringComparison.OrdinalIgnoreCase) ||
            (entry.StartsWith("*.", StringComparison.Ordinal) &&
             host.EndsWith(entry[1..], StringComparison.OrdinalIgnoreCase) &&
             host.Length > entry.Length - 1));
    }
}
