using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public abstract record QZoneImageSource
{
    public static QZoneImageSource OwnerFile(string path, string? fileName = null) =>
        new LocalFileSource(path, fileName, QZoneImageOrigin.OwnerProvided);

    public static QZoneImageSource GeneratedFile(string path, string? fileName = null) =>
        new LocalFileSource(path, fileName, QZoneImageOrigin.Generated);

    public static QZoneImageSource OwnerUrl(Uri url) => new OwnerUrlSource(url);

    internal sealed record LocalFileSource(string Path, string? FileName, QZoneImageOrigin Origin) : QZoneImageSource;
    internal sealed record OwnerUrlSource(Uri Url) : QZoneImageSource;
}

public sealed class QZoneImageSourceException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

public sealed class QZoneImageSourceResolver(HttpClient httpClient)
{
    readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<QZoneImageUpload> ResolveAsync(
        QZoneImageSource? source,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (maximumBytes <= 0)
            throw new QZoneImageSourceException("qzone_image_source_invalid");

        return source switch
        {
            QZoneImageSource.LocalFileSource local => await ResolveLocalFileAsync(local, maximumBytes, cancellationToken),
            QZoneImageSource.OwnerUrlSource ownerUrl => await ResolveOwnerUrlAsync(ownerUrl, maximumBytes, cancellationToken),
            _ => throw new QZoneImageSourceException("qzone_image_source_unavailable"),
        };
    }

    async Task<QZoneImageUpload> ResolveLocalFileAsync(
        QZoneImageSource.LocalFileSource source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
            throw new QZoneImageSourceException("qzone_image_source_invalid");

        try
        {
            if (File.Exists(source.Path) == false)
                throw new QZoneImageSourceException("qzone_image_source_unavailable");

            byte[] bytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
            ValidateLength(bytes, maximumBytes);
            string fileName = GetSafeFileName(source.FileName ?? Path.GetFileName(source.Path));
            return new QZoneImageUpload(fileName, GetContentType(fileName), bytes, source.Origin);
        }
        catch (QZoneImageSourceException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new QZoneImageSourceException("qzone_image_source_unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            throw new QZoneImageSourceException("qzone_image_source_unavailable");
        }
        catch (ArgumentException)
        {
            throw new QZoneImageSourceException("qzone_image_source_invalid");
        }
        catch (NotSupportedException)
        {
            throw new QZoneImageSourceException("qzone_image_source_invalid");
        }
    }

    async Task<QZoneImageUpload> ResolveOwnerUrlAsync(
        QZoneImageSource.OwnerUrlSource source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (source.Url is null
            || source.Url.IsAbsoluteUri == false
            || (source.Url.Scheme != Uri.UriSchemeHttp && source.Url.Scheme != Uri.UriSchemeHttps))
            throw new QZoneImageSourceException("qzone_image_source_invalid");

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                source.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode == false || response.Content is null)
                throw new QZoneImageSourceException("qzone_image_source_unavailable");
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
                throw new QZoneImageSourceException("qzone_image_too_large");

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            ValidateLength(bytes, maximumBytes);
            string fileName = GetSafeFileName(source.Url.Segments.LastOrDefault());
            string contentType = response.Content.Headers.ContentType?.MediaType ?? GetContentType(fileName);
            return new QZoneImageUpload(fileName, contentType, bytes, QZoneImageOrigin.OwnerProvided);
        }
        catch (QZoneImageSourceException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new QZoneImageSourceException("qzone_image_source_unavailable");
        }
    }

    static void ValidateLength(byte[] bytes, int maximumBytes)
    {
        if (bytes.Length == 0)
            throw new QZoneImageSourceException("qzone_image_source_unavailable");
        if (bytes.Length > maximumBytes)
            throw new QZoneImageSourceException("qzone_image_too_large");
    }

    static string GetSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "image";

        string normalized = Uri.UnescapeDataString(fileName)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string name = Path.GetFileName(normalized);
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(name) ? "image" : name;
    }

    static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }
}
