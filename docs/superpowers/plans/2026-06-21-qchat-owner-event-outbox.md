# QChat Owner Event Outbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a durable owner event outbox so QChat safety reports and long asynchronous task results survive NapCat, OneBot, and local service delivery failures.

**Architecture:** Add a small append-only JSONL outbox, a dispatcher that sends due pending events through OneBot, and a publisher facade used by QChat risk reports and desktop business job completion. Ordinary chat replies stay outside the outbox so reconnects do not replay stale casual conversation.

**Tech Stack:** C#/.NET 9, NUnit, existing QChat module, existing `IOneBotRuntime`, append-only JSONL under `AlifePath.StorageFolderPath`.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs`
  - Defines event status, request, entry, summary, and append-only persistence.
- Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs`
  - Flushes due pending owner events through `IOneBotRuntime.SendPrivateMessageWithResult`.
- Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventPublisher.cs`
  - Provides `PublishAsync`, `FlushAsync`, `GetSummary`, and `GetRecent` over outbox plus dispatcher.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Adds lazy owner event publisher wiring.
  - Replaces direct risk report send with outbox publish.
  - Replaces desktop job completion direct send with outbox publish.
  - Adds owner-only `/qchat events status` and `/qchat events retry`.
- Test `Tests/Alife.Test.QChat/QChatOwnerEventOutboxTests.cs`
  - Core persistence, dedupe, status, and retry behavior.
- Test `Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs`
  - Delivery success, delivery failure, formatting, and non-throwing retry behavior.
- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Integration coverage for risk reports, desktop job completion, and owner-only commands.

## Shared Types

Use these public names consistently across all tasks:

```csharp
public enum QChatOwnerEventStatus
{
    Pending,
    Delivered,
    Abandoned
}

public sealed record QChatOwnerEventRequest(
    string AgentId,
    long OwnerId,
    string Severity,
    string Category,
    string Source,
    string SourceId,
    string DedupeKey,
    string Message);

public sealed record QChatOwnerEventEntry(
    string EventId,
    DateTimeOffset CreatedAt,
    string AgentId,
    long OwnerId,
    string Severity,
    string Category,
    string Source,
    string SourceId,
    string DedupeKey,
    string Message,
    QChatOwnerEventStatus Status,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    long? DeliveryMessageId,
    string? LastError);

public sealed record QChatOwnerEventSummary(
    int Total,
    int Pending,
    int Delivered,
    int Abandoned,
    string? LastError);
```

The first implementation does not need a `Delivering` state. The dispatcher uses a semaphore to prevent overlapping flush loops.

---

### Task 1: Core Append-Only Outbox

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs`
- Create: `Tests/Alife.Test.QChat/QChatOwnerEventOutboxTests.cs`

- [ ] **Step 1: Write failing persistence and dedupe tests**

Create `Tests/Alife.Test.QChat/QChatOwnerEventOutboxTests.cs` with these tests:

```csharp
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatOwnerEventOutboxTests
{
    [Test]
    public void EnqueueCreatesPendingEventAndPersistsIt()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);

        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("risk-delete-1"));

        Assert.Multiple(() =>
        {
            Assert.That(entry.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(entry.EventId, Is.Not.Empty);
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.ReadAllText(path), Does.Contain("risk-delete-1"));
        });
    }

    [Test]
    public void ReloadsPendingEventsFromDisk()
    {
        string path = CreateTempPath();
        new QChatOwnerEventOutbox(path).Enqueue(CreateRequest("desktop-job-1"));

        QChatOwnerEventOutbox reloaded = new(path);

        Assert.That(reloaded.GetPending(DateTimeOffset.Now, 10), Has.Count.EqualTo(1));
        Assert.That(reloaded.GetPending(DateTimeOffset.Now, 10)[0].DedupeKey, Is.EqualTo("desktop-job-1"));
    }

    [Test]
    public void DuplicateDedupeKeyReturnsExistingEvent()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);

        QChatOwnerEventEntry first = outbox.Enqueue(CreateRequest("same-key"));
        QChatOwnerEventEntry second = outbox.Enqueue(CreateRequest("same-key"));

        Assert.Multiple(() =>
        {
            Assert.That(second.EventId, Is.EqualTo(first.EventId));
            Assert.That(outbox.GetRecent(10), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void MarkDeliveredPersistsDeliveredStatus()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("delivered-key"));

        outbox.MarkDelivered(entry.EventId, 42, DateTimeOffset.Parse("2026-06-21T10:00:00+08:00"));
        QChatOwnerEventEntry reloaded = new QChatOwnerEventOutbox(path).GetById(entry.EventId)!;

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(reloaded.DeliveryMessageId, Is.EqualTo(42));
            Assert.That(reloaded.DeliveredAt, Is.Not.Null);
        });
    }

    [Test]
    public void MarkFailedKeepsPendingAndSchedulesRetry()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        QChatOwnerEventEntry entry = outbox.Enqueue(CreateRequest("failed-key"));
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-21T10:00:00+08:00");

        QChatOwnerEventEntry failed = outbox.MarkFailed(entry.EventId, "NapCat disconnected", now);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(failed.AttemptCount, Is.EqualTo(1));
            Assert.That(failed.NextAttemptAt, Is.EqualTo(now.AddSeconds(30)));
            Assert.That(failed.LastError, Does.Contain("NapCat disconnected"));
        });
    }

    static QChatOwnerEventRequest CreateRequest(string dedupeKey) => new(
        AgentId: "xiayu",
        OwnerId: 1001,
        Severity: "info",
        Category: "risk",
        Source: "test",
        SourceId: dedupeKey,
        DedupeKey: dedupeKey,
        Message: $"action=test dedupe={dedupeKey}");

    static string CreateTempPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-events", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "qchat-owner-events.jsonl");
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter FullyQualifiedName~QChatOwnerEventOutboxTests
```

Expected: compile failure because `QChatOwnerEventOutbox`, `QChatOwnerEventEntry`, `QChatOwnerEventRequest`, `QChatOwnerEventStatus`, and `QChatOwnerEventSummary` do not exist.

- [ ] **Step 3: Implement the outbox**

Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs` with:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.QChat;

public enum QChatOwnerEventStatus
{
    Pending,
    Delivered,
    Abandoned
}

public sealed record QChatOwnerEventRequest(
    string AgentId,
    long OwnerId,
    string Severity,
    string Category,
    string Source,
    string SourceId,
    string DedupeKey,
    string Message);

public sealed record QChatOwnerEventEntry(
    string EventId,
    DateTimeOffset CreatedAt,
    string AgentId,
    long OwnerId,
    string Severity,
    string Category,
    string Source,
    string SourceId,
    string DedupeKey,
    string Message,
    QChatOwnerEventStatus Status,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    long? DeliveryMessageId,
    string? LastError);

public sealed record QChatOwnerEventSummary(
    int Total,
    int Pending,
    int Delivered,
    int Abandoned,
    string? LastError);

public sealed class QChatOwnerEventOutbox
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    readonly object syncRoot = new();
    readonly Dictionary<string, QChatOwnerEventEntry> entriesById = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> eventIdsByDedupeKey = new(StringComparer.Ordinal);
    readonly string filePath;
    readonly int maxDeliveredEntries;

    public QChatOwnerEventOutbox(string filePath, int maxDeliveredEntries = 1000)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Owner event outbox path cannot be empty.", nameof(filePath));

        this.filePath = Path.GetFullPath(filePath);
        this.maxDeliveredEntries = Math.Max(1, maxDeliveredEntries);
        Directory.CreateDirectory(Path.GetDirectoryName(this.filePath)!);
        LoadExistingEntries();
    }

    public QChatOwnerEventEntry Enqueue(QChatOwnerEventRequest request, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset timestamp = now ?? DateTimeOffset.Now;
        string dedupeKey = NormalizeRequired(request.DedupeKey, nameof(request.DedupeKey));

        lock (syncRoot)
        {
            if (eventIdsByDedupeKey.TryGetValue(dedupeKey, out string? existingId) &&
                entriesById.TryGetValue(existingId, out QChatOwnerEventEntry? existing))
            {
                return existing;
            }

            QChatOwnerEventEntry entry = new(
                EventId: CreateEventId(dedupeKey),
                CreatedAt: timestamp,
                AgentId: NormalizeOptional(request.AgentId),
                OwnerId: request.OwnerId,
                Severity: NormalizeOptional(request.Severity, "info"),
                Category: NormalizeOptional(request.Category, "general"),
                Source: NormalizeOptional(request.Source, "qchat"),
                SourceId: NormalizeOptional(request.SourceId),
                DedupeKey: dedupeKey,
                Message: NormalizeRequired(request.Message, nameof(request.Message)),
                Status: QChatOwnerEventStatus.Pending,
                AttemptCount: 0,
                NextAttemptAt: timestamp,
                DeliveredAt: null,
                DeliveryMessageId: null,
                LastError: null);

            RecordLocked(entry);
            TrimDeliveredLocked();
            return entry;
        }
    }

    public QChatOwnerEventEntry? GetById(string eventId)
    {
        lock (syncRoot)
            return entriesById.TryGetValue(eventId, out QChatOwnerEventEntry? entry) ? entry : null;
    }

    public IReadOnlyList<QChatOwnerEventEntry> GetPending(DateTimeOffset? now = null, int maxCount = 20)
    {
        DateTimeOffset dueAt = now ?? DateTimeOffset.Now;
        lock (syncRoot)
        {
            return entriesById.Values
                .Where(entry => entry.Status == QChatOwnerEventStatus.Pending && entry.NextAttemptAt <= dueAt)
                .OrderBy(entry => entry.CreatedAt)
                .Take(Math.Max(1, maxCount))
                .ToArray();
        }
    }

    public IReadOnlyList<QChatOwnerEventEntry> GetRecent(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        lock (syncRoot)
        {
            return entriesById.Values
                .OrderByDescending(entry => entry.CreatedAt)
                .Take(maxCount)
                .ToArray();
        }
    }

    public QChatOwnerEventSummary GetSummary()
    {
        lock (syncRoot)
        {
            QChatOwnerEventEntry? lastError = entriesById.Values
                .Where(entry => string.IsNullOrWhiteSpace(entry.LastError) == false)
                .OrderByDescending(entry => entry.CreatedAt)
                .FirstOrDefault();
            return new QChatOwnerEventSummary(
                entriesById.Count,
                entriesById.Values.Count(entry => entry.Status == QChatOwnerEventStatus.Pending),
                entriesById.Values.Count(entry => entry.Status == QChatOwnerEventStatus.Delivered),
                entriesById.Values.Count(entry => entry.Status == QChatOwnerEventStatus.Abandoned),
                lastError?.LastError);
        }
    }

    public QChatOwnerEventEntry MarkDelivered(string eventId, long? messageId, DateTimeOffset? now = null)
    {
        lock (syncRoot)
        {
            QChatOwnerEventEntry entry = RequireEntryLocked(eventId);
            QChatOwnerEventEntry updated = entry with
            {
                Status = QChatOwnerEventStatus.Delivered,
                DeliveredAt = now ?? DateTimeOffset.Now,
                DeliveryMessageId = messageId,
                LastError = null
            };
            RecordLocked(updated);
            TrimDeliveredLocked();
            return updated;
        }
    }

    public QChatOwnerEventEntry MarkFailed(string eventId, string error, DateTimeOffset? now = null)
    {
        lock (syncRoot)
        {
            DateTimeOffset timestamp = now ?? DateTimeOffset.Now;
            QChatOwnerEventEntry entry = RequireEntryLocked(eventId);
            int attempts = entry.AttemptCount + 1;
            QChatOwnerEventEntry updated = entry with
            {
                Status = QChatOwnerEventStatus.Pending,
                AttemptCount = attempts,
                NextAttemptAt = timestamp + GetRetryDelay(attempts),
                LastError = SanitizeError(error)
            };
            RecordLocked(updated);
            return updated;
        }
    }

    static TimeSpan GetRetryDelay(int attemptCount) => attemptCount switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(30)
    };
}
```

Add these private methods below the public methods in the same file:

```csharp
    void LoadExistingEntries()
    {
        if (File.Exists(filePath) == false)
            return;

        foreach (string line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            QChatOwnerEventEntry? entry = JsonSerializer.Deserialize<QChatOwnerEventEntry>(line, JsonOptions);
            if (entry == null)
                continue;

            entriesById[entry.EventId] = entry;
            eventIdsByDedupeKey[entry.DedupeKey] = entry.EventId;
        }
    }

    void RecordLocked(QChatOwnerEventEntry entry)
    {
        entriesById[entry.EventId] = entry;
        eventIdsByDedupeKey[entry.DedupeKey] = entry.EventId;
        File.AppendAllText(filePath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    QChatOwnerEventEntry RequireEntryLocked(string eventId)
    {
        if (entriesById.TryGetValue(eventId, out QChatOwnerEventEntry? entry))
            return entry;

        throw new InvalidOperationException($"Owner event not found: {eventId}");
    }

    void TrimDeliveredLocked()
    {
        QChatOwnerEventEntry[] delivered = entriesById.Values
            .Where(entry => entry.Status == QChatOwnerEventStatus.Delivered)
            .OrderByDescending(entry => entry.DeliveredAt ?? entry.CreatedAt)
            .Skip(maxDeliveredEntries)
            .ToArray();

        foreach (QChatOwnerEventEntry entry in delivered)
        {
            entriesById.Remove(entry.EventId);
            eventIdsByDedupeKey.Remove(entry.DedupeKey);
        }
    }

    static string CreateEventId(string dedupeKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(dedupeKey));
        return "owner-event-" + Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }

    static string NormalizeRequired(string? value, string name)
    {
        string normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException($"{name} cannot be empty.", name);

        return normalized;
    }

    static string NormalizeOptional(string? value, string fallback = "")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    static string SanitizeError(string? value)
    {
        string sanitized = NormalizeOptional(value, "unknown_error")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }
```

- [ ] **Step 4: Run outbox tests and verify they pass**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter FullyQualifiedName~QChatOwnerEventOutboxTests
```

Expected: all `QChatOwnerEventOutboxTests` pass.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs Tests/Alife.Test.QChat/QChatOwnerEventOutboxTests.cs
git commit -m "Add QChat owner event outbox"
```

---

### Task 2: Owner Event Dispatcher and Publisher

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventPublisher.cs`
- Create: `Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs`

- [ ] **Step 1: Write failing dispatcher tests**

Create `Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs`:

```csharp
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatOwnerEventDispatcherTests
{
    [Test]
    public async Task FlushAsyncSendsPendingEventsAndMarksDelivered()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeRuntime runtime = new();
        outbox.Enqueue(CreateRequest("event-1"));
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        int delivered = await dispatcher.FlushAsync();

        QChatOwnerEventEntry entry = outbox.GetRecent(1).Single();
        Assert.Multiple(() =>
        {
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
            Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
            Assert.That(runtime.PrivateMessages[0].Message, Does.Contain("action=test"));
            Assert.That(entry.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
            Assert.That(entry.DeliveryMessageId, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task FlushAsyncKeepsEventPendingWhenRuntimeThrows()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeRuntime runtime = new() { SendException = new InvalidOperationException("offline") };
        outbox.Enqueue(CreateRequest("event-2"));
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

        int delivered = await dispatcher.FlushAsync();

        QChatOwnerEventEntry entry = outbox.GetRecent(1).Single();
        Assert.Multiple(() =>
        {
            Assert.That(delivered, Is.Zero);
            Assert.That(entry.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(entry.AttemptCount, Is.EqualTo(1));
            Assert.That(entry.LastError, Does.Contain("offline"));
        });
    }

    [Test]
    public async Task PublisherEnqueuesAndFlushesWithoutThrowing()
    {
        string path = CreateTempPath();
        QChatOwnerEventOutbox outbox = new(path);
        FakeRuntime runtime = new();
        QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
        QChatOwnerEventPublisher publisher = new(outbox, dispatcher);

        QChatOwnerEventEntry entry = await publisher.PublishAsync(CreateRequest("event-3"));

        Assert.Multiple(() =>
        {
            Assert.That(entry.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
            Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
            Assert.That(outbox.GetRecent(1)[0].Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
        });
    }

    static QChatOwnerEventRequest CreateRequest(string dedupeKey) => new(
        AgentId: "xiayu",
        OwnerId: 1001,
        Severity: "info",
        Category: "risk",
        Source: "test",
        SourceId: dedupeKey,
        DedupeKey: dedupeKey,
        Message: "action=test result=success");

    static string CreateTempPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-event-dispatcher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "qchat-owner-events.jsonl");
    }

    sealed class FakeRuntime : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId => 999;
        public bool IsConnected => true;
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public Exception? SendException { get; init; }
        public long NextMessageId { get; set; } = 10;
        public List<(long Target, string Message)> PrivateMessages { get; } = [];
        public Task ConnectAsync() => Task.CompletedTask;
        public Task SendGroupMessage(long groupId, string message) => Task.CompletedTask;
        public Task SendPrivateMessage(long userId, string message) => Task.CompletedTask;
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
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatOwnerEventDispatcherTests"
```

Expected: compile failure because dispatcher and publisher types do not exist.

- [ ] **Step 3: Implement dispatcher**

Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs`:

```csharp
namespace Alife.Function.QChat;

public sealed class QChatOwnerEventDispatcher(
    QChatOwnerEventOutbox outbox,
    Func<IOneBotRuntime> runtimeProvider,
    int maxBatchSize = 20)
{
    readonly SemaphoreSlim flushGate = new(1, 1);

    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (await flushGate.WaitAsync(0, cancellationToken) == false)
            return 0;

        try
        {
            int delivered = 0;
            foreach (QChatOwnerEventEntry entry in outbox.GetPending(DateTimeOffset.Now, maxBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string formatted = QChatCommandPersonaFormatter.Format(
                        entry.AgentId,
                        QChatSenderRole.Owner,
                        entry.Message);
                    OneBotSendMessageResult? result = await runtimeProvider()
                        .SendPrivateMessageWithResult(entry.OwnerId, formatted);
                    outbox.MarkDelivered(entry.EventId, result?.MessageId, DateTimeOffset.Now);
                    delivered++;
                }
                catch (Exception ex)
                {
                    outbox.MarkFailed(entry.EventId, ex.Message, DateTimeOffset.Now);
                }
            }

            return delivered;
        }
        finally
        {
            flushGate.Release();
        }
    }
}
```

- [ ] **Step 4: Implement publisher**

Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventPublisher.cs`:

```csharp
namespace Alife.Function.QChat;

public interface IQChatOwnerEventPublisher
{
    Task<QChatOwnerEventEntry> PublishAsync(QChatOwnerEventRequest request, CancellationToken cancellationToken = default);
    Task<int> FlushAsync(CancellationToken cancellationToken = default);
    QChatOwnerEventSummary GetSummary();
    IReadOnlyList<QChatOwnerEventEntry> GetRecent(int maxCount);
}

public sealed class QChatOwnerEventPublisher(
    QChatOwnerEventOutbox outbox,
    QChatOwnerEventDispatcher dispatcher) : IQChatOwnerEventPublisher
{
    public async Task<QChatOwnerEventEntry> PublishAsync(
        QChatOwnerEventRequest request,
        CancellationToken cancellationToken = default)
    {
        QChatOwnerEventEntry entry = outbox.Enqueue(request);
        await dispatcher.FlushAsync(cancellationToken);
        return entry;
    }

    public Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        return dispatcher.FlushAsync(cancellationToken);
    }

    public QChatOwnerEventSummary GetSummary() => outbox.GetSummary();

    public IReadOnlyList<QChatOwnerEventEntry> GetRecent(int maxCount) => outbox.GetRecent(maxCount);
}
```

- [ ] **Step 5: Run dispatcher tests and verify they pass**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatOwnerEventOutboxTests|FullyQualifiedName~QChatOwnerEventDispatcherTests"
```

Expected: outbox and dispatcher tests pass.

- [ ] **Step 6: Commit Task 2**

```powershell
git add -- sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventPublisher.cs Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs
git commit -m "Add QChat owner event dispatcher"
```

---

### Task 3: Wire Risk Reports Through Outbox

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing integration test**

Add this test to `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs` near the existing risk delete tests:

```csharp
[Test]
public async Task RiskDeleteReportPersistsOwnerEventWhenOwnerMessageSendFails()
{
    FakeOneBotRuntime runtime = new() { SendException = new InvalidOperationException("offline") };
    string outboxPath = CreateTempOwnerEventOutboxPath();
    QChatOwnerEventOutbox outbox = new(outboxPath);
    QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
    QChatOwnerEventPublisher publisher = new(outbox, dispatcher);
    FakeFriendActionGateway friendGateway = new(new QChatFriendDeleteResult(true, "friend_delete_action=delete_friend"));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        AllowPrivateGuestChat = true,
        EnableAutoFriendDelete = true,
        LocalBlockThreshold = 25,
        AutoDeleteFriendThreshold = 25,
        MinIndependentEventsForDelete = 1,
        MinDeleteObservationMinutes = 0,
        EnableBalancedTextStreaming = false
    },
    riskScoreService: new QChatRiskScoreService(CreateTempRiskRoot()),
    friendActionGateway: friendGateway,
    ownerEventPublisher: publisher);

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 2001,
        RawMessage = "jailbreak"
    });

    await WaitUntilAsync(() => outbox.GetRecent(10).Any(item => item.Message.Contains("action=delete_friend", StringComparison.Ordinal)));

    QChatOwnerEventEntry entry = outbox.GetRecent(10).Single(item => item.Message.Contains("action=delete_friend", StringComparison.Ordinal));
    Assert.Multiple(() =>
    {
        Assert.That(entry.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
        Assert.That(entry.AttemptCount, Is.EqualTo(1));
        Assert.That(entry.LastError, Does.Contain("offline"));
        Assert.That(runtime.PrivateMessages, Is.Empty);
    });
}
```

Extend `CreateStartedService` signature:

```csharp
IQChatOwnerEventPublisher? ownerEventPublisher = null
```

Pass it into the `QChatService` constructor:

```csharp
ownerEventPublisher: ownerEventPublisher
```

Add helper:

```csharp
static string CreateTempOwnerEventOutboxPath()
{
    string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-events-service-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return Path.Combine(root, "qchat-owner-events.jsonl");
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter RiskDeleteReportPersistsOwnerEventWhenOwnerMessageSendFails
```

Expected: compile failure because `QChatService` does not accept `ownerEventPublisher`.

- [ ] **Step 3: Add QChatService owner event publisher wiring**

Modify the `QChatService` primary constructor to add:

```csharp
IQChatOwnerEventPublisher? ownerEventPublisher = null
```

Add lazy fields and properties inside `QChatService`:

```csharp
readonly IQChatOwnerEventPublisher? injectedOwnerEventPublisher = ownerEventPublisher;
QChatOwnerEventOutbox? resolvedOwnerEventOutbox;
QChatOwnerEventDispatcher? resolvedOwnerEventDispatcher;
IQChatOwnerEventPublisher? resolvedOwnerEventPublisher;

QChatOwnerEventOutbox OwnerEventOutbox => resolvedOwnerEventOutbox ??= new QChatOwnerEventOutbox(Path.Combine(
    AlifePath.StorageFolderPath,
    "AgentWorkspace",
    "qchat-owner-events.jsonl"));

QChatOwnerEventDispatcher OwnerEventDispatcher => resolvedOwnerEventDispatcher ??= new QChatOwnerEventDispatcher(
    OwnerEventOutbox,
    GetOneBotClient);

IQChatOwnerEventPublisher OwnerEventPublisher => injectedOwnerEventPublisher
    ?? resolvedOwnerEventPublisher
    ??= new QChatOwnerEventPublisher(OwnerEventOutbox, OwnerEventDispatcher);
```

- [ ] **Step 4: Replace direct risk owner send**

Change `SendOwnerRiskReportAsync` to:

```csharp
async Task SendOwnerRiskReportAsync(QChatConfig config, string agentId, string report)
{
    if (config.OwnerId == 0)
        return;

    await OwnerEventPublisher.PublishAsync(new QChatOwnerEventRequest(
        AgentId: agentId,
        OwnerId: config.OwnerId,
        Severity: "warning",
        Category: "risk",
        Source: "qchat-risk",
        SourceId: BuildOwnerRiskSourceId(report),
        DedupeKey: BuildOwnerRiskDedupeKey(agentId, config.OwnerId, report),
        Message: report));
}
```

Add helper methods:

```csharp
static string BuildOwnerRiskSourceId(string report)
{
    string action = ExtractReportField(report, "action");
    string userId = ExtractReportField(report, "user_id");
    return string.IsNullOrWhiteSpace(userId) ? action : $"{action}:{userId}";
}

static string BuildOwnerRiskDedupeKey(string agentId, long ownerId, string report)
{
    string action = ExtractReportField(report, "action");
    string userId = ExtractReportField(report, "user_id");
    string score = ExtractReportField(report, "risk_score");
    string events = ExtractReportField(report, "events");
    return $"risk:{agentId.Trim().ToLowerInvariant()}:{ownerId}:{action}:{userId}:{score}:{events}";
}

static string ExtractReportField(string report, string fieldName)
{
    foreach (string line in report.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        string prefix = fieldName + "=";
        if (line.StartsWith(prefix, StringComparison.Ordinal))
            return line[prefix.Length..].Trim();
    }

    return "";
}
```

- [ ] **Step 5: Run focused risk test and verify it passes**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "RiskDeleteReportPersistsOwnerEventWhenOwnerMessageSendFails|RiskThresholdAutoDeletesFriendAndReportsOwnerWhenEnabled"
```

Expected: both tests pass. The existing success test should still observe a private owner message because the publisher flushes immediately when runtime works.

- [ ] **Step 6: Commit Task 3**

```powershell
git add -- sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "Route QChat risk owner reports through outbox"
```

---

### Task 4: Wire Desktop Business Job Completion Through Outbox

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing desktop completion test**

Add this integration test near existing desktop job completion tests:

```csharp
[Test]
public async Task DesktopJobCompletionPersistsOwnerEventWhenOwnerMessageSendFails()
{
    FakeOneBotRuntime runtime = new() { SendException = new InvalidOperationException("offline") };
    string outboxPath = CreateTempOwnerEventOutboxPath();
    QChatOwnerEventOutbox outbox = new(outboxPath);
    QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
    QChatOwnerEventPublisher publisher = new(outbox, dispatcher);
    FakeDesktopBusinessExecutor executor = new()
    {
        Result = new DesktopBusinessExecutionResult(true, "desktop_execution=started action=open_notepad")
    };
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBalancedTextStreaming = false
    },
    desktopBusinessExecutor: executor,
    ownerEventPublisher: publisher);

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        RawMessage = "/qchat desktop request open_notepad"
    });

    await WaitUntilAsync(() => outbox.GetRecent(10).Any(item => item.Message.Contains("desktop_job=", StringComparison.Ordinal)));

    QChatOwnerEventEntry entry = outbox.GetRecent(10).Single(item => item.Message.Contains("desktop_job=", StringComparison.Ordinal));
    Assert.Multiple(() =>
    {
        Assert.That(entry.Status, Is.EqualTo(QChatOwnerEventStatus.Pending));
        Assert.That(entry.Message, Does.Contain("status=Succeeded"));
        Assert.That(entry.Message, Does.Contain("action=open_notepad"));
        Assert.That(entry.LastError, Does.Contain("offline"));
    });
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter DesktopJobCompletionPersistsOwnerEventWhenOwnerMessageSendFails
```

Expected: test fails because desktop job completion still sends directly through runtime and does not persist owner event.

- [ ] **Step 3: Change completion sink dependency**

In `QChatService.cs`, change `QChatDesktopBusinessJobCompletionSink` constructor from runtime provider to owner event publisher provider:

```csharp
sealed class QChatDesktopBusinessJobCompletionSink(Func<IQChatOwnerEventPublisher> ownerEventPublisherProvider) : IDesktopBusinessJobCompletionSink
```

Change the `DesktopGateway` lazy creation to pass:

```csharp
new QChatDesktopBusinessJobCompletionSink(() => OwnerEventPublisher)
```

Inside `NotifyCompletionAsync`, replace direct runtime send with:

```csharp
await ownerEventPublisherProvider().PublishAsync(new QChatOwnerEventRequest(
    AgentId: job.AgentId,
    OwnerId: job.ActorUserId,
    Severity: job.Status == DesktopBusinessJobStatus.Failed ? "warning" : "info",
    Category: "desktop_job",
    Source: "desktop-business-task-queue",
    SourceId: job.JobId,
    DedupeKey: $"desktop-job:{job.JobId}:{job.Status}",
    Message: message),
    cancellationToken);
```

Keep the terminal-state guard:

```csharp
if (job.Status is not (DesktopBusinessJobStatus.Succeeded or DesktopBusinessJobStatus.Failed))
    return;
```

- [ ] **Step 4: Run desktop job tests and verify they pass**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "DesktopJobCompletionPersistsOwnerEventWhenOwnerMessageSendFails|OwnerXiayuQChatDesktopQueuedJobCompletionSendsCompactOwnerNotification|OwnerXiayuQChatDesktopSlowQueuedJobDoesNotBlockLaterOwnerCommand"
```

Expected: all listed tests pass. The existing compact owner notification test should still receive a private message when runtime works.

- [ ] **Step 5: Commit Task 4**

```powershell
git add -- sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "Persist QChat desktop job completion events"
```

---

### Task 5: Owner-Only Outbox Status and Retry Commands

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing owner command tests**

Add these tests:

```csharp
[Test]
public async Task OwnerQChatEventsStatusReportsOutboxSummaryWithoutModelDispatch()
{
    FakeOneBotRuntime runtime = new();
    QChatOwnerEventOutbox outbox = new(CreateTempOwnerEventOutboxPath());
    QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
    QChatOwnerEventPublisher publisher = new(outbox, dispatcher);
    outbox.Enqueue(new QChatOwnerEventRequest(
        "xiayu",
        1001,
        "warning",
        "risk",
        "test",
        "source-1",
        "events-status-1",
        "action=test result=pending"));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBalancedTextStreaming = false
    },
    ownerEventPublisher: publisher);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        dispatchCount++;
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        RawMessage = "/qchat events status"
    });

    await WaitUntilAsync(() => HasPrivateMessageContaining(runtime, "owner_events="));
    Assert.Multiple(() =>
    {
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(FirstPrivateMessageContaining(runtime, "owner_events="), Does.Contain("pending=1"));
    });
}

[Test]
public async Task NonOwnerQChatEventsStatusIsDeniedWithoutModelDispatch()
{
    FakeOneBotRuntime runtime = new();
    QChatOwnerEventOutbox outbox = new(CreateTempOwnerEventOutboxPath());
    QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
    QChatOwnerEventPublisher publisher = new(outbox, dispatcher);
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        AllowPrivateGuestChat = true,
        EnableBalancedTextStreaming = false
    },
    ownerEventPublisher: publisher);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        dispatchCount++;
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 2001,
        RawMessage = "/qchat events status"
    });

    await WaitUntilAsync(() => HasPrivateMessageContaining(runtime, "Only the owner can use QChat owner events."));
    Assert.That(dispatchCount, Is.Zero);
}

[Test]
public async Task OwnerQChatEventsRetryFlushesPendingEvents()
{
    FakeOneBotRuntime runtime = new() { SendException = new InvalidOperationException("offline") };
    QChatOwnerEventOutbox outbox = new(CreateTempOwnerEventOutboxPath());
    QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
    QChatOwnerEventPublisher publisher = new(outbox, dispatcher);
    outbox.Enqueue(new QChatOwnerEventRequest(
        "xiayu",
        1001,
        "warning",
        "risk",
        "test",
        "source-1",
        "events-retry-1",
        "action=test result=pending"));
    await publisher.FlushAsync();
    runtime.SendException = null;
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBalancedTextStreaming = false
    },
    ownerEventPublisher: publisher);

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        RawMessage = "/qchat events retry"
    });

    await WaitUntilAsync(() => HasPrivateMessageContaining(runtime, "owner_events_retry="));
    Assert.Multiple(() =>
    {
        Assert.That(outbox.GetRecent(10).Single(item => item.DedupeKey == "events-retry-1").Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
        Assert.That(FirstPrivateMessageContaining(runtime, "owner_events_retry="), Does.Contain("delivered=1"));
    });
}
```

The current shared `FakeOneBotRuntime.SendException` property is already settable, so the retry test can clear it with `runtime.SendException = null`.

- [ ] **Step 2: Run command tests and verify they fail**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "OwnerQChatEventsStatusReportsOutboxSummaryWithoutModelDispatch|NonOwnerQChatEventsStatusIsDeniedWithoutModelDispatch|OwnerQChatEventsRetryFlushesPendingEvents"
```

Expected: tests fail because `/qchat events status` and `/qchat events retry` are not handled.

- [ ] **Step 3: Register owner event command handler**

In `BuildOwnerCommandService`, insert this handler before diagnostics:

```csharp
context => TryHandleOwnerEventsCommandAsync(context.MessageEvent, context.SenderRole),
```

Add handler:

```csharp
async Task<bool> TryHandleOwnerEventsCommandAsync(OneBotMessageEvent messageEvent, QChatSenderRole senderRole)
{
    string text = OneBotSegment.GetPlainText(messageEvent.RawMessage).Trim();
    string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length < 3 ||
        parts[0].Equals("/qchat", StringComparison.OrdinalIgnoreCase) == false ||
        parts[1].Equals("events", StringComparison.OrdinalIgnoreCase) == false)
    {
        return false;
    }

    OneBotMessageType targetType = messageEvent.MessageType;
    long targetId = targetType == OneBotMessageType.Group ? messageEvent.GroupId : messageEvent.UserId;
    if (targetId <= 0)
        return true;

    if (senderRole != QChatSenderRole.Owner)
    {
        await SendCommandReplyAsync(messageEvent, senderRole, targetType, targetId, "Only the owner can use QChat owner events.");
        return true;
    }

    string mode = parts[2].ToLowerInvariant();
    if (mode == "status")
    {
        await SendCommandReplyAsync(messageEvent, senderRole, targetType, targetId, FormatOwnerEventStatus());
        return true;
    }

    if (mode == "retry")
    {
        int delivered = await OwnerEventPublisher.FlushAsync();
        await SendCommandReplyAsync(messageEvent, senderRole, targetType, targetId, $"owner_events_retry=completed delivered={delivered}");
        return true;
    }

    await SendCommandReplyAsync(messageEvent, senderRole, targetType, targetId, "usage=/qchat events status|retry");
    return true;
}
```

Add formatter:

```csharp
string FormatOwnerEventStatus()
{
    QChatOwnerEventSummary summary = OwnerEventPublisher.GetSummary();
    return string.Join(Environment.NewLine,
        $"owner_events={summary.Total}",
        $"pending={summary.Pending}",
        $"delivered={summary.Delivered}",
        $"abandoned={summary.Abandoned}",
        $"last_error={NormalizeStatusLine(summary.LastError ?? "none")}");
}
```

- [ ] **Step 4: Run command tests and verify they pass**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "OwnerQChatEventsStatusReportsOutboxSummaryWithoutModelDispatch|NonOwnerQChatEventsStatusIsDeniedWithoutModelDispatch|OwnerQChatEventsRetryFlushesPendingEvents"
```

Expected: command tests pass.

- [ ] **Step 5: Commit Task 5**

```powershell
git add -- sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "Add QChat owner event commands"
```

---

### Task 6: Reconnect and Periodic Flush

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing periodic flush test**

Add this test to `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`:

```csharp
using Alife.Framework.Models.ModuleExtension;

[Test]
public async Task QChatPeriodicUpdateFlushesDueOwnerEvents()
{
    FakeOneBotRuntime runtime = new();
    QChatOwnerEventOutbox outbox = new(CreateTempOwnerEventOutboxPath());
    QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);
    QChatOwnerEventPublisher publisher = new(outbox, dispatcher);
    outbox.Enqueue(new QChatOwnerEventRequest(
        "xiayu",
        1001,
        "warning",
        "risk",
        "test",
        "periodic-source",
        "periodic-event-1",
        "action=test result=pending"));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBalancedTextStreaming = false
    },
    ownerEventPublisher: publisher);
    float seconds = 1;

    ((ITimeIterative)service).OnUpdate(ref seconds);

    await WaitUntilAsync(() => outbox.GetRecent(10).Single(item => item.DedupeKey == "periodic-event-1").Status == QChatOwnerEventStatus.Delivered);
    Assert.That(runtime.PrivateMessages.Any(message => message.Message.Contains("action=test result=pending", StringComparison.Ordinal)), Is.True);
}
```

- [ ] **Step 2: Run periodic flush test and verify it fails**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter QChatPeriodicUpdateFlushesDueOwnerEvents
```

Expected: test fails because `ITimeIterative.OnUpdate` does not flush pending owner events.

- [ ] **Step 3: Add production flush fields**

Add fields near the other runtime timing fields in `QChatService.cs`:

```csharp
DateTime lastOwnerEventFlushAttemptTime;
static readonly TimeSpan OwnerEventPeriodicFlushInterval = TimeSpan.FromSeconds(30);
```

- [ ] **Step 4: Flush after successful reconnect**

After the existing successful connect diagnostic in `ReconnectAsync`, schedule a flush:

```csharp
_ = OwnerEventPublisher.FlushAsync();
```

- [ ] **Step 5: Flush periodically from `ITimeIterative.OnUpdate`**

At the end of `void ITimeIterative.OnUpdate(ref float seconds)`, add:

```csharp
if (IsConnected &&
    DateTime.Now - lastOwnerEventFlushAttemptTime >= OwnerEventPeriodicFlushInterval)
{
    lastOwnerEventFlushAttemptTime = DateTime.Now;
    _ = OwnerEventPublisher.FlushAsync();
}
```

This fire-and-forget call must stay non-blocking so QChat chat handling is not interrupted by outbox retries.

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "OwnerQChatEventsRetryFlushesPendingEvents|QChatPeriodicUpdateFlushesDueOwnerEvents"
```

Expected: tests pass.

- [ ] **Step 7: Commit Task 6**

```powershell
git add -- sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "Flush QChat owner events after reconnect"
```

---

### Task 7: Verification and Cleanup

**Files:**
- Review: all files changed in Tasks 1-6

- [ ] **Step 1: Run focused owner event tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "QChatOwnerEventOutboxTests|QChatOwnerEventDispatcherTests|RiskDeleteReportPersistsOwnerEventWhenOwnerMessageSendFails|DesktopJobCompletionPersistsOwnerEventWhenOwnerMessageSendFails|OwnerQChatEventsStatusReportsOutboxSummaryWithoutModelDispatch|NonOwnerQChatEventsStatusIsDeniedWithoutModelDispatch|OwnerQChatEventsRetryFlushesPendingEvents"
```

Expected: all listed tests pass.

- [ ] **Step 2: Run full QChat tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Expected: QChat suite passes with the existing skipped live tests.

- [ ] **Step 3: Run full solution tests**

```powershell
dotnet test D:\Alife\Alife.slnx --no-restore
```

Expected: solution tests pass.

- [ ] **Step 4: Check whitespace and worktree**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors. `git status --short` should show only intentional files before the final commit.

- [ ] **Step 5: Final commit if uncommitted changes remain**

If Task 7 required small cleanup changes:

```powershell
git add -- sources/Alife.Function/Alife.Function.QChat Tests/Alife.Test.QChat
git commit -m "Verify QChat owner event outbox"
```

## Acceptance Checklist

- Owner risk reports are appended to the local outbox before QQ delivery.
- Desktop business job terminal results are appended to the local outbox before QQ delivery.
- When `SendPrivateMessageWithResult` throws, the event remains pending with attempt count and retry time.
- When delivery succeeds, the event is marked delivered and stores message id when available.
- Recreating the outbox from the same JSONL file reloads pending events.
- Duplicate dedupe keys do not generate duplicate owner notifications.
- `/qchat events status` is owner-only and does not dispatch to the model.
- `/qchat events retry` is owner-only and triggers a deterministic flush.
- Ordinary chat replies are not persisted in the owner event outbox.
- Focused QChat tests and full solution tests pass.

## Implementation Notes

- Keep outbox code in separate files; do not grow `QChatService.cs` with persistence logic.
- Keep delivery failure non-fatal for business tasks.
- Keep persona formatting in the dispatcher, not in stored raw messages.
- Keep all reports machine-readable inside the stored `Message`.
- Keep owner event commands deterministic; they must not call the model.
- Do not add a general QQ message queue in this implementation.
