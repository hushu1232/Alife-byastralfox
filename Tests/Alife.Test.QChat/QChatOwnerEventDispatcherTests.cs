using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatOwnerEventDispatcherTests
{
    [Test]
    public async Task FlushAsyncSendsPendingEventsAndMarksDelivered()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeOneBotRuntime runtime = new() { NextMessageId = 10 };
        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("deliver"));
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        int delivered = await dispatcher.FlushAsync();

        QChatOwnerEventEntry? stored = outbox.GetById(entry.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
            Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
            Assert.That(runtime.PrivateMessages[0].Message, Does.Contain("action=test"));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(stored.DeliveryMessageId, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task FlushAsyncKeepsEventPendingWhenRuntimeThrows()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("offline")
        };
        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("runtime-throws"));
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        int delivered = await dispatcher.FlushAsync();

        QChatOwnerEventEntry? stored = outbox.GetById(entry.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(delivered, Is.Zero);
            Assert.That(runtime.PrivateMessages, Is.Empty);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(stored.AttemptCount, Is.EqualTo(1));
            Assert.That(stored.LastError, Does.Contain("offline"));
        });
    }

    [Test]
    public async Task PublisherEnqueuesAndFlushesWithoutThrowing()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeOneBotRuntime runtime = new() { NextMessageId = 10 };
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
        QChatOwnerEventPublisher publisher = new(outbox, dispatcher);

        QChatOwnerEventEntry entry = await publisher.PublishAsync(CreateRequest("publish"));

        QChatOwnerEventEntry? stored = outbox.GetById(entry.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
            Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(stored.DeliveryMessageId, Is.EqualTo(10));
        });
    }

    static QChatOwnerEventRequest CreateRequest(string dedupeKey) => new(
        dedupeKey,
        "xiayu",
        1001,
        "info",
        "risk",
        "test",
        dedupeKey,
        "action=test");

    static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), "alife-qchat-owner-events", $"{Guid.NewGuid():N}.jsonl");

    sealed class FakeOneBotRuntime : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId { get; set; } = 999;
        public bool IsConnected { get; set; } = true;
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public List<(long Target, string Message)> PrivateMessages { get; } = new();
        public Exception? SendException { get; set; }
        public long NextMessageId { get; set; } = 1;

        public Task ConnectAsync() => Task.CompletedTask;
        public Task SendGroupMessage(long groupId, string message) => Task.CompletedTask;

        public Task SendPrivateMessage(long userId, string message)
        {
            if (SendException != null)
                throw SendException;

            PrivateMessages.Add((userId, message));
            return Task.CompletedTask;
        }

        public Task<OneBotSendMessageResult?> SendPrivateMessageWithResult(long userId, string message)
        {
            if (SendException != null)
                throw SendException;

            PrivateMessages.Add((userId, message));
            return Task.FromResult<OneBotSendMessageResult?>(new OneBotSendMessageResult { MessageId = NextMessageId++ });
        }

        public Task UploadGroupFile(long groupId, string filePath, string name) => Task.CompletedTask;
        public Task UploadPrivateFile(long userId, string filePath, string name) => Task.CompletedTask;
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => Task.FromResult<OneBotMessageEvent?>(null);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => Task.FromResult<List<OneBotForwardMessage>?>([]);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => Task.FromResult<IReadOnlyList<OneBotGroupInfo>>([]);
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId) => Task.FromResult<IReadOnlyList<OneBotGroupMember>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise(OneBotBaseEvent oneBotEvent) => EventReceived?.Invoke(oneBotEvent);
    }
}
