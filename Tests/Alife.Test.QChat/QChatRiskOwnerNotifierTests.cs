using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatRiskOwnerNotifierTests
{
    [Test]
    public void FormatsLocalBlockReportWithMachineReadableFields()
    {
        string message = QChatRiskOwnerNotifier.FormatLocalBlockReport(new QChatRiskUserState(
            AgentId: "xiayu",
            BotId: 2905391496,
            UserId: 2001,
            Score: 125,
            EventCount: 3,
            IsLocallyBlocked: true,
            FirstSeenAt: DateTimeOffset.Parse("2026-06-21T10:00:00+08:00"),
            LastSeenAt: DateTimeOffset.Parse("2026-06-21T10:20:00+08:00"),
            Reasons: ["prompt_injection", "owner_impersonation"]));

        Assert.That(message, Does.Contain("action=local_block"));
        Assert.That(message, Does.Contain("user_id=2001"));
        Assert.That(message, Does.Contain("risk_score=125"));
        Assert.That(message, Does.Contain("reason=prompt_injection;owner_impersonation"));
    }

    [Test]
    public void FormatsFriendDeleteReportWithMachineReadableFields()
    {
        string message = QChatRiskOwnerNotifier.FormatFriendDeleteReport(
            new QChatRiskUserState(
                AgentId: "xiayu",
                BotId: 2905391496,
                UserId: 2001,
                Score: 170,
                EventCount: 3,
                IsLocallyBlocked: true,
                FirstSeenAt: DateTimeOffset.Parse("2026-06-21T10:00:00+08:00"),
                LastSeenAt: DateTimeOffset.Parse("2026-06-21T10:20:00+08:00"),
                Reasons: ["prompt_injection", "owner_impersonation"]),
            new QChatFriendDeleteResult(true, "friend_delete_action=delete_friend"));

        Assert.That(message, Does.Contain("action=delete_friend"));
        Assert.That(message, Does.Contain("result=success"));
        Assert.That(message, Does.Contain("user_id=2001"));
        Assert.That(message, Does.Contain("risk_score=170"));
    }
}
