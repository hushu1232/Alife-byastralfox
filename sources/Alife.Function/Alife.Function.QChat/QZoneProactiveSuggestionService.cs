using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public sealed record QZoneProactiveInteraction(
    long TargetId,
    string PostId,
    string? CommentId,
    string Summary,
    bool AllowReply,
    bool AllowLike);

public class QZoneProactiveSuggestionService(
    QZoneInteractionConfig? config = null,
    QChatRelationCacheService? relationCache = null,
    Func<double>? random = null)
    : IAgentProactiveSuggestionProvider
{
    readonly QZoneInteractionConfig config = config ?? new QZoneInteractionConfig();
    readonly QChatRelationCacheService? relationCache = relationCache;
    readonly Func<double>? random = random;

    public IReadOnlyList<AgentProactiveSuggestion> BuildSuggestions(QZoneProactiveInteraction interaction)
    {
        if (config.EnableQZone == false)
            return [];

        List<AgentProactiveSuggestion> suggestions = [];
        string displayName = ResolveDisplayName(interaction.TargetId);
        string summary = string.IsNullOrWhiteSpace(interaction.Summary)
            ? "QZone activity was detected."
            : interaction.Summary.Trim();

        if (interaction.AllowReply && QZoneInteractionPolicy.ShouldReplyComment(config, interaction.TargetId, random))
        {
            suggestions.Add(new AgentProactiveSuggestion(
                AgentProactiveActionKind.QZoneReply,
                $"QZone activity from {displayName} can be answered naturally. Source: {summary}",
                AgentAuditRiskLevel.High,
                RequiresOwnerConfirmation: true,
                TargetType: "qzone",
                TargetId: interaction.TargetId,
                DraftText: BuildReplyDraft(interaction)));
        }

        if (interaction.AllowLike && QZoneInteractionPolicy.ShouldLikeTarget(config, interaction.TargetId, random))
        {
            suggestions.Add(new AgentProactiveSuggestion(
                AgentProactiveActionKind.QZoneLike,
                $"QZone post from private chat contact {displayName} is eligible for a lightweight random like.",
                AgentAuditRiskLevel.High,
                RequiresOwnerConfirmation: true,
                TargetType: "qzone",
                TargetId: interaction.TargetId,
                DraftText: $"like target={interaction.TargetId} post={interaction.PostId.Trim()}"));
        }

        return suggestions;
    }

    public IReadOnlyList<AgentProactiveSuggestion> BuildSuggestions(AgentProactiveSuggestionContext context)
    {
        List<AgentProactiveSuggestion> suggestions = [];
        foreach (Alife.Framework.LifeEvent lifeEvent in context.RecentExperiences
                     .Where(lifeEvent => lifeEvent.Source.Contains("QZone", StringComparison.OrdinalIgnoreCase)))
        {
            QZoneProactiveInteraction? interaction = TryParseInteraction(lifeEvent.Summary);
            if (interaction == null)
                continue;

            suggestions.AddRange(BuildSuggestions(interaction));
        }

        return suggestions;
    }

    string ResolveDisplayName(long targetId)
    {
        if (relationCache == null)
            return targetId.ToString();

        foreach (QChatGroupMemberCacheSnapshot snapshot in relationCache.GetCachedGroups())
        {
            OneBotGroupMember? member = snapshot.Members.FirstOrDefault(item => item.UserId == targetId);
            if (member != null)
                return member.DisplayName;
        }

        return targetId.ToString();
    }

    static string BuildReplyDraft(QZoneProactiveInteraction interaction)
    {
        string postId = interaction.PostId.Trim();
        if (string.IsNullOrWhiteSpace(interaction.CommentId))
            return $"reply target={interaction.TargetId} post={postId}";

        return $"reply target={interaction.TargetId} post={postId} comment={interaction.CommentId.Trim()}";
    }

    static QZoneProactiveInteraction? TryParseInteraction(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        long targetId = ReadLong(summary, "target", "targetId", "target_id");
        string postId = ReadToken(summary, "post", "postId", "post_id");
        if (targetId == 0 || string.IsNullOrWhiteSpace(postId))
            return null;

        string commentId = ReadToken(summary, "comment", "commentId", "comment_id");
        bool allowReply = ContainsAny(summary, "comment", "reply", "评论", "回复");
        bool allowLike = ContainsAny(summary, "posted", "post", "update", "dynamic", "动态");

        return new QZoneProactiveInteraction(
            targetId,
            postId,
            string.IsNullOrWhiteSpace(commentId) ? null : commentId,
            summary.Trim(),
            allowReply,
            allowLike);
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

    static bool ContainsAny(string text, params string[] tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
