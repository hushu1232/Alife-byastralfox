using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public delegate Task<AgentBrowserMediaFetchResult> AgentBrowserMediaFetcher(
    Uri uri,
    int maxBytes,
    CancellationToken cancellationToken);

public sealed class AgentBrowserMediaOutputService(
    HttpClient? httpClient = null,
    AgentBrowserMediaFetcher? fetcher = null,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolveHostAsync = null)
{
    const string DefaultMediaCacheRoot = @"D:\Alife\Runtime\BrowserAgentMedia";
    const string TestMediaCacheRoot = @"D:\tmp\alife-browser-media";

    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif"
    };

    static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".webm",
        ".mov",
        ".m4v"
    };

    readonly HttpClient client = httpClient ?? new HttpClient();
    readonly AgentBrowserMediaFetcher? fetcher = fetcher;
    readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveHostAsync =
        resolveHostAsync ?? ResolveHostWithDnsAsync;

    public async Task<AgentBrowserMediaOutputResult> PrepareAsync(
        AgentBrowserMediaOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string normalizedUrl = (request.Url ?? "").Trim();
        if (AgentBrowserActionPolicy.IsPublicHttpUrl(normalizedUrl) == false)
            return Deny(request, "browser_agent_unsafe_url");
        if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri) == false)
            return Deny(request, "browser_agent_unsafe_url");

        string extension = Path.GetExtension(uri.AbsolutePath);
        if (IsAllowedExtension(request.Kind, extension) == false)
            return Deny(request, "browser_agent_media_type_denied");

        if (request.Kind == AgentBrowserMediaOutputKind.VideoLink)
            return new AgentBrowserMediaOutputResult(true, "ok", request.Kind, normalizedUrl, normalizedUrl, null);

        string root = ResolveMediaRoot(request.Config);
        if (IsSafeDDriveRoot(root) == false)
            return Deny(request, "browser_agent_unsafe_media_root");

        if (await IsResolvedPublicHostAsync(uri, cancellationToken) == false)
            return Deny(request, "browser_agent_unsafe_url");

        int maxBytes = Math.Max(1, request.Config?.MaxImageBytes ?? new AgentBrowserAutomationConfig().MaxImageBytes);
        AgentBrowserMediaFetchResult fetchResult = await FetchAsync(uri, maxBytes, cancellationToken);
        if (fetchResult.Success == false)
            return Deny(request, NormalizeFetchReason(fetchResult.Reason));
        if (IsCompatibleImageContentType(extension, fetchResult.ContentType) == false)
            return Deny(request, "browser_agent_media_type_denied");
        if (fetchResult.Body.Length > maxBytes)
            return Deny(request, "browser_agent_media_too_large");
        if (HasCompatibleImageSignature(extension, fetchResult.Body) == false)
            return Deny(request, "browser_agent_media_type_denied");

        string localPath = BuildLocalImagePath(root, uri, extension);
        if (IsPathUnderRoot(localPath, root) == false)
            return Deny(request, "browser_agent_unsafe_media_root");

        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(localPath, fetchResult.Body, cancellationToken);
        return new AgentBrowserMediaOutputResult(true, "ok", request.Kind, normalizedUrl, localPath, localPath);
    }

    async Task<AgentBrowserMediaFetchResult> FetchAsync(
        Uri uri,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (fetcher != null)
            return await fetcher(uri, maxBytes, cancellationToken);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode == false)
                return new AgentBrowserMediaFetchResult(false, $"http_status_{(int)response.StatusCode}", "", []);
            if (response.Content.Headers.ContentLength is long length && length > maxBytes)
                return new AgentBrowserMediaFetchResult(false, "browser_agent_media_too_large", response.Content.Headers.ContentType?.MediaType ?? "", []);

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[81920];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                    break;

                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes)
                    return new AgentBrowserMediaFetchResult(false, "browser_agent_media_too_large", response.Content.Headers.ContentType?.MediaType ?? "", []);
            }

            return new AgentBrowserMediaFetchResult(
                true,
                "ok",
                response.Content.Headers.ContentType?.MediaType ?? "",
                buffer.ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
        {
            return new AgentBrowserMediaFetchResult(false, "browser_agent_media_download_failed", "", []);
        }
    }

    static string ResolveMediaRoot(AgentBrowserAutomationConfig? config)
    {
        string configured = config?.MediaCacheRoot?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(configured) ? DefaultMediaCacheRoot : configured;
    }

    static bool IsAllowedExtension(AgentBrowserMediaOutputKind kind, string extension) =>
        kind switch
        {
            AgentBrowserMediaOutputKind.Image => ImageExtensions.Contains(extension),
            AgentBrowserMediaOutputKind.VideoLink => VideoExtensions.Contains(extension),
            _ => false
        };

    static bool IsCompatibleImageContentType(string extension, string contentType)
    {
        string mediaType = (contentType ?? "").Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mediaType.StartsWith("image/", StringComparison.Ordinal) == false)
            return false;

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => mediaType is "image/jpeg" or "image/jpg",
            ".png" => mediaType == "image/png",
            ".webp" => mediaType == "image/webp",
            ".gif" => mediaType == "image/gif",
            _ => false
        };
    }

    async Task<bool> IsResolvedPublicHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            IPAddress[] addresses = await resolveHostAsync(uri.Host, cancellationToken);
            return addresses.Length > 0 && addresses.All(AgentBrowserActionPolicy.IsPublicAddress);
        }
        catch
        {
            return false;
        }
    }

    static Task<IPAddress[]> ResolveHostWithDnsAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);

    static bool HasCompatibleImageSignature(string extension, byte[] body) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => body.Length >= 3 && body[0] == 0xff && body[1] == 0xd8 && body[2] == 0xff,
            ".png" => StartsWith(body, [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
            ".webp" => body.Length >= 12
                       && StartsWith(body, [0x52, 0x49, 0x46, 0x46])
                       && body[8] == 0x57 && body[9] == 0x45 && body[10] == 0x42 && body[11] == 0x50,
            ".gif" => StartsWith(body, [0x47, 0x49, 0x46, 0x38, 0x37, 0x61])
                      || StartsWith(body, [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]),
            _ => false
        };

    static bool StartsWith(byte[] value, byte[] prefix)
    {
        if (value.Length < prefix.Length)
            return false;

        for (int i = 0; i < prefix.Length; i++)
        {
            if (value[i] != prefix[i])
                return false;
        }

        return true;
    }

    static bool IsSafeDDriveRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;
        if (ContainsParentTraversal(root))
            return false;

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
        }
        catch
        {
            return false;
        }

        return IsUnderControlledRoot(fullRoot, DefaultMediaCacheRoot) || IsUnderControlledRoot(fullRoot, TestMediaCacheRoot);
    }

    static bool ContainsParentTraversal(string path)
    {
        string[] parts = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return Array.Exists(parts, static part => part == "..");
    }

    static string BuildLocalImagePath(string root, Uri uri, string extension)
    {
        string safeHost = string.Join("_", uri.Host.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string fileName = $"{safeHost}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        return Path.Combine(Path.GetFullPath(root), fileName);
    }

    static bool IsPathUnderRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root);
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsUnderControlledRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root);
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeFetchReason(string reason) =>
        reason switch
        {
            "image_too_large" => "browser_agent_media_too_large",
            "browser_agent_media_too_large" => reason,
            "browser_agent_media_download_failed" => reason,
            _ when reason.StartsWith("browser_agent_", StringComparison.Ordinal) => reason,
            _ => "browser_agent_media_download_failed"
        };

    static AgentBrowserMediaOutputResult Deny(AgentBrowserMediaOutputRequest request, string reason) =>
        new(false, reason, request.Kind, request.Url, "", null);
}
