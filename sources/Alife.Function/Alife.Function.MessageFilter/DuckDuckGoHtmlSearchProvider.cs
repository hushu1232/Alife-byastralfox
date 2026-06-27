using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed class DuckDuckGoHtmlSearchProvider(
    HttpClient? httpClient = null,
    string endpoint = "https://duckduckgo.com/html/")
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
        MatchCollection linkMatches = Regex.Matches(
            html,
            """<a\b(?=[^>]*\bclass=(['"])[^'"]*result__a[^'"]*\1)(?=[^>]*\bhref=(['"])(?<href>.*?)\2)[^>]*>(?<title>.*?)</a>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (linkMatches.Count == 0)
            return [];

        List<AgentPublicSearchResult> results = [];
        for (int i = 0; i < linkMatches.Count && results.Count < maxResults; i++)
        {
            Match link = linkMatches[i];
            string? url = NormalizeResultUrl(link.Groups["href"].Value);
            if (url == null)
                continue;

            string title = CleanHtml(link.Groups["title"].Value);
            if (title.Length == 0)
                continue;

            int snippetStart = link.Index + link.Length;
            int snippetLength = i + 1 < linkMatches.Count
                ? Math.Max(0, linkMatches[i + 1].Index - snippetStart)
                : html.Length - snippetStart;
            string snippetRegion = html.Substring(snippetStart, snippetLength);
            string snippet = ExtractSnippet(snippetRegion);
            results.Add(new AgentPublicSearchResult(title, url, snippet));
        }

        return results;
    }

    static string ExtractSnippet(string html)
    {
        Match match = Regex.Match(
            html,
            """<(?:a|div|span|p)\b(?=[^>]*\bclass=['"][^'"]*result__snippet[^'"]*['"])[^>]*>(?<snippet>.*?)</(?:a|div|span|p)>""",
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
        if (decoded.StartsWith("//", StringComparison.Ordinal))
            decoded = "https:" + decoded;
        else if (decoded.StartsWith("/l/", StringComparison.OrdinalIgnoreCase))
            decoded = "https://duckduckgo.com" + decoded;

        if (Uri.TryCreate(decoded, UriKind.Absolute, out Uri? uri) == false)
            return null;

        if (IsDuckDuckGoRedirect(uri))
        {
            string? target = TryReadQueryValue(uri.Query, "uddg");
            if (target != null && Uri.TryCreate(target, UriKind.Absolute, out Uri? targetUri))
                return IsHttpUrl(targetUri) ? targetUri.ToString() : null;
        }

        return IsHttpUrl(uri) ? uri.ToString() : null;
    }

    static bool IsDuckDuckGoRedirect(Uri uri)
    {
        return uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.StartsWith("/l/", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsHttpUrl(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    static string? TryReadQueryValue(string query, string name)
    {
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            string key = Uri.UnescapeDataString(pair[0].Replace("+", " ", StringComparison.Ordinal));
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) == false)
                continue;

            string value = pair.Length > 1 ? pair[1] : "";
            return Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
        }

        return null;
    }
}
