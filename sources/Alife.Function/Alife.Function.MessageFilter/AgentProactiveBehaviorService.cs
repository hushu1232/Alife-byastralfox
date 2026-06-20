using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;

namespace Alife.Function.Agent;

public enum AgentProactiveActionKind
{
    None,
    Chat,
    QZoneReply,
    QZoneLike,
    DeskPetExpression,
    OwnerReminder
}

public sealed record AgentProactiveSuggestion(
    AgentProactiveActionKind Kind,
    string Reason,
    AgentAuditRiskLevel RiskLevel,
    bool RequiresOwnerConfirmation,
    string? TargetType = null,
    long? TargetId = null,
    string? DraftText = null);

public enum AgentProactivePendingStatus
{
    Pending,
    Confirmed,
    Dismissed,
    Executed
}

public sealed record AgentProactivePendingSuggestion(
    string Id,
    AgentProactiveSuggestion Suggestion,
    DateTimeOffset CreatedAt,
    AgentProactivePendingStatus Status,
    string Source);

public sealed record AgentProactiveSuggestionContext(
    AgentSelfModelSnapshot Snapshot,
    IReadOnlyList<LifeEvent> RecentExperiences);

public interface IAgentProactiveSuggestionProvider
{
    IReadOnlyList<AgentProactiveSuggestion> BuildSuggestions(AgentProactiveSuggestionContext context);
}

public sealed record AgentProactiveExternalExecutionResult(
    bool Succeeded,
    string Message);

public interface IAgentProactiveSuggestionExecutor
{
    bool CanExecute(AgentProactivePendingSuggestion pending);
    Task<AgentProactiveExternalExecutionResult> ExecuteAsync(AgentProactivePendingSuggestion pending);
}

public sealed class AgentProactiveSuggestionPersistenceState
{
    public List<AgentProactivePendingSuggestion> Pending { get; set; } = [];
    public List<AgentProactivePendingSuggestion> Completed { get; set; } = [];
}

public sealed record AgentProactiveCleanupResult(
    int ExpiredPendingCount,
    int RemovedCompletedCount);

[Module(
    "Agent Proactive Behavior",
    "Builds auditable proactive behavior suggestions from the agent self-model, control-center policy, recent events, and cooldown limits.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -63)]
public class AgentProactiveBehaviorService(
    AgentSelfModelService? selfModel = null,
    AgentControlCenterService? controlCenter = null,
    AgentAuditLogService? auditLog = null,
    ILifeEventStream? lifeEvents = null,
    IEnumerable<IAgentProactiveSuggestionProvider>? suggestionProviders = null,
    Func<DateTimeOffset>? clock = null,
    TimeSpan? minimumCooldown = null,
    string? persistencePath = null,
    XmlFunctionCaller? functionCaller = null)
    : InteractiveModule<AgentProactiveBehaviorService>
{
    readonly AgentSelfModelService? selfModel = selfModel;
    readonly AgentControlCenterService? controlCenter = controlCenter;
    readonly AgentAuditLogService? auditLog = auditLog;
    readonly ILifeEventStream? lifeEvents = lifeEvents;
    readonly IEnumerable<IAgentProactiveSuggestionProvider> suggestionProviders = suggestionProviders ?? [];
    readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.Now);
    readonly TimeSpan? minimumCooldown = minimumCooldown;
    string? persistenceFilePath = NormalizePersistencePath(persistencePath);
    readonly object pendingGate = new();
    readonly Dictionary<string, AgentProactivePendingSuggestion> pendingSuggestions = new(StringComparer.Ordinal);
    readonly Dictionary<string, AgentProactivePendingSuggestion> completedSuggestions = new(StringComparer.Ordinal);
    DateTimeOffset? lastSuggestionAt;
    bool persistenceLoaded;

    [XmlFunction(FunctionMode.OneShot, name: "agent_proactive_status")]
    [Description("Show the current proactive behavior suggestions. This only proposes actions and never executes QQ, QZone, or DeskPet side effects.")]
    public void ShowProactiveStatus()
    {
        AgentSelfModelSnapshot? snapshot = TryBuildLiveSnapshot();
        if (snapshot == null)
        {
            Poke("Agent proactive behavior: self-model is unavailable.");
            return;
        }

        Poke(FormatSuggestions(BuildSuggestions(snapshot)));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_proactive_pending")]
    [Description("Show pending proactive suggestions waiting for owner confirmation. This never executes external actions.")]
    public void ShowPendingSuggestions()
    {
        Poke(FormatPendingSuggestions(GetPendingSuggestions()));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_proactive_cleanup")]
    [Description("Expire stale pending proactive suggestions and remove old completed proactive suggestion history. This never executes external actions.")]
    public void CleanupSuggestionsFromXml(int maxPendingHours = 24, int maxCompletedDays = 30)
    {
        AgentProactiveCleanupResult result = CleanupSuggestions(
            TimeSpan.FromHours(Math.Max(1, maxPendingHours)),
            TimeSpan.FromDays(Math.Max(1, maxCompletedDays)),
            "agent");
        Poke($"Proactive cleanup: expired pending={result.ExpiredPendingCount}; removed completed={result.RemovedCompletedCount}");
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_proactive_confirmation_text")]
    [Description("Show the owner confirmation command for a pending proactive suggestion.")]
    public void ShowPendingSuggestionConfirmationText(string id)
    {
        AgentProactivePendingSuggestion? pending = GetPendingSuggestions().FirstOrDefault(item => item.Id == id);
        Poke(pending == null
            ? "Pending proactive suggestion was not found."
            : BuildPendingSuggestionConfirmationText(pending));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_proactive_prepare_qzone_reply")]
    [Description("Attach an explicit natural reply content draft to a pending QZone reply suggestion before owner confirmation. This never executes external actions.")]
    public void PrepareQZoneReplyContentFromXml(string id, string content)
    {
        AgentProactivePendingSuggestion pending = PrepareQZoneReplyContent(id, content, "agent");
        Poke($"Prepared QZone reply content for proactive suggestion: {pending.Id}");
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_proactive_confirm", riskLevel: XmlFunctionRiskLevel.High)]
    [Description("Confirm a pending proactive suggestion for later execution by an action executor. This method does not directly perform QQ, QZone, DeskPet, or workspace side effects.")]
    public void ConfirmPendingSuggestionFromXml(string id)
    {
        AgentProactivePendingSuggestion pending = ConfirmPendingSuggestion(id, "owner");
        Poke($"""
              Proactive suggestion confirmed: {pending.Id} {pending.Suggestion.Kind}
              Next: {BuildConfirmedSuggestionExecutionText(pending)}
              """);
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_proactive_dismiss")]
    [Description("Dismiss a pending proactive suggestion without executing it.")]
    public void DismissPendingSuggestionFromXml(string id)
    {
        AgentProactivePendingSuggestion pending = DismissPendingSuggestion(id, "owner");
        Poke($"Proactive suggestion dismissed: {pending.Id} {pending.Suggestion.Kind}");
    }

    public IReadOnlyList<AgentProactiveSuggestion> BuildSuggestions(AgentSelfModelSnapshot snapshot)
    {
        AgentControlCenterConfig config = controlCenter?.Configuration ?? new AgentControlCenterConfig();
        if (config.AllowProactiveChat == false || config.ProactiveChatIntensity <= 0)
            return RecordAndReturn([CreateNone("Proactive chat is disabled.", "policy-disabled")]);

        DateTimeOffset now = clock();
        TimeSpan cooldown = ResolveCooldown(config);
        if (lastSuggestionAt is { } previous && now - previous < cooldown)
        {
            return RecordAndReturn([
                CreateNone($"Proactive behavior is in cooldown until {previous.Add(cooldown):HH:mm:ss}.", "cooldown")
            ]);
        }

        List<AgentProactiveSuggestion> suggestions = [];
        LifeEvent[] recentExperiences = MergeRecentExperiences(snapshot);
        AgentProactiveSuggestionContext providerContext = new(snapshot, recentExperiences);
        suggestions.AddRange(BuildProviderSuggestions(providerContext));

        if (suggestions.Any(suggestion => suggestion.Kind is AgentProactiveActionKind.QZoneReply or AgentProactiveActionKind.QZoneLike) == false
            && ShouldSuggestQZoneReply(recentExperiences))
        {
            suggestions.Add(new AgentProactiveSuggestion(
                AgentProactiveActionKind.QZoneReply,
                "Recent QZone activity may deserve a natural reply, but QZone writing is high risk.",
                AgentAuditRiskLevel.High,
                RequiresOwnerConfirmation: true,
                TargetType: "qzone",
                DraftText: "可以回复一条简短、自然、不打扰对方的评论。"));
        }

        if (ShouldSuggestDeskPetExpression(snapshot, recentExperiences))
        {
            suggestions.Add(new AgentProactiveSuggestion(
                AgentProactiveActionKind.DeskPetExpression,
                "A recent owner request or active task can be acknowledged through a low-risk embodied expression.",
                AgentAuditRiskLevel.Low,
                RequiresOwnerConfirmation: false,
                TargetType: "deskpet",
                DraftText: "继续处理中，我会保持安静但有反馈。"));
        }

        if (suggestions.Count == 0)
            return RecordAndReturn([CreateNone("No proactive action is currently useful.", "no-opportunity")]);

        lastSuggestionAt = now;
        IReadOnlyList<AgentProactiveSuggestion> recordedSuggestions = RecordAndReturn(suggestions);
        QueueOwnerConfirmedSuggestions(recordedSuggestions, "build-suggestions");
        return recordedSuggestions;
    }

    public static string FormatSuggestions(IEnumerable<AgentProactiveSuggestion> suggestions)
    {
        AgentProactiveSuggestion[] items = suggestions.ToArray();
        StringBuilder builder = new();
        builder.AppendLine("Agent proactive behavior suggestions:");
        if (items.Length == 0)
        {
            builder.Append("- none");
            return builder.ToString();
        }

        foreach (AgentProactiveSuggestion suggestion in items)
        {
            builder.Append("- ");
            builder.Append(suggestion.Kind);
            builder.Append($": risk={suggestion.RiskLevel}; confirmation={(suggestion.RequiresOwnerConfirmation ? "required" : "not required")}; ");
            builder.Append(suggestion.Reason);
            if (string.IsNullOrWhiteSpace(suggestion.DraftText) == false)
                builder.Append($" Draft: {suggestion.DraftText}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public AgentProactivePendingSuggestion EnqueuePendingSuggestion(
        AgentProactiveSuggestion suggestion,
        string source = "agent-proactive")
    {
        if (suggestion.RequiresOwnerConfirmation == false)
            throw new InvalidOperationException("Only suggestions requiring owner confirmation can be queued.");

        EnsurePersistenceLoaded();
        AgentProactivePendingSuggestion pending = new(
            Guid.NewGuid().ToString("N"),
            suggestion,
            clock(),
            AgentProactivePendingStatus.Pending,
            string.IsNullOrWhiteSpace(source) ? "agent-proactive" : source.Trim());

        lock (pendingGate)
            pendingSuggestions[pending.Id] = pending;
        PersistSuggestions();

        auditLog?.Record(
            "agent.proactive.pending",
            "agent",
            $"{pending.Suggestion.Kind}: {pending.Suggestion.Reason}",
            pending.Suggestion.RiskLevel,
            true);

        return pending;
    }

    public IReadOnlyList<AgentProactivePendingSuggestion> GetPendingSuggestions()
    {
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            return pendingSuggestions.Values
                .Where(pending => pending.Status == AgentProactivePendingStatus.Pending)
                .OrderBy(pending => pending.CreatedAt)
                .ToArray();
        }
    }

    public AgentProactivePendingSuggestion ConfirmPendingSuggestion(string id, string actor)
    {
        return CompletePendingSuggestion(id, actor, AgentProactivePendingStatus.Confirmed);
    }

    public AgentProactivePendingSuggestion DismissPendingSuggestion(string id, string actor)
    {
        return CompletePendingSuggestion(id, actor, AgentProactivePendingStatus.Dismissed);
    }

    public AgentProactivePendingSuggestion PrepareQZoneReplyContent(
        string id,
        string content,
        string actor)
    {
        string normalizedContent = NormalizeReplyContent(content);
        AgentProactivePendingSuggestion updated;
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            if (pendingSuggestions.TryGetValue(id, out AgentProactivePendingSuggestion? pending) == false)
                throw new InvalidOperationException("QZone reply content can only be prepared for a pending proactive suggestion.");
            if (pending.Status != AgentProactivePendingStatus.Pending)
                throw new InvalidOperationException("QZone reply content can only be prepared while the suggestion is pending.");
            if (pending.Suggestion.Kind != AgentProactiveActionKind.QZoneReply
                || pending.Suggestion.TargetType?.Equals("qzone", StringComparison.OrdinalIgnoreCase) != true)
                throw new InvalidOperationException("QZone reply content can only be prepared for a QZone reply suggestion.");

            AgentProactiveSuggestion preparedSuggestion = pending.Suggestion with
            {
                DraftText = AppendOrReplaceDraftContent(pending.Suggestion.DraftText ?? string.Empty, normalizedContent)
            };
            updated = pending with { Suggestion = preparedSuggestion };
            pendingSuggestions[id] = updated;
        }
        PersistSuggestions();

        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim();
        auditLog?.Record(
            "agent.proactive.qzone_reply_prepared",
            normalizedActor,
            $"{updated.Suggestion.Kind}: content prepared for owner confirmation",
            updated.Suggestion.RiskLevel,
            true);
        return updated;
    }

    public AgentProactivePendingSuggestion? GetCompletedSuggestion(string id)
    {
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            return completedSuggestions.TryGetValue(id, out AgentProactivePendingSuggestion? pending)
                ? pending
                : null;
        }
    }

    public IReadOnlyList<AgentProactivePendingSuggestion> GetCompletedSuggestions()
    {
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            return completedSuggestions.Values
                .OrderByDescending(pending => pending.CreatedAt)
                .ToArray();
        }
    }

    public AgentProactiveCleanupResult CleanupSuggestions(
        TimeSpan maxPendingAge,
        TimeSpan maxCompletedAge,
        string actor)
    {
        if (maxPendingAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxPendingAge), "Pending suggestion age must be positive.");
        if (maxCompletedAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxCompletedAge), "Completed suggestion age must be positive.");

        DateTimeOffset now = clock();
        List<AgentProactivePendingSuggestion> expiredPending;
        int removedCompletedCount;
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            expiredPending = pendingSuggestions.Values
                .Where(pending => now - pending.CreatedAt > maxPendingAge)
                .Select(pending => pending with { Status = AgentProactivePendingStatus.Dismissed })
                .ToList();

            foreach (AgentProactivePendingSuggestion expired in expiredPending)
            {
                pendingSuggestions.Remove(expired.Id);
                completedSuggestions[expired.Id] = expired;
            }

            string[] oldCompletedIds = completedSuggestions.Values
                .Where(completed => now - completed.CreatedAt > maxCompletedAge)
                .Select(completed => completed.Id)
                .ToArray();
            foreach (string id in oldCompletedIds)
                completedSuggestions.Remove(id);
            removedCompletedCount = oldCompletedIds.Length;
        }

        if (expiredPending.Count > 0 || removedCompletedCount > 0)
            PersistSuggestions();

        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim();
        auditLog?.Record(
            "agent.proactive.cleanup",
            normalizedActor,
            $"expired_pending={expiredPending.Count}; removed_completed={removedCompletedCount}",
            AgentAuditRiskLevel.Low,
            true);
        return new AgentProactiveCleanupResult(expiredPending.Count, removedCompletedCount);
    }

    public AgentProactivePendingSuggestion MarkSuggestionExecuted(
        string id,
        string actor,
        string detail = "")
    {
        AgentProactivePendingSuggestion updated;
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            if (completedSuggestions.TryGetValue(id, out AgentProactivePendingSuggestion? pending) == false)
                throw new InvalidOperationException("Completed proactive suggestion was not found.");
            if (pending.Status == AgentProactivePendingStatus.Executed)
                throw new InvalidOperationException("Proactive suggestion was already executed.");
            if (pending.Status != AgentProactivePendingStatus.Confirmed)
                throw new InvalidOperationException("Proactive suggestion must be confirmed before execution.");

            updated = pending with { Status = AgentProactivePendingStatus.Executed };
            completedSuggestions[id] = updated;
        }
        PersistSuggestions();

        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim();
        auditLog?.Record(
            "agent.proactive.executed",
            normalizedActor,
            $"{updated.Suggestion.Kind}: {updated.Suggestion.Reason}; {detail}".TrimEnd(' ', ';'),
            updated.Suggestion.RiskLevel,
            true);
        return updated;
    }

    public static string BuildPendingSuggestionConfirmationText(AgentProactivePendingSuggestion pending)
    {
        return $"confirm execute <agent_proactive_confirm id=\"{EscapeXmlAttribute(pending.Id)}\" />";
    }

    public static string BuildConfirmedSuggestionExecutionText(AgentProactivePendingSuggestion pending)
    {
        if (pending.Status == AgentProactivePendingStatus.Executed)
            return $"Proactive suggestion {pending.Id} was already executed.";
        if (pending.Status != AgentProactivePendingStatus.Confirmed)
            return $"Proactive suggestion {pending.Id} is not confirmed.";
        if (pending.Suggestion.TargetType?.Equals("qzone", StringComparison.OrdinalIgnoreCase) == true
            || pending.Suggestion.Kind is AgentProactiveActionKind.QZoneLike or AgentProactiveActionKind.QZoneReply)
            return $"confirm execute <qzone_proactive_execute id=\"{EscapeXmlAttribute(pending.Id)}\" />";

        return $"No external executor is registered for {pending.Suggestion.Kind}.";
    }

    public static string FormatPendingSuggestions(IEnumerable<AgentProactivePendingSuggestion> pendingSuggestions)
    {
        AgentProactivePendingSuggestion[] items = pendingSuggestions.ToArray();
        if (items.Length == 0)
            return "No pending proactive suggestions.";

        StringBuilder builder = new();
        builder.AppendLine("Pending proactive suggestions:");
        foreach (AgentProactivePendingSuggestion pending in items)
        {
            builder.Append("- ");
            builder.Append(pending.Id);
            builder.Append(' ');
            builder.Append(pending.Suggestion.Kind);
            builder.Append($": risk={pending.Suggestion.RiskLevel}; ");
            builder.Append(pending.Suggestion.Reason);
            if (string.IsNullOrWhiteSpace(pending.Suggestion.DraftText) == false)
                builder.Append($" Draft: {pending.Suggestion.DraftText}");
            builder.AppendLine();
            builder.AppendLine($"  Confirm: {BuildPendingSuggestionConfirmationText(pending)}");
        }

        return builder.ToString().TrimEnd();
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        persistenceFilePath ??= CreateDefaultPersistencePath();
        EnsurePersistenceLoaded();
        functionCaller?.RegisterHandler(this);
    }

    AgentSelfModelSnapshot? TryBuildLiveSnapshot()
    {
        if (selfModel == null)
            return null;

        try
        {
            return selfModel.BuildSnapshot(ChatBot.GetRuntimeState(), Character.Name);
        }
        catch (NullReferenceException)
        {
            return null;
        }
    }

    IReadOnlyList<AgentProactiveSuggestion> RecordAndReturn(IReadOnlyList<AgentProactiveSuggestion> suggestions)
    {
        if (auditLog == null)
            return suggestions;

        foreach (AgentProactiveSuggestion suggestion in suggestions)
        {
            string action = suggestion.Kind == AgentProactiveActionKind.None
                ? "agent.proactive.skipped"
                : "agent.proactive.suggested";
            auditLog.Record(
                action,
                "agent",
                $"{suggestion.Kind}: {suggestion.Reason}",
                suggestion.RiskLevel,
                true);
        }

        return suggestions;
    }

    void QueueOwnerConfirmedSuggestions(IEnumerable<AgentProactiveSuggestion> suggestions, string source)
    {
        foreach (AgentProactiveSuggestion suggestion in suggestions)
        {
            if (suggestion.RequiresOwnerConfirmation == false)
                continue;
            if (suggestion.Kind == AgentProactiveActionKind.None)
                continue;
            if (HasEquivalentPendingSuggestion(suggestion))
                continue;

            EnqueuePendingSuggestion(suggestion, source);
        }
    }

    bool HasEquivalentPendingSuggestion(AgentProactiveSuggestion suggestion)
    {
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            return pendingSuggestions.Values.Any(pending =>
                pending.Status == AgentProactivePendingStatus.Pending
                && pending.Suggestion.Kind == suggestion.Kind
                && pending.Suggestion.TargetType == suggestion.TargetType
                && pending.Suggestion.TargetId == suggestion.TargetId
                && pending.Suggestion.DraftText == suggestion.DraftText);
        }
    }

    AgentProactivePendingSuggestion CompletePendingSuggestion(
        string id,
        string actor,
        AgentProactivePendingStatus status)
    {
        if (status == AgentProactivePendingStatus.Pending)
            throw new ArgumentException("Completion status cannot be Pending.", nameof(status));

        AgentProactivePendingSuggestion updated;
        EnsurePersistenceLoaded();
        lock (pendingGate)
        {
            if (pendingSuggestions.TryGetValue(id, out AgentProactivePendingSuggestion? pending) == false)
                throw new InvalidOperationException("Pending proactive suggestion was not found.");

            updated = pending with { Status = status };
            pendingSuggestions.Remove(id);
            completedSuggestions[id] = updated;
        }
        PersistSuggestions();

        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "owner" : actor.Trim();
        auditLog?.Record(
            status == AgentProactivePendingStatus.Confirmed
                ? "agent.proactive.confirmed"
                : "agent.proactive.dismissed",
            normalizedActor,
            $"{updated.Suggestion.Kind}: {updated.Suggestion.Reason}",
            updated.Suggestion.RiskLevel,
            true);
        return updated;
    }

    IReadOnlyList<AgentProactiveSuggestion> BuildProviderSuggestions(AgentProactiveSuggestionContext context)
    {
        List<AgentProactiveSuggestion> suggestions = [];
        foreach (IAgentProactiveSuggestionProvider provider in suggestionProviders)
        {
            try
            {
                suggestions.AddRange(provider.BuildSuggestions(context)
                    .Where(suggestion => suggestion.Kind != AgentProactiveActionKind.None));
            }
            catch (Exception exception)
            {
                suggestions.Add(new AgentProactiveSuggestion(
                    AgentProactiveActionKind.None,
                    $"Proactive suggestion provider {provider.GetType().Name} failed: {exception.Message}",
                    AgentAuditRiskLevel.Low,
                    RequiresOwnerConfirmation: false,
                    TargetType: "provider-error"));
            }
        }

        return suggestions;
    }

    LifeEvent[] MergeRecentExperiences(AgentSelfModelSnapshot snapshot)
    {
        return snapshot.RecentExperiences
            .Concat(lifeEvents?.GetRecentEvents(8) ?? [])
            .Where(lifeEvent => string.IsNullOrWhiteSpace(lifeEvent.Summary) == false)
            .GroupBy(lifeEvent => lifeEvent.Id, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(lifeEvent => lifeEvent.Timestamp)
            .TakeLast(12)
            .ToArray();
    }

    TimeSpan ResolveCooldown(AgentControlCenterConfig config)
    {
        if (minimumCooldown is { } explicitCooldown)
            return explicitCooldown;

        int intensity = Math.Clamp(config.ProactiveChatIntensity, 1, 10);
        return TimeSpan.FromMinutes(Math.Max(2, 18 - intensity * 3));
    }

    static AgentProactiveSuggestion CreateNone(string reason, string targetType)
    {
        return new AgentProactiveSuggestion(
            AgentProactiveActionKind.None,
            reason,
            AgentAuditRiskLevel.Low,
            RequiresOwnerConfirmation: false,
            TargetType: targetType);
    }

    static bool ShouldSuggestQZoneReply(IEnumerable<LifeEvent> recentExperiences)
    {
        return recentExperiences.Any(lifeEvent =>
            lifeEvent.Source.Contains("QZone", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(lifeEvent.Summary, "comment", "reply", "评论", "回复", "动态", "posted"));
    }

    static bool ShouldSuggestDeskPetExpression(
        AgentSelfModelSnapshot snapshot,
        IEnumerable<LifeEvent> recentExperiences)
    {
        bool ownerAskedToContinue = recentExperiences.Any(lifeEvent =>
            ContainsAny(lifeEvent.Summary, "owner", "主人", "continue", "继续"));
        bool activeTask = snapshot.LatestTask is { Status: AgentTaskStatus.Planned or AgentTaskStatus.Running };

        return ownerAskedToContinue || activeTask;
    }

    static bool ContainsAny(string text, params string[] tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    void EnsurePersistenceLoaded()
    {
        lock (pendingGate)
        {
            if (persistenceLoaded)
                return;

            persistenceLoaded = true;
            if (persistenceFilePath == null || File.Exists(persistenceFilePath) == false)
                return;

            AgentProactiveSuggestionPersistenceState? state;
            try
            {
                state = JsonSerializer.Deserialize<AgentProactiveSuggestionPersistenceState>(
                    File.ReadAllText(persistenceFilePath));
            }
            catch (JsonException)
            {
                return;
            }

            if (state == null)
                return;

            foreach (AgentProactivePendingSuggestion pending in state.Pending)
            {
                if (pending.Status == AgentProactivePendingStatus.Pending)
                    pendingSuggestions[pending.Id] = pending;
                else
                    completedSuggestions[pending.Id] = pending;
            }

            foreach (AgentProactivePendingSuggestion completed in state.Completed)
                completedSuggestions[completed.Id] = completed;
        }
    }

    void PersistSuggestions()
    {
        if (persistenceFilePath == null)
            return;

        AgentProactiveSuggestionPersistenceState state;
        lock (pendingGate)
        {
            state = new AgentProactiveSuggestionPersistenceState
            {
                Pending = pendingSuggestions.Values
                    .OrderBy(pending => pending.CreatedAt)
                    .ToList(),
                Completed = completedSuggestions.Values
                    .OrderByDescending(pending => pending.CreatedAt)
                    .ToList()
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(persistenceFilePath)!);
        File.WriteAllText(persistenceFilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    static string? NormalizePersistencePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    static string CreateDefaultPersistencePath()
    {
        return Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-proactive-suggestions.json");
    }

    static string EscapeXmlAttribute(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    static string AppendOrReplaceDraftContent(string draft, string content)
    {
        string normalizedDraft = draft.Trim();
        string escapedContent = EscapeDraftQuotedValue(content);
        int contentIndex = normalizedDraft.IndexOf("content=", StringComparison.OrdinalIgnoreCase);
        if (contentIndex >= 0)
            normalizedDraft = normalizedDraft[..contentIndex].TrimEnd();

        return string.IsNullOrWhiteSpace(normalizedDraft)
            ? $"content=\"{escapedContent}\""
            : $"{normalizedDraft} content=\"{escapedContent}\"";
    }

    static string NormalizeReplyContent(string content)
    {
        string normalized = content
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("QZone reply content cannot be empty.", nameof(content));
        if (normalized.Length > 200)
            throw new InvalidOperationException("QZone reply content cannot exceed 200 characters.");

        return normalized;
    }

    static string EscapeDraftQuotedValue(string value)
    {
        return value.Replace("\"", "'", StringComparison.Ordinal);
    }
}
