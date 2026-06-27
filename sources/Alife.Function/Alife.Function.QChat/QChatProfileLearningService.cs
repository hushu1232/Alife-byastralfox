using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed record QChatProfileLearningContext(
    string AgentId,
    long BotId,
    long SenderUserId,
    bool IsOwner,
    long? GroupId,
    string Text,
    IReadOnlyList<QChatProfileParticipant> RecentParticipants);

public sealed record QChatProfileLearningAppliedUpdate(
    long TargetUserId,
    QChatProfileField Field,
    string Value,
    float Confidence,
    string Evidence);

public sealed record QChatProfileLearningBlockedUpdate(
    long TargetUserId,
    QChatProfileField Field,
    string Value,
    string Reason,
    string Evidence);

public sealed record QChatProfileLearningResult(
    IReadOnlyList<QChatProfileLearningAppliedUpdate> Applied,
    IReadOnlyList<QChatProfileLearningBlockedUpdate> Blocked);

public sealed class QChatProfileLearningService(
    QChatUserProfileService profiles,
    IQChatProfileSemanticExtractor extractor,
    QChatProfileLearningPolicy policy)
{
    const string SemanticProfileLearningSource = "semantic-profile-learning";

    public async Task<QChatProfileLearningResult> LearnAsync(
        QChatProfileLearningContext context,
        CancellationToken cancellationToken = default)
    {
        QChatProfileSemanticResult semanticResult = await extractor.ExtractAsync(context, cancellationToken);
        List<QChatProfileLearningAppliedUpdate> applied = [];
        List<QChatProfileLearningBlockedUpdate> blocked = [];

        foreach (QChatProfileCandidate candidate in semanticResult.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QChatProfilePolicyDecision decision = policy.Evaluate(context, candidate);
            if (decision.CanApply == false)
            {
                blocked.Add(new QChatProfileLearningBlockedUpdate(
                    candidate.TargetUserId,
                    candidate.Field,
                    candidate.Value,
                    decision.Reason,
                    candidate.Evidence));
                continue;
            }

            ApplyCandidate(context, candidate);
            applied.Add(new QChatProfileLearningAppliedUpdate(
                candidate.TargetUserId,
                candidate.Field,
                candidate.Value,
                candidate.Confidence,
                candidate.Evidence));
        }

        return new QChatProfileLearningResult(applied, blocked);
    }

    void ApplyCandidate(QChatProfileLearningContext context, QChatProfileCandidate candidate)
    {
        profiles.TryGetProfile(context.AgentId, context.BotId, candidate.TargetUserId, out QChatUserProfile? existing);
        QChatUserProfile profile = existing ?? new QChatUserProfile(candidate.TargetUserId);

        profile = candidate.Field switch
        {
            QChatProfileField.PreferredNickname => profile with { PreferredNickname = candidate.Value },
            QChatProfileField.CuteNickname => profile with { CuteNicknames = AddDistinct(profile.CuteNicknames, candidate.Value) },
            QChatProfileField.FormalName => profile with { FormalName = candidate.Value },
            QChatProfileField.RelationshipLabel => profile with { RelationshipLabel = candidate.Value },
            QChatProfileField.AddressStyle => profile with { AddressStyle = candidate.Value },
            QChatProfileField.Notes => profile with { Notes = AppendNote(profile.Notes, candidate.Value) },
            _ => profile
        };

        profile = profile with
        {
            AgentId = context.AgentId,
            BotId = context.BotId,
            Source = MergeSource(profile.Source),
            Confidence = Math.Max(profile.Confidence, candidate.Confidence),
            LastSeenGroupId = context.GroupId,
            LastSeenAt = DateTimeOffset.Now
        };

        profiles.SetProfile(context.AgentId, context.BotId, profile);
    }

    static IReadOnlyList<string> AddDistinct(IReadOnlyList<string>? values, string value)
    {
        string trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return values ?? [];

        return (values ?? [])
            .Append(trimmed)
            .Where(item => string.IsNullOrWhiteSpace(item) == false)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    static string AppendNote(string? existing, string value)
    {
        string trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return existing?.Trim() ?? "";

        string current = existing?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(current))
            return trimmed;

        return current.Contains(trimmed, StringComparison.Ordinal)
            ? current
            : $"{current}{Environment.NewLine}{trimmed}";
    }

    static string MergeSource(string? existing)
    {
        string current = existing?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(current))
            return SemanticProfileLearningSource;

        if (current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(SemanticProfileLearningSource, StringComparer.Ordinal))
            return current;

        return $"{current};{SemanticProfileLearningSource}";
    }
}
