using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class QChatContextStoreTests
{
    [Test]
    public void SearchesOnlyOlderTurnsFromTheRequestedConversation()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();

        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:private:opaque-peer-a",
            1,
            QChatConversationSpeaker.Peer,
            "空间发布频率设为两天一条",
            DateTimeOffset.Parse("2026-07-21T00:00:00Z"),
            false));
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "mixu:private:opaque-peer-b",
            1,
            QChatConversationSpeaker.Peer,
            "空间发布频率设为一天一条",
            DateTimeOffset.Parse("2026-07-21T00:00:01Z"),
            false));
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:private:opaque-peer-a",
            7,
            QChatConversationSpeaker.Self,
            "最近六条不应被回放",
            DateTimeOffset.Parse("2026-07-21T00:00:02Z"),
            false));

        QChatTopicReplayResult result = store.SearchQChatTopicReplay(new QChatTopicReplayQuery(
            "xiayu:private:opaque-peer-a",
            "空间 发布 频率",
            new HashSet<long> { 7 },
            12,
            3000));

        Assert.Multiple(() =>
        {
            Assert.That(result.Turns, Has.Count.EqualTo(1));
            Assert.That(result.Turns[0].ConversationKey, Is.EqualTo("xiayu:private:opaque-peer-a"));
            Assert.That(result.Turns[0].Sequence, Is.EqualTo(1));
            Assert.That(result.Turns[0].Content, Is.EqualTo("空间发布频率设为两天一条"));
        });
    }

    [Test]
    public void RecalledTurnCannotBeRestoredByALaterWrite()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-21T00:00:00Z");

        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "mixu:group:opaque-group-a", 4, QChatConversationSpeaker.Peer, "这条会被撤回", now, false));
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "mixu:group:opaque-group-a", 4, QChatConversationSpeaker.Peer, string.Empty, now.AddSeconds(1), true));
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "mixu:group:opaque-group-a", 4, QChatConversationSpeaker.Peer, "不能恢复原文", now.AddSeconds(2), false));

        QChatTopicReplayResult result = store.SearchQChatTopicReplay(new QChatTopicReplayQuery(
            "mixu:group:opaque-group-a", "原文", new HashSet<long>(), 12, 3000));

        Assert.That(result.Turns, Is.Empty);
    }

    [Test]
    public void DuplicateConversationSequenceRemainsOneDurableTurn()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:group:opaque-member", 10, QChatConversationSpeaker.Peer, "旧内容", now, false));
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:group:opaque-member", 10, QChatConversationSpeaker.Peer, "更新内容", now.AddSeconds(1), false));

        QChatTopicReplayResult result = store.SearchQChatTopicReplay(new QChatTopicReplayQuery(
            "xiayu:group:opaque-member", "更新内容", new HashSet<long>(), 12, 3000));

        Assert.Multiple(() =>
        {
            Assert.That(result.Turns, Has.Count.EqualTo(1));
            Assert.That(result.Turns.Single().Content, Is.EqualTo("更新内容"));
        });
    }

    [Test]
    public void RecalledSourceMessageKeyHidesOnlyTheMatchingConversation()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-21T00:00:00Z");

        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:group:opaque-thread-a", 41, QChatConversationSpeaker.Peer,
            "thread-a recalled topic", now, false, "source-thread-a-41"));
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:group:opaque-thread-b", 41, QChatConversationSpeaker.Peer,
            "thread-b stays visible", now, false, "source-thread-b-41"));

        int changed = store.MarkQChatConversationTurnsRecalled("source-thread-a-41");
        QChatTopicReplayResult threadA = store.SearchQChatTopicReplay(new QChatTopicReplayQuery(
            "xiayu:group:opaque-thread-a", "recalled topic", new HashSet<long>(), 12, 3000));
        QChatTopicReplayResult threadB = store.SearchQChatTopicReplay(new QChatTopicReplayQuery(
            "xiayu:group:opaque-thread-b", "stays visible", new HashSet<long>(), 12, 3000));

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(threadA.Turns, Is.Empty);
            Assert.That(threadB.Turns.Single().Content, Is.EqualTo("thread-b stays visible"));
        });
    }

    [Test]
    public void RedactsCredentialShapedRuntimeAuditSummary()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();

        store.RecordQChatRuntimeAudit(new QChatRuntimeAuditRecord(
            "xiayu", "status_snapshot", "recorded", "api_key=secret-value", DateTimeOffset.UtcNow));

        IReadOnlyList<QChatRuntimeAuditRecord> records = store.ReadQChatRuntimeAudit(10);

        Assert.That(records.Single().Summary, Is.EqualTo("[redacted]"));
    }

    [Test]
    public void SanitizesUrlsAndParticipantIdentifiersBeforeReplay()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "xiayu:group:opaque-participant",
            9,
            QChatConversationSpeaker.Peer,
            "[图片: https://multimedia.nt.qq.com.cn/download?fileid=x&rkey=y] @123456 [对“654321：收到”的回复]",
            DateTimeOffset.UtcNow,
            false));

        QChatConversationTurn turn = store.SearchQChatTopicReplay(new QChatTopicReplayQuery(
            "xiayu:group:opaque-participant", "图片", new HashSet<long>(), 12, 3000)).Turns.Single();

        Assert.Multiple(() =>
        {
            Assert.That(turn.Content, Does.Contain("[url-hidden]"));
            Assert.That(turn.Content, Does.Contain("@participant"));
            Assert.That(turn.Content, Does.Not.Contain("http"));
            Assert.That(turn.Content, Does.Not.Contain("rkey"));
            Assert.That(turn.Content, Does.Not.Contain("123456"));
            Assert.That(turn.Content, Does.Not.Contain("654321"));
        });
    }

    [Test]
    public void ReturnsABoundedFragmentWhenAMatchingTurnExceedsTheReplayBudget()
    {
        string databasePath = NewDatabasePath();
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();
        string content = "空间频率" + new string('字', 200);
        store.RecordQChatConversationTurn(new QChatConversationTurn(
            "mixu:private:opaque-peer-d", 1, QChatConversationSpeaker.Peer, content, DateTimeOffset.UtcNow, false));

        QChatTopicReplayResult result = store.SearchQChatTopicReplay(new QChatTopicReplayQuery(
            "mixu:private:opaque-peer-d", "空间频率", new HashSet<long>(), 12, 100));

        Assert.Multiple(() =>
        {
            Assert.That(result.Turns, Has.Count.EqualTo(1));
            Assert.That(result.Turns[0].Content, Has.Length.EqualTo(100));
            Assert.That(result.Turns[0].Content, Does.StartWith("空间频率"));
            Assert.That(result.HasOlderMatches, Is.True);
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "qchat-context-store-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }
}
