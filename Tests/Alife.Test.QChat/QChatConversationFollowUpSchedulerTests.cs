using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatConversationFollowUpSchedulerTests
{
    static readonly DateTimeOffset Start = new(2026, 7, 17, 12, 0, 0, TimeSpan.FromHours(8));

    [Test]
    public async Task NewInboundMessageCancelsPendingPlan()
    {
        DateTimeOffset now = Start;
        FakeFollowUpDelay delay = new();
        await using QChatConversationFollowUpScheduler scheduler = new(() => now, delay.WaitAsync);
        QChatFollowUpSessionKey key = QChatFollowUpSessionKey.Create("xiayu", 1, 1001);
        scheduler.ObserveNormalReply(key);

        Task<QChatFollowUpExecutionResult> pending = scheduler.ScheduleAsync(Request(key), () => true);
        await delay.WaitForPendingAsync();
        now = now.AddSeconds(1);
        scheduler.ObserveInbound(key);
        delay.ReleaseAll();

        QChatFollowUpExecutionResult result = await pending;
        Assert.That(result.Kind, Is.EqualTo(QChatFollowUpExecutionKind.CancelledByNewInput));
    }

    [Test]
    public async Task OneSentFollowUpConsumesTurnAndDailyQuota()
    {
        DateTimeOffset now = Start;
        FakeFollowUpDelay delay = new();
        await using QChatConversationFollowUpScheduler scheduler = new(() => now, delay.WaitAsync);
        QChatFollowUpSessionKey key = QChatFollowUpSessionKey.Create("mixu", 2, 1001);
        QChatFollowUpScheduleRequest request = Request(key, dailyLimit: 1);
        scheduler.ObserveNormalReply(key);

        Task<QChatFollowUpExecutionResult> firstPending = scheduler.ScheduleAsync(request, () => true);
        await delay.WaitForPendingAsync();
        delay.ReleaseAll();
        QChatFollowUpExecutionResult first = await firstPending;
        scheduler.Complete(key, first, sent: true);

        QChatFollowUpExecutionResult sameTurn = await scheduler.ScheduleAsync(request, () => true);
        now = now.AddMinutes(16);
        scheduler.ObserveInbound(key);
        scheduler.ObserveNormalReply(key);
        QChatFollowUpExecutionResult dailyLimit = await scheduler.ScheduleAsync(request, () => true);

        Assert.Multiple(() =>
        {
            Assert.That(first.Kind, Is.EqualTo(QChatFollowUpExecutionKind.Eligible));
            Assert.That(sameTurn.Kind, Is.EqualTo(QChatFollowUpExecutionKind.DroppedTurnLimit));
            Assert.That(dailyLimit.Kind, Is.EqualTo(QChatFollowUpExecutionKind.DroppedDailyLimit));
        });
    }

    [Test]
    public async Task RevalidationFailureDropsPlanWithoutSending()
    {
        DateTimeOffset now = Start;
        FakeFollowUpDelay delay = new();
        await using QChatConversationFollowUpScheduler scheduler = new(() => now, delay.WaitAsync);
        QChatFollowUpSessionKey key = QChatFollowUpSessionKey.Create("xiayu", 1, 1001);
        scheduler.ObserveNormalReply(key);

        Task<QChatFollowUpExecutionResult> pending = scheduler.ScheduleAsync(Request(key), () => false);
        await delay.WaitForPendingAsync();
        delay.ReleaseAll();

        QChatFollowUpExecutionResult result = await pending;
        Assert.That(result.Kind, Is.EqualTo(QChatFollowUpExecutionKind.DroppedPresence));
    }

    static QChatFollowUpScheduleRequest Request(QChatFollowUpSessionKey key, int dailyLimit = 6) => new(
        key,
        new QChatFollowUpSettings(
            Enabled: true,
            OwnerPrivateOnly: true,
            AllowGroups: false,
            DelayMin: TimeSpan.FromSeconds(1),
            DelayMax: TimeSpan.FromSeconds(1),
            MaxFollowUpsPerTurn: 1,
            SessionCooldown: TimeSpan.FromMinutes(15),
            DailyLimitPerSession: dailyLimit),
        IsOwnerPrivate: true,
        Intent: QChatFollowUpIntent.WarmCoda);

    sealed class FakeFollowUpDelay
    {
        readonly List<TaskCompletionSource> pending = [];
        readonly TaskCompletionSource pendingObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitAsync(TimeSpan _, CancellationToken cancellationToken)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(completion);
            pendingObserved.TrySetResult();
            return completion.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForPendingAsync() => pendingObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void ReleaseAll()
        {
            foreach (TaskCompletionSource completion in pending)
                completion.TrySetResult();
        }
    }
}
