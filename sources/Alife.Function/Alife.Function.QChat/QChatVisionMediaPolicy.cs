using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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

        return IPAddress.TryParse(host, out IPAddress? address) == false || IsPublicAddress(address);
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] != 0 &&
                   bytes[0] != 10 &&
                   bytes[0] != 127 &&
                   (bytes[0] != 100 || (bytes[1] & 0xC0) != 0x40) &&
                   (bytes[0] != 169 || bytes[1] != 254) &&
                   (bytes[0] != 172 || bytes[1] is < 16 or > 31) &&
                   (bytes[0] != 192 || bytes[1] != 168) &&
                   (bytes[0] != 198 || bytes[1] is < 18 or > 19) &&
                   bytes[0] < 224;
        }

        return address.IsIPv6LinkLocal == false &&
               address.IsIPv6SiteLocal == false &&
               address.IsIPv6Multicast == false &&
               (bytes[0] & 0xFE) != 0xFC &&
               (bytes[0] != 0x20 || bytes[1] != 0x01 || bytes[2] != 0x0D || bytes[3] != 0xB8);
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

public static class QChatSafeImageDownloader
{
    const int MaxRedirects = 3;
    static readonly HttpClient Client = new(CreateHandler())
    {
        Timeout = TimeSpan.FromMinutes(1)
    };

    public static async Task<byte[]> DownloadAsync(
        string source,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        Uri uri = ParseAllowedUri(source);
        for (int redirectCount = 0; ; redirectCount++)
        {
            using HttpResponseMessage response = await Client.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaxRedirects || response.Headers.Location is null)
                    throw new InvalidOperationException("image_redirect_not_allowed");

                uri = ParseAllowedUri(new Uri(uri, response.Headers.Location).AbsoluteUri);
                continue;
            }

            if (response.IsSuccessStatusCode == false)
                throw new InvalidOperationException("image_download_failed");
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                throw new InvalidOperationException("image_content_type_invalid");
            }
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
                throw new InvalidOperationException("image_too_large");

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream output = new();
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                if (output.Length + read > maxBytes)
                    throw new InvalidOperationException("image_too_large");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            byte[] bytes = output.ToArray();
            if (QChatImageAssetService.TryDetectMedia(bytes, out _, out _) == false)
                throw new InvalidOperationException("image_content_invalid");
            return bytes;
        }
    }

    static Uri ParseAllowedUri(string source)
    {
        QChatVisionMediaDecision decision = QChatVisionMediaPolicy.CheckImageUrl(source, allowedHosts: null);
        if (decision.Allowed == false)
            throw new InvalidOperationException(decision.Reason);
        return new Uri(source, UriKind.Absolute);
    }

    static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.Moved or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = ConnectPublicHostAsync
    };

    static async ValueTask<Stream> ConnectPublicHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new HttpRequestException("image_host_unavailable", exception);
        }

        Exception? lastError = null;
        foreach (IPAddress address in addresses.Where(QChatVisionMediaPolicy.IsPublicAddress))
        {
            Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                socket.Dispose();
                lastError = exception;
            }
        }

        throw new HttpRequestException(
            lastError is null ? "image_host_not_public" : "image_download_failed",
            lastError);
    }
}
