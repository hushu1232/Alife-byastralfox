using System.IO;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatOwnerEventOutboxTests
{
    [Test]
    public void EnqueueCreatesPendingEventAndPersistsIt()
    {
        string path = CreateTempPath();
        DateTimeOffset now = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox outbox = new(path);

        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("enqueue"), now);

        Assert.Multiple(() =>
        {
            Assert.That(entry.EventId, Does.StartWith("owner-event-"));
            Assert.That(entry.DedupeKey, Is.EqualTo("enqueue"));
            Assert.That(entry.AgentId, Is.EqualTo("xiayu"));
            Assert.That(entry.OwnerId, Is.EqualTo(1001));
            Assert.That(entry.Severity, Is.EqualTo("info"));
            Assert.That(entry.Category, Is.EqualTo("risk"));
            Assert.That(entry.Source, Is.EqualTo("test"));
            Assert.That(entry.SourceId, Is.EqualTo("enqueue"));
            Assert.That(entry.Message, Is.EqualTo("action=test dedupe=enqueue"));
            Assert.That(entry.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(entry.CreatedAt, Is.EqualTo(now));
            Assert.That(entry.NextAttemptAt, Is.EqualTo(now));
            Assert.That(entry.AttemptCount, Is.EqualTo(0));
            Assert.That(entry.DeliveredAt, Is.Null);
            Assert.That(entry.DeliveryMessageId, Is.Null);
            Assert.That(entry.LastError, Is.Null);
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void ReloadsPendingEventsFromDisk()
    {
        string path = CreateTempPath();
        DateTimeOffset now = new(2026, 6, 21, 10, 1, 0, TimeSpan.Zero);
        QChatOwnerEventEntry created = new QChatOwnerEventOutbox(path).Enqueue(CreateRequest("reload"), now);

        QChatOwnerEventOutbox reloaded = new(path);
        IReadOnlyList<QChatOwnerEventEntry> pending = reloaded.GetPending(now.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(pending[0].EventId, Is.EqualTo(created.EventId));
            Assert.That(pending[0].Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(reloaded.GetById(created.EventId)?.Message, Is.EqualTo("action=test dedupe=reload"));
        });
    }

    [Test]
    public void DuplicateDedupeKeyReturnsExistingEvent()
    {
        string path = CreateTempPath();
        DateTimeOffset now = new(2026, 6, 21, 10, 2, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox outbox = new(path);

        QChatOwnerEventEntry first = outbox.Enqueue(CreateRequest("duplicate"), now);
        QChatOwnerEventEntry second = outbox.Enqueue(CreateRequest("duplicate"), now.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(second.EventId, Is.EqualTo(first.EventId));
            Assert.That(second.CreatedAt, Is.EqualTo(first.CreatedAt));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void MarkDeliveredPersistsDeliveredStatus()
    {
        string path = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 3, 0, TimeSpan.Zero);
        DateTimeOffset deliveredAt = createdAt.AddMinutes(1);
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry created = outbox.Enqueue(CreateRequest("delivered"), createdAt);

        QChatOwnerEventEntry delivered = outbox.MarkDelivered(created.EventId, 123456, deliveredAt);
        QChatOwnerEventOutbox reloaded = new(path);
        QChatOwnerEventEntry? persisted = reloaded.GetById(created.EventId);

        Assert.Multiple(() =>
        {
            Assert.That(delivered.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(delivered.DeliveredAt, Is.EqualTo(deliveredAt));
            Assert.That(delivered.DeliveryMessageId, Is.EqualTo(123456));
            Assert.That(delivered.LastError, Is.Null);
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(persisted.DeliveredAt, Is.EqualTo(deliveredAt));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void MarkFailedKeepsPendingAndSchedulesRetry()
    {
        string path = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 4, 0, TimeSpan.Zero);
        DateTimeOffset failedAt = createdAt.AddSeconds(10);
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry created = outbox.Enqueue(CreateRequest("failed"), createdAt);
        string longError = "first line\r\nsecond\tline " + new string('x', 300);

        QChatOwnerEventEntry failed = outbox.MarkFailed(created.EventId, longError, failedAt);
        QChatOwnerEventOutbox reloaded = new(path);
        QChatOwnerEventEntry? persisted = reloaded.GetById(created.EventId);
        QChatOwnerEventSummary summary = reloaded.GetSummary();

        Assert.Multiple(() =>
        {
            Assert.That(failed.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(failed.AttemptCount, Is.EqualTo(1));
            Assert.That(failed.NextAttemptAt, Is.EqualTo(failedAt.AddSeconds(30)));
            Assert.That(failed.LastError, Does.Not.Contain("\r"));
            Assert.That(failed.LastError, Does.Not.Contain("\n"));
            Assert.That(failed.LastError, Does.Not.Contain("\t"));
            Assert.That(failed.LastError, Has.Length.EqualTo(240));
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(persisted.AttemptCount, Is.EqualTo(1));
            Assert.That(persisted.NextAttemptAt, Is.EqualTo(failedAt.AddSeconds(30)));
            Assert.That(summary.Total, Is.EqualTo(1));
            Assert.That(summary.Pending, Is.EqualTo(1));
            Assert.That(summary.Delivered, Is.EqualTo(0));
            Assert.That(summary.Abandoned, Is.EqualTo(0));
            Assert.That(summary.LastError, Is.EqualTo(failed.LastError));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(2));
        });
    }

    static QChatOwnerEventRequest CreateRequest(string dedupeKey) => new(
        DedupeKey: dedupeKey,
        AgentId: "xiayu",
        OwnerId: 1001,
        Severity: "info",
        Category: "risk",
        Source: "test",
        SourceId: dedupeKey,
        Message: $"action=test dedupe={dedupeKey}");

    static string CreateTempPath() => Path.Combine(
        Path.GetTempPath(),
        "alife-qchat-owner-event-outbox-tests",
        $"{Guid.NewGuid():N}.jsonl");
}
