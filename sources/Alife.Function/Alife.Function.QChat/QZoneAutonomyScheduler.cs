using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public enum QZoneAutonomyReasonCode
{
    Disabled,
    Paused,
    DryRunDisabled,
    OutsidePostWindow,
    DailyPostLimitReached,
    MinimumPostInterval,
    RetryBackoff,
    NotDue,
    Due
}

public sealed record QZoneAutonomyContext(
    QZoneAutonomyAgentKey AgentKey,
    QZoneAutonomySettings Settings,
    bool Paused,
    bool IsDryRun);

public sealed record QZoneAutonomyDecision(
    QZoneAutonomyAction Action,
    QZoneAutonomyReasonCode ReasonCode);

public sealed record QZoneAutonomyState(
    QZoneAutonomyAgentKey AgentKey,
    DateTimeOffset? LastSuccessfulPostAt,
    DateTimeOffset? LastSuccessfulCommentAt,
    DateTimeOffset? NextPostCandidateAt,
    DateOnly DailyCountDate,
    int PostsToday,
    int CommentsToday,
    DateTimeOffset? CooldownUntil,
    string? LastFailureKind,
    string? LastAuditId,
    IReadOnlyList<string> ContentHashes)
{
    const int MaximumContentHashes = 8;
    const int MaximumMetadataLength = 256;

    public static QZoneAutonomyState Create(QZoneAutonomyAgentKey agentKey) =>
        new(
            agentKey,
            LastSuccessfulPostAt: null,
            LastSuccessfulCommentAt: null,
            NextPostCandidateAt: null,
            DailyCountDate: DateOnly.MinValue,
            PostsToday: 0,
            CommentsToday: 0,
            CooldownUntil: null,
            LastFailureKind: null,
            LastAuditId: null,
            ContentHashes: []);

    internal QZoneAutonomyState NormalizeForPersistence() => this with {
        PostsToday = Math.Max(0, PostsToday),
        CommentsToday = Math.Max(0, CommentsToday),
        LastFailureKind = NormalizeFailureKind(LastFailureKind),
        LastAuditId = NormalizeAuditId(LastAuditId),
        ContentHashes = (ContentHashes ?? [])
            .Where(IsSha256Hash)
            .Select(hash => hash.ToLowerInvariant())
            .TakeLast(MaximumContentHashes)
            .ToArray()
    };

    static bool IsSha256Hash(string? value)
    {
        if (value is not { Length: 64 })
            return false;

        return value.All(Uri.IsHexDigit);
    }

    internal static string? NormalizeAuditId(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumMetadataLength
            || value.All(IsSafeAuditIdCharacter) == false)
            return null;

        return value;
    }

    internal static string? NormalizeFailureKind(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumMetadataLength
            || value.All(IsSafeFailureKindCharacter) == false)
        {
            return null;
        }

        return value;
    }

    static bool IsSafeAuditIdCharacter(char value) =>
        value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.'
            or '_'
            or ':'
            or '-';

    static bool IsSafeFailureKindCharacter(char value) =>
        value is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.'
            or '_'
            or '-';
}

public sealed class QZoneAutonomyScheduler
{
    static readonly TimeSpan MinimumCandidateDelay = TimeSpan.FromHours(24);
    static readonly TimeSpan MaximumCandidateDelay = TimeSpan.FromHours(42);
    static readonly TimeSpan MissedCandidateDelay = TimeSpan.FromHours(48);
    const string MissedWindowFailureKind = "missed_window";

    readonly object syncRoot = new();
    readonly Func<DateTimeOffset> clock;
    readonly Func<double> random;
    readonly QZoneAutonomyStateStore? stateStore;
    readonly Dictionary<QZoneAutonomyAgentKey, QZoneAutonomyState> states = [];

    public QZoneAutonomyScheduler(
        Func<DateTimeOffset> clock,
        Func<double> random,
        QZoneAutonomyStateStore? stateStore = null)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.random = random ?? throw new ArgumentNullException(nameof(random));
        this.stateStore = stateStore;
    }

    public QZoneAutonomyState GetState(QZoneAutonomyAgentKey agentKey)
    {
        lock (syncRoot)
            return CreateSnapshot(GetStateUnsafe(agentKey));
    }

    public QZoneAutonomyState RecordPostSucceeded(QZoneAutonomyAgentKey agentKey, DateTimeOffset now)
    {
        lock (syncRoot)
        {
            QZoneAutonomyState current = ResetDailyCounts(GetStateUnsafe(agentKey), now);
            double sample = Math.Clamp(random(), 0d, 1d);
            TimeSpan delay = MinimumCandidateDelay + TimeSpan.FromTicks(
                (long)((MaximumCandidateDelay - MinimumCandidateDelay).Ticks * sample));
            QZoneAutonomyState updated = current with {
                LastSuccessfulPostAt = now,
                NextPostCandidateAt = now + delay,
                PostsToday = current.PostsToday + 1,
                CooldownUntil = null,
                LastFailureKind = null
            };
            SaveUnsafe(updated);
            return CreateSnapshot(GetStateUnsafe(agentKey));
        }
    }

    public QZoneAutonomyDecision EvaluatePostCandidate(QZoneAutonomyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (syncRoot)
        {
            DateTimeOffset now = clock();
            if (context.Settings.Enabled == false)
                return Skip(QZoneAutonomyReasonCode.Disabled);

            if (context.Paused)
                return Skip(QZoneAutonomyReasonCode.Paused);

            if (context.Settings.DryRunOnly && context.IsDryRun == false)
                return Skip(QZoneAutonomyReasonCode.DryRunDisabled);

            QZoneAutonomyState originalState = GetStateUnsafe(context.AgentKey);
            QZoneAutonomyState state = ResetDailyCounts(originalState, now);
            if (state.DailyCountDate != originalState.DailyCountDate)
                SaveUnsafe(state);

            if (IsWithinPostWindow(now, context.Settings) == false)
            {
                if (ShouldDeferCandidate(state, now, context.Settings))
                {
                    state = DeferCandidate(
                        state,
                        now,
                        context.Settings,
                        IsMissedCandidate(state.NextPostCandidateAt, now));
                    SaveUnsafe(state);
                }

                return Skip(QZoneAutonomyReasonCode.OutsidePostWindow);
            }

            if (state.PostsToday >= context.Settings.MaxPostsPerDay)
                return Skip(QZoneAutonomyReasonCode.DailyPostLimitReached);

            if (state.LastSuccessfulPostAt is { } lastSuccessfulPostAt
                && now - lastSuccessfulPostAt < context.Settings.PostHardMinimumInterval)
            {
                return Skip(QZoneAutonomyReasonCode.MinimumPostInterval);
            }

            if (state.CooldownUntil is { } cooldownUntil && now < cooldownUntil)
                return Skip(QZoneAutonomyReasonCode.RetryBackoff);

            if (state.NextPostCandidateAt is not { } nextCandidate || now < nextCandidate)
                return Skip(QZoneAutonomyReasonCode.NotDue);

            if (IsWithinPostWindow(nextCandidate, context.Settings) == false)
            {
                SaveUnsafe(DeferCandidate(state, now, context.Settings, missed: false));
                return Skip(QZoneAutonomyReasonCode.NotDue);
            }

            if (IsMissedCandidate(nextCandidate, now))
            {
                SaveUnsafe(DeferCandidate(state, now, context.Settings, missed: true));
                return Skip(QZoneAutonomyReasonCode.NotDue);
            }

            return new QZoneAutonomyDecision(QZoneAutonomyAction.Post, QZoneAutonomyReasonCode.Due);
        }
    }

    public QZoneAutonomyState RecordDryRunOutcome(
        QZoneAutonomyAgentKey agentKey,
        DateTimeOffset now,
        string? auditId,
        string? failureKind,
        TimeSpan? cooldown)
    {
        lock (syncRoot)
        {
            QZoneAutonomyState current = GetStateUnsafe(agentKey);
            QZoneAutonomyState updated = current with {
                LastAuditId = QZoneAutonomyState.NormalizeAuditId(auditId),
                LastFailureKind = QZoneAutonomyState.NormalizeFailureKind(failureKind),
                CooldownUntil = cooldown is { } duration && duration > TimeSpan.Zero
                    ? now + duration
                    : null
            };
            SaveUnsafe(updated);
            return CreateSnapshot(GetStateUnsafe(agentKey));
        }
    }

    QZoneAutonomyState GetStateUnsafe(QZoneAutonomyAgentKey agentKey)
    {
        if (states.TryGetValue(agentKey, out QZoneAutonomyState? state))
            return state;

        state = stateStore?.Load(agentKey) ?? QZoneAutonomyState.Create(agentKey);
        states.Add(agentKey, state);
        return state;
    }

    void SaveUnsafe(QZoneAutonomyState state)
    {
        QZoneAutonomyState safeState = state.NormalizeForPersistence();
        states[safeState.AgentKey] = safeState;
        stateStore?.Save(safeState);
    }

    QZoneAutonomyState DeferCandidate(
        QZoneAutonomyState state,
        DateTimeOffset now,
        QZoneAutonomySettings settings,
        bool missed) =>
        state with {
            NextPostCandidateAt = CreateRandomWindowCandidate(now, settings),
            LastFailureKind = missed ? MissedWindowFailureKind : state.LastFailureKind
        };

    bool ShouldDeferCandidate(QZoneAutonomyState state, DateTimeOffset now, QZoneAutonomySettings settings)
    {
        if (state.NextPostCandidateAt is not { } nextCandidate || now < nextCandidate)
            return false;

        return IsWithinPostWindow(nextCandidate, settings) == false
            || IsMissedCandidate(nextCandidate, now);
    }

    static bool IsMissedCandidate(DateTimeOffset? candidate, DateTimeOffset now) =>
        candidate is { } scheduledAt && now - scheduledAt >= MissedCandidateDelay;

    DateTimeOffset CreateRandomWindowCandidate(DateTimeOffset now, QZoneAutonomySettings settings)
    {
        DateOnly candidateDate = DateOnly.FromDateTime(now.DateTime);
        DateTimeOffset windowStart;
        DateTimeOffset windowEnd;
        TimeOnly currentTime = TimeOnly.FromDateTime(now.DateTime);
        if (currentTime < settings.PostWindowStart)
        {
            windowStart = AtLocalTime(candidateDate, settings.PostWindowStart, now.Offset);
            windowEnd = AtLocalTime(candidateDate, settings.PostWindowEnd, now.Offset);
        }
        else if (currentTime >= settings.PostWindowEnd)
        {
            candidateDate = candidateDate.AddDays(1);
            windowStart = AtLocalTime(candidateDate, settings.PostWindowStart, now.Offset);
            windowEnd = AtLocalTime(candidateDate, settings.PostWindowEnd, now.Offset);
        }
        else
        {
            windowStart = now;
            windowEnd = AtLocalTime(candidateDate, settings.PostWindowEnd, now.Offset);
        }

        if (windowEnd - windowStart <= TimeSpan.FromTicks(2))
        {
            candidateDate = candidateDate.AddDays(1);
            windowStart = AtLocalTime(candidateDate, settings.PostWindowStart, now.Offset);
            windowEnd = AtLocalTime(candidateDate, settings.PostWindowEnd, now.Offset);
        }

        long selectableTicks = (windowEnd - windowStart).Ticks - 2;
        long offsetTicks = 1 + (long)(selectableTicks * Math.Clamp(random(), 0d, 1d));
        return windowStart.AddTicks(offsetTicks);
    }

    static DateTimeOffset AtLocalTime(DateOnly date, TimeOnly time, TimeSpan offset) =>
        new(date.ToDateTime(time), offset);

    static QZoneAutonomyState CreateSnapshot(QZoneAutonomyState state) => state with {
        ContentHashes = (state.ContentHashes ?? []).ToArray()
    };

    static QZoneAutonomyState ResetDailyCounts(QZoneAutonomyState state, DateTimeOffset now)
    {
        DateOnly today = DateOnly.FromDateTime(now.DateTime);
        return state.DailyCountDate == today
            ? state
            : state with { DailyCountDate = today, PostsToday = 0, CommentsToday = 0 };
    }

    static bool IsWithinPostWindow(DateTimeOffset now, QZoneAutonomySettings settings)
    {
        TimeOnly currentTime = TimeOnly.FromDateTime(now.DateTime);
        return settings.PostWindowStart <= currentTime && currentTime < settings.PostWindowEnd;
    }

    static QZoneAutonomyDecision Skip(QZoneAutonomyReasonCode reasonCode) =>
        new(QZoneAutonomyAction.Skip, reasonCode);
}
