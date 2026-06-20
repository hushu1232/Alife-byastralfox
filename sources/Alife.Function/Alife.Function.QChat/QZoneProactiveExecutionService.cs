using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public sealed record QZoneProactiveExecutionResult(
    bool Succeeded,
    string Message,
    QZoneActionResult? ActionResult = null);

public sealed class QZoneProactiveExecutionService(
    QZoneService qZoneService,
    Func<double>? random = null,
    AgentActionGatewayService? actionGateway = null)
{
    readonly QZoneService qZoneService = qZoneService;
    readonly Func<double>? random = random;
    readonly AgentActionGatewayService actionGateway = actionGateway ?? new AgentActionGatewayService();

    public async Task<QZoneProactiveExecutionResult> ExecuteAsync(AgentProactivePendingSuggestion pending)
    {
        if (pending.Status != AgentProactivePendingStatus.Confirmed)
            return new QZoneProactiveExecutionResult(false, "Proactive suggestion must be confirmed before execution.");

        AgentProactiveSuggestion suggestion = pending.Suggestion;
        if (suggestion.TargetType?.Equals("qzone", StringComparison.OrdinalIgnoreCase) != true)
            return new QZoneProactiveExecutionResult(false, "Only qzone proactive suggestions are supported.");

        string draft = suggestion.DraftText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(draft))
            return new QZoneProactiveExecutionResult(false, "QZone proactive suggestion draft is empty.");

        try
        {
            return suggestion.Kind switch
            {
                AgentProactiveActionKind.QZoneLike => await ExecuteLikeAsync(draft),
                AgentProactiveActionKind.QZoneReply => await ExecuteReplyAsync(draft),
                _ => new QZoneProactiveExecutionResult(false, $"Unsupported QZone proactive action: {suggestion.Kind}.")
            };
        }
        catch (Exception exception)
        {
            return new QZoneProactiveExecutionResult(false, exception.Message);
        }
    }

    public async Task<QZoneProactiveExecutionResult> ExecuteAsync(
        AgentProactivePendingSuggestion pending,
        AgentPermissionRequest request,
        AgentPermissionConfig config)
    {
        AgentProactiveSuggestion suggestion = pending.Suggestion;
        AgentPermissionRequest normalizedRequest = request with
        {
            RiskLevel = ToAgentRiskLevel(suggestion.RiskLevel),
            Action = string.IsNullOrWhiteSpace(request.Action)
                ? ToActionName(suggestion.Kind)
                : request.Action.Trim()
        };

        AgentActionGatewayResult<QZoneProactiveExecutionResult> gatewayResult = await actionGateway.ExecuteAsync(
            normalizedRequest,
            config,
            () => ExecuteAsync(pending),
            detail: suggestion.DraftText ?? string.Empty);

        return gatewayResult.Executed
            ? gatewayResult.Value ?? new QZoneProactiveExecutionResult(false, gatewayResult.Message)
            : new QZoneProactiveExecutionResult(false, gatewayResult.Message);
    }

    async Task<QZoneProactiveExecutionResult> ExecuteLikeAsync(string draft)
    {
        long targetId = ReadLong(draft, "target", "targetId", "target_id");
        string postId = ReadToken(draft, "post", "postId", "post_id");
        if (targetId == 0 || string.IsNullOrWhiteSpace(postId))
            return new QZoneProactiveExecutionResult(false, "QZone like suggestion requires target and post.");

        QZoneActionResult result = await qZoneService.QZoneLike(targetId, postId, random);
        return new QZoneProactiveExecutionResult(result.Executed || result.Reason.StartsWith("dry-run:", StringComparison.OrdinalIgnoreCase),
            result.Reason,
            result);
    }

    async Task<QZoneProactiveExecutionResult> ExecuteReplyAsync(string draft)
    {
        long targetId = ReadLong(draft, "target", "targetId", "target_id");
        string postId = ReadToken(draft, "post", "postId", "post_id");
        string commentId = ReadToken(draft, "comment", "commentId", "comment_id");
        string content = ReadContent(draft);
        if (targetId == 0 || string.IsNullOrWhiteSpace(postId) || string.IsNullOrWhiteSpace(commentId))
            return new QZoneProactiveExecutionResult(false, "QZone reply suggestion requires target, post, and comment.");
        if (string.IsNullOrWhiteSpace(content))
            return new QZoneProactiveExecutionResult(false, "QZone reply suggestion requires explicit content.");

        QZoneActionResult result = await qZoneService.QZoneReplyComment(targetId, postId, commentId, content, random);
        return new QZoneProactiveExecutionResult(result.Executed || result.Reason.StartsWith("dry-run:", StringComparison.OrdinalIgnoreCase),
            result.Reason,
            result);
    }

    static long ReadLong(string text, params string[] keys)
    {
        string value = ReadToken(text, keys);
        return long.TryParse(value, out long result) ? result : 0;
    }

    static string ReadToken(string text, params string[] keys)
    {
        foreach (string key in keys)
        {
            Match match = Regex.Match(
                text,
                $@"(?:^|[\s;,]){Regex.Escape(key)}\s*[:=]\s*([^\s;,]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }

        return string.Empty;
    }

    static string ReadContent(string text)
    {
        Match quoted = Regex.Match(
            text,
            @"(?:^|[\s;,])content\s*[:=]\s*""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (quoted.Success)
            return quoted.Groups[1].Value.Trim();

        return ReadToken(text, "content");
    }

    static AgentRiskLevel ToAgentRiskLevel(AgentAuditRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            AgentAuditRiskLevel.Low => AgentRiskLevel.Low,
            AgentAuditRiskLevel.Medium => AgentRiskLevel.Medium,
            _ => AgentRiskLevel.High
        };
    }

    static string ToActionName(AgentProactiveActionKind kind)
    {
        return kind switch
        {
            AgentProactiveActionKind.QZoneLike => "qzone.like",
            AgentProactiveActionKind.QZoneReply => "qzone.reply",
            _ => $"qzone.{kind.ToString().ToLowerInvariant()}"
        };
    }
}
