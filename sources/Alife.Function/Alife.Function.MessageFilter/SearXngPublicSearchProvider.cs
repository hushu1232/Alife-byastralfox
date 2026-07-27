using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed class SearXngPublicSearchProvider(
    HttpClient? httpClient = null,
    string endpoint = "http://127.0.0.1:8080/search")
    : IAgentPublicSearchProvider
{
    readonly HttpClient client = httpClient ?? new HttpClient();
    readonly string endpoint = endpoint;

    public async Task<IReadOnlyList<AgentPublicSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        string normalized = query.Trim();
        if (normalized.Length == 0)
            return [];

        int limit = Math.Clamp(maxResults, 1, 10);
        bool isNewsQuery = IsNewsQuery(normalized);
        using HttpRequestMessage request = new(HttpMethod.Get, BuildRequestUri(normalized, isNewsQuery));
        request.Headers.UserAgent.ParseAdd("astralfox-alife-SearXNG/1.0");

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode == false)
            throw new InvalidOperationException($"searxng_http_status_{(int)response.StatusCode}");

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResults(json, limit, isNewsQuery);
    }

    Uri BuildRequestUri(string query, bool isNewsQuery)
    {
        string separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        string category = isNewsQuery ? "news" : "general";
        string language = ContainsCjk(query) ? "zh-CN" : "auto";
        return new Uri(
            $"{endpoint}{separator}q={Uri.EscapeDataString(query)}" +
            $"&format=json&categories={category}&language={language}&safesearch=1");
    }

    static IReadOnlyList<AgentPublicSearchResult> ParseResults(string json, int maxResults, bool preferNewest)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("results", out JsonElement resultsElement) == false ||
                resultsElement.ValueKind != JsonValueKind.Array)
                return [];

            List<SearchCandidate> candidates = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in resultsElement.EnumerateArray())
            {
                string title = CleanText(ReadString(item, "title"));
                string url = ReadString(item, "url").Trim();
                if (title.Length == 0 || IsHttpUrl(url) == false || seen.Add(url) == false)
                    continue;

                string snippet = CleanText(ReadString(item, "content"));
                string published = ReadString(item, "publishedDate").Trim();
                if (published.Length > 0)
                    snippet = $"发布时间：{published} {snippet}".Trim();
                if (snippet.Length > 600)
                    snippet = snippet[..600];

                DateTimeOffset? publishedAt = DateTimeOffset.TryParse(
                    published,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out DateTimeOffset parsed)
                    ? parsed
                    : null;
                candidates.Add(new SearchCandidate(
                    new AgentPublicSearchResult(title, url, snippet),
                    publishedAt));
            }

            IEnumerable<SearchCandidate> ordered = preferNewest
                ? candidates.OrderByDescending(candidate => candidate.PublishedAt ?? DateTimeOffset.MinValue)
                : candidates;
            return ordered.Take(maxResults).Select(candidate => candidate.Result).ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("searxng_invalid_response", ex);
        }
    }

    static string ReadString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    static bool IsNewsQuery(string query) =>
        query.Contains("新闻", StringComparison.OrdinalIgnoreCase) ||
        query.Contains("资讯", StringComparison.OrdinalIgnoreCase) ||
        query.Contains("news", StringComparison.OrdinalIgnoreCase);

    static bool ContainsCjk(string value)
    {
        foreach (char character in value)
        {
            if (character is >= '\u3400' and <= '\u9fff')
                return true;
        }

        return false;
    }

    static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    static string CleanText(string value)
    {
        string withoutTags = Regex.Replace(
            value,
            "<[^>]+>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, "\\s+", " ").Trim();
    }

    sealed record SearchCandidate(AgentPublicSearchResult Result, DateTimeOffset? PublishedAt);
}
