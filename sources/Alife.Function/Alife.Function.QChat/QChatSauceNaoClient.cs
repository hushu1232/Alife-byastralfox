using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public enum QChatImageSearchFailureKind
{
    None,
    MissingApiKey,
    AssetUnavailable,
    RateLimited,
    QuotaOrKeyRejected,
    HttpError,
    Timeout,
    InvalidResponse,
    NoMatch
}

public sealed record QChatImageSearchMatch(
    double Similarity,
    string Title,
    string Author,
    string SourceUrl,
    string Provider);

public sealed record QChatImageSearchResult(
    bool Success,
    IReadOnlyList<QChatImageSearchMatch> Matches,
    QChatImageSearchFailureKind FailureKind,
    string FailureReason)
{
    public static QChatImageSearchResult Ok(IReadOnlyList<QChatImageSearchMatch> matches) =>
        new(true, matches, QChatImageSearchFailureKind.None, string.Empty);

    public static QChatImageSearchResult Fail(
        QChatImageSearchFailureKind kind,
        string reason) => new(false, [], kind, reason);
}

public sealed class QChatSauceNaoClient
{
    public const string DefaultApiKeyEnvironmentVariable = "ALIFE_SAUCENAO_API_KEY";
    public const string DefaultEndpoint = "https://saucenao.com/search.php";
    const long MaxImageBytes = 10L * 1024 * 1024;

    readonly HttpClient httpClient;
    readonly Func<string?> apiKeyResolver;
    readonly Uri endpoint;

    public QChatSauceNaoClient(
        HttpClient httpClient,
        Func<string?> apiKeyResolver,
        string endpoint = DefaultEndpoint)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiKeyResolver = apiKeyResolver ?? throw new ArgumentNullException(nameof(apiKeyResolver));
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed) == false ||
            parsed.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("SauceNAO endpoint must be an absolute HTTPS URI.", nameof(endpoint));
        }
        this.endpoint = parsed;
    }

    public async Task<QChatImageSearchResult> SearchAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = apiKeyResolver()?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.MissingApiKey, "missing_api_key");

        if (string.IsNullOrWhiteSpace(imagePath))
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.AssetUnavailable, "asset_unavailable");
        FileInfo image;
        try
        {
            image = new FileInfo(imagePath);
            if (image.Exists == false || image.Length is <= 0 or > MaxImageBytes)
                return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.AssetUnavailable, "asset_unavailable");
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.AssetUnavailable, "asset_unavailable");
        }

        try
        {
            await using FileStream imageStream = new(
                image.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using MultipartFormDataContent form = new();
            form.Add(new StringContent("2"), "output_type");
            form.Add(new StringContent("5"), "numres");
            form.Add(new StringContent("999"), "db");
            form.Add(new StringContent(apiKey), "api_key");
            using StreamContent fileContent = new(imageStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", image.Name);

            using HttpRequestMessage request = new(HttpMethod.Post, endpoint) { Content = form };
            request.Headers.UserAgent.ParseAdd("Alife-QChat/1.0");
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.RateLimited, "http_429");
            if (response.StatusCode is HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden)
            {
                return QChatImageSearchResult.Fail(
                    QChatImageSearchFailureKind.QuotaOrKeyRejected,
                    $"http_{(int)response.StatusCode}");
            }
            if (response.IsSuccessStatusCode == false)
            {
                return QChatImageSearchResult.Fail(
                    QChatImageSearchFailureKind.HttpError,
                    $"http_{(int)response.StatusCode}");
            }

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(
                responseStream,
                cancellationToken: cancellationToken);
            return Parse(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
        {
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.Timeout, "timeout");
        }
        catch (HttpRequestException)
        {
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.HttpError, "http_error");
        }
        catch (JsonException)
        {
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.InvalidResponse, "invalid_json");
        }
        catch (IOException)
        {
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.AssetUnavailable, "asset_unavailable");
        }
    }

    public static string? ResolveApiKey(string environmentVariableName = DefaultApiKeyEnvironmentVariable)
    {
        foreach (EnvironmentVariableTarget target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            string? value = Environment.GetEnvironmentVariable(environmentVariableName, target);
            if (string.IsNullOrWhiteSpace(value) == false)
                return value.Trim();
        }
        return null;
    }

    static QChatImageSearchResult Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.InvalidResponse, "invalid_response");

        if (root.TryGetProperty("header", out JsonElement header) &&
            header.TryGetProperty("status", out JsonElement status) &&
            status.TryGetInt32(out int statusCode) &&
            statusCode != 0)
        {
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.QuotaOrKeyRejected, "provider_rejected");
        }

        if (root.TryGetProperty("results", out JsonElement results) == false ||
            results.ValueKind != JsonValueKind.Array)
        {
            return QChatImageSearchResult.Fail(QChatImageSearchFailureKind.InvalidResponse, "missing_results");
        }

        List<QChatImageSearchMatch> matches = [];
        foreach (JsonElement item in results.EnumerateArray().Take(5))
        {
            if (item.ValueKind != JsonValueKind.Object ||
                item.TryGetProperty("header", out JsonElement itemHeader) == false ||
                item.TryGetProperty("data", out JsonElement data) == false)
            {
                continue;
            }

            string similarityText = ReadText(itemHeader, "similarity");
            if (double.TryParse(similarityText, NumberStyles.Float, CultureInfo.InvariantCulture, out double similarity) == false)
                continue;
            string sourceUrl = ReadSafeSourceUrl(data);
            matches.Add(new QChatImageSearchMatch(
                Math.Clamp(similarity, 0, 100),
                SanitizeEvidenceText(ReadFirstText(data, "title", "eng_name", "jp_name", "source", "material")),
                SanitizeEvidenceText(ReadFirstText(data, "author_name", "member_name", "creator", "author")),
                sourceUrl,
                "saucenao"));
        }

        return matches.Count == 0
            ? QChatImageSearchResult.Fail(QChatImageSearchFailureKind.NoMatch, "no_match")
            : QChatImageSearchResult.Ok(matches);
    }

    static string ReadSafeSourceUrl(JsonElement data)
    {
        if (data.TryGetProperty("ext_urls", out JsonElement urls) == false ||
            urls.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (JsonElement value in urls.EnumerateArray())
        {
            string candidate = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
            }
        }
        return string.Empty;
    }

    static string ReadFirstText(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            string value = ReadText(element, name);
            if (string.IsNullOrWhiteSpace(value) == false)
                return value;
        }
        return string.Empty;
    }

    static string ReadText(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            element.TryGetProperty(name, out JsonElement value) == false)
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        if (value.ValueKind == JsonValueKind.Array)
        {
            return string.Join(
                ", ",
                value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => string.IsNullOrWhiteSpace(item) == false)
                    .Take(3));
        }
        return string.Empty;
    }

    static string SanitizeEvidenceText(string value)
    {
        string sanitized = (value ?? string.Empty)
            .Replace("[qchat image search]", "[image search boundary removed]", StringComparison.OrdinalIgnoreCase)
            .Replace("[/qchat image search]", "[image search boundary removed]", StringComparison.OrdinalIgnoreCase)
            .Replace("[qchat image analysis]", "[image analysis boundary removed]", StringComparison.OrdinalIgnoreCase)
            .Replace("[/qchat image analysis]", "[image analysis boundary removed]", StringComparison.OrdinalIgnoreCase)
            .ReplaceLineEndings(" ")
            .Trim();
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }
}
