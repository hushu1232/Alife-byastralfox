using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatRiskScoreServiceTests
{
    [Test]
    public void AddEventsAccumulatesScoreAndMarksLocalBlock()
    {
        QChatRiskScoreService service = new(CreateTempRoot());
        QChatRiskScoreUpdate update = service.AddEvents(
            "xiayu",
            2905391496,
            2001,
            [
                new QChatRiskEvent(QChatRiskEventKind.PromptInjection, 25, "prompt_injection"),
                new QChatRiskEvent(QChatRiskEventKind.OwnerImpersonation, 30, "owner_impersonation"),
                new QChatRiskEvent(QChatRiskEventKind.SuspiciousFileOrLink, 50, "suspicious_file_or_link"),
                new QChatRiskEvent(QChatRiskEventKind.Harassment, 60, "harassment")
            ],
            new QChatRiskThresholds(LocalBlockThreshold: 120));

        Assert.Multiple(() =>
        {
            Assert.That(update.State.Score, Is.EqualTo(165));
            Assert.That(update.State.IsLocallyBlocked, Is.True);
            Assert.That(update.CrossedLocalBlockThreshold, Is.True);
        });
    }

    [Test]
    public void ServiceReloadsPersistedRiskState()
    {
        string root = CreateTempRoot();
        QChatRiskScoreService service = new(root);
        service.AddEvents(
            "xiayu",
            2905391496,
            2001,
            [new QChatRiskEvent(QChatRiskEventKind.OwnerImpersonation, 30, "owner_impersonation")],
            new QChatRiskThresholds());

        QChatRiskScoreService reloaded = new(root);

        Assert.That(reloaded.TryGetState("xiayu", 2905391496, 2001, out QChatRiskUserState? state), Is.True);
        Assert.That(state!.Score, Is.EqualTo(30));
    }

    static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "alife-qchat-risk-score-tests", Guid.NewGuid().ToString("N"));
    }
}
