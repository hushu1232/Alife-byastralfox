using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alife.Function.Agent;

public static class AgentBrowserTaskPlanner
{
    const int MaxQueryChars = 160;

    static readonly Regex PublicUrlCandidate = new(
        @"https?://[^\s<>'""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly string[] PreferredLinkTerms =
    [
        "docs",
        "documentation",
        "readme",
        "getting-started",
        "getting started",
        "quickstart",
        "install",
        "installation",
        "api",
        "usage",
        "guide",
        "tutorial"
    ];

    static readonly string[] LowValueLinkTerms =
    [
        "contact",
        "privacy",
        "terms",
        "login",
        "log in",
        "sign in",
        "signup",
        "sign up",
        "pricing"
    ];

    public static AgentBrowserAutomationAction PlanInitialAction(string? request)
    {
        foreach (Match match in PublicUrlCandidate.Matches(request ?? ""))
        {
            string url = TrimUrlCandidate(match.Value);
            if (AgentBrowserActionPolicy.IsPublicHttpUrl(url))
            {
                return new AgentBrowserAutomationAction(
                    AgentBrowserAutomationActionKind.NavigatePublicUrl,
                    url,
                    "first_public_url");
            }
        }

        return new AgentBrowserAutomationAction(
            AgentBrowserAutomationActionKind.SearchPublicWeb,
            Compact(RemoveUrlCandidates(request)),
            "no_public_url");
    }

    public static AgentBrowserAutomationAction SelectNextLink(
        string? task,
        IReadOnlyList<AgentBrowserSnapshotLink>? links,
        IReadOnlyCollection<string>? visitedUrls)
    {
        HashSet<string> visited = new(
            (visitedUrls ?? []).Select(NormalizeUrlForComparison),
            StringComparer.OrdinalIgnoreCase);

        AgentBrowserSnapshotLink? selected = (links ?? [])
            .Select(link => new LinkCandidate(link, TrimUrlCandidate(link.Href), ScoreLink(task, link)))
            .Where(candidate => candidate.Score > 0)
            .Where(candidate => AgentBrowserActionPolicy.IsPublicHttpUrl(candidate.Url))
            .Where(candidate => visited.Contains(NormalizeUrlForComparison(candidate.Url)) == false)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault()
            ?.Link;

        if (selected == null)
            return new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.Stop, "", "no_safe_useful_link");

        return new AgentBrowserAutomationAction(
            AgentBrowserAutomationActionKind.ClickPublicLink,
            TrimUrlCandidate(selected.Href),
            "safe_useful_public_link");
    }

    static int ScoreLink(string? task, AgentBrowserSnapshotLink link)
    {
        string haystack = (link.Text + " " + link.Href).ToLowerInvariant();
        int score = PreferredLinkTerms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) * 10;

        foreach (string taskTerm in Tokenize(task))
        {
            if (haystack.Contains(taskTerm, StringComparison.OrdinalIgnoreCase))
                score += 3;
        }

        if (score == 0 && LowValueLinkTerms.Any(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) == false)
            score = 1;

        return score;
    }

    static IEnumerable<string> Tokenize(string? value) =>
        Compact(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .Select(term => term.ToLowerInvariant());

    static string Compact(string? value)
    {
        string compact = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= MaxQueryChars ? compact : compact[..MaxQueryChars].TrimEnd();
    }

    static string RemoveUrlCandidates(string? value) =>
        PublicUrlCandidate.Replace(value ?? "", " ");

    static string TrimUrlCandidate(string? value)
    {
        string candidate = (value ?? "").Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        candidate = TrimUnbalancedTrailing(candidate, '(', ')');
        candidate = TrimUnbalancedTrailing(candidate, '[', ']');
        candidate = TrimUnbalancedTrailing(candidate, '{', '}');
        return candidate;
    }

    static string TrimUnbalancedTrailing(string value, char open, char close)
    {
        while (value.EndsWith(close) && value.Count(character => character == close) > value.Count(character => character == open))
            value = value[..^1];
        return value;
    }

    static string NormalizeUrlForComparison(string? value) =>
        TrimUrlCandidate(value).TrimEnd('/');

    sealed record LinkCandidate(AgentBrowserSnapshotLink Link, string Url, int Score);
}
