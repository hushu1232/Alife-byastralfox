using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.Agent;

public sealed record AgentPublicSearchCandidate(
    string ProviderId,
    int ProviderOrder,
    int ResultOrder,
    AgentPublicSearchResult Result);

public static class AgentPublicSearchResultMerger
{
    public static IReadOnlyList<AgentPublicSearchResult> Merge(
        IEnumerable<AgentPublicSearchCandidate> candidates,
        int maxResults)
    {
        int limit = Math.Clamp(maxResults, 1, 5);
        HashSet<string> seenUrls = new(StringComparer.Ordinal);
        List<AgentPublicSearchResult> results = [];

        foreach (AgentPublicSearchCandidate candidate in candidates
                     .OrderBy(item => item.ProviderOrder)
                     .ThenBy(item => item.ResultOrder)
                     .ThenBy(item => item.Result.Title, StringComparer.Ordinal)
                     .ThenBy(item => item.Result.Url, StringComparer.Ordinal))
        {
            if (TryNormalizeUrl(candidate.Result.Url, out string url) == false || seenUrls.Add(url) == false)
                continue;

            results.Add(candidate.Result with { Url = url });
            if (results.Count == limit)
                break;
        }

        return results;
    }

    internal static bool TryNormalizeUrl(string? value, out string normalized)
    {
        normalized = "";
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        UriBuilder builder = new(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = "",
            Port = uri.IsDefaultPort ? -1 : uri.Port
        };
        if (builder.Path.Length > 1)
            builder.Path = builder.Path.TrimEnd('/');

        normalized = builder.Uri.AbsoluteUri;
        return true;
    }
}
