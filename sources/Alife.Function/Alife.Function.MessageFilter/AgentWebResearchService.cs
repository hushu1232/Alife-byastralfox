using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Agent;

public sealed class AgentWebResearchService(
    AgentPublicSearchService? searchService = null,
    AgentWebAccessService? webAccessService = null,
    AgentBrowserSiteExperienceStore? siteExperienceStore = null,
    AgentWebResearchControlState? controlState = null)
{
    readonly AgentPublicSearchService? searchService = searchService;
    readonly AgentWebAccessService? webAccessService = webAccessService;
    readonly AgentBrowserSiteExperienceStore? siteExperienceStore = siteExperienceStore;
    readonly AgentWebResearchControlState controlState = controlState ?? new AgentWebResearchControlState();

    public async Task<AgentWebResearchResult> ResearchAsync(
        AgentWebResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            string query = NormalizeQuery(request.Query);
            if (query.Length == 0)
                return Failure("empty_query", query, "没查到可靠来源。");

            int maxSources = Math.Clamp(request.MaxSources, 1, 5);
            if (controlState.TryGetCachedResult(request, query, maxSources, out AgentWebResearchResult cached))
                return cached;

            if (controlState.TryEnterCooldown(request, out _) == false)
                return Failure("web_research_cooldown", query, "web_research_rate_limited: cooldown");

            if (controlState.TryAcquireConcurrency(request.Config.WebResearchMaxConcurrent, out IDisposable lease) == false)
                return Failure("web_research_busy", query, "web_research_busy: try again later");

            using (lease)
            {
                AgentWebResearchResult result = await ResearchCoreAsync(request, cancellationToken);
                if (result.Success)
                {
                    controlState.RecordSummaryText(result.Answer);
                    controlState.StoreCachedResult(request, query, maxSources, result);
                }

                return result;
            }
        }
        finally
        {
            controlState.RecordLatency(stopwatch.Elapsed);
        }
    }

    async Task<AgentWebResearchResult> ResearchCoreAsync(
        AgentWebResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string query = NormalizeQuery(request.Query);
        if (query.Length == 0)
                return Failure("empty_query", query, "没查到可靠来源。");

        if (searchService == null)
            return Failure("public_search_not_configured", query, "搜索现在不可用。");

        AgentPublicSearchResponse search = await SearchAsync(query, cancellationToken);
        if (search.Success == false)
            return Failure(search.Reason, query, "搜索失败，先不乱说。");

        int maxSources = Math.Clamp(request.MaxSources, 1, 5);
        AgentPublicSearchResult[] candidates = BuildCandidates(search.Results, maxSources);
        if (candidates.Length == 0 && request.ActorRole == AgentWebAccessActorRole.Owner)
        {
            foreach (string expandedQuery in PlanOwnerExpandedQueries(query))
            {
                AgentPublicSearchResponse expandedSearch = await SearchAsync(expandedQuery, cancellationToken);
                if (expandedSearch.Success == false)
                    continue;

                candidates = BuildCandidates(expandedSearch.Results, maxSources);
                if (candidates.Length > 0)
                    break;
            }
        }
        if (candidates.Length == 0)
            return Failure("no_results", query, "没查到可靠来源。");

        List<AgentWebResearchEvidence> evidence = [];
        foreach (AgentPublicSearchResult result in candidates)
        {
            AgentWebResearchEvidence? item = request.ActorRole == AgentWebAccessActorRole.Owner
                ? await TryReadOwnerEvidenceAsync(result, request.Config, cancellationToken)
                : BuildSearchEvidence(result);
            if (item != null)
                evidence.Add(item);
        }

        if (evidence.Count == 0)
            return Failure("no_readable_results", query, "查到了结果，但没有可用的公开内容。");

        string answer = ComposeAnswer(evidence);
        return new AgentWebResearchResult(true, "ok", query, answer, evidence);
    }

    async Task<AgentPublicSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        controlState.RecordSearch();
        return await searchService!.SearchAsync(query, cancellationToken);
    }

    async Task<AgentWebResearchEvidence?> TryReadOwnerEvidenceAsync(
        AgentPublicSearchResult result,
        AgentWebAccessConfig config,
        CancellationToken cancellationToken)
    {
        if (webAccessService == null)
            return BuildSearchEvidence(result);

        AgentBrowserSiteExperience? experience = GetSiteExperience(result.Url);
        if (experience is { HasAntiBotSignals: true })
            return BuildSearchEvidence(result);

        AgentWebAccessResponse response = await webAccessService.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.Owner,
            AgentWebAccessCapability.AutoRead,
            result.Url,
            config),
            cancellationToken);
        controlState.RecordRead(response.FormattedContent);
        if (response.Success == false)
            return BuildSearchEvidence(result);

        string summary = Compact(response.FormattedContent);
        if (summary.Length == 0)
            summary = Compact(result.Snippet);

        return new AgentWebResearchEvidence(
            CleanOneLine(result.Title),
            result.Url,
            summary,
            InferSourceType(result.Url));
    }

    static AgentWebResearchEvidence BuildSearchEvidence(AgentPublicSearchResult result)
    {
        string summary = Compact(result.Snippet);
        if (summary.Length == 0)
            summary = "搜索结果没有提供摘要。";
        return new AgentWebResearchEvidence(
            CleanOneLine(result.Title),
            result.Url,
            summary,
            InferSourceType(result.Url));
    }

    static AgentWebResearchResult Failure(string reason, string query, string answer) =>
        new(false, reason, query, answer, []);

    static string ComposeAnswer(IReadOnlyList<AgentWebResearchEvidence> evidence)
    {
        AgentWebResearchEvidence first = evidence[0];
        string conclusion = $"结论：先看 {first.Title}，核心信息是：{first.Summary}";
        IEnumerable<string> lines = evidence
            .Take(3)
            .Select((item, index) => $"{index + 1}. {item.Title}：{item.Summary}");
        string sources = "来源：" + string.Join(" / ", evidence.Take(3).Select(item => $"{item.Title} {item.Url}"));
        return string.Join(Environment.NewLine, [conclusion, .. lines, sources]);
    }

    static string NormalizeQuery(string? query)
    {
        return Regex.Replace((query ?? "").Trim(), @"\s+", " ");
    }

    AgentPublicSearchResult[] BuildCandidates(
        IReadOnlyList<AgentPublicSearchResult> results,
        int maxSources)
    {
        return results
            .Where(IsUsableCandidate)
            .OrderByDescending(GetCandidateScore)
            .Take(maxSources)
            .ToArray();
    }

    bool IsUsableCandidate(AgentPublicSearchResult result)
    {
        if (AgentBrowserSiteExperienceStore.TryNormalizeHttpHost(result.Url, out _) == false)
            return false;

        AgentBrowserSiteExperience? experience = GetSiteExperience(result.Url);
        return experience?.PreferredStrategy != AgentBrowserSiteStrategy.Blocked;
    }

    int GetCandidateScore(AgentPublicSearchResult result)
    {
        int score = GetSourceTrustScore(result);
        AgentBrowserSiteExperience? experience = GetSiteExperience(result.Url);
        if (experience == null)
            return score;

        if (experience.LastSuccess)
            score += 8;
        if (experience.HasAntiBotSignals)
            score -= 25;
        if (experience.RiskLevel == AgentBrowserSiteRiskLevel.Medium)
            score -= 10;
        if (experience.RiskLevel == AgentBrowserSiteRiskLevel.High)
            score -= 40;
        return score;
    }

    AgentBrowserSiteExperience? GetSiteExperience(string url)
    {
        if (siteExperienceStore == null)
            return null;

        return AgentBrowserSiteExperienceStore.TryNormalizeHttpHost(url, out string host)
            ? siteExperienceStore.Get(host)
            : null;
    }

    static IEnumerable<string> PlanOwnerExpandedQueries(string query)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { query };
        foreach (string plannedQuery in PlanIntentAwareQueries(query)
                     .Concat(PlanGenericFallbackQueries(query)))
        {
            string normalized = NormalizeQuery(plannedQuery);
            if (normalized.Length == 0 || seen.Add(normalized) == false)
                continue;

            yield return normalized;
        }
    }

    static IEnumerable<string> PlanIntentAwareQueries(string query)
    {
        string? exactErrorQuery = TryBuildExactErrorQuery(query);
        if (exactErrorQuery != null)
            yield return exactErrorQuery;

        if (IsFreshnessQuery(query))
            yield return $"{query} latest release notes";

        string? englishTechnicalQuery = TryBuildEnglishTechnicalQuery(query);
        if (englishTechnicalQuery != null)
            yield return englishTechnicalQuery;
    }

    static IEnumerable<string> PlanGenericFallbackQueries(string query)
    {
        yield return $"official docs {query}";
        yield return $"github {query}";
        yield return $"release notes {query}";
    }

    static string? TryBuildExactErrorQuery(string query)
    {
        Match httpStatus = Regex.Match(
            query,
            @"\bHTTP\s+\d{3}\s+[A-Za-z]+(?:\s+[A-Za-z]+){0,2}\b",
            RegexOptions.IgnoreCase);
        if (httpStatus.Success)
        {
            List<string> parts = [$"\"{httpStatus.Value}\""];
            foreach (Match token in Regex.Matches(query, @"\b[A-Za-z]+(?:-[A-Za-z0-9]+)+\b"))
            {
                string value = token.Value;
                if (httpStatus.Value.Contains(value, StringComparison.OrdinalIgnoreCase))
                    continue;

                parts.Add(value);
            }

            return string.Join(" ", parts);
        }

        Match exception = Regex.Match(
            query,
            @"\b[A-Za-z][A-Za-z0-9_.]+(?:Exception|Error)\b",
            RegexOptions.IgnoreCase);
        return exception.Success ? $"\"{exception.Value}\"" : null;
    }

    static bool IsFreshnessQuery(string query)
    {
        return ContainsAny(
            query,
            "\u6700\u65b0",
            "\u53d1\u5e03\u65e5\u671f",
            "\u53d1\u5e03",
            "\u7248\u672c",
            "\u65b0\u95fb",
            "\u66f4\u65b0",
            "latest",
            "current",
            "release",
            "released",
            "version",
            "news",
            "changelog");
    }

    static string? TryBuildEnglishTechnicalQuery(string query)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["\u6d4f\u89c8\u5668"] = "browser",
            ["\u7f51\u9875"] = "web",
            ["\u8054\u7f51"] = "web",
            ["\u641c\u7d22"] = "search",
            ["\u81ea\u52a8\u8bfb\u53d6"] = "auto read",
            ["\u8bfb\u53d6"] = "read",
            ["\u53cd\u722c"] = "anti bot",
            ["\u9a8c\u8bc1\u7801"] = "captcha",
            ["\u767b\u5f55\u5899"] = "login wall",
            ["\u77e5\u8bc6\u5e93"] = "knowledge base",
            ["\u5916\u90e8\u77e5\u8bc6\u5e93"] = "external knowledge base",
            ["\u622a\u56fe"] = "snapshot",
            ["\u6458\u8981"] = "summary",
            ["\u6765\u6e90"] = "source",
            ["\u4ee4\u724c"] = "token",
            ["\u8282\u7701"] = "saving"
        };

        List<string> terms = [];
        foreach ((string chinese, string english) in map)
        {
            if (chinese == "\u8bfb\u53d6" && query.Contains("\u81ea\u52a8\u8bfb\u53d6", StringComparison.OrdinalIgnoreCase))
                continue;

            if (query.Contains(chinese, StringComparison.OrdinalIgnoreCase))
                terms.Add(english);
        }

        if (terms.Count == 0)
            return null;

        return string.Join(" ", terms.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    static string Compact(string? value)
    {
        string text = value ?? "";
        text = Regex.Replace(text, @"\[UNTRUSTED EXTERNAL CONTEXT:[^\]]+\]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length <= 140)
            return text;
        return text[..140].TrimEnd() + "...";
    }

    static string CleanOneLine(string? value)
    {
        string text = Regex.Replace((value ?? "").Trim(), @"\s+", " ");
        return text.Length == 0 ? "\u672a\u547d\u540d\u6765\u6e90" : text;
    }

    static string InferSourceType(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) == false)
            return "unknown";

        string host = uri.Host.ToLowerInvariant();
        if (host.Contains("github.com", StringComparison.Ordinal))
            return "github";
        if (host.Contains("docs.", StringComparison.Ordinal) || host.Contains("learn.microsoft.com", StringComparison.Ordinal))
            return "docs";
        if (host.EndsWith(".gov", StringComparison.Ordinal) || host.EndsWith(".edu", StringComparison.Ordinal))
            return "official";
        return "web";
    }

    static int GetSourceTrustScore(AgentPublicSearchResult result)
    {
        return InferSourceType(result.Url) switch
        {
            "official" => 40,
            "docs" => 35,
            "github" => 30,
            _ => 10
        };
    }
}
