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
        QZoneAutonomyScheduler scheduler = CreateOverdueScheduler();

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

        scheduler.RecordDryRunOutcome(agentKey, Now, "audit-456", "transport-unavailable", TimeSpan.FromMinutes(30));

        QZoneAutonomyState after = scheduler.GetState(agentKey);
        Assert.Multiple(() =>
        {
            Assert.That(after.LastAuditId, Is.EqualTo("audit-456"));
            Assert.That(after.LastFailureKind, Is.EqualTo("transport-unavailable"));
            Assert.That(after.CooldownUntil, Is.EqualTo(Now.AddMinutes(30)));
            Assert.That(after.LastSuccessfulPostAt, Is.EqualTo(before.LastSuccessfulPostAt));
            Assert.That(after.NextPostCandidateAt, Is.EqualTo(before.NextPostCandidateAt));
            Assert.That(after.PostsToday, Is.EqualTo(before.PostsToday));
        });
    }

    QZoneAutonomyScheduler CreateOverdueScheduler(
        int postsToday = 0,
        DateTimeOffset? lastSuccessfulPostAt = null,
        DateTimeOffset? cooldownUntil = null)
    {
        QZoneAutonomyAgentKey agentKey = QZoneAutonomyAgentKey.Create("xiayu", 10001);
        QZoneAutonomyStateStore stateStore = new(CreateTemporaryDirectory());
        QZoneAutonomyState state = QZoneAutonomyState.Create(agentKey) with {
            DailyCountDate = DateOnly.FromDateTime(Now.DateTime),
            PostsToday = postsToday,
            LastSuccessfulPostAt = lastSuccessfulPostAt,
            NextPostCandidateAt = Now.AddHours(-48),
            CooldownUntil = cooldownUntil
        };
        stateStore.Save(state);
        return new QZoneAutonomyScheduler(() => Now, () => 0d, stateStore);
    }

    static QZoneAutonomyContext CreateContext(
        QZoneAutonomySettings? settings = null,
        bool paused = false,
        bool isDryRun = true) =>
        new(QZoneAutonomyAgentKey.Create("xiayu", 10001), settings ?? Settings(), paused, isDryRun);

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
