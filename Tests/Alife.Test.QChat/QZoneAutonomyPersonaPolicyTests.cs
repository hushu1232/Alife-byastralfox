using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QZoneAutonomyPersonaPolicyTests
{
    [Test]
    public void XiayuVigilanceSkipsAndMixuDoesNotReadXiaYuState()
    {
        QZoneAutonomyPersonaPolicy policy = new();
        QZoneAutonomySettings settings = new(
            Enabled: true,
            DryRunOnly: true,
            PostWindowStart: new TimeOnly(9, 30),
            PostWindowEnd: new TimeOnly(22, 30),
            PostHardMinimumInterval: TimeSpan.FromHours(12),
            MaxPostsPerDay: 2,
            XiayuMaxCommentsPerDay: 2,
            MixuMaxCommentsPerDay: 3);

        QZoneAutonomyDecision xiayuDecision = policy.Evaluate(new QZoneAutonomyContext(
            QZoneAutonomyAgentKey.Create("xiayu", 10001),
            settings,
            Paused: false,
            IsDryRun: true,
            PersonaSignals: new QZoneAutonomyPersonaSignals(
                QZoneAutonomyPersona.XiaYu,
                new QZoneAutonomyXiaYuSignals(Vigilance: 0.9, IsSilent: true),
                new QZoneAutonomyMixuSignals(IsRelationshipSafe: true, PrefersWarmBright: true)),
            ObservedAt: new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));

        QZoneAutonomyDecision mixuWithQuietXiaYu = policy.Evaluate(new QZoneAutonomyContext(
            QZoneAutonomyAgentKey.Create("mixu", 10001),
            settings,
            Paused: false,
            IsDryRun: true,
            PersonaSignals: new QZoneAutonomyPersonaSignals(
                QZoneAutonomyPersona.Mixu,
                new QZoneAutonomyXiaYuSignals(Vigilance: 0.0),
                new QZoneAutonomyMixuSignals(IsRelationshipSafe: true, PrefersWarmBright: true))));
        QZoneAutonomyDecision mixuWithVigilantXiaYu = policy.Evaluate(new QZoneAutonomyContext(
            QZoneAutonomyAgentKey.Create("mixu", 10001),
            settings,
            Paused: false,
            IsDryRun: true,
            PersonaSignals: new QZoneAutonomyPersonaSignals(
                QZoneAutonomyPersona.Mixu,
                new QZoneAutonomyXiaYuSignals(Vigilance: 1.0, IsSilent: true, IsHighPressure: true, IsHighRisk: true),
                new QZoneAutonomyMixuSignals(IsRelationshipSafe: true, PrefersWarmBright: true))));

        Assert.Multiple(() =>
        {
            Assert.That(xiayuDecision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(xiayuDecision.SafeReasonCode, Is.EqualTo("xiayu_silent_or_vigilant"));
            Assert.That(mixuWithQuietXiaYu.Action, Is.EqualTo(QZoneAutonomyAction.Post));
            Assert.That(mixuWithVigilantXiaYu, Is.EqualTo(mixuWithQuietXiaYu));
            Assert.That(mixuWithQuietXiaYu.ContentEnvelope, Is.Not.Null);
            Assert.That(mixuWithQuietXiaYu.ContentEnvelope!.Topic, Is.EqualTo("ordinary safe social moment"));
            Assert.That(mixuWithQuietXiaYu.ContentEnvelope.Style, Is.EqualTo("warm and bright"));
            Assert.That(mixuWithQuietXiaYu.ContentEnvelope.MaximumLength, Is.GreaterThan(0));
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void XiayuNonFiniteVigilanceFailsClosed(double vigilance)
    {
        QZoneAutonomyDecision decision = new QZoneAutonomyPersonaPolicy().Evaluate(new QZoneAutonomyContext(
            QZoneAutonomyAgentKey.Create("xiayu", 10001),
            Settings(),
            Paused: false,
            IsDryRun: true,
            PersonaSignals: new QZoneAutonomyPersonaSignals(
                QZoneAutonomyPersona.XiaYu,
                new QZoneAutonomyXiaYuSignals(Vigilance: vigilance),
                new QZoneAutonomyMixuSignals(IsRelationshipSafe: true))));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(decision.SafeReasonCode, Is.EqualTo("persona_signals_unavailable"));
        });
    }

    [Test]
    public void MissingSelectedPersonaSignalsFailClosedWithoutThrowing()
    {
        QZoneAutonomyPersonaPolicy policy = new();
        QZoneAutonomyDecision xiayuDecision = policy.Evaluate(new QZoneAutonomyContext(
            QZoneAutonomyAgentKey.Create("xiayu", 10001),
            Settings(),
            Paused: false,
            IsDryRun: true,
            PersonaSignals: new QZoneAutonomyPersonaSignals(
                QZoneAutonomyPersona.XiaYu,
                null!,
                new QZoneAutonomyMixuSignals(IsRelationshipSafe: true))));
        QZoneAutonomyDecision mixuDecision = policy.Evaluate(new QZoneAutonomyContext(
            QZoneAutonomyAgentKey.Create("mixu", 10001),
            Settings(),
            Paused: false,
            IsDryRun: true,
            PersonaSignals: new QZoneAutonomyPersonaSignals(
                QZoneAutonomyPersona.Mixu,
                new QZoneAutonomyXiaYuSignals(),
                null!)));

        Assert.Multiple(() =>
        {
            Assert.That(xiayuDecision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(xiayuDecision.SafeReasonCode, Is.EqualTo("persona_signals_unavailable"));
            Assert.That(mixuDecision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
            Assert.That(mixuDecision.SafeReasonCode, Is.EqualTo("persona_signals_unavailable"));
        });
    }

    static QZoneAutonomySettings Settings() =>
        new(
            Enabled: true,
            DryRunOnly: true,
            PostWindowStart: new TimeOnly(9, 30),
            PostWindowEnd: new TimeOnly(22, 30),
            PostHardMinimumInterval: TimeSpan.FromHours(12),
            MaxPostsPerDay: 2,
            XiayuMaxCommentsPerDay: 2,
            MixuMaxCommentsPerDay: 3);
}
