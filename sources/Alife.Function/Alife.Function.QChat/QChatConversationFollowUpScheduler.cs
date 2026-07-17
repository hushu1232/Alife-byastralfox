using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed class QChatConversationFollowUpScheduler : IAsyncDisposable
{
    static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    readonly object gate = new();
    readonly Dictionary<QChatFollowUpSessionKey, Session> sessions = [];
    readonly Func<DateTimeOffset> now;
    readonly Func<TimeSpan, CancellationToken, Task> delayAsync;

    public QChatConversationFollowUpScheduler(
        Func<DateTimeOffset>? now = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.now = now ?? (() => DateTimeOffset.Now);
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    public void ObserveInbound(QChatFollowUpSessionKey key)
    {
        lock (gate)
        {
            DateTimeOffset timestamp = now();
            Session session = GetOrCreateSessionLocked(key, timestamp);
            session.Revision++;
            session.LastUserAt = timestamp;
            session.UpdatedAt = timestamp;
            CancelPendingLocked(session);
            TrimExpiredLocked(timestamp);
        }
    }

    public void ObserveNormalReply(QChatFollowUpSessionKey key)
    {
        lock (gate)
        {
            DateTimeOffset timestamp = now();
            Session session = GetOrCreateSessionLocked(key, timestamp);
            session.Revision++;
            session.FollowUpsForCurrentTurn = 0;
            session.LastReplyAt = timestamp;
            session.UpdatedAt = timestamp;
            CancelPendingLocked(session);
            TrimExpiredLocked(timestamp);
        }
    }

    public Task<QChatFollowUpExecutionResult> ScheduleAsync(
        QChatFollowUpScheduleRequest request,
        Func<bool> revalidate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(revalidate);

        CancellationTokenSource? cancellation = null;
        long scheduledRevision = 0;
        DateTimeOffset timestamp = now();
        lock (gate)
        {
            Session session = GetOrCreateSessionLocked(request.SessionKey, timestamp);
            ResetDailyLimitLocked(session, timestamp);
            QChatFollowUpExecutionKind? rejection = GetRejectionLocked(session, request, timestamp);
            if (rejection is { } kind)
                return Task.FromResult(Result(kind, request, session.Revision));

            cancellation = new CancellationTokenSource();
            scheduledRevision = session.Revision;
            session.PendingCancellation = cancellation;
            session.UpdatedAt = timestamp;
        }

        return WaitForEligibilityAsync(request, scheduledRevision, cancellation!, revalidate);
    }

    public void Complete(QChatFollowUpSessionKey key, QChatFollowUpExecutionResult result, bool sent)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (sent == false || result.Kind != QChatFollowUpExecutionKind.Eligible)
            return;

        lock (gate)
        {
            if (sessions.TryGetValue(key, out Session? session) == false || session.Revision != result.Revision)
                return;

            DateTimeOffset timestamp = now();
            ResetDailyLimitLocked(session, timestamp);
            session.FollowUpsForCurrentTurn++;
            session.DailyFollowUpCount++;
            session.CooldownUntil = timestamp.Add(result.Settings.SessionCooldown);
            session.UpdatedAt = timestamp;
            TrimExpiredLocked(timestamp);
        }
    }

    async Task<QChatFollowUpExecutionResult> WaitForEligibilityAsync(
        QChatFollowUpScheduleRequest request,
        long scheduledRevision,
        CancellationTokenSource cancellation,
        Func<bool> revalidate)
    {
        try
        {
            await delayAsync(SelectDelay(request.Settings), cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result(QChatFollowUpExecutionKind.CancelledByNewInput, request, scheduledRevision);
        }
        finally
        {
            cancellation.Dispose();
        }

        lock (gate)
        {
            if (sessions.TryGetValue(request.SessionKey, out Session? session) == false ||
                session.Revision != scheduledRevision ||
                ReferenceEquals(session.PendingCancellation, cancellation) == false)
            {
                return Result(QChatFollowUpExecutionKind.DroppedRevision, request, scheduledRevision);
            }

            session.PendingCancellation = null;
            session.UpdatedAt = now();
        }

        return revalidate()
            ? Result(QChatFollowUpExecutionKind.Eligible, request, scheduledRevision)
            : Result(QChatFollowUpExecutionKind.DroppedPresence, request, scheduledRevision);
    }

    static TimeSpan SelectDelay(QChatFollowUpSettings settings)
    {
        if (settings.DelayMax <= settings.DelayMin)
            return settings.DelayMin;

        double seconds = settings.DelayMin.TotalSeconds +
                         Random.Shared.NextDouble() * (settings.DelayMax - settings.DelayMin).TotalSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    static QChatFollowUpExecutionResult Result(
        QChatFollowUpExecutionKind kind,
        QChatFollowUpScheduleRequest request,
        long revision) => new(kind, request.SessionKey, request.Settings, revision);

    static QChatFollowUpExecutionKind? GetRejectionLocked(
        Session session,
        QChatFollowUpScheduleRequest request,
        DateTimeOffset timestamp)
    {
        if (request.Settings.CanSchedule == false)
            return QChatFollowUpExecutionKind.DroppedDisabled;
        if (request.IsOwnerPrivate == false || request.Intent is QChatFollowUpIntent.None or QChatFollowUpIntent.DoNotInterrupt)
            return QChatFollowUpExecutionKind.DroppedIneligible;
        if (session.PendingCancellation != null)
            return QChatFollowUpExecutionKind.DroppedPending;
        if (session.FollowUpsForCurrentTurn >= request.Settings.MaxFollowUpsPerTurn)
            return QChatFollowUpExecutionKind.DroppedTurnLimit;
        if (session.DailyFollowUpCount >= request.Settings.DailyLimitPerSession)
            return QChatFollowUpExecutionKind.DroppedDailyLimit;
        if (timestamp < session.CooldownUntil)
            return QChatFollowUpExecutionKind.DroppedCooldown;
        return null;
    }

    Session GetOrCreateSessionLocked(QChatFollowUpSessionKey key, DateTimeOffset timestamp)
    {
        if (sessions.TryGetValue(key, out Session? session))
            return session;

        session = new Session { UpdatedAt = timestamp, DailyLimitDate = DateOnly.FromDateTime(timestamp.Date) };
        sessions[key] = session;
        return session;
    }

    static void ResetDailyLimitLocked(Session session, DateTimeOffset timestamp)
    {
        DateOnly date = DateOnly.FromDateTime(timestamp.Date);
        if (session.DailyLimitDate == date)
            return;

        session.DailyLimitDate = date;
        session.DailyFollowUpCount = 0;
    }

    static void CancelPendingLocked(Session session)
    {
        CancellationTokenSource? cancellation = session.PendingCancellation;
        session.PendingCancellation = null;
        cancellation?.Cancel();
    }

    void TrimExpiredLocked(DateTimeOffset timestamp)
    {
        List<QChatFollowUpSessionKey>? expired = null;
        foreach ((QChatFollowUpSessionKey key, Session session) in sessions)
        {
            if (session.PendingCancellation == null && timestamp - session.UpdatedAt > Retention)
                (expired ??= []).Add(key);
        }

        if (expired == null)
            return;

        foreach (QChatFollowUpSessionKey key in expired)
            sessions.Remove(key);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            foreach (Session session in sessions.Values)
                CancelPendingLocked(session);
            sessions.Clear();
        }

        return ValueTask.CompletedTask;
    }

    sealed class Session
    {
        public long Revision { get; set; }
        public DateTimeOffset LastUserAt { get; set; }
        public DateTimeOffset LastReplyAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset CooldownUntil { get; set; }
        public DateOnly DailyLimitDate { get; set; }
        public int DailyFollowUpCount { get; set; }
        public int FollowUpsForCurrentTurn { get; set; }
        public CancellationTokenSource? PendingCancellation { get; set; }
    }
}
