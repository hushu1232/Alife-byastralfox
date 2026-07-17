using System.IO;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QZoneAutonomySchedulerTests
{
    static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    readonly List<string> directoriesToDelete = [];

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in directoriesToDelete)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void RecordPostSucceededSchedulesIndependentCandidatesForXiayuAndMixu()
    {
        Queue<double> samples = new([0d, 1d]);
        QZoneAutonomyScheduler scheduler = new(() => Now, () => samples.Dequeue());
        QZoneAutonomyAgentKey xiayu = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyAgentKey mixu = QZoneAutonomyAgentKey.Create("mixu", 10001);

        scheduler.RecordPostSucceeded(xiayu, Now);
        scheduler.RecordPostSucceeded(mixu, Now);

        Assert.Multiple(() =>
        {
            Assert.That(scheduler.GetState(xiayu).NextPostCandidateAt, Is.EqualTo(Now.AddHours(24)));
            Assert.That(scheduler.GetState(mixu).NextPostCandidateAt, Is.EqualTo(Now.AddHours(42)));
            Assert.That(scheduler.GetState(xiayu).NextPostCandidateAt, Is.Not.EqualTo(scheduler.GetState(mixu).NextPostCandidateAt));
            Assert.That(scheduler.GetState(xiayu).PostsToday, Is.EqualTo(1));
            Assert.That(scheduler.GetState(mixu).PostsToday, Is.EqualTo(1));
        });
    }

    [Test]
    public void RecordPostSucceededUsesUniformMidpointBetweenTwentyFourAndFortyTwoHours()
    {
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0.5d);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);

        scheduler.RecordPostSucceeded(agentKey, Now);

        Assert.That(scheduler.GetState(agentKey).NextPostCandidateAt, Is.EqualTo(Now.AddHours(33)));
    }

    [Test]
    public void DisabledBeatsAnOverdueCandidate()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler();
        QZoneAutonomyContext context = CreateContext(settings: Settings() with { Enabled = false });

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(context);

        Assert.That(decision, Is.EqualTo(new QZoneAutonomyDecision(QZoneAutonomyAction.Skip, QZoneAutonomyReasonCode.Disabled)));
    }

    [Test]
    public void PauseBeatsAnOverdueCandidate()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler();
        QZoneAutonomyContext context = CreateContext(paused: true);

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(context);

        Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.Paused));
    }

    [Test]
    public void OwnerPauseCancelsDueCandidateAndResumingDoesNotPostIt()
    {
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyStateStore stateStore = new(CreateTemporaryDirectory());
        stateStore.Save(QZoneAutonomyState.Create(agentKey) with {
            DailyCountDate = DateOnly.FromDateTime(Now.DateTime),
            NextPostCandidateAt = Now.AddMinutes(-1)
        });
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0.5d, stateStore);

        QZoneAutonomyDecision pausedDecision = scheduler.EvaluatePostCandidate(CreateContext(agentKey: agentKey, paused: true));
        QZoneAutonomyState pausedState = scheduler.GetState(agentKey);
        QZoneAutonomyScheduler resumedScheduler = new(() => Now, () => 0.5d, stateStore);
        QZoneAutonomyDecision resumedDecision = resumedScheduler.EvaluatePostCandidate(CreateContext(agentKey: agentKey));

        Assert.Multiple(() =>
        {
            Assert.That(pausedDecision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.Paused));
            Assert.That(pausedState.NextPostCandidateAt, Is.Null);
            Assert.That(pausedState.LastFailureKind, Is.EqualTo("paused"));
            Assert.That(resumedDecision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(resumedDecision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.NotDue));
            Assert.That(resumedScheduler.GetState(agentKey).PostsToday, Is.EqualTo(0));
        });
    }

    [Test]
    public void DryRunDisabledBeatsAnOverdueCandidate()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler();
        QZoneAutonomyContext context = CreateContext(settings: Settings() with { DryRunOnly = true }, isDryRun: false);

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(context);

        Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.DryRunDisabled));
    }

    [Test]
    public void OutsideWindowBeatsAnOverdueCandidate()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler();
        QZoneAutonomyContext context = CreateContext(settings: Settings() with {
            PostWindowStart = new TimeOnly(13, 0),
            PostWindowEnd = new TimeOnly(22, 30)
        });

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(context);

        Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.OutsidePostWindow));
    }

    [Test]
    public void DailyLimitBeatsAnOverdueCandidate()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler(postsToday: 2);
        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext(settings: Settings() with { MaxPostsPerDay = 2 }));

        Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.DailyPostLimitReached));
    }

    [Test]
    public void MinimumIntervalBeatsAnOverdueCandidate()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler(lastSuccessfulPostAt: Now.AddHours(-1));
        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext(settings: Settings() with {
            PostHardMinimumInterval = TimeSpan.FromHours(12)
        }));

        Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.MinimumPostInterval));
    }

    [Test]
    public void RetryBackoffBeatsAnOverdueCandidate()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler(cooldownUntil: Now.AddHours(2));

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext());

        Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.RetryBackoff));
    }

    [Test]
    public void MissingCandidateIsNotDueAndDoesNotCreateCatchUpPosting()
    {
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0d);

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext());

        Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.NotDue));
    }

    [Test]
    public void DueCandidateReturnsPostOnlyAfterHigherPriorityGatesAllowIt()
    {
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler(nextCandidateAt: Now.AddMinutes(-1));

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext());

        Assert.That(decision, Is.EqualTo(new QZoneAutonomyDecision(QZoneAutonomyAction.Post, QZoneAutonomyReasonCode.Due)));
    }

    [Test]
    public void RecordDryRunOutcomeChangesOnlyAuditFailureAndCooldownState()
    {
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0d);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        scheduler.RecordPostSucceeded(agentKey, Now.AddHours(-2));
        QZoneAutonomyState before = scheduler.GetState(agentKey);

        scheduler.RecordDryRunOutcome(
            agentKey,
            Now,
            "F47AC10B-58CC-4372-A567-0E02B2C3D479",
            "dry_run",
            TimeSpan.FromMinutes(30));

        QZoneAutonomyState after = scheduler.GetState(agentKey);
        Assert.Multiple(() =>
        {
            Assert.That(after.LastAuditId, Is.EqualTo("f47ac10b-58cc-4372-a567-0e02b2c3d479"));
            Assert.That(after.LastFailureKind, Is.EqualTo("dry_run"));
            Assert.That(after.CooldownUntil, Is.EqualTo(Now.AddMinutes(30)));
            Assert.That(after.LastSuccessfulPostAt, Is.EqualTo(before.LastSuccessfulPostAt));
            Assert.That(after.NextPostCandidateAt, Is.EqualTo(before.NextPostCandidateAt));
            Assert.That(after.PostsToday, Is.EqualTo(before.PostsToday));
        });
    }

    [Test]
    public void RecordDryRunOutcomeRejectsUnsafeAuditMetadata()
    {
        const string fakeCookie = "pt_key=opaque-cookie-value";
        const string fakePrompt = "SYSTEM PROMPT: do-not-persist";
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0d);
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);

        scheduler.RecordDryRunOutcome(
            agentKey,
            Now,
            $"audit:{fakeCookie}",
            $"transport-{fakePrompt}",
            TimeSpan.FromMinutes(30));

        QZoneAutonomyState state = scheduler.GetState(agentKey);
        Assert.Multiple(() =>
        {
            Assert.That(state.LastAuditId, Is.Null);
            Assert.That(state.LastFailureKind, Is.Null);
            Assert.That(state.CooldownUntil, Is.EqualTo(Now.AddMinutes(30)));
        });
    }

    [Test]
    public void OffWindowRawCandidatesAreIndependentlyDeferredToRandomInteriorWindowSlotsWithoutCatchUpPosting()
    {
        DateTimeOffset current = new(2026, 7, 17, 6, 0, 0, TimeSpan.Zero);
        Queue<double> samples = new([0d, 0d, 0.2d, 0.8d]);
        QZoneAutonomyScheduler scheduler = new(() => current, () => samples.Dequeue());
        QZoneAutonomyAgentKey xiayu = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyAgentKey mixu = QZoneAutonomyAgentKey.Create("mixu", 10001);
        QZoneAutonomyContext xiayuContext = CreateContext(agentKey: xiayu);
        QZoneAutonomyContext mixuContext = CreateContext(agentKey: mixu);

        scheduler.RecordPostSucceeded(xiayu, current.AddHours(-24));
        scheduler.RecordPostSucceeded(mixu, current.AddHours(-24));

        QZoneAutonomyDecision xiayuDecision = scheduler.EvaluatePostCandidate(xiayuContext);
        QZoneAutonomyDecision mixuDecision = scheduler.EvaluatePostCandidate(mixuContext);
        QZoneAutonomyState xiayuState = scheduler.GetState(xiayu);
        QZoneAutonomyState mixuState = scheduler.GetState(mixu);
        current = new DateTimeOffset(2026, 7, 17, 9, 30, 0, TimeSpan.Zero);
        QZoneAutonomyDecision openingDecision = scheduler.EvaluatePostCandidate(xiayuContext);

        Assert.Multiple(() =>
        {
            Assert.That(xiayuDecision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.OutsidePostWindow));
            Assert.That(mixuDecision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.OutsidePostWindow));
            Assert.That(xiayuState.NextPostCandidateAt, Is.GreaterThan(new DateTimeOffset(2026, 7, 17, 9, 30, 0, TimeSpan.Zero)));
            Assert.That(xiayuState.NextPostCandidateAt, Is.LessThan(new DateTimeOffset(2026, 7, 17, 22, 30, 0, TimeSpan.Zero)));
            Assert.That(mixuState.NextPostCandidateAt, Is.GreaterThan(new DateTimeOffset(2026, 7, 17, 9, 30, 0, TimeSpan.Zero)));
            Assert.That(mixuState.NextPostCandidateAt, Is.LessThan(new DateTimeOffset(2026, 7, 17, 22, 30, 0, TimeSpan.Zero)));
            Assert.That(xiayuState.NextPostCandidateAt, Is.Not.EqualTo(mixuState.NextPostCandidateAt));
            Assert.That(xiayuState.PostsToday, Is.EqualTo(0));
            Assert.That(mixuState.PostsToday, Is.EqualTo(0));
            Assert.That(openingDecision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(openingDecision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.NotDue));
        });
    }

    [Test]
    public void MissedFortyEightHourCandidateIsDeferredWithSafeReasonWithoutPostingOrResettingTodayCount()
    {
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyStateStore stateStore = new(CreateTemporaryDirectory());
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            DailyCountDate = DateOnly.FromDateTime(Now.DateTime),
            PostsToday = 1,
            LastSuccessfulPostAt = Now.AddHours(-48),
            NextPostCandidateAt = Now.AddMinutes(-1)
        };
        stateStore.Save(state);
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0.5d, stateStore);

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext(agentKey: agentKey));

        QZoneAutonomyState deferredState = scheduler.GetState(agentKey);
        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.NotDue));
            Assert.That(deferredState.PostsToday, Is.EqualTo(1));
            Assert.That(deferredState.LastFailureKind, Is.EqualTo("missed_window"));
            Assert.That(deferredState.NextPostCandidateAt, Is.GreaterThan(Now));
            Assert.That(deferredState.NextPostCandidateAt, Is.LessThan(new DateTimeOffset(2026, 7, 17, 22, 30, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void MissedSixtyOneHourOffWindowCandidateRecordsMissedWindowBeforeOriginalWindowDeferral()
    {
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyStateStore stateStore = new(CreateTemporaryDirectory());
        DateTimeOffset originalCandidate = new(2026, 7, 14, 23, 0, 0, TimeSpan.Zero);
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            DailyCountDate = DateOnly.FromDateTime(Now.DateTime),
            PostsToday = 1,
            LastSuccessfulPostAt = Now.AddHours(-61),
            NextPostCandidateAt = originalCandidate
        };
        stateStore.Save(state);
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0.5d, stateStore);

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext(agentKey: agentKey));

        QZoneAutonomyState deferredState = scheduler.GetState(agentKey);
        Assert.Multiple(() =>
        {
            Assert.That(originalCandidate, Is.EqualTo(Now.AddHours(-61)));
            Assert.That(decision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.NotDue));
            Assert.That(deferredState.PostsToday, Is.EqualTo(1));
            Assert.That(deferredState.LastFailureKind, Is.EqualTo("missed_window"));
            Assert.That(deferredState.NextPostCandidateAt, Is.GreaterThan(Now));
        });
    }

    [Test]
    public void LastSuccessfulPostOlderThanFortyEightHoursDefersDueCandidateWithoutPostingOrResettingCount()
    {
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyStateStore stateStore = new(CreateTemporaryDirectory());
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            DailyCountDate = DateOnly.FromDateTime(Now.DateTime),
            PostsToday = 1,
            LastSuccessfulPostAt = Now.AddHours(-48).AddTicks(-1),
            NextPostCandidateAt = Now.AddMinutes(-1)
        };
        stateStore.Save(state);
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0.5d, stateStore);

        QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(CreateContext(agentKey: agentKey));

        QZoneAutonomyState deferredState = scheduler.GetState(agentKey);
        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(decision.ReasonCode, Is.EqualTo(QZoneAutonomyReasonCode.NotDue));
            Assert.That(deferredState.LastFailureKind, Is.EqualTo("missed_window"));
            Assert.That(deferredState.NextPostCandidateAt, Is.GreaterThan(Now));
            Assert.That(deferredState.PostsToday, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetStateReturnsAContentHashSnapshotThatCannotMutateTheCachedState()
    {
        const string originalHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string mutatedHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyStateStore stateStore = new(CreateTemporaryDirectory());
        stateStore.Save(QZoneAutonomyState.Create(agentKey) with { ContentHashes = [originalHash] });
        QZoneAutonomyScheduler scheduler = new(() => Now, () => 0d, stateStore);

        QZoneAutonomyState exposedState = scheduler.GetState(agentKey);
        ((IList<string>)exposedState.ContentHashes)[0] = mutatedHash;

        QZoneAutonomyState laterState = scheduler.GetState(agentKey);
        Assert.That(laterState.ContentHashes, Is.EqualTo(new[] { originalHash }));
    }

    QZoneAutonomyScheduler CreateOverdueScheduler(
        int postsToday = 0,
        DateTimeOffset? lastSuccessfulPostAt = null,
        DateTimeOffset? cooldownUntil = null,
        DateTimeOffset? nextCandidateAt = null)
    {
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyStateStore stateStore = new(CreateTemporaryDirectory());
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            DailyCountDate = DateOnly.FromDateTime(Now.DateTime),
            PostsToday = postsToday,
            LastSuccessfulPostAt = lastSuccessfulPostAt,
            NextPostCandidateAt = nextCandidateAt ?? Now.AddHours(-48),
            CooldownUntil = cooldownUntil
        };
        stateStore.Save(state);
        return new QZoneAutonomyScheduler(() => Now, () => 0d, stateStore);
    }

    static QZoneAutonomyContext CreateContext(
        QZoneAutonomyAgentKey? agentKey = null,
        QZoneAutonomySettings? settings = null,
        bool paused = false,
        bool isDryRun = true) =>
        new(agentKey ?? QZoneAutonomyAgentKey.Create("xiayu", 10001), settings ?? Settings(), paused, isDryRun);

    static QZoneAutonomySettings Settings() =>
        new(
            Enabled: true,
            DryRunOnly: false,
            PostWindowStart: new TimeOnly(9, 30),
            PostWindowEnd: new TimeOnly(22, 30),
            PostHardMinimumInterval: TimeSpan.FromHours(12),
            MaxPostsPerDay: 2,
            XiayuMaxCommentsPerDay: 2,
            MixuMaxCommentsPerDay: 3);

    string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Alife.QZoneAutonomy.Tests", Guid.NewGuid().ToString("N"));
        directoriesToDelete.Add(directory);
        return directory;
    }
}
