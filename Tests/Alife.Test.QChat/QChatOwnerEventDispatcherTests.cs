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
            Assert.That(runtime.PrivateMessages[0].Message, Is.Not.EqualTo("action=test result=success"));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(stored.DeliveryMessageId, Is.EqualTo(10));
        });
    }

    [Test]
    public void OwnerEventRecordsPreserveOriginalPositionalContract()
    {
        QChatOwnerEventRequest request = new(
            "request-dedupe", "xiayu", 1001, "info", "engineering", "test", "source-id", "request-message");
        QChatOwnerEventEntry entry = new(
            "event-id", "entry-dedupe", "xiayu", 1001, "info", "engineering", "test", "source-id",
            "entry-message", QChatOwnerEventStatus.Pending, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            0, null, null, null);

        var (requestDedupeKey, requestAgentId, requestOwnerId, requestSeverity, requestCategory, requestSource,
            requestSourceId, requestMessage) = request;
        var (entryEventId, entryDedupeKey, entryAgentId, entryOwnerId, entrySeverity, entryCategory, entrySource,
            entrySourceId, entryMessage, entryStatus, entryCreatedAt, entryNextAttemptAt, entryAttemptCount,
            entryDeliveredAt, entryDeliveryMessageId, entryLastError) = entry;

        Assert.Multiple(() =>
        {
            Assert.That(requestDedupeKey, Is.EqualTo("request-dedupe"));
            Assert.That(requestAgentId, Is.EqualTo("xiayu"));
            Assert.That(requestOwnerId, Is.EqualTo(1001));
            Assert.That(requestSeverity, Is.EqualTo("info"));
            Assert.That(requestCategory, Is.EqualTo("engineering"));
            Assert.That(requestSource, Is.EqualTo("test"));
            Assert.That(requestSourceId, Is.EqualTo("source-id"));
            Assert.That(requestMessage, Is.EqualTo("request-message"));
            Assert.That(entryEventId, Is.EqualTo("event-id"));
            Assert.That(entryDedupeKey, Is.EqualTo("entry-dedupe"));
            Assert.That(entryAgentId, Is.EqualTo("xiayu"));
            Assert.That(entryOwnerId, Is.EqualTo(1001));
            Assert.That(entrySeverity, Is.EqualTo("info"));
            Assert.That(entryCategory, Is.EqualTo("engineering"));
            Assert.That(entrySource, Is.EqualTo("test"));
            Assert.That(entrySourceId, Is.EqualTo("source-id"));
            Assert.That(entryMessage, Is.EqualTo("entry-message"));
            Assert.That(entryStatus, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(entryCreatedAt, Is.EqualTo(DateTimeOffset.UnixEpoch));
            Assert.That(entryNextAttemptAt, Is.EqualTo(DateTimeOffset.UnixEpoch));
            Assert.That(entryAttemptCount, Is.Zero);
            Assert.That(entryDeliveredAt, Is.Null);
            Assert.That(entryDeliveryMessageId, Is.Null);
            Assert.That(entryLastError, Is.Null);
        });
    }

    [Test]
    public async Task FlushAsyncFormatsTypedEngineeringEventWithoutChangingGenericEvents()
    {
        QChatOwnerEventOutbox outbox = new(CreateTempPath());
        FakeOneBotRuntime runtime = new() { NextMessageId = 10 };
        QChatOwnerEventEntry genericEntry = outbox.Enqueue(CreateRequest("generic") with { Category = "engineering" });
        QChatOwnerEventEntry engineeringEntry = outbox.Enqueue(new QChatOwnerEventRequest(
            DedupeKey: "engineering", AgentId: "xiayu", OwnerId: 1001,
            Severity: "info", Category: "engineering", Source: "test", SourceId: "engineering",
            Message: "generic-message-must-not-be-sent")
        {
            EngineeringReply = new QChatOwnerEngineeringReply(
                QChatOwnerEngineeringReplyStage.Blocked,
                "checked=qchat-owner-event-dispatcher",
                "tests=not-run",
                "missing_evidence=correlation-id")
        });
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        int delivered = await dispatcher.FlushAsync();

        string genericMessage = runtime.PrivateMessages.Single(message =>
            message.Message.Contains("action=test result=success", StringComparison.Ordinal)).Message;
        string engineeringMessage = runtime.PrivateMessages.Single(message =>
            message.Message.Contains("checked=qchat-owner-event-dispatcher", StringComparison.Ordinal)).Message;
        Assert.Multiple(() =>
        {
            Assert.That(delivered, Is.EqualTo(2));
            Assert.That(genericMessage, Does.StartWith("术术，我看过了。"));
            Assert.That(engineeringMessage, Does.StartWith("术术，"));
            Assert.That(engineeringMessage, Does.Contain("tests=not-run"));
            Assert.That(engineeringMessage, Does.Contain("missing_evidence=correlation-id"));
            Assert.That(engineeringMessage, Does.Not.Contain("generic-message-must-not-be-sent"));
            Assert.That(outbox.GetById(genericEntry.EventId)!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(outbox.GetById(engineeringEntry.EventId)!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
        });
    }

    [Test]
    public async Task FlushAsyncPreservesTypedEngineeringPayloadAcrossReloadAndRetry()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry entry = outbox.Enqueue(new QChatOwnerEventRequest(
            "typed-reload", "xiayu", 1001, "info", "engineering", "test", "typed-reload",
            "raw-message-placeholder-must-not-be-sent")
        {
            EngineeringReply = new QChatOwnerEngineeringReply(
                QChatOwnerEngineeringReplyStage.Blocked,
                "checked=qchat-owner-event-dispatcher-reload",
                "tests=not-run",
                "missing_evidence=correlation-id")
        });
        FakeOneBotRuntime failingRuntime = new()
        {
            SendException = new InvalidOperationException("offline")
        };

        int firstDelivered = await new QChatOwnerEventDispatcher(outbox, () => failingRuntime).FlushAsync();

        QChatOwnerEventOutbox reloadedOutbox = new(path);
        FakeOneBotRuntime successfulRuntime = new() { NextMessageId = 10 };
        int retryDelivered = await new QChatOwnerEventDispatcher(reloadedOutbox, () => successfulRuntime)
            .FlushAsync(includeScheduled: true);

        string deliveredMessage = successfulRuntime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(firstDelivered, Is.Zero);
            Assert.That(retryDelivered, Is.EqualTo(1));
            Assert.That(deliveredMessage, Does.Contain("checked=qchat-owner-event-dispatcher-reload"));
            Assert.That(deliveredMessage, Does.Contain("tests=not-run"));
            Assert.That(deliveredMessage, Does.Contain("missing_evidence=correlation-id"));
            Assert.That(deliveredMessage, Does.Not.Contain("raw-message-placeholder-must-not-be-sent"));
            Assert.That(reloadedOutbox.GetById(entry.EventId)!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
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
    public void FlushAsyncPropagatesCancellationWithoutMarkingFailed()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeOneBotRuntime runtime = new()
        {
            SendException = new OperationCanceledException("cancelled")
        };
        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("runtime-cancelled"));
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        Assert.ThrowsAsync<OperationCanceledException>(() => dispatcher.FlushAsync());

        QChatOwnerEventEntry? stored = outbox.GetById(entry.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.SendAttemptCount, Is.EqualTo(1));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(stored.AttemptCount, Is.Zero);
            Assert.That(stored.LastError, Is.Null);
        });
    }

    [Test]
    public async Task FlushAsyncReturnsZeroWhenAlreadyFlushing()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeOneBotRuntime runtime = new()
        {
            BlockSend = true,
            NextMessageId = 10
        };
        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("overlap"));
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        Task<int> firstFlush = dispatcher.FlushAsync();
        await runtime.WaitForSendStartedAsync();
        int secondDelivered = await dispatcher.FlushAsync();
        runtime.ReleaseBlockedSend();
        int firstDelivered = await firstFlush;

        QChatOwnerEventEntry? stored = outbox.GetById(entry.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(secondDelivered, Is.Zero);
            Assert.That(firstDelivered, Is.EqualTo(1));
            Assert.That(runtime.SendAttemptCount, Is.EqualTo(1));
            Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
        });
    }

    [Test]
    public async Task ForcedFlushWaitsForCurrentFlushAndSendsScheduledPendingEvents()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeOneBotRuntime runtime = new()
        {
            BlockSend = true,
            NextMessageId = 10
        };
        QChatOwnerEventEntry blockingEntry = outbox.Enqueue(CreateRequest("blocking-flush"));
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        Task<int> ordinaryFlush = dispatcher.FlushAsync();
        await runtime.WaitForSendStartedAsync();
        QChatOwnerEventEntry scheduledEntry = outbox.Enqueue(CreateRequest("scheduled-force"));
        outbox.MarkFailed(scheduledEntry.EventId, "offline", DateTimeOffset.UtcNow);

        Task<int> forcedFlush = dispatcher.FlushAsync(includeScheduled: true);
        await Task.Delay(50);
        Assert.That(forcedFlush.IsCompleted, Is.False);

        runtime.ReleaseBlockedSend();
        int ordinaryDelivered = await ordinaryFlush;
        int forcedDelivered = await forcedFlush;

        QChatOwnerEventEntry? storedBlocking = outbox.GetById(blockingEntry.EventId);
        QChatOwnerEventEntry? storedScheduled = outbox.GetById(scheduledEntry.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(ordinaryDelivered, Is.EqualTo(1));
            Assert.That(forcedDelivered, Is.EqualTo(1));
            Assert.That(runtime.SendAttemptCount, Is.EqualTo(2));
            Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(2));
            Assert.That(storedBlocking, Is.Not.Null);
            Assert.That(storedBlocking!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(storedScheduled, Is.Not.Null);
            Assert.That(storedScheduled!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
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
        "action=test result=success");

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
        public bool BlockSend { get; set; }
        public int SendAttemptCount { get; private set; }
        public long NextMessageId { get; set; } = 1;
        readonly TaskCompletionSource sendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource releaseSend = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            SendAttemptCount++;
            sendStarted.TrySetResult();

            if (SendException != null)
                throw SendException;

            if (BlockSend)
                return SendPrivateMessageWithResultAfterReleaseAsync(userId, message);

            PrivateMessages.Add((userId, message));
            return Task.FromResult<OneBotSendMessageResult?>(new OneBotSendMessageResult { MessageId = NextMessageId++ });
        }

        public Task WaitForSendStartedAsync() => sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void ReleaseBlockedSend() => releaseSend.TrySetResult();

        async Task<OneBotSendMessageResult?> SendPrivateMessageWithResultAfterReleaseAsync(long userId, string message)
        {
            await releaseSend.Task;
            PrivateMessages.Add((userId, message));
            return new OneBotSendMessageResult { MessageId = NextMessageId++ };
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
