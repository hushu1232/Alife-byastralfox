using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatConversationFollowUpModelsTests
{
    [Test]
    public void DisabledDefaultCannotSchedule()
    {
        QChatFollowUpSettings settings = QChatFollowUpSettings.From(new QChatConfig());

        Assert.Multiple(() =>
        {
            Assert.That(settings.Enabled, Is.False);
            Assert.That(settings.CanSchedule, Is.False);
            Assert.That(settings.AllowGroups, Is.False);
            Assert.That(settings.DelayMin, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(settings.DelayMax, Is.EqualTo(TimeSpan.FromSeconds(20)));
        });
    }

    [Test]
    public void InvalidBoundsClampAndBotScopedSessionKeysDoNotCollide()
    {
        QChatFollowUpSettings settings = QChatFollowUpSettings.From(new QChatConfig
        {
            EnableConversationFollowUp = true,
            FollowUpDelayMinSeconds = 30,
            FollowUpDelayMaxSeconds = 1,
            MaxFollowUpsPerTurn = -1,
            FollowUpDailyLimitPerSession = -2
        });

        Assert.Multiple(() =>
        {
            Assert.That(settings.DelayMin, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(settings.DelayMax, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(settings.MaxFollowUpsPerTurn, Is.Zero);
            Assert.That(settings.DailyLimitPerSession, Is.Zero);
            Assert.That(QChatFollowUpSessionKey.Create("xiayu", 100, 200).Value,
                Is.Not.EqualTo(QChatFollowUpSessionKey.Create("mixu", 101, 200).Value));
        });
    }
}
