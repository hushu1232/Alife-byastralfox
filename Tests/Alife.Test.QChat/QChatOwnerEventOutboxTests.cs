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
    public void ReloadIgnoresMalformedLinesAndKeepsNewestValidSnapshot()
    {
        string sourcePath = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 1, 0, TimeSpan.Zero);
        DateTimeOffset failedAt = createdAt.AddSeconds(10);
        QChatOwnerEventOutbox source = new(sourcePath);
        QChatOwnerEventEntry created = source.Enqueue(CreateRequest("reload-malformed"), createdAt);
        source.MarkFailed(created.EventId, "temporary failure", failedAt);
        string[] validLines = File.ReadAllLines(sourcePath);
        string path = CreateTempPath();
        File.WriteAllLines(path, new[] { validLines[0], "{ malformed json", "", validLines[1] });

        QChatOwnerEventOutbox reloaded = new(path);
        QChatOwnerEventEntry? loaded = reloaded.GetById(created.EventId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(loaded.AttemptCount, Is.EqualTo(1));
            Assert.That(loaded.LastError, Is.EqualTo("temporary failure"));
            Assert.That(reloaded.GetPending(failedAt.AddSeconds(31)).Select(entry => entry.EventId), Is.EqualTo(new[] { created.EventId }));
        });
    }

    [Test]
    public void ReloadIgnoresSemanticallyInvalidJsonAndKeepsNewestValidSnapshot()
    {
        string sourcePath = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 1, 0, TimeSpan.Zero);
        DateTimeOffset failedAt = createdAt.AddSeconds(10);
        QChatOwnerEventOutbox source = new(sourcePath);
        QChatOwnerEventEntry created = source.Enqueue(CreateRequest("reload-invalid-object"), createdAt);
        source.MarkFailed(created.EventId, "temporary failure", failedAt);
        string[] validLines = File.ReadAllLines(sourcePath);
        string path = CreateTempPath();
        File.WriteAllLines(path, new[] { validLines[0], "{}", validLines[1] });

        QChatOwnerEventOutbox reloaded = new(path);
        QChatOwnerEventEntry? loaded = reloaded.GetById(created.EventId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(loaded.AttemptCount, Is.EqualTo(1));
            Assert.That(loaded.LastError, Is.EqualTo("temporary failure"));
            Assert.That(reloaded.GetPending(failedAt.AddSeconds(31)).Select(entry => entry.EventId), Is.EqualTo(new[] { created.EventId }));
        });
    }

    [Test]
    public void ReloadKeepsDeliveredTerminalWhenLaterSnapshotIsPending()
    {
        string sourcePath = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 1, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox source = new(sourcePath);
        QChatOwnerEventEntry created = source.Enqueue(CreateRequest("reload-terminal"), createdAt);
        source.MarkDelivered(created.EventId, 123456, createdAt.AddMinutes(1));
        string[] lines = File.ReadAllLines(sourcePath);
        string path = CreateTempPath();
        File.WriteAllLines(path, new[] { lines[0], lines[1], lines[0] });

        QChatOwnerEventOutbox reloaded = new(path);
        QChatOwnerEventEntry? loaded = reloaded.GetById(created.EventId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(loaded.DeliveryMessageId, Is.EqualTo(123456));
            Assert.That(reloaded.GetPending(createdAt.AddMinutes(10)).Select(entry => entry.EventId), Is.Empty);
        });
    }

    [Test]
    public void ReloadSkipsMismatchedEventIdForDedupeKey()
    {
        string sourcePath = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 1, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox source = new(sourcePath);
        QChatOwnerEventEntry created = source.Enqueue(CreateRequest("reload-mismatched-id"), createdAt);
        string invalidLine = File.ReadAllText(sourcePath).Replace(created.EventId, "owner-event-not-the-deterministic-id");
        string path = CreateTempPath();
        File.WriteAllText(path, invalidLine);

        QChatOwnerEventOutbox reloaded = new(path);
        QChatOwnerEventEntry enqueued = reloaded.Enqueue(CreateRequest("reload-mismatched-id"), createdAt.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.GetRecent(10), Has.Count.EqualTo(1));
            Assert.That(enqueued.EventId, Is.EqualTo(created.EventId));
            Assert.That(enqueued.EventId, Is.Not.EqualTo("owner-event-not-the-deterministic-id"));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void ReloadSkipsEntriesWithUndefinedStatus()
    {
        string sourcePath = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 1, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox source = new(sourcePath);
        QChatOwnerEventEntry created = source.Enqueue(CreateRequest("reload-invalid-status"), createdAt);
        string invalidLine = File.ReadAllText(sourcePath).Replace("\"status\":\"Pending\"", "\"status\":999");
        string path = CreateTempPath();
        File.WriteAllText(path, invalidLine);

        QChatOwnerEventOutbox reloaded = new(path);
        IReadOnlyList<QChatOwnerEventEntry> recentBeforeEnqueue = reloaded.GetRecent(10);
        QChatOwnerEventEntry enqueued = reloaded.Enqueue(CreateRequest("reload-invalid-status"), createdAt.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(recentBeforeEnqueue, Is.Empty);
            Assert.That(enqueued.EventId, Is.EqualTo(created.EventId));
            Assert.That(enqueued.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(enqueued.CreatedAt, Is.EqualTo(createdAt.AddMinutes(1)));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(2));
        });
    }

    [TestCase("agentId", "xiayu")]
    [TestCase("severity", "info")]
    [TestCase("category", "risk")]
    [TestCase("source", "test")]
    [TestCase("sourceId", "reload-blank-loaded-field")]
    [TestCase("message", "action=test dedupe=reload-blank-loaded-field")]
    public void ReloadSkipsEntriesWithBlankRequiredTextField(string jsonProperty, string originalValue)
    {
        string sourcePath = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 1, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox source = new(sourcePath);
        QChatOwnerEventEntry created = source.Enqueue(CreateRequest("reload-blank-loaded-field"), createdAt);
        string invalidLine = File.ReadAllText(sourcePath).Replace(
            $"\"{jsonProperty}\":\"{originalValue}\"",
            $"\"{jsonProperty}\":\" \"");
        string path = CreateTempPath();
        File.WriteAllText(path, invalidLine);

        QChatOwnerEventOutbox reloaded = new(path);
        QChatOwnerEventEntry enqueued = reloaded.Enqueue(CreateRequest("reload-blank-loaded-field"), createdAt.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.GetRecent(10), Has.Count.EqualTo(1));
            Assert.That(enqueued.EventId, Is.EqualTo(created.EventId));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void ConstructorRejectsEmptyFilePath()
    {
        Assert.Throws<ArgumentException>(() => new QChatOwnerEventOutbox(""));
    }

    [Test]
    public void EnqueueRejectsEmptyDedupeKey()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventRequest request = CreateRequest("") with { SourceId = "empty-dedupe-key" };

        Assert.Throws<ArgumentException>(() => outbox.Enqueue(request));
    }

    [Test]
    public void EnqueueRejectsEmptyMessage()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventRequest request = CreateRequest("empty-message") with { Message = "" };

        Assert.Throws<ArgumentException>(() => outbox.Enqueue(request));
    }

    [Test]
    public void EnqueueRejectsInvalidOwnerId()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventRequest request = CreateRequest("invalid-owner") with { OwnerId = 0 };

        Assert.Throws<ArgumentException>(() => outbox.Enqueue(request));
    }

    [Test]
    public void EnqueueRejectsBlankAgentId()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventRequest request = CreateRequest("blank-agent") with { AgentId = " " };

        Assert.Throws<ArgumentException>(() => outbox.Enqueue(request));
    }

    [Test]
    public void EnqueueRejectsBlankSourceId()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventRequest request = CreateRequest("blank-source-id") with { SourceId = " " };

        Assert.Throws<ArgumentException>(() => outbox.Enqueue(request));
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
    public void PersistenceUsesStringEnumsAndUtf8WithoutBom()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);

        outbox.Enqueue(CreateRequest("format"), new DateTimeOffset(2026, 6, 21, 10, 2, 0, TimeSpan.Zero));
        byte[] bytes = File.ReadAllBytes(path);
        string text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(bytes.Take(3).ToArray(), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(text, Does.Contain("\"status\":\"Pending\""));
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
    public void MarkDeliveredIsIdempotentForDeliveredEvent()
    {
        string path = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 3, 0, TimeSpan.Zero);
        DateTimeOffset deliveredAt = createdAt.AddMinutes(1);
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry created = outbox.Enqueue(CreateRequest("delivered-idempotent"), createdAt);
        QChatOwnerEventEntry first = outbox.MarkDelivered(created.EventId, 123456, deliveredAt);

        QChatOwnerEventEntry second = outbox.MarkDelivered(created.EventId, 999999, deliveredAt.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(second.DeliveryMessageId, Is.EqualTo(123456));
            Assert.That(second.DeliveredAt, Is.EqualTo(deliveredAt));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void GetRecentIncludesDeliveredEventsNewestFirst()
    {
        string path = CreateTempPath();
        DateTimeOffset olderAt = new(2026, 6, 21, 10, 3, 0, TimeSpan.Zero);
        DateTimeOffset newerAt = olderAt.AddMinutes(1);
        QChatOwnerEventOutbox outbox = new(path, maxDeliveredEntries: 0);
        QChatOwnerEventEntry older = outbox.Enqueue(CreateRequest("recent-older"), olderAt);
        QChatOwnerEventEntry newer = outbox.Enqueue(CreateRequest("recent-newer"), newerAt);
        outbox.MarkDelivered(newer.EventId, 123456, newerAt.AddSeconds(10));

        IReadOnlyList<QChatOwnerEventEntry> recent = outbox.GetRecent(2);

        Assert.Multiple(() =>
        {
            Assert.That(recent, Has.Count.EqualTo(2));
            Assert.That(recent[0].EventId, Is.EqualTo(newer.EventId));
            Assert.That(recent[0].Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(recent[1].EventId, Is.EqualTo(older.EventId));
            Assert.That(recent[1].Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
        });
    }

    [Test]
    public void RetentionOmitsOldDeliveredEventsButKeepsPendingVisibleAndDedupeProtected()
    {
        string path = CreateTempPath();
        DateTimeOffset baseTime = new(2026, 6, 21, 10, 3, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox outbox = new(path, maxDeliveredEntries: 1);
        QChatOwnerEventEntry oldDelivered = outbox.Enqueue(CreateRequest("retention-old-delivered"), baseTime);
        QChatOwnerEventEntry pending = outbox.Enqueue(CreateRequest("retention-pending"), baseTime.AddMinutes(1));
        QChatOwnerEventEntry newDelivered = outbox.Enqueue(CreateRequest("retention-new-delivered"), baseTime.AddMinutes(2));
        outbox.MarkDelivered(oldDelivered.EventId, 111, baseTime.AddMinutes(3));
        outbox.MarkDelivered(newDelivered.EventId, 222, baseTime.AddMinutes(4));

        IReadOnlyList<QChatOwnerEventEntry> recent = outbox.GetRecent(10);
        QChatOwnerEventSummary summary = outbox.GetSummary();
        QChatOwnerEventOutbox reloaded = new(path, maxDeliveredEntries: 1);
        QChatOwnerEventEntry duplicate = reloaded.Enqueue(CreateRequest("retention-old-delivered"), baseTime.AddMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(recent.Select(entry => entry.EventId), Is.EqualTo(new[] { newDelivered.EventId, pending.EventId }));
            Assert.That(summary.Total, Is.EqualTo(2));
            Assert.That(summary.Pending, Is.EqualTo(1));
            Assert.That(summary.Delivered, Is.EqualTo(1));
            Assert.That(duplicate.EventId, Is.EqualTo(oldDelivered.EventId));
            Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(5));
        });
    }

    [Test]
    public void MaxDeliveredEntriesZeroKeepsOneDeliveredEventInRecentHistory()
    {
        string path = CreateTempPath();
        DateTimeOffset baseTime = new(2026, 6, 21, 10, 3, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox outbox = new(path, maxDeliveredEntries: 0);
        QChatOwnerEventEntry older = outbox.Enqueue(CreateRequest("clamp-older"), baseTime);
        QChatOwnerEventEntry newer = outbox.Enqueue(CreateRequest("clamp-newer"), baseTime.AddMinutes(1));
        outbox.MarkDelivered(older.EventId, 111, baseTime.AddMinutes(2));
        outbox.MarkDelivered(newer.EventId, 222, baseTime.AddMinutes(3));

        IReadOnlyList<QChatOwnerEventEntry> recent = outbox.GetRecent(10);

        Assert.That(recent.Select(entry => entry.EventId), Is.EqualTo(new[] { newer.EventId }));
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

    [Test]
    public void MarkFailedSchedulesRetryDelaysForAttemptsOneThroughFour()
    {
        string path = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 5, 0, TimeSpan.Zero);
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("retry-delays"), createdAt);

        QChatOwnerEventEntry first = outbox.MarkFailed(entry.EventId, "failure 1", createdAt.AddSeconds(1));
        QChatOwnerEventEntry second = outbox.MarkFailed(entry.EventId, "failure 2", createdAt.AddSeconds(2));
        QChatOwnerEventEntry third = outbox.MarkFailed(entry.EventId, "failure 3", createdAt.AddSeconds(3));
        QChatOwnerEventEntry fourth = outbox.MarkFailed(entry.EventId, "failure 4", createdAt.AddSeconds(4));

        Assert.Multiple(() =>
        {
            Assert.That(first.NextAttemptAt, Is.EqualTo(createdAt.AddSeconds(31)));
            Assert.That(second.NextAttemptAt, Is.EqualTo(createdAt.AddSeconds(2).AddMinutes(2)));
            Assert.That(third.NextAttemptAt, Is.EqualTo(createdAt.AddSeconds(3).AddMinutes(10)));
            Assert.That(fourth.NextAttemptAt, Is.EqualTo(createdAt.AddSeconds(4).AddMinutes(30)));
            Assert.That(fourth.AttemptCount, Is.EqualTo(4));
        });
    }

    [Test]
    public void MarkFailedDoesNotResurrectDeliveredEvent()
    {
        string path = CreateTempPath();
        DateTimeOffset createdAt = new(2026, 6, 21, 10, 6, 0, TimeSpan.Zero);
        DateTimeOffset deliveredAt = createdAt.AddMinutes(1);
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry created = outbox.Enqueue(CreateRequest("failed-delivered"), createdAt);
        QChatOwnerEventEntry delivered = outbox.MarkDelivered(created.EventId, 123456, deliveredAt);

        QChatOwnerEventEntry failed = outbox.MarkFailed(created.EventId, "late failure", deliveredAt.AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(delivered));
            Assert.That(failed.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(failed.AttemptCount, Is.EqualTo(0));
            Assert.That(failed.NextAttemptAt, Is.EqualTo(createdAt));
            Assert.That(failed.LastError, Is.Null);
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
