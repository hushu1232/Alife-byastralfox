using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;

namespace Alife.Function.MessageFilter;

[Module(
    "Life Event Stream",
    "Keeps a bounded short-term stream of recent embodied experiences for self-context injection.",
    defaultCategory: "Alife Official/Living Environment",
    LaunchOrder = -85)]
public class LifeEventStreamService
    : InteractiveModule<LifeEventStreamService>, ILifeEventStream, IModuleHealthReporter
{
    readonly object syncRoot = new();
    readonly List<LifeEvent> events = new();
    readonly int maxRetainedEvents;
    string? storeFilePath;

    public LifeEventStreamService(int maxRetainedEvents = 32, string? storagePath = null)
    {
        this.maxRetainedEvents = Math.Max(1, maxRetainedEvents);
        if (string.IsNullOrWhiteSpace(storagePath) == false)
        {
            InitializeStorage(storagePath);
        }
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        if (storeFilePath == null)
            InitializeStorage(Path.Combine(AlifePath.StorageFolderPath, context.Character.StorageKey, "LifeEvents"));
    }

    public void Publish(LifeEvent lifeEvent)
    {
        if (string.IsNullOrWhiteSpace(lifeEvent.Summary))
            return;

        lock (syncRoot)
        {
            LifeEvent normalized = Normalize(lifeEvent);

            events.RemoveAll(existing => existing.Id == normalized.Id);
            events.Add(normalized);
            int overflow = events.Count - maxRetainedEvents;
            if (overflow > 0)
                events.RemoveRange(0, overflow);

            SavePersistedEvents();
        }
    }

    public IReadOnlyList<LifeEvent> GetRecentEvents(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        lock (syncRoot)
        {
            return events
                .OrderBy(lifeEvent => lifeEvent.Timestamp)
                .TakeLast(maxCount)
                .ToArray();
        }
    }

    public ModuleHealth GetHealth()
    {
        int count;
        lock (syncRoot)
        {
            count = events.Count;
        }

        return new ModuleHealth("LifeEventStream", ModuleHealthStatus.Healthy, $"In-memory life event stream is available; retained events: {count}.");
    }

    public void MarkPersisted(IEnumerable<string> eventIds)
    {
        HashSet<string> ids = eventIds
            .Where(id => string.IsNullOrWhiteSpace(id) == false)
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return;

        lock (syncRoot)
        {
            for (int index = 0; index < events.Count; index++)
            {
                if (ids.Contains(events[index].Id))
                    events[index] = events[index] with { IsPersisted = true };
            }

            SavePersistedEvents();
        }
    }

    public static string FormatRecentExperiences(
        IEnumerable<LifeEvent> recentEvents,
        string? currentMessage = null,
        int maxCount = 8,
        int maxSummaryLength = 180)
    {
        HashSet<string> currentQqIds = ExtractQqIds(currentMessage ?? "");
        LifeEvent[] selectedEvents = recentEvents
            .Where(lifeEvent => string.IsNullOrWhiteSpace(lifeEvent.Summary) == false)
            .Where(lifeEvent => IsRelevantToCurrentMessage(lifeEvent, currentQqIds))
            .OrderBy(lifeEvent => lifeEvent.Timestamp)
            .TakeLast(Math.Max(0, maxCount))
            .ToArray();

        if (selectedEvents.Length == 0)
            return string.Empty;

        int summaryLength = Math.Max(8, maxSummaryLength);
        StringBuilder builder = new();
        builder.AppendLine("[Recent experiences]");
        foreach (LifeEvent lifeEvent in selectedEvents)
        {
            builder.Append("- ");
            builder.Append(lifeEvent.Timestamp.ToString("HH:mm"));
            builder.Append(' ');
            builder.AppendLine(Truncate(lifeEvent.Summary.Trim(), summaryLength));
        }
        builder.Append("[/Recent experiences]");
        return builder.ToString();
    }

    static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text[..maxLength] + "...";
    }

    static bool IsRelevantToCurrentMessage(LifeEvent lifeEvent, IReadOnlySet<string> currentQqIds)
    {
        if (currentQqIds.Count == 0)
            return true;
        if (lifeEvent.Kind != LifeEventKind.Communication ||
            lifeEvent.Source.Equals("QChat", StringComparison.OrdinalIgnoreCase) == false)
        {
            return true;
        }

        HashSet<string> eventQqIds = ExtractQqIds($"{lifeEvent.Source} {lifeEvent.Summary}");
        return eventQqIds.Count == 0 || eventQqIds.Overlaps(currentQqIds);
    }

    static HashSet<string> ExtractQqIds(string text)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
            return ids;

        foreach (Match match in Regex.Matches(text, @"(?<!\d)\d{4,12}(?!\d)"))
            ids.Add(match.Value);
        return ids;
    }

    void LoadPersistedEvents()
    {
        if (storeFilePath == null || File.Exists(storeFilePath) == false)
            return;

        foreach (string line in File.ReadLines(storeFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                LifeEvent? lifeEvent = JsonSerializer.Deserialize<LifeEvent>(line);
                if (lifeEvent != null && string.IsNullOrWhiteSpace(lifeEvent.Summary) == false)
                {
                    LifeEvent normalized = Normalize(lifeEvent);
                    int existingIndex = events.FindIndex(existing => existing.Id == normalized.Id);
                    if (existingIndex < 0)
                    {
                        events.Add(normalized);
                    }
                    else if (normalized.Timestamp >= events[existingIndex].Timestamp)
                    {
                        events[existingIndex] = normalized;
                    }
                }
            }
            catch
            {
                // Ignore malformed historical lines; the stream is best-effort context.
            }
        }

        events.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
        int overflow = events.Count - maxRetainedEvents;
        if (overflow > 0)
            events.RemoveRange(0, overflow);
    }

    static LifeEvent Normalize(LifeEvent lifeEvent)
    {
        return lifeEvent with
        {
            Id = string.IsNullOrWhiteSpace(lifeEvent.Id) ? Guid.NewGuid().ToString("N") : lifeEvent.Id.Trim(),
            Importance = Math.Max(0, lifeEvent.Importance),
            Source = string.IsNullOrWhiteSpace(lifeEvent.Source) ? "Unknown" : lifeEvent.Source.Trim(),
            Summary = lifeEvent.Summary.Trim()
        };
    }

    void SavePersistedEvents()
    {
        if (storeFilePath == null)
            return;

        File.WriteAllLines(storeFilePath, events.Select(lifeEvent => JsonSerializer.Serialize(lifeEvent)));
    }

    void InitializeStorage(string storagePath)
    {
        Directory.CreateDirectory(storagePath);
        storeFilePath = Path.Combine(storagePath, "life-events.jsonl");
        LoadPersistedEvents();
    }
}
