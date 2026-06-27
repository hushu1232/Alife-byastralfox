using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Alife.Function.Agent;

public sealed class AgentBrowserAutomationService(
    IAgentBrowserProvider? browserProvider = null,
    IAgentPublicSearchProvider? searchProvider = null,
    AgentBrowserSiteExperienceStore? siteExperienceStore = null)
{
    readonly IAgentBrowserProvider? browserProvider = browserProvider;
    readonly IAgentPublicSearchProvider? searchProvider = searchProvider;
    readonly AgentBrowserSiteExperienceStore? siteExperienceStore = siteExperienceStore;
    static readonly Regex UrlCandidate = new(
        @"(?:[a-z][a-z0-9+.-]*://|(?:file|javascript|data):)[^\s<>'""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<AgentBrowserAutomationResult> ExecuteAsync(
        AgentBrowserAutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AgentBrowserAutomationConfig config = request.Config ?? new AgentBrowserAutomationConfig();
        if (string.IsNullOrWhiteSpace(request.Task))
            return Failure("browser_agent_empty_task", FormatFailure("browser_agent_empty_task"), [], [], 0);
        if (HasUnsafeUrlCandidate(request.Task))
            return Failure("browser_agent_unsafe_url", FormatFailure("browser_agent_unsafe_url"), [], [], 0);

        List<AgentBrowserAutomationStep> steps = [];
        List<AgentBrowserEvidence> evidence = [];
        HashSet<string> visitedUrls = new(StringComparer.OrdinalIgnoreCase);
        int openedPageCount = 0;
        int maxSteps = Math.Max(config.MaxSteps, 1);
        int maxEvidenceItems = Math.Max(config.MaxEvidenceItems, 1);

        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(request.Task);
        for (int stepIndex = 0; stepIndex < maxSteps; stepIndex++)
        {
            AgentBrowserActionDecision decision = AgentBrowserActionPolicy.Evaluate(new AgentBrowserActionPolicyRequest(
                request.ActorRole,
                action,
                config,
                stepIndex,
                openedPageCount));
            steps.Add(new AgentBrowserAutomationStep(stepIndex, action, decision.Allowed, decision.Reason, StepUrl(action)));

            if (decision.Allowed == false)
                return Failure(decision.Reason, FormatFailure(decision.Reason), evidence, steps, openedPageCount);

            switch (action.Kind)
            {
                case AgentBrowserAutomationActionKind.Stop:
                    return Finish(evidence, steps, openedPageCount);

                case AgentBrowserAutomationActionKind.SearchPublicWeb:
                    action = await SearchToNavigationActionAsync(action.Target, config, cancellationToken);
                    if (action.Kind == AgentBrowserAutomationActionKind.Stop)
                        return Failure(action.Reason, FormatFailure(action.Reason), evidence, steps, openedPageCount);
                    continue;

                case AgentBrowserAutomationActionKind.NavigatePublicUrl:
                case AgentBrowserAutomationActionKind.ClickPublicLink:
                case AgentBrowserAutomationActionKind.ClickSamePageNavigation:
                    AgentBrowserSnapshotResult snapshotResult = await CaptureEvidenceSnapshotAsync(
                        action,
                        config,
                        cancellationToken);
                    if (snapshotResult.Result != null)
                        return snapshotResult.Result with
                        {
                            Evidence = evidence,
                            Steps = steps,
                            OpenedPageCount = openedPageCount
                        };

                    openedPageCount++;
                    AgentBrowserSnapshot snapshot = snapshotResult.Snapshot!;
                    visitedUrls.Add(action.Target);
                    if (string.IsNullOrWhiteSpace(snapshot.Url) == false)
                        visitedUrls.Add(snapshot.Url);

                    string? blockingReason = ClassifyBlockingSnapshot(snapshot);
                    siteExperienceStore?.RecordSnapshotResult(snapshot.Url, blockingReason == null, blockingReason ?? snapshot.Reason);
                    if (blockingReason != null)
                        return Failure(blockingReason, FormatFailure(blockingReason), evidence, steps, openedPageCount);

                    AgentBrowserEvidence item = CreateEvidence(snapshot, config);
                    if (string.IsNullOrWhiteSpace(item.Summary) == false && evidence.Count < maxEvidenceItems)
                        evidence.Add(item);

                    if (evidence.Count >= maxEvidenceItems)
                        return Finish(evidence, steps, openedPageCount);

                    action = AgentBrowserTaskPlanner.SelectNextLink(
                        request.Task,
                        SnapshotLinks(snapshot, config),
                        visitedUrls);
                    continue;

                default:
                    return Failure("browser_agent_action_denied", FormatFailure("browser_agent_action_denied"), evidence, steps, openedPageCount);
            }
        }

        return evidence.Count > 0
            ? new AgentBrowserAutomationResult(true, "ok", ComposeAnswer(evidence), evidence, steps, openedPageCount)
            : Failure("browser_agent_step_limit", FormatFailure("browser_agent_step_limit"), evidence, steps, openedPageCount);
    }

    async Task<AgentBrowserSnapshotResult> CaptureEvidenceSnapshotAsync(
        AgentBrowserAutomationAction action,
        AgentBrowserAutomationConfig config,
        CancellationToken cancellationToken)
    {
        if (browserProvider == null)
        {
            return new AgentBrowserSnapshotResult(
                null,
                Failure("browser_agent_runtime_unavailable", FormatFailure("browser_agent_runtime_unavailable"), [], [], 0));
        }

        AgentBrowserSnapshot snapshot;
        try
        {
            snapshot = await browserProvider.CaptureSnapshotAsync(new AgentBrowserSnapshotRequest(
                action.Target,
                MaxTextChars: Math.Max(config.MaxTextCharsPerPage, 0),
                MaxElements: Math.Max(config.MaxLinksPerPage, 0)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AgentBrowserSnapshotResult(
                null,
                Failure("browser_agent_runtime_unavailable", FormatFailure("browser_agent_runtime_unavailable"), [], [], 0));
        }

        return new AgentBrowserSnapshotResult(snapshot, null);
    }

    async Task<AgentBrowserAutomationAction> SearchToNavigationActionAsync(
        string query,
        AgentBrowserAutomationConfig config,
        CancellationToken cancellationToken)
    {
        if (searchProvider == null)
            return new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.Stop, "", "search_provider_not_configured");

        IReadOnlyList<AgentPublicSearchResult> results;
        try
        {
            results = await searchProvider.SearchAsync(
                query,
                Math.Max(config.MaxLinksPerPage, 1),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.Stop, "", "search_failed");
        }

        AgentPublicSearchResult? result = results.FirstOrDefault(item => AgentBrowserActionPolicy.IsPublicHttpUrl(item.Url));
        return result == null
            ? new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.Stop, "", "no_public_search_result")
            : new AgentBrowserAutomationAction(AgentBrowserAutomationActionKind.NavigatePublicUrl, result.Url, "search_result");
    }

    static IReadOnlyList<AgentBrowserSnapshotLink> SnapshotLinks(
        AgentBrowserSnapshot snapshot,
        AgentBrowserAutomationConfig config) =>
        (snapshot.Elements ?? [])
            .Where(element => element.Type.Equals("link", StringComparison.OrdinalIgnoreCase))
            .Where(element => string.IsNullOrWhiteSpace(element.Href) == false)
            .Take(Math.Max(config.MaxLinksPerPage, 0))
            .Select(element => new AgentBrowserSnapshotLink(element.Text, element.Href))
            .ToArray();

    static AgentBrowserEvidence CreateEvidence(
        AgentBrowserSnapshot snapshot,
        AgentBrowserAutomationConfig config)
    {
        int textLimit = Math.Max(config.MaxTextCharsPerPage, 0);
        return new AgentBrowserEvidence(
            Compact(string.IsNullOrWhiteSpace(snapshot.Title) ? snapshot.Url : snapshot.Title, 80),
            snapshot.Url,
            Compact(snapshot.Text, textLimit));
    }

    static string? ClassifyBlockingSnapshot(AgentBrowserSnapshot snapshot)
    {
        if (snapshot.Diagnostics?.LoginWallDetected == true)
            return "browser_agent_login_required";
        if (snapshot.Diagnostics?.AntiBotDetected == true)
            return "browser_agent_anti_bot_challenge";

        string reason = snapshot.Reason ?? "";
        if (ContainsAny(reason, "login", "signin", "sign in", "auth", "unauthorized", "401"))
            return "browser_agent_login_required";
        if (ContainsAny(reason, "anti_bot", "anti-bot", "captcha", "challenge", "cloudflare", "verification", "403", "forbidden"))
            return "browser_agent_anti_bot_challenge";

        return snapshot.Success ? null : "browser_agent_runtime_unavailable";
    }

    static AgentBrowserAutomationResult Finish(
        IReadOnlyList<AgentBrowserEvidence> evidence,
        IReadOnlyList<AgentBrowserAutomationStep> steps,
        int openedPageCount) =>
        evidence.Count > 0
            ? new AgentBrowserAutomationResult(true, "ok", ComposeAnswer(evidence), evidence, steps, openedPageCount)
            : Failure("browser_agent_no_reliable_evidence", FormatFailure("browser_agent_no_reliable_evidence"), evidence, steps, openedPageCount);

    static AgentBrowserAutomationResult Failure(
        string reason,
        string answer,
        IReadOnlyList<AgentBrowserEvidence> evidence,
        IReadOnlyList<AgentBrowserAutomationStep> steps,
        int openedPageCount) =>
        new(false, reason, answer, evidence, steps, openedPageCount);

    static string ComposeAnswer(IReadOnlyList<AgentBrowserEvidence> evidence)
    {
        AgentBrowserEvidence first = evidence[0];
        string sources = string.Join(" / ", evidence.Select(item => item.Url).Where(url => string.IsNullOrWhiteSpace(url) == false).Distinct());
        string lines = $"Conclusion: {first.Title}: {first.Summary}";
        return string.IsNullOrWhiteSpace(sources)
            ? lines
            : lines + Environment.NewLine + "Sources: " + sources;
    }

    static string FormatFailure(string reason) => reason switch
    {
        "browser_agent_empty_task" => "No browser task was provided.",
        "browser_agent_owner_required" => "Browser automation is owner-only.",
        "browser_agent_disabled" => "Browser automation is disabled.",
        "browser_agent_login_required" => "Cannot use that page because it requires login.",
        "browser_agent_anti_bot_challenge" => "Cannot use that page because it shows anti-bot verification.",
        "browser_agent_runtime_unavailable" => "Browser runtime is unavailable.",
        "browser_agent_step_limit" => "Browser task stopped at the step limit.",
        "browser_agent_page_limit" => "Browser task stopped at the page limit.",
        "browser_agent_unsafe_url" => "That browser target is not a safe public URL.",
        "browser_agent_no_reliable_evidence" => "No reliable browser evidence was found.",
        "search_provider_not_configured" => "Public search is not configured.",
        "search_failed" => "Public search failed.",
        "no_public_search_result" => "No public search result was found.",
        _ => "Browser automation failed."
    };

    static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    static string Compact(string? value, int maxChars)
    {
        string compact = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (maxChars <= 0)
            return "";
        return compact.Length <= maxChars ? compact : compact[..maxChars].TrimEnd() + "...";
    }

    static string? StepUrl(AgentBrowserAutomationAction action) =>
        action.Kind is AgentBrowserAutomationActionKind.NavigatePublicUrl
            or AgentBrowserAutomationActionKind.ClickPublicLink
            or AgentBrowserAutomationActionKind.ClickSamePageNavigation
            ? action.Target
            : null;

    static bool HasUnsafeUrlCandidate(string task)
    {
        MatchCollection matches = UrlCandidate.Matches(task);
        return matches.Cast<Match>().Any(match => AgentBrowserActionPolicy.IsPublicHttpUrl(TrimUrlCandidate(match.Value)) == false);
    }

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

    sealed record AgentBrowserSnapshotResult(
        AgentBrowserSnapshot? Snapshot,
        AgentBrowserAutomationResult? Result);
}
