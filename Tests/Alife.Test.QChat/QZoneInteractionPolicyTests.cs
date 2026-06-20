using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QZoneInteractionPolicyTests
{
    [Test]
    public void ShouldLikeTarget_OnlyAllowsPrivateChatContactsWithinProbability()
    {
        QZoneInteractionConfig config = new()
        {
            EnableQZone = true,
            PrivateChatContactIds = "1001,1002",
            PrivateContactLikeProbability = 0.25
        };

        Assert.That(QZoneInteractionPolicy.ShouldLikeTarget(config, 1001, () => 0.10), Is.True);
        Assert.That(QZoneInteractionPolicy.ShouldLikeTarget(config, 1001, () => 0.90), Is.False);
        Assert.That(QZoneInteractionPolicy.ShouldLikeTarget(config, 2001, () => 0.10), Is.False);
    }

    [Test]
    public void ShouldReplyComment_DefaultsToMostlyReplying()
    {
        QZoneInteractionConfig config = new()
        {
            EnableQZone = true
        };

        Assert.That(QZoneInteractionPolicy.ShouldReplyComment(config, 1001, () => 0.79), Is.True);
        Assert.That(QZoneInteractionPolicy.ShouldReplyComment(config, 1001, () => 0.81), Is.False);
    }

    [Test]
    public void AllowlistRestrictsLikesAndCommentReplies()
    {
        QZoneInteractionConfig config = new()
        {
            EnableQZone = true,
            AllowedQZoneTargetIds = "1001",
            PrivateChatContactIds = "1001,1002",
            PrivateContactLikeProbability = 1.0,
            CommentReplyProbability = 1.0
        };

        Assert.That(QZoneInteractionPolicy.ShouldLikeTarget(config, 1001, () => 0.0), Is.True);
        Assert.That(QZoneInteractionPolicy.ShouldLikeTarget(config, 1002, () => 0.0), Is.False);
        Assert.That(QZoneInteractionPolicy.ShouldReplyComment(config, 1001, () => 0.0), Is.True);
        Assert.That(QZoneInteractionPolicy.ShouldReplyComment(config, 1002, () => 0.0), Is.False);
    }
}
