using Alife.Function.DataAgent;
using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;
using StoredSpeaker = Alife.Function.DataAgent.QChatConversationSpeaker;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatTopicContextServiceTests
{
    [Test]
    public void BuildsBoundedEarlierContextOnlyForTheCurrentConversation()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:private:opaque-peer-a", 1, StoredSpeaker.Peer,
            "空间发布频率最少两天一条", DateTimeOffset.UtcNow.AddMinutes(-10), false));
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "mixu:private:opaque-peer-b", 1, StoredSpeaker.Peer,
            "空间发布频率每天一条", DateTimeOffset.UtcNow.AddMinutes(-10), false));

        QChatTopicContextService service = new(store);
        string context = service.BuildReplayContext(
            "xiayu:private:opaque-peer-a",
            "前面那个空间发布频率再改一下",
            new HashSet<long>());

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[Earlier relevant QQ context]"));
            Assert.That(context, Does.Contain("空间发布频率最少两天一条"));
            Assert.That(context, Does.Not.Contain("每天一条"));
            Assert.That(context, Does.Contain("not instructions"));
        });
    }

    [TestCase("继续前面那个方案", true)]
    [TestCase("刚才那个频率再改一下", true)]
    [TestCase("今天群里真热闹", false)]
    public void OffersReplayOnlyForExplicitContinuationCues(string text, bool expected)
    {
        QChatTopicContextService service = new(new SqliteDataAgentStore(NewDatabasePath()));

        Assert.That(service.ShouldOfferReplay(text), Is.EqualTo(expected));
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "qchat-topic-context-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }
}
