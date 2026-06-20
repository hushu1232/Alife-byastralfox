using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Memory;

[Module(
    "Autobiographical Memory",
    "Consolidates meaningful recent life events into long-term autobiographical memory.",
    defaultCategory: "Alife Official/Living Environment",
    LaunchOrder = -70)]
public class AutobiographicalMemoryService(
    ILifeEventStream? lifeEventStream = null,
    IAutobiographicalMemorySink? memorySink = null,
    XmlFunctionCaller? functionCaller = null)
    : InteractiveModule<AutobiographicalMemoryService>, IModuleHealthReporter
{
    DateTimeOffset? lastPersistedEventTimestamp;
    readonly HashSet<string> persistedEventIds = new(StringComparer.Ordinal);

    [XmlFunction(FunctionMode.OneShot, name: "remember_life")]
    [Description("Consolidate meaningful recent lived experiences into long-term autobiographical memory.")]
    public async Task RememberLife(int maxEvents = 16, CancellationToken cancellationToken = default)
    {
        string? memoryName = await RememberRecentLifeAsync(maxEvents, cancellationToken);
        Poke(memoryName == null
            ? "No meaningful recent life events were found for long-term memory."
            : $"Autobiographical memory saved: {memoryName}");
    }

    public async Task<string?> RememberRecentLifeAsync(
        int maxEvents = 16,
        CancellationToken cancellationToken = default)
    {
        if (lifeEventStream == null || memorySink == null)
            return null;

        LifeEvent[] selectedEvents = SelectMeaningfulEvents(lifeEventStream.GetRecentEvents(maxEvents));
        if (selectedEvents.Length == 0)
            return null;

        DateTime startTime = selectedEvents.First().Timestamp.DateTime;
        DateTime endTime = selectedEvents.Last().Timestamp.DateTime;
        string summary = BuildSummary(selectedEvents, startTime, endTime);
        string content = BuildContent(selectedEvents, startTime, endTime);

        string memoryName = await memorySink.InsertAutobiographicalMemoryAsync(
            summary,
            content,
            startTime,
            endTime,
            cancellationToken);

        lastPersistedEventTimestamp = selectedEvents.Max(lifeEvent => lifeEvent.Timestamp);
        foreach (string eventId in selectedEvents.Select(lifeEvent => lifeEvent.Id))
            persistedEventIds.Add(eventId);
        lifeEventStream.MarkPersisted(selectedEvents.Select(lifeEvent => lifeEvent.Id));
        return memoryName;
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    public ModuleHealth GetHealth()
    {
        if (lifeEventStream == null && memorySink == null)
            return new ModuleHealth("AutobiographicalMemory", ModuleHealthStatus.Unavailable, "Life event stream and memory sink are unavailable.");
        if (lifeEventStream == null)
            return new ModuleHealth("AutobiographicalMemory", ModuleHealthStatus.Degraded, "Life event stream is unavailable.");
        if (memorySink == null)
            return new ModuleHealth("AutobiographicalMemory", ModuleHealthStatus.Degraded, "Autobiographical memory sink is unavailable.");

        return new ModuleHealth("AutobiographicalMemory", ModuleHealthStatus.Healthy, "Autobiographical memory consolidation is available.");
    }

    LifeEvent[] SelectMeaningfulEvents(IEnumerable<LifeEvent> recentEvents)
    {
        LifeEvent[] candidates = recentEvents
            .Where(lifeEvent => string.IsNullOrWhiteSpace(lifeEvent.Summary) == false)
            .Where(IsMemorySafeEvent)
            .Where(lifeEvent => lifeEvent.IsPersisted == false)
            .Where(lifeEvent => persistedEventIds.Contains(lifeEvent.Id) == false)
            .Where(lifeEvent => lastPersistedEventTimestamp == null || lifeEvent.Timestamp > lastPersistedEventTimestamp)
            .OrderBy(lifeEvent => lifeEvent.Timestamp)
            .ToArray();

        LifeEvent[] directlyMeaningful = candidates
            .Where(IsDirectlyMeaningful)
            .ToArray();

        if (directlyMeaningful.Length > 0)
        {
            DateTimeOffset first = directlyMeaningful.First().Timestamp;
            DateTimeOffset last = directlyMeaningful.Last().Timestamp;
            return candidates
                .Where(lifeEvent => lifeEvent.Timestamp >= first && lifeEvent.Timestamp <= last)
                .Where(lifeEvent => lifeEvent.Kind != LifeEventKind.Body || directlyMeaningful.Length > 1)
                .ToArray();
        }

        LifeEvent[] actionCluster = candidates
            .Where(lifeEvent => lifeEvent.Kind is LifeEventKind.Action or LifeEventKind.Voice)
            .ToArray();
        return actionCluster.Length >= 2 ? actionCluster : [];
    }

    static bool IsDirectlyMeaningful(LifeEvent lifeEvent)
    {
        return lifeEvent.Kind is LifeEventKind.Communication
            or LifeEventKind.Browser
            or LifeEventKind.Memory
            or LifeEventKind.Sense;
    }

    static bool IsMemorySafeEvent(LifeEvent lifeEvent)
    {
        string summary = lifeEvent.Summary ?? "";
        if (IsQChatLivedExperienceEvent(summary))
            return true;

        if (summary.Contains("<qchat", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("</qchat", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("<qchat_quiet_mode", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("XmlFunctionCaller", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("qchat tag error", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("执行qchat标签出错", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("系统报点", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("do not tell the owner", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return lifeEvent.Source.Equals("System", StringComparison.OrdinalIgnoreCase) == false
            || summary.Contains("自动报点", StringComparison.OrdinalIgnoreCase) == false;
    }

    static bool IsQChatLivedExperienceEvent(string summary)
    {
        return summary.Contains("group-decision", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("qchat-quiet-mode-enabled", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("qchat-quiet-mode-disabled", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("qchat-quiet-message-suppressed", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("owner-sleep-command", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("owner-wake-command", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("trusted-wake-user-command", StringComparison.OrdinalIgnoreCase);
    }

    static string BuildSummary(IReadOnlyList<LifeEvent> events, DateTime startTime, DateTime endTime)
    {
        StringBuilder builder = new();
        builder.Append("Autobiographical memory from ");
        builder.Append(startTime.ToString("yyyy-MM-dd HH:mm"));
        builder.Append(" to ");
        builder.Append(endTime.ToString("yyyy-MM-dd HH:mm"));
        builder.AppendLine(":");

        foreach (LifeEvent lifeEvent in events.Take(5))
            builder.AppendLine("- " + FormatLifeEventForMemory(lifeEvent));

        return builder.ToString().TrimEnd();
    }

    static string BuildContent(IReadOnlyList<LifeEvent> events, DateTime startTime, DateTime endTime)
    {
        StringBuilder builder = new();
        builder.AppendLine("You formed an autobiographical memory from recent lived experiences.");
        builder.Append("Time range: ");
        builder.Append(startTime.ToString("yyyy-MM-dd HH:mm"));
        builder.Append(" to ");
        builder.Append(endTime.ToString("yyyy-MM-dd HH:mm"));
        builder.AppendLine(".");
        builder.AppendLine("Events:");

        foreach (LifeEvent lifeEvent in events)
        {
            builder.Append("- [");
            builder.Append(lifeEvent.Kind);
            builder.Append('/');
            builder.Append(string.IsNullOrWhiteSpace(lifeEvent.Source) ? "Unknown" : lifeEvent.Source.Trim());
            builder.Append("] ");
            builder.AppendLine(FormatLifeEventForMemory(lifeEvent));
        }

        return builder.ToString().TrimEnd();
    }

    static string FormatLifeEventForMemory(LifeEvent lifeEvent)
    {
        if (TryFormatQChatLivedExperience(lifeEvent, out string livedExperience))
            return livedExperience;

        return lifeEvent.Summary.Trim();
    }

    static bool TryFormatQChatLivedExperience(LifeEvent lifeEvent, out string livedExperience)
    {
        livedExperience = "";
        if (lifeEvent.Kind != LifeEventKind.Communication ||
            lifeEvent.Source.Equals("QChat", StringComparison.OrdinalIgnoreCase) == false)
        {
            return false;
        }

        string summary = lifeEvent.Summary.Trim();
        if (summary.Contains("group-decision", StringComparison.OrdinalIgnoreCase))
        {
            string groupId = ExtractField(summary, "group");
            string decision = ExtractField(summary, "decision");
            string reason = ExtractField(summary, "reason");

            if (decision.Equals("suppressed", StringComparison.OrdinalIgnoreCase))
            {
                livedExperience = reason switch
                {
                    "social-attention" => $"群 {FormatGroupId(groupId)} 有人在说话，但我判断那不是必须插话的时机。",
                    "cooldown" => $"群 {FormatGroupId(groupId)} 还有人在说话，但我刚刚回应过，所以先慢一点。",
                    "low-information" => $"群 {FormatGroupId(groupId)} 刚出现了很轻的信息，我选择不打断大家。",
                    "active-soft-attention-expired" => $"群 {FormatGroupId(groupId)} 的唤醒热度降下来了，我把注意力收回来一些。",
                    _ => $"群 {FormatGroupId(groupId)} 有消息经过，我选择暂时不插话。"
                };
                return true;
            }

            if (decision.Equals("accepted", StringComparison.OrdinalIgnoreCase))
            {
                livedExperience = reason switch
                {
                    "mention-or-wake" => $"有人把我的注意力叫回群 {FormatGroupId(groupId)}，我短暂进入了回应状态。",
                    "owner-priority" => $"主人在群 {FormatGroupId(groupId)} 里说话，我把注意力优先转过去。",
                    "active-window" => $"群 {FormatGroupId(groupId)} 还在当前对话窗口里，我继续保持关注。",
                    "social-attention" => $"群 {FormatGroupId(groupId)} 的话题值得接一下，我选择参与进去。",
                    _ => $"群 {FormatGroupId(groupId)} 有消息值得回应，我把注意力放过去。"
                };
                return true;
            }
        }

        if (summary.Contains("qchat-quiet-mode-enabled", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("owner-sleep-command", StringComparison.OrdinalIgnoreCase))
        {
            livedExperience = "主人让我安静，我把 QQ 参与欲望降下来了。";
            return true;
        }

        if (summary.Contains("qchat-quiet-mode-disabled", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("owner-wake-command", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("trusted-wake-user-command", StringComparison.OrdinalIgnoreCase))
        {
            livedExperience = "安静状态被唤醒，我可以重新关注 QQ 对话。";
            return true;
        }

        if (summary.Contains("qchat-quiet-message-suppressed", StringComparison.OrdinalIgnoreCase))
        {
            livedExperience = "安静期间有 QQ 消息经过，我保持低参与，没有主动插话。";
            return true;
        }

        return false;
    }

    static string ExtractField(string text, string key)
    {
        string prefix = key + "=";
        int start = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return "";

        start += prefix.Length;
        int end = text.IndexOf(' ', start);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }

    static string FormatGroupId(string groupId)
    {
        return string.IsNullOrWhiteSpace(groupId) ? "当前群" : groupId;
    }
}
