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
    AgentWebResearchControlState? controlState = null) : IAgentWebResearchService
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

        IReadOnlyList<AgentPublicSearchResult> searchResults = search.Results;
        if (request.ActorRole == AgentWebAccessActorRole.GroupMember &&
            request.Config.AllowGroupMemberPublicFetch &&
            webAccessService != null)
        {
            string? refinement = TryBuildOfficialSupportRefinementQuery(query);
            if (refinement != null)
            {
                AgentPublicSearchResponse refinedSearch = await SearchAsync(refinement, cancellationToken);
                if (refinedSearch.Success && refinedSearch.Results.Count > 0)
                    searchResults = refinedSearch.Results;
            }
        }

        int maxSources = Math.Clamp(request.MaxSources, 1, 5);
        string? requiredSiteHost = TryGetRequiredSiteHost(query);
        AgentPublicSearchResult[] candidates = BuildCandidates(searchResults, query, maxSources, requiredSiteHost);
        if (candidates.Length == 0 && request.ActorRole == AgentWebAccessActorRole.Owner)
        {
            foreach (string expandedQuery in PlanOwnerExpandedQueries(query))
            {
                AgentPublicSearchResponse expandedSearch = await SearchAsync(expandedQuery, cancellationToken);
                if (expandedSearch.Success == false)
                    continue;

                candidates = BuildCandidates(expandedSearch.Results, query, maxSources, requiredSiteHost);
                if (candidates.Length > 0)
                    break;
            }
        }
        if (request.ActorRole == AgentWebAccessActorRole.GroupMember && request.Config.AllowGroupMemberPublicFetch)
        {
            candidates = candidates
                .OrderByDescending(result => IsTrustedOfficialCandidate(result, query))
                .ThenByDescending(GetCandidateScore)
                .ToArray();
        }
        if (candidates.Length == 0)
            return Failure("no_results", query, "没查到可靠来源。");

        List<AgentWebResearchEvidence> evidence = [];
        bool groupMemberPublicReadAttempted = false;
        foreach (AgentPublicSearchResult result in candidates)
        {
            AgentWebResearchEvidence? item;
            if (request.ActorRole == AgentWebAccessActorRole.Owner)
            {
                item = await TryReadOwnerEvidenceAsync(result, request.Config, cancellationToken);
            }
            else if (request.ActorRole == AgentWebAccessActorRole.GroupMember &&
                     request.Config.AllowGroupMemberPublicFetch &&
                     groupMemberPublicReadAttempted == false &&
                     IsTrustedOfficialCandidate(result, query))
            {
                groupMemberPublicReadAttempted = true;
                item = await TryReadGroupMemberEvidenceAsync(result, query, request.Config, cancellationToken);
            }
            else
            {
                item = BuildSearchEvidence(result);
            }

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

    async Task<AgentWebResearchEvidence> TryReadGroupMemberEvidenceAsync(
        AgentPublicSearchResult result,
        string query,
        AgentWebAccessConfig config,
        CancellationToken cancellationToken)
    {
        if (webAccessService == null)
            return BuildSearchEvidence(result);

        AgentWebAccessResponse response = await webAccessService.ExecuteAsync(new AgentWebAccessRequest(
            AgentWebAccessActorRole.GroupMember,
            AgentWebAccessCapability.PublicFetch,
            result.Url,
            config),
            cancellationToken);
        controlState.RecordRead(response.FormattedContent);
        if (response.Success == false)
            return BuildSearchEvidence(result);

        string summary = CompactRelevant(response.FormattedContent, query);
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

    static string? TryBuildOfficialSupportRefinementQuery(string query)
    {
        if (ContainsAny(
                query,
                "\u5b98\u65b9",
                "\u652f\u6301\u5468\u671f",
                "\u751f\u547d\u5468\u671f",
                "official",
                "support lifecycle",
                "end of support") == false ||
            TryExtractProductVersion(query, out string product, out string version) == false)
            return null;

        return $"\"{product} {version}\" support policy releases patches";
    }

    static bool TryExtractProductVersion(string value, out string product, out string version)
    {
        Match match = Regex.Match(
            value,
            @"(?<product>\.?[A-Za-z][A-Za-z.+#-]*?)\s*(?<version>\d+(?:\.\d+)*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        product = match.Groups["product"].Value;
        version = match.Groups["version"].Value;
        return match.Success;
    }

    static string? TryGetRequiredSiteHost(string query)
    {
        Match match = Regex.Match(
            query,
            @"(?:^|\s)site:(?<host>[A-Za-z0-9.-]+)(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success == false)
            return null;

        string host = match.Groups["host"].Value.Trim('.');
        return host.Length == 0 ? null : host.ToLowerInvariant();
    }

    AgentPublicSearchResult[] BuildCandidates(
        IReadOnlyList<AgentPublicSearchResult> results,
        string query,
        int maxSources,
        string? requiredSiteHost)
    {
        IReadOnlySet<string> relevanceTerms = BuildRelevanceTerms(query);
        return results
            .Where(result => IsUsableCandidate(result, requiredSiteHost))
            .OrderByDescending(result => GetQueryRelevanceScore(result, relevanceTerms))
            .ThenByDescending(GetCandidateScore)
            .Take(maxSources)
            .ToArray();
    }

    static IReadOnlySet<string> BuildRelevanceTerms(string query)
    {
        HashSet<string> terms = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(query, @"[A-Za-z]+|\d+|[\u4e00-\u9fff]+"))
        {
            string value = match.Value;
            if (value[0] is >= '\u4e00' and <= '\u9fff')
            {
                for (int index = 0; index + 1 < value.Length; index++)
                    terms.Add(value.Substring(index, 2));
            }
            else if (value.Length > 1 || char.IsDigit(value[0]))
            {
                terms.Add(value);
            }
        }

        if (query.Contains("\u5b98\u65b9", StringComparison.OrdinalIgnoreCase))
            terms.Add("official");
        if (query.Contains("\u652f\u6301", StringComparison.OrdinalIgnoreCase))
            terms.Add("support");
        if (query.Contains("\u652f\u6301\u5468\u671f", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("\u751f\u547d\u5468\u671f", StringComparison.OrdinalIgnoreCase))
        {
            terms.Add("lifecycle");
            terms.Add("policy");
        }
        return terms;
    }

    static int GetQueryRelevanceScore(
        AgentPublicSearchResult result,
        IReadOnlySet<string> relevanceTerms)
    {
        string candidate = $"{result.Title} {result.Snippet} {result.Url}";
        return relevanceTerms
            .Where(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Sum(term => Math.Min(term.Length, 8));
    }

    static bool IsTrustedOfficialCandidate(AgentPublicSearchResult result, string query)
    {
        if (Uri.TryCreate(result.Url, UriKind.Absolute, out Uri? uri) == false ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        string candidate = $"{result.Title} {result.Snippet} {result.Url}";
        string sourceType = InferSourceType(result.Url);
        bool hasOfficialSignal = sourceType is "docs" or "official" ||
                                 ContainsAny(result.Title, "official", "\u5b98\u65b9");
        if (hasOfficialSignal == false)
            return false;

        if (TryExtractProductVersion(query, out string product, out string version) == false)
            return true;

        string productToken = product.TrimStart('.');
        bool productMatches = candidate.Contains(productToken, StringComparison.OrdinalIgnoreCase) ||
                              uri.Host.Contains(productToken, StringComparison.OrdinalIgnoreCase);
        if (productMatches == false)
            return false;

        bool versionMatches = Regex.IsMatch(
            candidate,
            $@"(?<!\d){Regex.Escape(version)}(?![\d.])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return versionMatches ||
               ContainsAny(result.Title, "official", "\u5b98\u65b9") &&
               ContainsAny(result.Title, "policy", "\u7b56\u7565");
    }

    bool IsUsableCandidate(AgentPublicSearchResult result, string? requiredSiteHost)
    {
        if (AgentBrowserSiteExperienceStore.TryNormalizeHttpHost(result.Url, out string host) == false)
            return false;
        if (requiredSiteHost != null &&
            host.Equals(requiredSiteHost, StringComparison.OrdinalIgnoreCase) == false &&
            host.EndsWith($".{requiredSiteHost}", StringComparison.OrdinalIgnoreCase) == false)
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

    static string CompactRelevant(string? value, string query)
    {
        string text = Regex.Replace(value ?? "", @"\[UNTRUSTED EXTERNAL CONTEXT:[^\]]+\]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (TryExtractProductVersion(query, out string product, out string version))
        {
            Match match = Regex.Match(
                text,
                $@"{Regex.Escape(product)}\s*{Regex.Escape(version)}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                int rowStart = match.Index;
                int afterMatch = match.Index + match.Length;
                Match nextVersion = Regex.Match(
                    text[afterMatch..],
                    $@"{Regex.Escape(product)}\s*\d",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                int rowEnd = nextVersion.Success ? afterMatch + nextVersion.Index : text.Length;
                string row = text[rowStart..rowEnd];
                MatchCollection dates = Regex.Matches(
                    row,
                    @"(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}|\d{4}\u5e74\d{1,2}\u6708\d{1,2}\u65e5",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (dates.Count >= 2)
                    return $"{match.Value} end of support: {dates[dates.Count - 1].Value}. {Compact(row, 160)}";
                return Compact(row, 240);
            }
        }

        return Compact(text);
    }

    static string Compact(string? value, int maxLength = 140)
    {
        string text = value ?? "";
        text = Regex.Replace(text, @"\[UNTRUSTED EXTERNAL CONTEXT:[^\]]+\]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        int limit = Math.Max(1, maxLength);
        if (text.Length <= limit)
            return text;
        return text[..limit].TrimEnd() + "...";
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
