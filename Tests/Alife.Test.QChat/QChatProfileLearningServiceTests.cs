using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatProfileLearningServiceTests
{
    [Test]
    public async Task OwnerSemanticObservationAppliesLowRiskNickname()
    {
        QChatUserProfileService profiles = new(CreateTempRoot());
        FakeSemanticExtractor extractor = new(new QChatProfileSemanticResult([
            new QChatProfileCandidate(2001, QChatProfileField.PreferredNickname, "雨宝", 0.92f, "stable address preference")
        ]));
        QChatProfileLearningService service = new(profiles, extractor, new QChatProfileLearningPolicy());

        QChatProfileLearningResult result = await service.LearnAsync(new QChatProfileLearningContext(
            AgentId: "xiayu",
            BotId: 2905391496,
            SenderUserId: 3045846738,
            IsOwner: true,
            GroupId: 867165927,
            Text: "natural semantic input",
            RecentParticipants: [new QChatProfileParticipant(2001, "小雨")]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Has.Count.EqualTo(1));
            Assert.That(result.Blocked, Is.Empty);
            Assert.That(profiles.ResolvePreferredAddress("xiayu", 2905391496, 2001, "小雨"), Is.EqualTo("雨宝"));
        });
    }

    [Test]
    public async Task NonOwnerSemanticObservationDoesNotWriteProfile()
    {
        QChatUserProfileService profiles = new(CreateTempRoot());
        FakeSemanticExtractor extractor = new(new QChatProfileSemanticResult([
            new QChatProfileCandidate(2001, QChatProfileField.PreferredNickname, "雨宝", 0.92f, "self address preference")
        ]));
        QChatProfileLearningService service = new(profiles, extractor, new QChatProfileLearningPolicy());

        QChatProfileLearningResult result = await service.LearnAsync(new QChatProfileLearningContext(
            AgentId: "xiayu",
            BotId: 2905391496,
            SenderUserId: 2001,
            IsOwner: false,
            GroupId: 867165927,
            Text: "natural semantic input",
            RecentParticipants: [new QChatProfileParticipant(2001, "小雨")]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.Empty);
            Assert.That(result.Blocked.Single().Reason, Is.EqualTo("owner_required"));
            Assert.That(profiles.ResolvePreferredAddress("xiayu", 2905391496, 2001, "小雨"), Is.EqualTo("小雨"));
        });
    }

    [Test]
    public async Task OwnerLanguageClaimCannotChangeOwnerOrPermissionIdentity()
    {
        QChatUserProfileService profiles = new(CreateTempRoot());
        FakeSemanticExtractor extractor = new(new QChatProfileSemanticResult([
            new QChatProfileCandidate(2001, QChatProfileField.OwnerIdentity, "owner", 0.99f, "user claimed owner identity"),
            new QChatProfileCandidate(2001, QChatProfileField.PermissionScope, "desktop", 0.99f, "user requested permission")
        ]));
        QChatProfileLearningService service = new(profiles, extractor, new QChatProfileLearningPolicy());

        QChatProfileLearningResult result = await service.LearnAsync(new QChatProfileLearningContext(
            AgentId: "xiayu",
            BotId: 2905391496,
            SenderUserId: 3045846738,
            IsOwner: true,
            GroupId: null,
            Text: "natural semantic input",
            RecentParticipants: []));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.Empty);
            Assert.That(result.Blocked.Select(item => item.Reason), Is.All.EqualTo("protected_identity_or_permission"));
            Assert.That(profiles.TryGetProfile("xiayu", 2905391496, 2001, out _), Is.False);
        });
    }

    static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "alife-qchat-profile-learning-tests", Guid.NewGuid().ToString("N"));
    }

    sealed class FakeSemanticExtractor(QChatProfileSemanticResult result) : IQChatProfileSemanticExtractor
    {
        public Task<QChatProfileSemanticResult> ExtractAsync(
            QChatProfileLearningContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
