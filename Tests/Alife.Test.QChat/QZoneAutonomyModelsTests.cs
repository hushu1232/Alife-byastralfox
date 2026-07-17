using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QZoneAutonomyModelsTests
{
    [Test]
    public void DefaultAutonomySettingsAreDisabledAndDryRunOnly()
    {
        QZoneServiceConfig config = new();
        QZoneAutonomySettings settings = QZoneAutonomySettings.From(config);

        Assert.Multiple(() =>
        {
            Assert.That(config.QZoneAutonomyPaused, Is.False);
            Assert.That(settings.Enabled, Is.False);
            Assert.That(settings.DryRunOnly, Is.True);
            Assert.That(settings.PostWindowStart, Is.EqualTo(new TimeOnly(9, 30)));
            Assert.That(settings.PostWindowEnd, Is.EqualTo(new TimeOnly(22, 30)));
            Assert.That(settings.PostHardMinimumInterval, Is.EqualTo(TimeSpan.FromHours(12)));
            Assert.That(settings.MaxPostsPerDay, Is.EqualTo(2));
            Assert.That(settings.XiayuMaxCommentsPerDay, Is.EqualTo(2));
            Assert.That(settings.MixuMaxCommentsPerDay, Is.EqualTo(3));
        });
    }

    [Test]
    public void AgentKeysKeepXiayuAndMixuStateSeparate()
    {
        Assert.That(
            QZoneAutonomyAgentKey.Create("xiayu", 100).Value,
            Is.Not.EqualTo(QZoneAutonomyAgentKey.Create("mixu", 100).Value));
    }

    [Test]
    public void MalformedAutonomySettingsFallBackToSafeLimits()
    {
        QZoneAutonomySettings settings = QZoneAutonomySettings.From(new QZoneServiceConfig {
            AutonomyPostWindowStart = "invalid",
            AutonomyPostWindowEnd = "25:00",
            AutonomyMaxPostsPerDay = 0,
            AutonomyPostMinimumIntervalHours = -1,
            XiayuAutonomyMaxCommentsPerDay = 0,
            MixuAutonomyMaxCommentsPerDay = -1
        });
        QZoneAutonomySettings invertedWindowSettings = QZoneAutonomySettings.From(new QZoneServiceConfig {
            AutonomyPostWindowStart = "22:30",
            AutonomyPostWindowEnd = "09:30"
        });

        Assert.Multiple(() =>
        {
            Assert.That(settings.PostWindowStart, Is.EqualTo(new TimeOnly(9, 30)));
            Assert.That(settings.PostWindowEnd, Is.EqualTo(new TimeOnly(22, 30)));
            Assert.That(settings.PostHardMinimumInterval, Is.EqualTo(TimeSpan.FromHours(12)));
            Assert.That(settings.MaxPostsPerDay, Is.EqualTo(2));
            Assert.That(settings.XiayuMaxCommentsPerDay, Is.EqualTo(2));
            Assert.That(settings.MixuMaxCommentsPerDay, Is.EqualTo(3));
            Assert.That(invertedWindowSettings.PostWindowStart, Is.EqualTo(new TimeOnly(9, 30)));
            Assert.That(invertedWindowSettings.PostWindowEnd, Is.EqualTo(new TimeOnly(22, 30)));
        });
    }

    [Test]
    public void ExcessiveAutonomyPostIntervalFallsBackToSafeDefault()
    {
        QZoneAutonomySettings settings = QZoneAutonomySettings.From(new QZoneServiceConfig {
            AutonomyPostMinimumIntervalHours = int.MaxValue
        });

        Assert.That(settings.PostHardMinimumInterval, Is.EqualTo(TimeSpan.FromHours(12)));
    }

    [Test]
    public void AgentKeysNormalizeNamesAndIncludeBotId()
    {
        string key = QZoneAutonomyAgentKey.Create("  XiAyU  ", 100).Value;

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo("qzone:xiayu:100"));
            Assert.That(QZoneAutonomyAgentKey.Create("xiayu", 101).Value, Is.Not.EqualTo(key));
        });
    }
}
