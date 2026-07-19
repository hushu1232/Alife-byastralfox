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
        ArgumentNullException.ThrowIfNull(candidates);
        int limit = Math.Clamp(maxResults, 1, 5);
        HashSet<string> seenUrls = new(StringComparer.Ordinal);
        List<string> acceptedTitles = [];
        List<AgentPublicSearchResult> results = [];

        foreach (AgentPublicSearchCandidate candidate in candidates
                     .Where(static item => item is { Result: not null })
                     .OrderBy(item => item.ProviderOrder)
                     .ThenBy(item => item.ResultOrder)
                     .ThenBy(item => NormalizeTitle(item.Result.Title), StringComparer.Ordinal)
                     .ThenBy(item => item.Result.Url, StringComparer.Ordinal))
        {
            string title = NormalizeTitle(candidate.Result.Title);
            if (TryNormalizeUrl(candidate.Result.Url, out string url) == false ||
                seenUrls.Contains(url) ||
                acceptedTitles.Any(accepted => IsNearDuplicateTitle(accepted, title)))
                continue;

            seenUrls.Add(url);
            results.Add(candidate.Result with { Url = url });
            acceptedTitles.Add(title);
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

        normalized = builder.Uri.AbsoluteUri.TrimEnd('/');
        return true;
    }

    internal static string NormalizeTitle(string? value) => string.Concat((value ?? "")
        .Trim()
        .ToLowerInvariant()
        .Where(char.IsLetterOrDigit));

    internal static bool IsNearDuplicateTitle(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return false;
        if (left == right)
            return true;

        int length = Math.Max(left.Length, right.Length);
        return length >= 5 && LevenshteinDistance(left, right) <= Math.Max(1, length / 7);
    }

    static int LevenshteinDistance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int row = 1; row <= left.Length; row++)
        {
            int[] current = new int[right.Length + 1];
            current[0] = row;
            for (int column = 1; column <= right.Length; column++)
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            previous = current;
        }

        return previous[right.Length];
    }
}
