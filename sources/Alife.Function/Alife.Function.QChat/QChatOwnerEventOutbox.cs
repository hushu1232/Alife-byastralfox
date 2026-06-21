using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    string DedupeKey,
    string AgentId,
    long OwnerId,
    string Severity,
    string Category,
    string Source,
    string SourceId,
    string Message);

public sealed record QChatOwnerEventEntry(
    string EventId,
    string DedupeKey,
    string AgentId,
    long OwnerId,
    string Severity,
    string Category,
    string Source,
    string SourceId,
    string Message,
    QChatOwnerEventStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    int AttemptCount,
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
    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    static readonly UTF8Encoding Utf8NoBom = new(false);

    readonly object syncRoot = new();
    readonly string filePath;
    readonly int maxDeliveredEntries;
    readonly Dictionary<string, QChatOwnerEventEntry> entriesById = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> eventIdByDedupeKey = new(StringComparer.Ordinal);

    public QChatOwnerEventOutbox(string filePath, int maxDeliveredEntries = 1000)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        this.filePath = filePath;
        this.maxDeliveredEntries = Math.Max(1, maxDeliveredEntries);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Load();
    }

    public QChatOwnerEventEntry Enqueue(QChatOwnerEventRequest request, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequired(request.DedupeKey, nameof(request.DedupeKey));
        ValidateRequired(request.AgentId, nameof(request.AgentId));
        if (request.OwnerId <= 0)
            throw new ArgumentException("OwnerId must be greater than zero.", nameof(request.OwnerId));
        ValidateRequired(request.Severity, nameof(request.Severity));
        ValidateRequired(request.Category, nameof(request.Category));
        ValidateRequired(request.Source, nameof(request.Source));
        ValidateRequired(request.SourceId, nameof(request.SourceId));
        ValidateRequired(request.Message, nameof(request.Message));

        lock (syncRoot)
        {
            if (eventIdByDedupeKey.TryGetValue(request.DedupeKey, out string? existingEventId) &&
                entriesById.TryGetValue(existingEventId, out QChatOwnerEventEntry? existing))
            {
                return existing;
            }

            DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
            QChatOwnerEventEntry entry = new(
                CreateEventId(request.DedupeKey),
                request.DedupeKey,
                request.AgentId,
                request.OwnerId,
                request.Severity,
                request.Category,
                request.Source,
                request.SourceId,
                request.Message,
                QChatOwnerEventStatus.Pending,
                timestamp,
                timestamp,
                AttemptCount: 0,
                DeliveredAt: null,
                DeliveryMessageId: null,
                LastError: null);

            Store(entry, append: true);
            return entry;
        }
    }

    public QChatOwnerEventEntry? GetById(string eventId)
    {
        lock (syncRoot)
        {
            return entriesById.GetValueOrDefault(eventId);
        }
    }

    public IReadOnlyList<QChatOwnerEventEntry> GetPending(DateTimeOffset? now = null, int maxCount = 20)
    {
        if (maxCount <= 0)
            return [];

        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        lock (syncRoot)
        {
            return entriesById.Values
                .Where(entry => entry.Status == QChatOwnerEventStatus.Pending && entry.NextAttemptAt <= timestamp)
                .OrderBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.EventId, StringComparer.Ordinal)
                .Take(maxCount)
                .ToArray();
        }
    }

    public IReadOnlyList<QChatOwnerEventEntry> GetRecent(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        lock (syncRoot)
        {
            return GetRetainedEntries()
                .OrderByDescending(entry => entry.CreatedAt)
                .ThenByDescending(entry => entry.DeliveredAt)
                .ThenBy(entry => entry.EventId, StringComparer.Ordinal)
                .Take(maxCount)
                .ToArray();
        }
    }

    public QChatOwnerEventSummary GetSummary()
    {
        lock (syncRoot)
        {
            QChatOwnerEventEntry[] entries = GetRetainedEntries().ToArray();
            return new QChatOwnerEventSummary(
                entries.Length,
                entries.Count(entry => entry.Status == QChatOwnerEventStatus.Pending),
                entries.Count(entry => entry.Status == QChatOwnerEventStatus.Delivered),
                entries.Count(entry => entry.Status == QChatOwnerEventStatus.Abandoned),
                entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.LastError))
                    .OrderByDescending(entry => entry.NextAttemptAt)
                    .ThenByDescending(entry => entry.CreatedAt)
                    .Select(entry => entry.LastError)
                    .FirstOrDefault());
        }
    }

    public QChatOwnerEventEntry MarkDelivered(string eventId, long? messageId, DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        lock (syncRoot)
        {
            QChatOwnerEventEntry existing = GetRequired(eventId);
            if (existing.Status == QChatOwnerEventStatus.Delivered)
                return existing;

            QChatOwnerEventEntry delivered = existing with
            {
                Status = QChatOwnerEventStatus.Delivered,
                DeliveredAt = timestamp,
                DeliveryMessageId = messageId,
                LastError = null
            };
            Store(delivered, append: true);
            return delivered;
        }
    }

    public QChatOwnerEventEntry MarkFailed(string eventId, string error, DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        lock (syncRoot)
        {
            QChatOwnerEventEntry existing = GetRequired(eventId);
            if (existing.Status == QChatOwnerEventStatus.Delivered)
                return existing;

            int attemptCount = existing.AttemptCount + 1;
            QChatOwnerEventEntry failed = existing with
            {
                Status = QChatOwnerEventStatus.Pending,
                AttemptCount = attemptCount,
                NextAttemptAt = timestamp + GetRetryDelay(attemptCount),
                LastError = SanitizeError(error)
            };
            Store(failed, append: true);
            return failed;
        }
    }

    static TimeSpan GetRetryDelay(int attemptCount) => attemptCount switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(30)
    };

    static string SanitizeError(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return string.Empty;

        string sanitized = error
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }

    static string CreateEventId(string dedupeKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(dedupeKey));
        StringBuilder builder = new("owner-event-", capacity: "owner-event-".Length + 32);
        for (int i = 0; i < 16; i++)
            builder.Append(hash[i].ToString("x2"));
        return builder.ToString();
    }

    static void ValidateRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} is required.", paramName);
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    void Load()
    {
        if (!File.Exists(filePath))
            return;

        foreach (string line in File.ReadLines(filePath, Utf8NoBom))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            QChatOwnerEventEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<QChatOwnerEventEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry != null && IsValidLoadedEntry(entry))
                StoreLoaded(entry);
        }

    }

    void Store(QChatOwnerEventEntry entry, bool append)
    {
        if (append)
            File.AppendAllText(filePath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, Utf8NoBom);

        entriesById[entry.EventId] = entry;
        eventIdByDedupeKey[entry.DedupeKey] = entry.EventId;
    }

    QChatOwnerEventEntry GetRequired(string eventId)
    {
        ValidateRequired(eventId, nameof(eventId));
        if (!entriesById.TryGetValue(eventId, out QChatOwnerEventEntry? entry))
            throw new KeyNotFoundException($"Owner event '{eventId}' was not found.");

        return entry;
    }

    static bool IsValidLoadedEntry(QChatOwnerEventEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.EventId) &&
        !string.IsNullOrWhiteSpace(entry.DedupeKey) &&
        !string.IsNullOrWhiteSpace(entry.AgentId) &&
        entry.OwnerId > 0 &&
        !string.IsNullOrWhiteSpace(entry.Severity) &&
        !string.IsNullOrWhiteSpace(entry.Category) &&
        !string.IsNullOrWhiteSpace(entry.Source) &&
        !string.IsNullOrWhiteSpace(entry.SourceId) &&
        !string.IsNullOrWhiteSpace(entry.Message) &&
        Enum.IsDefined(entry.Status) &&
        string.Equals(entry.EventId, CreateEventId(entry.DedupeKey), StringComparison.Ordinal);

    void StoreLoaded(QChatOwnerEventEntry entry)
    {
        if (entriesById.TryGetValue(entry.EventId, out QChatOwnerEventEntry? existing) &&
            existing.Status == QChatOwnerEventStatus.Delivered &&
            entry.Status != QChatOwnerEventStatus.Delivered)
        {
            return;
        }

        Store(entry, append: false);
    }

    IEnumerable<QChatOwnerEventEntry> GetRetainedEntries()
    {
        HashSet<string> retainedDeliveredIds = entriesById.Values
            .Where(entry => entry.Status == QChatOwnerEventStatus.Delivered)
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.DeliveredAt)
            .ThenBy(entry => entry.EventId, StringComparer.Ordinal)
            .Take(maxDeliveredEntries)
            .Select(entry => entry.EventId)
            .ToHashSet(StringComparer.Ordinal);

        return entriesById.Values.Where(entry =>
            entry.Status != QChatOwnerEventStatus.Delivered ||
            retainedDeliveredIds.Contains(entry.EventId));
    }
}
