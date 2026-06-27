using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed class BingHtmlSearchProvider(
    HttpClient? httpClient = null,
    string endpoint = "https://www.bing.com/search")
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
        Uri requestUri = BuildRequestUri(normalized);
        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("astralfox-alife-PublicSearch/1.0");

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode == false)
            throw new InvalidOperationException($"public_search_http_status_{(int)response.StatusCode}");

        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResults(html, limit);
    }

    Uri BuildRequestUri(string query)
    {
        string separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return new Uri($"{endpoint}{separator}q={Uri.EscapeDataString(query)}");
    }

    static IReadOnlyList<AgentPublicSearchResult> ParseResults(string html, int maxResults)
    {
        MatchCollection titleMatches = Regex.Matches(
            html,
            """<h2\b[^>]*>\s*<a\b(?=[^>]*\bhref=(['"])(?<href>.*?)\1)[^>]*>(?<title>.*?)</a>\s*</h2>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (titleMatches.Count == 0)
            return [];

        List<AgentPublicSearchResult> results = [];
        for (int i = 0; i < titleMatches.Count && results.Count < maxResults; i++)
        {
            Match titleMatch = titleMatches[i];
            string? url = NormalizeResultUrl(titleMatch.Groups["href"].Value);
            if (url == null)
                continue;

            string title = CleanHtml(titleMatch.Groups["title"].Value);
            if (title.Length == 0)
                continue;

            int snippetStart = titleMatch.Index + titleMatch.Length;
            int snippetLength = i + 1 < titleMatches.Count
                ? Math.Max(0, titleMatches[i + 1].Index - snippetStart)
                : html.Length - snippetStart;
            string snippetRegion = html.Substring(snippetStart, snippetLength);
            results.Add(new AgentPublicSearchResult(title, url, ExtractSnippet(snippetRegion)));
        }

        return results;
    }

    static string ExtractSnippet(string html)
    {
        Match match = Regex.Match(
            html,
            """<p\b[^>]*>(?<snippet>.*?)</p>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? CleanHtml(match.Groups["snippet"].Value) : "";
    }

    static string CleanHtml(string html)
    {
        string withoutTags = Regex.Replace(
            html,
            "<[^>]+>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, "\\s+", " ").Trim();
    }

    static string? NormalizeResultUrl(string href)
    {
        string decoded = WebUtility.HtmlDecode(href).Trim();
        if (Uri.TryCreate(decoded, UriKind.Absolute, out Uri? uri) == false)
            return null;

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return uri.ToString();

        return null;
    }
}
