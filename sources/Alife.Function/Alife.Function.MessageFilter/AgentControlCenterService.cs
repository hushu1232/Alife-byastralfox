using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;
using Autofac;

namespace Alife.Function.Agent;

public sealed class AgentControlCenterConfig
{
    public bool AllowAgentLowRiskSelfConfiguration { get; set; } = true;
    public bool RequireOwnerConfirmationForHighRiskConfiguration { get; set; } = true;
    public bool AllowMentionWakeup { get; set; } = true;
    public bool AllowPassiveGroupListening { get; set; } = true;
    public bool AllowProactiveChat { get; set; } = true;
    public bool AllowAutomaticMaintenanceInspection { get; set; } = true;
    public int ProactiveChatIntensity { get; set; } = 2;
    public int MaxSelfConfigChangesPerHour { get; set; } = 6;
    public int MaintenanceInspectionIntervalMinutes { get; set; } = 15;
    public int MaintenanceDuplicateCooldownMinutes { get; set; } = 120;
    public string LowRiskConfigurationKeys { get; set; } =
        "AllowMentionWakeup;AllowPassiveGroupListening;AllowProactiveChat;AllowAutomaticMaintenanceInspection;ProactiveChatIntensity;MaxSelfConfigChangesPerHour;MaintenanceInspectionIntervalMinutes;MaintenanceDuplicateCooldownMinutes";
    public string ProtectedConfigurationKeys { get; set; } =
        "OwnerUserIds;AllowedWorkspaceRoots;AllowedCommands;RequireOwnerConfirmationForHighRiskConfiguration;ProtectedConfigurationKeys;GitHubUpload;QZonePost;QZoneComment;QZoneLike;GroupFileUpload;CodeExecution";
}

public sealed record AgentConfigurationChangeProposal(
    string Id,
    string Key,
    string RequestedValue,
    string CurrentValue,
    string Reason,
    AgentAuditRiskLevel RiskLevel,
    DateTimeOffset CreatedAt,
    string Actor);

public sealed record AgentConfigurationChangeResult(
    bool Applied,
    bool RequiresOwnerConfirmation,
    string Key,
    string RequestedValue,
    string Message,
    AgentConfigurationChangeProposal? Proposal = null);

public sealed record AgentControlCenterAttentionSummary(
    int OwnerConfirmationRequiredCount,
    int AutonomousLowRiskActivityCount,
    IReadOnlyList<string> OwnerConfirmationItems,
    IReadOnlyList<string> AutonomousLowRiskItems);

public sealed record AgentControlCenterNotification(
    string Kind,
    AgentAuditRiskLevel RiskLevel,
    string Message);

public sealed record AgentControlCenterNotificationSummary(
    bool ShouldNotifyOwner,
    IReadOnlyList<AgentControlCenterNotification> Items);

public sealed record AgentOwnerNotificationPlan(
    bool ShouldNotifyOwner,
    string TargetSessionId,
    string PublicGroupSummary,
    IReadOnlyList<string> PrivateMessages,
    string? SourceGroupSessionId = null);

public sealed record AgentControlCenterSelfCheckSchedulerStatus(
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastWakeAt,
    DateTimeOffset? NextCheckAt,
    string LastSkipReason);

public sealed record AgentControlCenterSelfCheckItem(
    string Category,
    AgentAuditRiskLevel RiskLevel,
    string Summary,
    string RecommendedAction,
    bool CanAgentHandleAutonomously,
    string? ActionId = null);

public sealed record AgentControlCenterSelfCheckSnapshot(
    int OwnerReviewCount,
    int AutonomousRecommendationCount,
    IReadOnlyList<AgentControlCenterSelfCheckItem> Items);

public sealed record AgentControlCenterSelfCheckActionResult(
    bool Applied,
    bool RequiresOwnerConfirmation,
    string ActionId,
    string Key,
    string Message,
    AgentConfigurationChangeProposal? Proposal = null);

public sealed record AgentControlCenterSelfCheckLoopResult(
    AgentControlCenterSelfCheckSnapshot SelfCheck,
    AgentBackgroundTaskResult BackgroundResult,
    bool WakeRecommended,
    AgentEvent? WakeEvent,
    AgentMaintenanceInspectionResult? MaintenanceInspection);

public sealed record AgentStreamingPolicyVisibility(
    string Name,
    string Target,
    StreamingOutputMode Mode,
    int MinBufferedCharacters,
    int MaxBufferedCharacters);

public sealed record AgentChatLatencyVisibility(
    long? LastFirstContentLatencyMs,
    long? LastChatDurationMs,
    DateTimeOffset? LastChatStartedAt,
    DateTimeOffset? LastFirstContentAt,
    DateTimeOffset? LastChatEndedAt);

public sealed record AgentActionGatewayAuditSummary(
    int SucceededCount,
    int BlockedCount,
    int FailedCount,
    IReadOnlyList<AgentAuditLogEntry> RecentEntries);

public sealed record AgentQChatRuntimeVisibility(
    int RecentConnectSucceededCount,
    int RecentInboundMessageCount,
    int RecentOutboundMessageCount,
    int RecentFailureCount,
    int RecentQuietSuppressionCount,
    DateTimeOffset? LastConnectSucceededAt,
    DateTimeOffset? LastInboundMessageAt,
    DateTimeOffset? LastOutboundMessageAt,
    IReadOnlyList<string> RecentFailures,
    AgentQChatAntiSpamVisibility AntiSpam)
{
    public static AgentQChatRuntimeVisibility Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        null,
        null,
        null,
        [],
        AgentQChatAntiSpamVisibility.Empty);
}

public sealed record AgentQChatGroupDecisionVisibility(
    DateTimeOffset? Timestamp,
    long GroupId,
    long UserId,
    string Decision,
    string Reason,
    bool IsMentionedOrWoken,
    bool IsGroupEnabled,
    double? SocialAttentionProbability,
    int? CooldownRemainingSeconds,
    int? ActiveSoftAttentionRemainingSeconds,
    string RawMessage);

public sealed record AgentQChatAntiSpamVisibility(
    int RecentGroupMessageCount,
    int RecentGroupBufferedCount,
    int RecentSuppressedCount,
    int RecentLowInformationSuppressionCount,
    int RecentCooldownSuppressionCount,
    int RecentQuietSuppressionCount,
    int RecentScopeSuppressionCount,
    int RecentMediaChanceAllowedCount,
    bool QuietModeEnabled,
    string QuietModeReason,
    string AllowedGroupIds,
    int PassiveCooldownSeconds,
    double? LastPassiveElapsedSeconds,
    double? ObservedProactiveProbability,
    double? ObservedMediaOnlyReplyProbability,
    string LastSuppressionReason,
    IReadOnlyList<string> RecentSuppressionReasons,
    IReadOnlyList<AgentQChatGroupDecisionVisibility> RecentGroupDecisions)
{
    public static AgentQChatAntiSpamVisibility Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        "",
        "",
        0,
        null,
        null,
        null,
        "",
        [],
        []);
}

public sealed record AgentControlCenterRuntimeVisibility(
    IReadOnlyList<AgentStreamingPolicyVisibility> StreamingPolicies,
    AgentChatLatencyVisibility ChatLatency,
    AgentEventPipelineSnapshot EventPipeline,
    IReadOnlyList<AgentBackgroundTaskResult> BackgroundTasks,
    AgentActionGatewayAuditSummary ActionGatewayAudit,
    IReadOnlyList<AgentRunSnapshot> RecentRunSessions,
    AgentQChatRuntimeVisibility QChat);

public sealed record AgentControlCenterHealthTriageSnapshot(
    IReadOnlyList<ModuleHealth> Waiting,
    IReadOnlyList<ModuleHealth> ExternalEnvironment,
    IReadOnlyList<ModuleHealth> Faults,
    IReadOnlyList<string> OwnerConfirmationItems);

public sealed record AgentControlCenterCleanupPreview(
    int StaleTerminalTaskCount,
    int StaleMaintenanceProposalCount,
    int ExcessDiagnosticLineCount,
    bool RequiresOwnerConfirmation)
{
    public bool HasWork => StaleTerminalTaskCount > 0
                           || StaleMaintenanceProposalCount > 0
                           || ExcessDiagnosticLineCount > 0;
}

public sealed record AgentControlCenterCleanupResult(
    bool Applied,
    string Message,
    int RemovedTerminalTaskCount,
    int ArchivedMaintenanceProposalCount,
    int TrimmedDiagnosticLineCount,
    bool RequiresOwnerConfirmation);

public sealed record AgentQChatPolicySnapshot(
    string CharacterRoot,
    string AllowedGroupIds,
    string Mode,
    int PassiveCooldownSeconds,
    float ProactiveChatProbability,
    float MediaOnlyReplyProbability,
    bool AllowGroupMemberChat,
    bool AllowGroupMemberMentions,
    bool AllowMentionOutsideAllowedGroups,
    bool AllowProactiveGroupChat);

public sealed record AgentQChatPolicyChangeResult(
    bool Applied,
    string Message,
    AgentQChatPolicySnapshot? Snapshot = null);

public sealed record AgentQChatJoinedGroupSourceItem(
    long GroupId,
    string GroupName,
    int MemberCount,
    int MaxMemberCount);

public sealed record AgentQChatJoinedGroupSourceSnapshot(
    DateTimeOffset RefreshedAt,
    IReadOnlyList<AgentQChatJoinedGroupSourceItem> Groups);

public sealed record AgentQChatJoinedGroupVisibility(
    long GroupId,
    string GroupName,
    int MemberCount,
    int MaxMemberCount,
    bool IsAllowed);

public sealed record AgentQChatJoinedGroupSnapshot(
    bool Available,
    DateTimeOffset? RefreshedAt,
    string Message,
    IReadOnlyList<AgentQChatJoinedGroupVisibility> Groups);

public interface IAgentQChatJoinedGroupProvider
{
    Task<AgentQChatJoinedGroupSourceSnapshot> RefreshAgentJoinedGroupsAsync();

    AgentQChatJoinedGroupSourceSnapshot GetCachedAgentJoinedGroups();
}

public sealed record AgentControlCenterSnapshot(
    DateTimeOffset Timestamp,
    AgentStateSnapshot AgentState,
    AgentIssueReportSnapshot IssueReport,
    AgentEnvironmentCheckSnapshot EnvironmentCheck,
    AgentControlCenterConfig Configuration,
    IReadOnlyList<AgentConfigurationChangeProposal> PendingConfigurationProposals,
    AgentTaskState? LatestTask,
    IReadOnlyList<AgentTaskState> ActiveTasks,
    IReadOnlyList<AgentWorkspacePatchProposal> PendingWorkspaceProposals,
    IReadOnlyList<AgentProactivePendingSuggestion> PendingProactiveSuggestions,
    IReadOnlyList<AgentProactivePendingSuggestion> CompletedProactiveSuggestions,
    IReadOnlyList<AgentMaintenanceProposal> PendingMaintenanceProposals,
    IReadOnlyDictionary<string, IReadOnlyList<AgentMaintenanceRepairEvidence>> MaintenanceRepairEvidenceByProposalId,
    IReadOnlyList<string> WorkspaceRoots,
    IReadOnlyList<AgentCommandDefinition> AllowedCommands,
    IReadOnlyList<AgentAuditLogEntry> RecentAuditEntries,
    AgentControlCenterAttentionSummary AttentionSummary,
    AgentControlCenterNotificationSummary NotificationSummary,
    AgentControlCenterHealthTriageSnapshot HealthTriage,
    AgentControlCenterCleanupPreview CleanupPreview,
    IReadOnlyList<AgentExecutionGatewayDecision> SecurityGatewayPreview,
    MemoryConsistencySnapshot MemoryConsistency,
    AgentControlCenterSelfCheckSnapshot SelfCheck,
    AgentControlCenterSelfCheckSchedulerStatus SelfCheckScheduler,
    AgentControlCenterRuntimeVisibility RuntimeVisibility);

[Module(
    "Agent Control Center",
    "Shows the agent runtime state, task status, audit trail, allowed commands, issue report, and workspace proposals.",
    defaultCategory: "Alife Official/Agent",
    editorUI: typeof(AgentControlCenterServiceUI),
    LaunchOrder = -59)]
public class AgentControlCenterService(
    AgentDiagnosticsService? diagnostics = null,
    AgentIssueReportService? issueReports = null,
    AgentTaskService? tasks = null,
    AgentWorkspaceService? workspace = null,
    AgentWorkspacePolicy? workspacePolicy = null,
    AgentCommandPolicy? commandPolicy = null,
    AgentAuditLogService? auditLog = null,
    XmlFunctionCaller? functionCaller = null,
    ConfigurationSystem? configurationSystem = null,
    AgentMaintenanceService? maintenance = null,
    AgentEnvironmentCheckService? environmentChecks = null,
    AgentEventPipeline? eventPipeline = null,
    IMemoryConsistencyReporter? memoryConsistencyReporter = null,
    Func<DateTimeOffset>? clock = null,
    string? qchatDiagnosticsPath = null)
    : InteractiveModule<AgentControlCenterService>, IConfigurable<AgentControlCenterConfig>
{
    readonly AgentAuditLogService auditLog = auditLog ?? new AgentAuditLogService(
        Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-audit.jsonl"));
    readonly AgentCommandPolicy commandPolicy = NormalizeCommandPolicy(commandPolicy ?? CreateDefaultCommandPolicy());
    readonly AgentDiagnosticsService diagnostics = diagnostics ?? new AgentDiagnosticsService();
    readonly AgentIssueReportService issueReports = issueReports ?? new AgentIssueReportService(auditLog);
    readonly AgentEnvironmentCheckService environmentChecks = environmentChecks ?? new AgentEnvironmentCheckService();
    readonly AgentEventPipeline eventPipeline = eventPipeline ?? new AgentEventPipeline();
    readonly IMemoryConsistencyReporter? memoryConsistencyReporter = memoryConsistencyReporter;
    readonly AgentMaintenanceService maintenance = maintenance ?? new AgentMaintenanceService(issueReports, auditLog);
    readonly AgentTaskService tasks = tasks ?? new AgentTaskService(auditLog);
    readonly string qchatDiagnosticsPath = Path.GetFullPath(
        qchatDiagnosticsPath ?? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "qchat-diagnostics.jsonl"));
    readonly AgentWorkspaceService workspace = workspace ?? new AgentWorkspaceService(
        workspacePolicy,
        auditLog: auditLog);
    AgentProactiveBehaviorService? proactiveBehavior;
    IReadOnlyList<IAgentProactiveSuggestionExecutor> proactiveExecutors = [];
    readonly AgentWorkspacePolicy workspacePolicy = NormalizeWorkspacePolicy(workspacePolicy ?? CreateDefaultWorkspacePolicy());
    readonly Dictionary<string, AgentConfigurationChangeProposal> configurationProposals = new(StringComparer.OrdinalIgnoreCase);
    readonly List<AgentBackgroundTaskResult> backgroundTaskResults = [];
    readonly List<AgentRunSnapshot> recentRunSessions = [];
    readonly ConfigurationSystem? configurationSystem = configurationSystem;
    readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.Now);
    DateTimeOffset? lastAutomaticMaintenanceInspectionAt;
    DateTimeOffset? lastAutomaticSelfCheckAt;
    DateTimeOffset? lastAutomaticSelfCheckWakeAt;
    string? lastAutomaticSelfCheckWakeFingerprint;
    string lastAutomaticSelfCheckSkipReason = "not-run";

    public AgentControlCenterConfig? Configuration { get; set; } = new();
    public IAgentQChatJoinedGroupProvider? QChatJoinedGroupProviderOverride { get; set; }
    public AgentProactiveBehaviorService? ProactiveBehavior
    {
        get => proactiveBehavior;
        set => proactiveBehavior = value;
    }
    public IReadOnlyList<IAgentProactiveSuggestionExecutor> ProactiveExecutors
    {
        get => proactiveExecutors;
        set => proactiveExecutors = value ?? [];
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_control_center")]
    [Description("Show a concise Agent control center summary for runtime state, tasks, audit, commands, errors, and workspace proposals.")]
    public void ShowAgentControlCenter()
    {
        AgentControlCenterSnapshot snapshot = BuildSnapshot(ChatBot.GetRuntimeState(), Character.Name);
        Poke($"""
              Agent control center
              State: {(snapshot.AgentState.IsChatting ? "chatting" : "idle")}
              Last error: {snapshot.AgentState.LastError ?? "none"}
              Latest task: {snapshot.LatestTask?.Goal ?? "none"}
              Pending proposals: {snapshot.PendingWorkspaceProposals.Count}
              Pending proactive suggestions: {snapshot.PendingProactiveSuggestions.Count}
              Recent audit entries: {snapshot.RecentAuditEntries.Count}
              Allowed commands: {snapshot.AllowedCommands.Count}
              """);
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_self_check")]
    [Description("Show an agent-readable control-center self-check with owner-review items and autonomous recommendations.")]
    public void ShowAgentSelfCheck()
    {
        AgentControlCenterSnapshot snapshot = BuildSnapshot(ChatBot.GetRuntimeState(), Character.Name);
        auditLog.Record("agent.self_check", "agent", "read control-center self-check", AgentAuditRiskLevel.Low, true);
        Poke(FormatSelfCheckForAgent(snapshot.SelfCheck));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_qchat_recent_decisions")]
    [Description("Show an internal, agent-readable summary of recent QQ group reply decisions and suppression reasons.")]
    public void ShowQChatRecentDecisions()
    {
        AgentControlCenterSnapshot snapshot = BuildSnapshot(ChatBot.GetRuntimeState(), Character.Name);
        auditLog.Record("agent.qchat.recent_decisions", "agent", "read recent QQ group reply decisions", AgentAuditRiskLevel.Low, true);
        Poke(FormatQChatRecentDecisionSummaryForAgent(snapshot.RuntimeVisibility.QChat.AntiSpam.RecentGroupDecisions));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_self_check_apply")]
    [Description("Apply one allowlisted low-risk self-check action, or create an owner-confirmation proposal for protected actions.")]
    public void ApplyAgentSelfCheckAction(string actionId)
    {
        AgentControlCenterSelfCheckActionResult result = ApplySelfCheckAction(
            actionId,
            ChatBot.GetRuntimeState(),
            "agent");
        Poke(result.RequiresOwnerConfirmation
            ? $"Self-check action needs owner confirmation: {result.ActionId} key={result.Key}"
            : $"Self-check action applied: {result.ActionId} key={result.Key}");
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_config_status")]
    [Description("Show the Agent Control Center self-configuration state and pending configuration proposals.")]
    public void ShowAgentConfigurationStatus()
    {
        AgentControlCenterConfig config = EnsureConfiguration();
        auditLog.Record("agent.config.status", "agent", "read self-configuration status", AgentAuditRiskLevel.Low, true);
        Poke($"""
              Agent self-configuration
              Low-risk self-configuration: {(config.AllowAgentLowRiskSelfConfiguration ? "enabled" : "disabled")}
              Mention wakeup: {(config.AllowMentionWakeup ? "enabled" : "disabled")}
              Passive group listening: {(config.AllowPassiveGroupListening ? "enabled" : "disabled")}
              Proactive chat: {FormatProactiveChatStatus(config)}
              Automatic maintenance inspection: {(config.AllowAutomaticMaintenanceInspection ? "enabled" : "disabled")} every {config.MaintenanceInspectionIntervalMinutes}m, duplicate cooldown={config.MaintenanceDuplicateCooldownMinutes}m
              Pending configuration proposals: {configurationProposals.Count}
              """);
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_config_apply")]
    [Description("Apply an allowed low-risk Agent Control Center configuration change, or create a proposal when owner confirmation is required.")]
    public void ApplyAgentConfiguration(string key, string value, string reason = "")
    {
        AgentConfigurationChangeResult result = ApplyConfigurationChange(key, value, "agent", reason);
        Poke(result.Proposal == null
            ? $"Agent configuration: {result.Message} key={result.Key}"
            : $"Agent configuration proposal created: {result.Proposal.Id} key={result.Key}");
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_config_propose")]
    [Description("Create a pending Agent Control Center configuration proposal for owner review.")]
    public void ProposeAgentConfiguration(string key, string value, string reason = "")
    {
        AgentConfigurationChangeProposal proposal = ProposeConfigurationChange(key, value, "agent", reason);
        Poke($"Agent configuration proposal created: {proposal.Id} key={proposal.Key}");
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_config_confirmation_text")]
    [Description("Show the owner confirmation command for a pending Agent Control Center configuration proposal.")]
    public void ShowAgentConfigurationConfirmationText(string id)
    {
        if (configurationProposals.TryGetValue(id, out AgentConfigurationChangeProposal? proposal) == false)
        {
            Poke("Configuration proposal was not found.");
            return;
        }

        Poke(BuildConfigurationProposalConfirmationText(proposal));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_config_apply_proposal", riskLevel: XmlFunctionRiskLevel.High)]
    [Description("Apply a pending Agent Control Center configuration proposal after owner confirmation.")]
    public void ApplyAgentConfigurationProposal(string id)
    {
        AgentConfigurationChangeResult result = ApplyConfigurationProposal(id, "owner");
        Poke($"Agent configuration proposal: {result.Message} key={result.Key}");
    }

    public AgentControlCenterSnapshot BuildSnapshot(ChatRuntimeState runtimeState, string characterName)
    {
        AgentControlCenterConfig configuration = EnsureConfiguration();
        IReadOnlyList<AgentConfigurationChangeProposal> pendingConfigurationProposals = GetPendingConfigurationProposals();
        IReadOnlyList<AgentWorkspacePatchProposal> pendingWorkspaceProposals = workspace.GetPendingProposals();
        IReadOnlyList<AgentProactivePendingSuggestion> pendingProactiveSuggestions =
            proactiveBehavior?.GetPendingSuggestions() ?? [];
        IReadOnlyList<AgentProactivePendingSuggestion> completedProactiveSuggestions =
            proactiveBehavior?.GetCompletedSuggestions() ?? [];
        IReadOnlyList<AgentMaintenanceProposal> pendingMaintenanceProposals = maintenance.GetPendingProposals();
        IReadOnlyDictionary<string, IReadOnlyList<AgentMaintenanceRepairEvidence>> repairEvidence =
            maintenance.GetRepairEvidenceByProposalId();
        IReadOnlyList<AgentTaskState> activeTasks = tasks.GetTasks()
            .Where(task => task.Status is AgentTaskStatus.Planned or AgentTaskStatus.Running)
            .ToArray();
        IReadOnlyList<AgentAuditLogEntry> recentAuditEntries = auditLog.GetRecentEntries(12);
        AgentIssueReportSnapshot issueReport = issueReports.BuildSnapshot(runtimeState);
        AgentControlCenterAttentionSummary attentionSummary = BuildAttentionSummary(
            pendingConfigurationProposals,
            pendingWorkspaceProposals,
            pendingProactiveSuggestions,
            pendingMaintenanceProposals,
            recentAuditEntries);
        AgentControlCenterRuntimeVisibility runtimeVisibility = BuildRuntimeVisibility(runtimeState, recentAuditEntries);
        MemoryConsistencySnapshot memoryConsistency = BuildMemoryConsistencySnapshot();
        AgentControlCenterCleanupPreview cleanupPreview = BuildRuntimeCleanupPreview(
            maxTerminalTaskAge: TimeSpan.FromDays(30),
            maxPendingMaintenanceAge: TimeSpan.FromDays(14),
            maxDiagnosticLines: 500);
        return new AgentControlCenterSnapshot(
            DateTimeOffset.Now,
            diagnostics.BuildSnapshot(runtimeState, characterName),
            issueReport,
            environmentChecks.BuildSnapshot(),
            configuration,
            pendingConfigurationProposals,
            tasks.GetLatestTask(),
            activeTasks,
            pendingWorkspaceProposals,
            pendingProactiveSuggestions,
            completedProactiveSuggestions,
            pendingMaintenanceProposals,
            repairEvidence,
            workspacePolicy.AllowedRoots,
            commandPolicy.AllowedCommands,
            recentAuditEntries,
            attentionSummary,
            BuildNotificationSummary(attentionSummary, issueReport),
            BuildHealthTriage(issueReport, attentionSummary),
            cleanupPreview,
            BuildSecurityGatewayPreview(),
            memoryConsistency,
            BuildSelfCheck(configuration, attentionSummary, issueReport, memoryConsistency, runtimeVisibility),
            BuildSelfCheckSchedulerStatus(configuration),
            runtimeVisibility);
    }

    public void RecordBackgroundTaskResult(AgentBackgroundTaskResult result)
    {
        backgroundTaskResults.Add(result);
        int overflow = backgroundTaskResults.Count - 12;
        if (overflow > 0)
            backgroundTaskResults.RemoveRange(0, overflow);
    }

    public void RecordAgentRunSession(AgentRunSession session)
    {
        recentRunSessions.Add(session.Snapshot());
        int overflow = recentRunSessions.Count - 12;
        if (overflow > 0)
            recentRunSessions.RemoveRange(0, overflow);
    }

    public AgentControlCenterSelfCheckActionResult ApplySelfCheckAction(
        string actionId,
        ChatRuntimeState runtimeState,
        string actor)
    {
        string normalizedActionId = string.IsNullOrWhiteSpace(actionId)
            ? throw new ArgumentException("Self-check action id cannot be empty.", nameof(actionId))
            : actionId.Trim();
        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim();

        AgentControlCenterSelfCheckActionResult actionResult;
        switch (normalizedActionId)
        {
            case "enable-auto-maintenance":
                actionResult = ToSelfCheckActionResult(normalizedActionId, ApplyConfigurationChange(
                    "AllowAutomaticMaintenanceInspection",
                    "true",
                    normalizedActor,
                    "self-check action: enable automatic maintenance inspection after a runtime error"));
                break;
            case "reduce-proactive-intensity":
            {
                AgentControlCenterConfig config = EnsureConfiguration();
                int requestedValue = Math.Max(0, config.ProactiveChatIntensity - 1);
                actionResult = ToSelfCheckActionResult(normalizedActionId, ApplyConfigurationChange(
                    "ProactiveChatIntensity",
                    requestedValue.ToString(),
                    normalizedActor,
                    "self-check action: reduce proactive chat intensity"));
                break;
            }
            case "extend-maintenance-cooldown":
            {
                AgentControlCenterConfig config = EnsureConfiguration();
                int requestedValue = Math.Min(1440, Math.Max(30, config.MaintenanceDuplicateCooldownMinutes * 2));
                actionResult = ToSelfCheckActionResult(normalizedActionId, ApplyConfigurationChange(
                    "MaintenanceDuplicateCooldownMinutes",
                    requestedValue.ToString(),
                    normalizedActor,
                    "self-check action: extend duplicate maintenance cooldown"));
                break;
            }
            case "cleanup-proactive-suggestions":
            {
                AgentProactiveCleanupResult cleanup = CleanupProactiveSuggestionsFromControlCenter(
                    TimeSpan.FromHours(24),
                    TimeSpan.FromDays(30));
                actionResult = new AgentControlCenterSelfCheckActionResult(
                    Applied: true,
                    RequiresOwnerConfirmation: false,
                    normalizedActionId,
                    "ProactiveSuggestions",
                    $"expired_pending={cleanup.ExpiredPendingCount}; removed_completed={cleanup.RemovedCompletedCount}");
                break;
            }
            case "repair-memory-storage-consistency":
                actionResult = ApplyMemoryConsistencyRepairAction(normalizedActionId, normalizedActor);
                break;
            case "set-owner-user-ids":
                actionResult = ToSelfCheckActionResult(normalizedActionId, ApplyConfigurationChange(
                    "OwnerUserIds",
                    "",
                    normalizedActor,
                    "self-check action: protected owner identity configuration requires owner confirmation"));
                break;
            default:
                throw new InvalidOperationException($"Unknown self-check action: {normalizedActionId}");
        }

        auditLog.Record(
            "agent.self_check.action",
            normalizedActor,
            $"action={normalizedActionId}; key={actionResult.Key}; message={actionResult.Message}",
            actionResult.RequiresOwnerConfirmation ? AgentAuditRiskLevel.High : AgentAuditRiskLevel.Low,
            actionResult.Applied || actionResult.RequiresOwnerConfirmation,
            actionResult.Applied || actionResult.RequiresOwnerConfirmation ? null : actionResult.Message);

        return actionResult;
    }

    AgentControlCenterSelfCheckActionResult ApplyMemoryConsistencyRepairAction(string actionId, string actor)
    {
        if (memoryConsistencyReporter == null)
        {
            return new AgentControlCenterSelfCheckActionResult(
                Applied: false,
                RequiresOwnerConfirmation: false,
                actionId,
                "MemoryStorageConsistency",
                "memory consistency reporter is unavailable");
        }

        try
        {
            MemoryConsistencySnapshot repair = memoryConsistencyReporter
                .RepairMemoryConsistencyAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            string message =
                $"issues={repair.TotalIssues}; repaired_archives={repair.RepairedArchiveFiles}; repaired_indexes={repair.RepairedIndexRecords}; repaired_content_mismatches={repair.RepairedContentMismatches}";
            auditLog.Record(
                "agent.memory.consistency.repair",
                actor,
                message,
                AgentAuditRiskLevel.Low,
                true);
            return new AgentControlCenterSelfCheckActionResult(
                Applied: true,
                RequiresOwnerConfirmation: false,
                actionId,
                "MemoryStorageConsistency",
                message);
        }
        catch (Exception ex)
        {
            string message = $"memory consistency repair failed: {ex.Message}";
            auditLog.Record(
                "agent.memory.consistency.repair",
                actor,
                message,
                AgentAuditRiskLevel.Low,
                false,
                message);
            return new AgentControlCenterSelfCheckActionResult(
                Applied: false,
                RequiresOwnerConfirmation: false,
                actionId,
                "MemoryStorageConsistency",
                message);
        }
    }

    public AgentTaskState StartTaskFromControlCenter(string taskId)
    {
        return tasks.StartTask(taskId, "agent-control-ui");
    }

    public AgentTaskState CompleteTaskFromControlCenter(string taskId, string detail = "completed from Agent Control Center")
    {
        return tasks.CompleteTask(taskId, "agent-control-ui", detail);
    }

    public AgentProactivePendingSuggestion ConfirmProactiveSuggestionFromControlCenter(string id)
    {
        return GetProactiveBehavior().ConfirmPendingSuggestion(id, "agent-control-ui");
    }

    public AgentProactivePendingSuggestion DismissProactiveSuggestionFromControlCenter(string id)
    {
        return GetProactiveBehavior().DismissPendingSuggestion(id, "agent-control-ui");
    }

    public AgentProactiveCleanupResult CleanupProactiveSuggestionsFromControlCenter(
        TimeSpan maxPendingAge,
        TimeSpan maxCompletedAge)
    {
        return GetProactiveBehavior().CleanupSuggestions(maxPendingAge, maxCompletedAge, "agent-control-ui");
    }

    public AgentMaintenanceArchiveResult ArchiveMaintenanceProposalFromControlCenter(
        string id,
        string resolution = "handled from Agent Control Center")
    {
        return maintenance.ArchiveProposal(id, "agent-control-ui", resolution);
    }

    public AgentMaintenanceInspectionResult InspectMaintenanceFromControlCenter(
        ChatRuntimeState runtimeState,
        TimeSpan duplicateCooldown)
    {
        return maintenance.InspectIssueReport(
            issueReports.BuildSnapshot(runtimeState),
            "agent-control-ui",
            duplicateCooldown);
    }

    public AgentControlCenterCleanupPreview BuildRuntimeCleanupPreview(
        TimeSpan maxTerminalTaskAge,
        TimeSpan maxPendingMaintenanceAge,
        int maxDiagnosticLines)
    {
        TimeSpan safeTerminalAge = maxTerminalTaskAge < TimeSpan.Zero ? TimeSpan.Zero : maxTerminalTaskAge;
        TimeSpan safeMaintenanceAge = maxPendingMaintenanceAge < TimeSpan.Zero ? TimeSpan.Zero : maxPendingMaintenanceAge;
        DateTimeOffset now = clock();
        DateTimeOffset terminalCutoff = now - safeTerminalAge;
        DateTimeOffset maintenanceCutoff = now - safeMaintenanceAge;
        int staleTerminalTasks = tasks.GetTasks()
            .Count(task => task.Status is (AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled)
                           && task.UpdatedAt <= terminalCutoff);
        int staleMaintenance = maintenance.GetPendingProposals()
            .Count(proposal => proposal.CreatedAt <= maintenanceCutoff);
        int excessDiagnosticLines = CountExcessDiagnosticLines(maxDiagnosticLines);

        return new AgentControlCenterCleanupPreview(
            staleTerminalTasks,
            staleMaintenance,
            excessDiagnosticLines,
            RequiresOwnerConfirmation: staleTerminalTasks > 0 || staleMaintenance > 0 || excessDiagnosticLines > 0);
    }

    public AgentControlCenterCleanupResult CleanupRuntimeNoiseFromControlCenter(
        TimeSpan maxTerminalTaskAge,
        TimeSpan maxPendingMaintenanceAge,
        int maxDiagnosticLines)
    {
        AgentControlCenterCleanupPreview preview = BuildRuntimeCleanupPreview(
            maxTerminalTaskAge,
            maxPendingMaintenanceAge,
            maxDiagnosticLines);
        if (preview.HasWork == false)
        {
            return new AgentControlCenterCleanupResult(
                false,
                "No runtime cleanup work was found.",
                0,
                0,
                0,
                RequiresOwnerConfirmation: false);
        }

        int removedTasks = tasks.RemoveTerminalTasksOlderThan(maxTerminalTaskAge, "agent-control-ui");
        int archivedMaintenance = ArchiveStaleMaintenanceProposals(maxPendingMaintenanceAge);
        int trimmedDiagnostics = TrimDiagnostics(maxDiagnosticLines);
        string detail =
            $"removed_terminal_tasks={removedTasks}; archived_maintenance={archivedMaintenance}; trimmed_diagnostic_lines={trimmedDiagnostics}";
        auditLog.Record(
            "agent.control.cleanup",
            "agent-control-ui",
            detail,
            archivedMaintenance > 0 ? AgentAuditRiskLevel.Medium : AgentAuditRiskLevel.Low,
            succeeded: true);

        return new AgentControlCenterCleanupResult(
            true,
            $"Runtime cleanup applied: {detail}",
            removedTasks,
            archivedMaintenance,
            trimmedDiagnostics,
            preview.RequiresOwnerConfirmation);
    }

    int ArchiveStaleMaintenanceProposals(TimeSpan maxAge)
    {
        TimeSpan safeAge = maxAge < TimeSpan.Zero ? TimeSpan.Zero : maxAge;
        DateTimeOffset cutoff = clock() - safeAge;
        int archived = 0;
        foreach (AgentMaintenanceProposal proposal in maintenance.GetPendingProposals()
                     .Where(proposal => proposal.CreatedAt <= cutoff)
                     .ToArray())
        {
            AgentMaintenanceArchiveResult result = maintenance.ArchiveProposal(
                proposal.Id,
                "agent-control-ui",
                "archived during owner-confirmed control-center runtime cleanup");
            if (result.Archived)
                archived++;
        }

        return archived;
    }

    int CountExcessDiagnosticLines(int maxDiagnosticLines)
    {
        int safeMaxLines = Math.Max(1, maxDiagnosticLines);
        if (File.Exists(qchatDiagnosticsPath) == false)
            return 0;

        try
        {
            return Math.Max(0, File.ReadLines(qchatDiagnosticsPath).Count() - safeMaxLines);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    int TrimDiagnostics(int maxDiagnosticLines)
    {
        int safeMaxLines = Math.Max(1, maxDiagnosticLines);
        if (File.Exists(qchatDiagnosticsPath) == false)
            return 0;

        try
        {
            string[] lines = File.ReadAllLines(qchatDiagnosticsPath);
            int excess = Math.Max(0, lines.Length - safeMaxLines);
            if (excess == 0)
                return 0;

            string[] retained = lines.Skip(excess).ToArray();
            File.WriteAllLines(qchatDiagnosticsPath, retained);
            return excess;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public AgentMaintenanceInspectionResult? TryAutomaticMaintenanceInspection(ChatRuntimeState runtimeState)
    {
        AgentControlCenterConfig config = EnsureConfiguration();
        if (config.AllowAutomaticMaintenanceInspection == false)
            return null;

        DateTimeOffset now = clock();
        TimeSpan interval = TimeSpan.FromMinutes(Math.Max(1, config.MaintenanceInspectionIntervalMinutes));
        if (lastAutomaticMaintenanceInspectionAt.HasValue
            && now - lastAutomaticMaintenanceInspectionAt.Value < interval)
            return null;

        lastAutomaticMaintenanceInspectionAt = now;
        TimeSpan duplicateCooldown = TimeSpan.FromMinutes(Math.Max(1, config.MaintenanceDuplicateCooldownMinutes));
        AgentMaintenanceInspectionResult result = InspectMaintenanceFromControlCenter(runtimeState, duplicateCooldown);
        EnsureMaintenanceTask(result.Proposal);
        return result;
    }

    public async Task<AgentProactiveExternalExecutionResult> ExecuteProactiveSuggestionFromControlCenter(string id)
    {
        return await ExecuteProactiveSuggestionFromControlCenter(
            id,
            new AgentPermissionRequest(
                ActorUserId: null,
                Source: AgentRequestSource.System,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "proactive.execute"),
            new AgentPermissionConfig());
    }

    public async Task<AgentProactiveExternalExecutionResult> ExecuteProactiveSuggestionFromControlCenter(
        string id,
        AgentPermissionRequest request,
        AgentPermissionConfig config)
    {
        AgentProactiveBehaviorService proactive = GetProactiveBehavior();
        AgentProactivePendingSuggestion? pending = proactive.GetCompletedSuggestion(id);
        if (pending == null)
            return new AgentProactiveExternalExecutionResult(false, "Confirmed proactive suggestion was not found.");
        if (pending.Status == AgentProactivePendingStatus.Executed)
            return new AgentProactiveExternalExecutionResult(false, "Proactive suggestion was already executed.");
        if (pending.Status != AgentProactivePendingStatus.Confirmed)
            return new AgentProactiveExternalExecutionResult(false, "Proactive suggestion must be confirmed before execution.");

        AgentExecutionGatewayDecision gatewayDecision = new AgentActionAuthorizationService().EvaluateExecution(request with
        {
            RiskLevel = ToAgentRiskLevel(pending.Suggestion.RiskLevel),
            Action = string.IsNullOrWhiteSpace(request.Action)
                ? $"proactive.{pending.Suggestion.Kind}"
                : request.Action.Trim()
        }, config);
        if (gatewayDecision.Status == AgentExecutionDecisionStatus.OwnerConfirmationRequired)
        {
            auditLog.Record(
                "agent.proactive.control.blocked",
                "agent-control-ui",
                $"{pending.Suggestion.Kind}: {gatewayDecision.Reason}",
                pending.Suggestion.RiskLevel,
                false,
                gatewayDecision.Reason);
            return new AgentProactiveExternalExecutionResult(false, $"Owner confirmation required: {gatewayDecision.Reason}");
        }
        if (gatewayDecision.Status == AgentExecutionDecisionStatus.Blocked)
        {
            auditLog.Record(
                "agent.proactive.control.blocked",
                "agent-control-ui",
                $"{pending.Suggestion.Kind}: {gatewayDecision.Reason}",
                pending.Suggestion.RiskLevel,
                false,
                gatewayDecision.Reason);
            return new AgentProactiveExternalExecutionResult(false, $"Blocked: {gatewayDecision.Reason}");
        }

        IAgentProactiveSuggestionExecutor? executor = proactiveExecutors.FirstOrDefault(item => item.CanExecute(pending));
        if (executor == null)
            return new AgentProactiveExternalExecutionResult(false, $"No executor is available for {pending.Suggestion.Kind}.");

        AgentProactiveExternalExecutionResult result = await executor.ExecuteAsync(pending);
        if (result.Succeeded)
            proactive.MarkSuggestionExecuted(id, "agent-control-ui", result.Message);

        auditLog.Record(
            "agent.proactive.control.execute",
            "agent-control-ui",
            $"{pending.Suggestion.Kind}: {result.Message}",
            pending.Suggestion.RiskLevel,
            result.Succeeded,
            result.Succeeded ? null : result.Message);
        return result;
    }

    public static string BuildWorkspaceProposalConfirmationText(AgentWorkspacePatchProposal proposal)
    {
        return $"confirm execute <workspace_apply_proposal id=\"{EscapeXmlAttribute(proposal.Id)}\" />";
    }

    public IReadOnlyList<AgentConfigurationChangeProposal> GetPendingConfigurationProposals()
    {
        return configurationProposals.Values
            .OrderBy(proposal => proposal.CreatedAt)
            .ToArray();
    }

    public AgentConfigurationChangeProposal ProposeConfigurationChange(
        string key,
        string requestedValue,
        string actor,
        string reason = "")
    {
        AgentControlCenterConfig config = EnsureConfiguration();
        string normalizedKey = key.Trim();
        string normalizedValue = requestedValue.Trim();
        string normalizedReason = reason.Trim();
        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim();
        AgentConfigurationChangeProposal proposal = new(
            Guid.NewGuid().ToString("N"),
            normalizedKey,
            normalizedValue,
            GetConfigValue(config, normalizedKey),
            normalizedReason,
            GetConfigurationRisk(normalizedKey),
            DateTimeOffset.Now,
            normalizedActor);

        configurationProposals[proposal.Id] = proposal;
        auditLog.Record(
            "agent.config.proposed",
            normalizedActor,
            FormatConfigAuditDetail(proposal.Key, proposal.RequestedValue, proposal.Reason),
            proposal.RiskLevel,
            true);
        return proposal;
    }

    public AgentConfigurationChangeResult ApplyConfigurationChange(
        string key,
        string requestedValue,
        string actor,
        string reason = "")
    {
        AgentControlCenterConfig config = EnsureConfiguration();
        string normalizedKey = key.Trim();
        string normalizedValue = requestedValue.Trim();
        string normalizedReason = reason.Trim();
        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim();

        if (CanApplyDirectly(config, normalizedKey) == false)
        {
            AgentConfigurationChangeProposal proposal = ProposeConfigurationChange(
                normalizedKey,
                normalizedValue,
                normalizedActor,
                normalizedReason);
            return new AgentConfigurationChangeResult(
                false,
                true,
                normalizedKey,
                normalizedValue,
                "Owner confirmation required.",
                proposal);
        }

        try
        {
            ApplyConfigValue(config, normalizedKey, normalizedValue);
            PersistConfiguration(config);
            auditLog.Record(
                "agent.config.applied",
                normalizedActor,
                FormatConfigAuditDetail(normalizedKey, normalizedValue, normalizedReason),
                AgentAuditRiskLevel.Low,
                true);
            return new AgentConfigurationChangeResult(
                true,
                false,
                normalizedKey,
                normalizedValue,
                "Configuration applied.");
        }
        catch (Exception exception)
        {
            auditLog.Record(
                "agent.config.failed",
                normalizedActor,
                FormatConfigAuditDetail(normalizedKey, normalizedValue, normalizedReason),
                AgentAuditRiskLevel.Low,
                false,
                exception.Message);
            return new AgentConfigurationChangeResult(
                false,
                false,
                normalizedKey,
                normalizedValue,
                exception.Message);
        }
    }

    public AgentConfigurationChangeResult ApplyConfigurationProposal(string id, string actor)
    {
        if (configurationProposals.TryGetValue(id, out AgentConfigurationChangeProposal? proposal) == false)
        {
            return new AgentConfigurationChangeResult(
                false,
                false,
                "",
                "",
                "Configuration proposal was not found.");
        }

        AgentControlCenterConfig config = EnsureConfiguration();
        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "owner" : actor.Trim();
        try
        {
            ApplyConfigValue(config, proposal.Key, proposal.RequestedValue);
            PersistConfiguration(config);
            configurationProposals.Remove(id);
            auditLog.Record(
                "agent.config.confirmed",
                normalizedActor,
                FormatConfigAuditDetail(proposal.Key, proposal.RequestedValue, proposal.Reason),
                proposal.RiskLevel,
                true);
            return new AgentConfigurationChangeResult(
                true,
                false,
                proposal.Key,
                proposal.RequestedValue,
                "Configuration proposal applied.",
                proposal);
        }
        catch (Exception exception)
        {
            auditLog.Record(
                "agent.config.failed",
                normalizedActor,
                FormatConfigAuditDetail(proposal.Key, proposal.RequestedValue, proposal.Reason),
                proposal.RiskLevel,
                false,
                exception.Message);
            return new AgentConfigurationChangeResult(
                false,
                false,
                proposal.Key,
                proposal.RequestedValue,
                exception.Message,
                proposal);
        }
    }

    public AgentQChatPolicyChangeResult ApplyQChatPolicyFromControlCenter(
        string characterRoot,
        string allowedGroupIds,
        string mode,
        int passiveCooldownSeconds,
        float mediaOnlyReplyProbability,
        string actor,
        bool allowMentionOutsideAllowedGroups = true)
    {
        if (configurationSystem == null)
            return new AgentQChatPolicyChangeResult(false, "Configuration system is unavailable.");

        Type? qchatServiceType = ResolveType("Alife.Function.QChat.QChatService");
        if (qchatServiceType == null)
            return new AgentQChatPolicyChangeResult(false, "QChat service type is unavailable.");

        string normalizedCharacterRoot = string.IsNullOrWhiteSpace(characterRoot)
            ? ""
            : characterRoot.Trim();
        string normalizedMode = NormalizeQChatPolicyMode(mode);
        if (normalizedMode.Length == 0)
            return new AgentQChatPolicyChangeResult(false, $"Unknown QChat policy mode: {mode}");

        object? qchatConfig = configurationSystem.GetConfiguration(qchatServiceType, normalizedCharacterRoot);
        if (qchatConfig == null)
            return new AgentQChatPolicyChangeResult(false, "QChat configuration is unavailable.");

        SetPropertyValue(qchatConfig, "AllowedGroupIds", NormalizeAllowedGroupIds(allowedGroupIds));
        ApplyQChatPolicyMode(qchatConfig, normalizedMode);
        SetPropertyValue(qchatConfig, "AllowMentionOutsideAllowedGroups", allowMentionOutsideAllowedGroups);
        SetPropertyValue(qchatConfig, "PassiveGroupReplyCooldownSeconds", ClampPassiveCooldownSeconds(passiveCooldownSeconds));
        SetPropertyValue(qchatConfig, "MediaOnlyPassiveGroupReplyProbability", ClampProbability(mediaOnlyReplyProbability, 0.5f));
        configurationSystem.SetConfiguration(qchatServiceType, qchatConfig, normalizedCharacterRoot);

        AgentQChatPolicySnapshot snapshot = BuildQChatPolicySnapshot(normalizedCharacterRoot, qchatConfig, normalizedMode);
        auditLog.Record(
            "agent.qchat.policy.applied",
            string.IsNullOrWhiteSpace(actor) ? "agent-control-ui" : actor.Trim(),
            $"root={normalizedCharacterRoot}; groups={snapshot.AllowedGroupIds}; mode={snapshot.Mode}; cooldown={snapshot.PassiveCooldownSeconds}; media={snapshot.MediaOnlyReplyProbability:0.###}",
            AgentAuditRiskLevel.Low,
            true);
        return new AgentQChatPolicyChangeResult(true, "QChat policy applied.", snapshot);
    }

    public AgentQChatPolicySnapshot? GetQChatPolicySnapshotFromControlCenter(string characterRoot)
    {
        if (configurationSystem == null)
            return null;

        Type? qchatServiceType = ResolveType("Alife.Function.QChat.QChatService");
        if (qchatServiceType == null)
            return null;

        string normalizedCharacterRoot = string.IsNullOrWhiteSpace(characterRoot)
            ? ""
            : characterRoot.Trim();
        object? qchatConfig = configurationSystem.GetConfiguration(qchatServiceType, normalizedCharacterRoot);
        return qchatConfig == null
            ? null
            : BuildQChatPolicySnapshot(
                normalizedCharacterRoot,
                qchatConfig,
                InferQChatPolicyMode(qchatConfig));
    }

    public AgentQChatJoinedGroupSnapshot GetJoinedQChatGroupsFromControlCenter(string characterRoot)
    {
        IAgentQChatJoinedGroupProvider? joinedGroups = ResolveQChatJoinedGroupProvider();
        if (joinedGroups == null)
            return new AgentQChatJoinedGroupSnapshot(false, null, "QChat joined group provider is unavailable.", []);

        return BuildJoinedQChatGroupSnapshot(
            characterRoot,
            joinedGroups.GetCachedAgentJoinedGroups(),
            "QQ joined groups loaded from cached OneBot state.");
    }

    public async Task<AgentQChatJoinedGroupSnapshot> RefreshJoinedQChatGroupsFromControlCenter(string characterRoot)
    {
        IAgentQChatJoinedGroupProvider? joinedGroups = ResolveQChatJoinedGroupProvider();
        if (joinedGroups == null)
            return new AgentQChatJoinedGroupSnapshot(false, null, "QChat joined group provider is unavailable.", []);

        try
        {
            AgentQChatJoinedGroupSourceSnapshot source = await joinedGroups.RefreshAgentJoinedGroupsAsync();
            return BuildJoinedQChatGroupSnapshot(
                characterRoot,
                source,
                "QQ joined groups refreshed from OneBot.");
        }
        catch (Exception exception)
        {
            return new AgentQChatJoinedGroupSnapshot(false, null, $"QQ joined group refresh failed: {exception.Message}", []);
        }
    }

    public AgentQChatPolicyChangeResult AddAllowedQChatGroupFromControlCenter(
        string characterRoot,
        long groupId,
        string actor)
    {
        return ChangeAllowedQChatGroupFromControlCenter(characterRoot, groupId, add: true, actor);
    }

    public AgentQChatPolicyChangeResult RemoveAllowedQChatGroupFromControlCenter(
        string characterRoot,
        long groupId,
        string actor)
    {
        return ChangeAllowedQChatGroupFromControlCenter(characterRoot, groupId, add: false, actor);
    }

    AgentQChatPolicyChangeResult ChangeAllowedQChatGroupFromControlCenter(
        string characterRoot,
        long groupId,
        bool add,
        string actor)
    {
        if (groupId <= 0)
            return new AgentQChatPolicyChangeResult(false, "QChat group id must be a positive number.");

        AgentQChatPolicySnapshot? current = GetQChatPolicySnapshotFromControlCenter(characterRoot);
        if (current == null)
            return new AgentQChatPolicyChangeResult(false, "QChat configuration is unavailable.");

        List<string> groupIds = ParseAllowedGroupIds(current.AllowedGroupIds).ToList();
        string groupIdText = groupId.ToString();
        if (add)
        {
            if (groupIds.Contains(groupIdText, StringComparer.Ordinal) == false)
                groupIds.Add(groupIdText);
        }
        else
        {
            groupIds.RemoveAll(value => value.Equals(groupIdText, StringComparison.Ordinal));
        }

        AgentQChatPolicyChangeResult result = ApplyQChatPolicyFromControlCenter(
            current.CharacterRoot,
            string.Join(',', groupIds),
            current.Mode,
            current.PassiveCooldownSeconds,
            current.MediaOnlyReplyProbability,
            actor,
            current.AllowMentionOutsideAllowedGroups);
        if (result.Applied == false)
            return result;

        string action = add ? "added to" : "removed from";
        return result with { Message = $"QChat group {groupId} {action} allowed scope." };
    }

    AgentQChatJoinedGroupSnapshot BuildJoinedQChatGroupSnapshot(
        string characterRoot,
        AgentQChatJoinedGroupSourceSnapshot source,
        string message)
    {
        HashSet<string> allowedGroupIds = new(
            ParseAllowedGroupIds(GetQChatPolicySnapshotFromControlCenter(characterRoot)?.AllowedGroupIds ?? ""),
            StringComparer.Ordinal);
        AgentQChatJoinedGroupVisibility[] groups = source.Groups
            .Where(group => group.GroupId > 0)
            .OrderBy(group => group.GroupId)
            .Select(group => new AgentQChatJoinedGroupVisibility(
                group.GroupId,
                string.IsNullOrWhiteSpace(group.GroupName) ? "(unnamed)" : group.GroupName.Trim(),
                Math.Max(0, group.MemberCount),
                Math.Max(0, group.MaxMemberCount),
                allowedGroupIds.Contains(group.GroupId.ToString())))
            .ToArray();

        return new AgentQChatJoinedGroupSnapshot(
            true,
            source.RefreshedAt == DateTimeOffset.MinValue ? null : source.RefreshedAt,
            message,
            groups);
    }

    public static string BuildConfigurationProposalConfirmationText(AgentConfigurationChangeProposal proposal)
    {
        return $"confirm execute <agent_config_apply_proposal id=\"{EscapeXmlAttribute(proposal.Id)}\" />";
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        proactiveBehavior ??= context.Services.GetService(typeof(AgentProactiveBehaviorService)) as AgentProactiveBehaviorService;
        QChatJoinedGroupProviderOverride ??= context.Services.GetService(typeof(IAgentQChatJoinedGroupProvider)) as IAgentQChatJoinedGroupProvider;
        if (QChatJoinedGroupProviderOverride == null
            && context.Services.GetService(typeof(IEnumerable<IAgentQChatJoinedGroupProvider>)) is IEnumerable<IAgentQChatJoinedGroupProvider> providers)
            QChatJoinedGroupProviderOverride = providers.FirstOrDefault();
        if (context.Services.GetService(typeof(IEnumerable<IAgentProactiveSuggestionExecutor>)) is IEnumerable<IAgentProactiveSuggestionExecutor> executors)
            proactiveExecutors = executors.ToArray();
        functionCaller?.RegisterHandler(this);
    }

    IAgentQChatJoinedGroupProvider? ResolveQChatJoinedGroupProvider()
    {
        if (QChatJoinedGroupProviderOverride != null)
            return QChatJoinedGroupProviderOverride;

        try
        {
            return ChatActivity.ModuleService.ResolveOptional<IAgentQChatJoinedGroupProvider>()
                   ?? ChatActivity.ModuleService.Resolve<IEnumerable<IAgentQChatJoinedGroupProvider>>().FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    AgentProactiveBehaviorService GetProactiveBehavior()
    {
        return proactiveBehavior ?? throw new InvalidOperationException("Agent proactive behavior service is unavailable.");
    }

    static AgentWorkspacePolicy NormalizeWorkspacePolicy(AgentWorkspacePolicy rawPolicy)
    {
        string[] roots = rawPolicy.AllowedRoots
            .Where(root => string.IsNullOrWhiteSpace(root) == false)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length == 0)
            throw new ArgumentException("At least one workspace root is required.", nameof(rawPolicy));

        return rawPolicy with { AllowedRoots = roots };
    }

    static AgentCommandPolicy NormalizeCommandPolicy(AgentCommandPolicy rawPolicy)
    {
        AgentCommandDefinition[] commands = rawPolicy.AllowedCommands
            .Where(command => string.IsNullOrWhiteSpace(command.Id) == false)
            .Select(command => command with
            {
                Id = command.Id.Trim(),
                Description = command.Description.Trim(),
                FileName = command.FileName.Trim(),
                Arguments = command.Arguments.Trim(),
                WorkingDirectory = Path.GetFullPath(command.WorkingDirectory),
                Timeout = command.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : command.Timeout
            })
            .ToArray();

        return new AgentCommandPolicy(commands);
    }

    static AgentWorkspacePolicy CreateDefaultWorkspacePolicy()
    {
        string agentWorkspace = Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace");
        return new AgentWorkspacePolicy([Environment.CurrentDirectory, agentWorkspace, AlifePath.TempFolderPath]);
    }

    static AgentCommandPolicy CreateDefaultCommandPolicy()
    {
        string cwd = Environment.CurrentDirectory;
        return new AgentCommandPolicy([
            new AgentCommandDefinition("git-status", "Show repository status.", "git", "status --short", cwd, TimeSpan.FromSeconds(20)),
            new AgentCommandDefinition("git-diff", "Show unstaged repository diff.", "git", "diff --", cwd, TimeSpan.FromSeconds(20)),
            new AgentCommandDefinition("dotnet-build-solution", "Build the Alife solution without restoring packages.", "dotnet", "build Alife.slnx --no-restore", cwd, TimeSpan.FromMinutes(3)),
            new AgentCommandDefinition("dotnet-test-solution", "Run the Alife solution tests without restoring packages.", "dotnet", "test Alife.slnx --no-restore", cwd, TimeSpan.FromMinutes(5))
        ]);
    }

    static AgentControlCenterAttentionSummary BuildAttentionSummary(
        IReadOnlyList<AgentConfigurationChangeProposal> pendingConfigurationProposals,
        IReadOnlyList<AgentWorkspacePatchProposal> pendingWorkspaceProposals,
        IReadOnlyList<AgentProactivePendingSuggestion> pendingProactiveSuggestions,
        IReadOnlyList<AgentMaintenanceProposal> pendingMaintenanceProposals,
        IReadOnlyList<AgentAuditLogEntry> recentAuditEntries)
    {
        List<string> ownerItems = [];
        ownerItems.AddRange(pendingConfigurationProposals
            .Select(proposal => $"Configuration: {proposal.Key}"));
        ownerItems.AddRange(pendingWorkspaceProposals
            .Select(proposal => $"Workspace: {proposal.RelativePath}"));
        ownerItems.AddRange(pendingMaintenanceProposals
            .Where(proposal => proposal.RequiresOwnerConfirmationForExecution)
            .Select(proposal => $"Maintenance: {proposal.Title}"));
        ownerItems.AddRange(pendingProactiveSuggestions
            .Where(pending => pending.Suggestion.RequiresOwnerConfirmation)
            .Select(pending => $"Proactive: {pending.Suggestion.Kind}"));

        string[] autonomousItems = recentAuditEntries
            .Where(entry => entry.Succeeded
                            && entry.RiskLevel == AgentAuditRiskLevel.Low
                            && entry.Actor.StartsWith("agent", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Timestamp)
            .Select(entry => $"{entry.Action}: {entry.Detail}")
            .Take(6)
            .ToArray();

        return new AgentControlCenterAttentionSummary(
            ownerItems.Count,
            autonomousItems.Length,
            ownerItems,
            autonomousItems);
    }

    static AgentControlCenterNotificationSummary BuildNotificationSummary(
        AgentControlCenterAttentionSummary attentionSummary,
        AgentIssueReportSnapshot issueReport)
    {
        List<AgentControlCenterNotification> items = [];
        if (attentionSummary.OwnerConfirmationRequiredCount > 0)
        {
            string preview = string.Join("; ", attentionSummary.OwnerConfirmationItems.Take(3));
            items.Add(new AgentControlCenterNotification(
                "owner-confirmation",
                AgentAuditRiskLevel.High,
                $"{attentionSummary.OwnerConfirmationRequiredCount} item(s) need owner confirmation: {preview}"));
        }

        items.AddRange(issueReport.FailedAuditEntries
            .GroupBy(entry => entry.Action, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .Select(group => new AgentControlCenterNotification(
                "repeated-failure",
                group.Any(entry => entry.RiskLevel == AgentAuditRiskLevel.High)
                    ? AgentAuditRiskLevel.High
                    : AgentAuditRiskLevel.Medium,
                $"{group.Key} failed {group.Count()} times. Latest: {group.Last().Error ?? group.Last().Detail}")));

        items.AddRange(issueReport.UnhealthyModules
            .Where(health => AgentHealthIssueClassifier.Classify(health) == AgentHealthIssueKind.ExternalEnvironment)
            .Select(health => new AgentControlCenterNotification(
                IsQqEnvironmentHealth(health) ? "qq-environment" : "external-environment",
                AgentAuditRiskLevel.Low,
                $"{health.Name} external environment is not ready: {health.Summary}")));

        AgentControlCenterNotification[] snapshot = items
            .Take(8)
            .ToArray();
        return new AgentControlCenterNotificationSummary(snapshot.Length > 0, snapshot);
    }

    static AgentControlCenterHealthTriageSnapshot BuildHealthTriage(
        AgentIssueReportSnapshot issueReport,
        AgentControlCenterAttentionSummary attentionSummary)
    {
        List<ModuleHealth> waiting = [];
        List<ModuleHealth> externalEnvironment = [];
        List<ModuleHealth> faults = [];

        foreach (ModuleHealth health in issueReport.UnhealthyModules)
        {
            switch (AgentHealthIssueClassifier.Classify(health))
            {
                case AgentHealthIssueKind.ActionableFault:
                    faults.Add(health);
                    break;
                case AgentHealthIssueKind.ExternalEnvironment:
                    externalEnvironment.Add(health);
                    break;
                default:
                    waiting.Add(health);
                    break;
            }
        }

        return new AgentControlCenterHealthTriageSnapshot(
            waiting,
            externalEnvironment,
            faults,
            attentionSummary.OwnerConfirmationItems.ToArray());
    }

    public static AgentOwnerNotificationPlan BuildOwnerNotificationPlan(
        AgentControlCenterSnapshot snapshot,
        string ownerPrivateSessionId,
        string? sourceGroupSessionId = null)
    {
        List<string> privateMessages = [];
        privateMessages.AddRange(snapshot.AttentionSummary.OwnerConfirmationItems
            .Select(item => $"Owner confirmation required: {item}"));
        privateMessages.AddRange(snapshot.NotificationSummary.Items
            .Where(item => item.Kind != "owner-confirmation")
            .Select(item => $"{item.Kind}: {item.Message}"));

        bool shouldNotify = privateMessages.Count > 0 || snapshot.SelfCheck.OwnerReviewCount > 0;
        string targetSession = string.IsNullOrWhiteSpace(ownerPrivateSessionId)
            ? "owner:private"
            : ownerPrivateSessionId.Trim();
        string groupSummary = shouldNotify
            ? "Internal control-center items need owner attention. Details were kept private."
            : "No owner attention is currently required.";

        return new AgentOwnerNotificationPlan(
            shouldNotify,
            targetSession,
            groupSummary,
            privateMessages.Take(8).ToArray(),
            string.IsNullOrWhiteSpace(sourceGroupSessionId) ? null : sourceGroupSessionId.Trim());
    }

    public static string FormatSelfCheckForAgent(AgentControlCenterSelfCheckSnapshot selfCheck)
    {
        StringBuilder builder = new();
        builder.AppendLine("Agent self-check");
        builder.AppendLine($"Owner review: {selfCheck.OwnerReviewCount}");
        builder.AppendLine($"Autonomous recommendations: {selfCheck.AutonomousRecommendationCount}");

        if (selfCheck.Items.Count == 0)
        {
            builder.Append("- no items");
            return builder.ToString();
        }

        foreach (AgentControlCenterSelfCheckItem item in selfCheck.Items)
        {
            string handling = item.CanAgentHandleAutonomously ? "agent-can-handle" : "owner-review";
            builder.AppendLine($"- [{item.RiskLevel}] {item.Category} ({handling}): {item.Summary}");
            builder.AppendLine($"  action: {item.RecommendedAction}");
            if (string.IsNullOrWhiteSpace(item.ActionId) == false)
                builder.AppendLine($"  action_id: {item.ActionId}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatQChatRecentDecisionSummaryForAgent(
        IReadOnlyList<AgentQChatGroupDecisionVisibility> decisions)
    {
        if (decisions.Count == 0)
            return "Internal QQ group decision diagnostic: no recent group reply decisions. Keep this out of user-facing chat.";

        StringBuilder builder = new();
        builder.AppendLine("Internal QQ group decision diagnostic");
        builder.AppendLine("Keep this out of user-facing chat.");
        foreach (AgentQChatGroupDecisionVisibility decision in decisions.Take(10))
        {
            builder.Append("- ");
            builder.Append(decision.Timestamp?.ToString("HH:mm:ss") ?? "time=unknown");
            builder.Append($"; group={decision.GroupId}");
            builder.Append($"; user={decision.UserId}");
            builder.Append($"; decision={decision.Decision}");
            builder.Append($"; reason={decision.Reason}");
            builder.Append($"; wake={decision.IsMentionedOrWoken}");
            builder.Append($"; active={decision.IsGroupEnabled}");
            builder.Append($"; probability={FormatAgentProbability(decision.SocialAttentionProbability)}");
            builder.Append($"; cooldown={FormatAgentSeconds(decision.CooldownRemainingSeconds)}");
            builder.Append($"; activeWindow={FormatAgentSeconds(decision.ActiveSoftAttentionRemainingSeconds)}");
            if (string.IsNullOrWhiteSpace(decision.RawMessage) == false)
                builder.Append($"; message={TrimAgentDiagnostic(decision.RawMessage, 80)}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    static AgentControlCenterSelfCheckSnapshot BuildSelfCheck(
        AgentControlCenterConfig configuration,
        AgentControlCenterAttentionSummary attentionSummary,
        AgentIssueReportSnapshot issueReport,
        MemoryConsistencySnapshot memoryConsistency,
        AgentControlCenterRuntimeVisibility runtimeVisibility)
    {
        List<AgentControlCenterSelfCheckItem> items = [];

        items.AddRange(attentionSummary.OwnerConfirmationItems
            .Take(4)
            .Select(item => new AgentControlCenterSelfCheckItem(
                "owner-confirmation",
                AgentAuditRiskLevel.High,
                item,
                "Wait for owner confirmation before applying this change or executing the external action.",
                CanAgentHandleAutonomously: false)));

        if (memoryConsistency.HasIssues)
        {
            items.Add(new AgentControlCenterSelfCheckItem(
                "memory-consistency",
                AgentAuditRiskLevel.Low,
                $"Memory storage consistency issues: missing archives={memoryConsistency.MissingArchiveFiles}; missing indexes={memoryConsistency.MissingIndexRecords}; content mismatches={memoryConsistency.ContentMismatches}.",
                "Apply the low-risk memory consistency repair action so storage can recreate missing archive/index records and rewrite mismatched archive text from DB-authoritative content.",
                CanAgentHandleAutonomously: true,
                ActionId: "repair-memory-storage-consistency"));
        }

        string? latestRuntimeError = issueReport.LastError
                                     ?? issueReport.RuntimeErrors.LastOrDefault()?.Detail;
        if (string.IsNullOrWhiteSpace(latestRuntimeError) == false)
        {
            if (configuration.AllowAutomaticMaintenanceInspection == false)
            {
                items.Add(new AgentControlCenterSelfCheckItem(
                    "maintenance-disabled",
                    AgentAuditRiskLevel.Low,
                    "Automatic maintenance inspection is disabled while a runtime error is visible.",
                    "Apply the low-risk control-center action to enable automatic maintenance inspection.",
                    CanAgentHandleAutonomously: true,
                    ActionId: "enable-auto-maintenance"));
            }

            items.Add(new AgentControlCenterSelfCheckItem(
                "runtime-error",
                AgentAuditRiskLevel.Medium,
                latestRuntimeError,
                configuration.AllowAutomaticMaintenanceInspection
                    ? "Run the automatic maintenance inspection path and create a repair task if the issue repeats."
                    : "Report the runtime error to the owner because automatic maintenance inspection is disabled.",
                configuration.AllowAutomaticMaintenanceInspection));
        }

        foreach (ModuleHealth health in issueReport.UnhealthyModules.Take(6))
        {
            AgentHealthIssueKind kind = AgentHealthIssueClassifier.Classify(health);
            if (kind == AgentHealthIssueKind.ActionableFault)
            {
                items.Add(new AgentControlCenterSelfCheckItem(
                    "module-fault",
                    AgentAuditRiskLevel.Medium,
                    $"{health.Name}: {health.Summary}",
                    configuration.AllowAutomaticMaintenanceInspection
                        ? "Run maintenance inspection only if this module fault persists or blocks a requested ability."
                        : "Report the module fault to the owner because automatic maintenance inspection is disabled.",
                    configuration.AllowAutomaticMaintenanceInspection));
                continue;
            }

            if (kind == AgentHealthIssueKind.ExternalEnvironment)
            {
                items.Add(new AgentControlCenterSelfCheckItem(
                    "external-environment",
                    AgentAuditRiskLevel.Low,
                    $"{health.Name}: {health.Summary}",
                    "Keep normal chat responsive; ask the owner only when this external environment is needed for a requested action.",
                    CanAgentHandleAutonomously: true));
                continue;
            }

            items.Add(new AgentControlCenterSelfCheckItem(
                "module-waiting",
                AgentAuditRiskLevel.Low,
                $"{health.Name}: {health.Summary}",
                "Treat this as a normal waiting/configuration state unless a real runtime error appears.",
                CanAgentHandleAutonomously: true));
        }

        AgentBackgroundTaskResult? failedBackgroundTask = runtimeVisibility.BackgroundTasks
            .FirstOrDefault(result => result.Status == AgentBackgroundTaskStatus.Failed);
        if (failedBackgroundTask != null)
        {
            items.Add(new AgentControlCenterSelfCheckItem(
                "background-task",
                AgentAuditRiskLevel.Medium,
                $"{failedBackgroundTask.TaskName}: {failedBackgroundTask.Error}",
                "Review the failed background result, summarize it, and retry only through the normal action gateway when safe.",
                CanAgentHandleAutonomously: true));
        }

        if (runtimeVisibility.ChatLatency.LastFirstContentLatencyMs is > 3000)
        {
            items.Add(new AgentControlCenterSelfCheckItem(
                "streaming-latency",
                AgentAuditRiskLevel.Low,
                $"First content latency is {runtimeVisibility.ChatLatency.LastFirstContentLatencyMs} ms.",
                "Keep normal chat on fast context mode and move diagnostics, long memory, and tool work to background tasks.",
                CanAgentHandleAutonomously: true,
                ActionId: "reduce-proactive-intensity"));
        }

        if (runtimeVisibility.QChat.RecentFailureCount > 0)
        {
            items.Add(new AgentControlCenterSelfCheckItem(
                "qq-runtime-failure",
                AgentAuditRiskLevel.Medium,
                $"QQ runtime has {runtimeVisibility.QChat.RecentFailureCount} recent failure event(s): {string.Join("; ", runtimeVisibility.QChat.RecentFailures.Take(2))}",
                "Do not retry noisy QQ output automatically. Summarize the failure and ask the owner to check OneBot/NapCat only when QQ communication is needed.",
                CanAgentHandleAutonomously: false));
        }

        if (runtimeVisibility.QChat.RecentOutboundMessageCount >= 6)
        {
            items.Add(new AgentControlCenterSelfCheckItem(
                "qq-output-volume",
                AgentAuditRiskLevel.Low,
                $"QQ sent {runtimeVisibility.QChat.RecentOutboundMessageCount} recent message(s).",
                "Reduce proactive chat intensity before sending more unsolicited QQ messages.",
                CanAgentHandleAutonomously: true,
                ActionId: "reduce-proactive-intensity"));
        }

        if (runtimeVisibility.ActionGatewayAudit.BlockedCount > 0)
        {
            items.Add(new AgentControlCenterSelfCheckItem(
                "security-gateway",
                AgentAuditRiskLevel.Low,
                $"{runtimeVisibility.ActionGatewayAudit.BlockedCount} external action(s) were blocked by the gateway.",
                "Treat blocked actions as successful safety control; do not retry unless the owner gives explicit confirmation.",
                CanAgentHandleAutonomously: false));
        }

        if (items.Count == 0)
        {
            items.Add(new AgentControlCenterSelfCheckItem(
                "ready",
                AgentAuditRiskLevel.Low,
                "No immediate runtime issues or owner-review items are visible.",
                "Continue normal fast-path chat and use background tasks for heavier work.",
                CanAgentHandleAutonomously: true));
        }

        return new AgentControlCenterSelfCheckSnapshot(
            items.Count(item => item.CanAgentHandleAutonomously == false),
            items.Count(item => item.CanAgentHandleAutonomously),
            items);
    }

    static bool IsQqEnvironmentHealth(ModuleHealth health)
    {
        return health.Name.Contains("QChat", StringComparison.OrdinalIgnoreCase)
               || health.Name.Contains("QQ", StringComparison.OrdinalIgnoreCase)
               || health.Name.Contains("OneBot", StringComparison.OrdinalIgnoreCase)
               || health.Summary.Contains("OneBot", StringComparison.OrdinalIgnoreCase)
               || health.Summary.Contains("QQ", StringComparison.OrdinalIgnoreCase);
    }

    static IReadOnlyList<AgentExecutionGatewayDecision> BuildSecurityGatewayPreview()
    {
        AgentActionAuthorizationService authorization = new();
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [0],
            AllowGroupLowRisk = true,
            AllowGroupMediumRiskWhenMentioned = true,
            RequireConfirmationForHighRisk = true
        };

        return [
            authorization.EvaluateExecution(
                new AgentPermissionRequest(
                    ActorUserId: null,
                    Source: AgentRequestSource.System,
                    IsMentioned: false,
                    RiskLevel: AgentRiskLevel.Low,
                    HasExplicitConfirmation: false,
                    Action: "maintenance.inspect"),
                config),
            authorization.EvaluateExecution(
                new AgentPermissionRequest(
                    ActorUserId: 0,
                    Source: AgentRequestSource.PrivateChat,
                    IsMentioned: false,
                    RiskLevel: AgentRiskLevel.High,
                    HasExplicitConfirmation: false,
                    Action: "workspace.apply"),
                config),
            authorization.EvaluateExecution(
                new AgentPermissionRequest(
                    ActorUserId: 20002,
                    Source: AgentRequestSource.GroupChat,
                    IsMentioned: true,
                    RiskLevel: AgentRiskLevel.High,
                    HasExplicitConfirmation: true,
                    Action: "qzone.reply"),
                config),
            authorization.EvaluateExecution(
                new AgentPermissionRequest(
                    ActorUserId: 20002,
                    Source: AgentRequestSource.GroupChat,
                    IsMentioned: true,
                    RiskLevel: AgentRiskLevel.High,
                    HasExplicitConfirmation: true,
                    Action: "github.upload"),
                config)
        ];
    }

    MemoryConsistencySnapshot BuildMemoryConsistencySnapshot()
    {
        return memoryConsistencyReporter?.GetMemoryConsistencySnapshot()
               ?? MemoryConsistencySnapshot.Empty;
    }

    AgentControlCenterRuntimeVisibility BuildRuntimeVisibility(
        ChatRuntimeState runtimeState,
        IReadOnlyList<AgentAuditLogEntry> recentAuditEntries)
    {
        AgentAuditLogEntry[] gatewayEntries = recentAuditEntries
            .Where(IsExternalActionAuditEntry)
            .ToArray();

        return new AgentControlCenterRuntimeVisibility(
            BuildStreamingPolicyVisibility(),
            new AgentChatLatencyVisibility(
                ToMilliseconds(runtimeState.Latency.LastFirstContentLatency),
                ToMilliseconds(runtimeState.Latency.LastChatDuration),
                runtimeState.Latency.LastChatStartedAt,
                runtimeState.Latency.LastFirstContentAt,
                runtimeState.Latency.LastChatEndedAt),
            eventPipeline.GetSnapshot(),
            backgroundTaskResults
                .OrderByDescending(result => result.CompletedAt)
                .Take(8)
                .ToArray(),
            new AgentActionGatewayAuditSummary(
                gatewayEntries.Count(entry => entry.Succeeded),
                gatewayEntries.Count(IsBlockedGatewayAuditEntry),
                gatewayEntries.Count(entry => entry.Succeeded == false && IsBlockedGatewayAuditEntry(entry) == false),
                gatewayEntries.TakeLast(8).ToArray()),
            recentRunSessions
                .OrderByDescending(session => session.StartedAt)
                .Take(8)
                .ToArray(),
            BuildQChatRuntimeVisibility());
    }

    AgentQChatRuntimeVisibility BuildQChatRuntimeVisibility()
    {
        try
        {
            if (File.Exists(qchatDiagnosticsPath) == false)
                return AgentQChatRuntimeVisibility.Empty;

            QChatDiagnosticEntry[] entries = File.ReadLines(qchatDiagnosticsPath)
                .TakeLast(200)
                .Select(TryParseQChatDiagnosticEntry)
                .OfType<QChatDiagnosticEntry>()
                .ToArray();
            int latestStartIndex = Array.FindLastIndex(
                entries,
                entry => entry.EventName.Equals("start", StringComparison.OrdinalIgnoreCase));
            if (latestStartIndex >= 0)
                entries = entries.Skip(latestStartIndex).ToArray();

            QChatDiagnosticEntry[] failures = entries
                .Where(entry => IsQChatFailureEvent(entry.EventName))
                .ToArray();
            AgentQChatAntiSpamVisibility antiSpam = BuildQChatAntiSpamVisibility(entries);

            return new AgentQChatRuntimeVisibility(
                entries.Count(entry => entry.EventName.Equals("connect-succeeded", StringComparison.OrdinalIgnoreCase)),
                entries.Count(entry => entry.EventName.Equals("message-dispatching", StringComparison.OrdinalIgnoreCase)),
                entries.Count(entry => entry.EventName.Equals("qchat-sent", StringComparison.OrdinalIgnoreCase)
                                       || entry.EventName.Equals("plain-fallback-sent", StringComparison.OrdinalIgnoreCase)),
                failures.Length,
                entries.Count(entry => entry.EventName.Equals("qchat-quiet-message-suppressed", StringComparison.OrdinalIgnoreCase)),
                entries.LastOrDefault(entry => entry.EventName.Equals("connect-succeeded", StringComparison.OrdinalIgnoreCase))?.Timestamp,
                entries.LastOrDefault(entry => entry.EventName.Equals("message-dispatching", StringComparison.OrdinalIgnoreCase))?.Timestamp,
                entries.LastOrDefault(entry => entry.EventName.Equals("qchat-sent", StringComparison.OrdinalIgnoreCase)
                                               || entry.EventName.Equals("plain-fallback-sent", StringComparison.OrdinalIgnoreCase))?.Timestamp,
                failures
                    .TakeLast(5)
                    .Select(entry => $"{entry.EventName}: {entry.Detail}")
                    .ToArray(),
                antiSpam);
        }
        catch
        {
            return AgentQChatRuntimeVisibility.Empty;
        }
    }

    static AgentQChatAntiSpamVisibility BuildQChatAntiSpamVisibility(IReadOnlyList<QChatDiagnosticEntry> entries)
    {
        QChatDiagnosticEntry[] suppressed = entries
            .Where(entry => GetAntiSpamSuppressionReason(entry.EventName).Length > 0)
            .ToArray();
        QChatDiagnosticEntry[] lowInformation = suppressed
            .Where(entry => entry.EventName.Equals("group-passive-low-information-skipped", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        QChatDiagnosticEntry[] cooldown = suppressed
            .Where(entry => entry.EventName.Equals("group-passive-throttled", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        QChatDiagnosticEntry[] quiet = suppressed
            .Where(entry => entry.EventName.Equals("qchat-quiet-message-suppressed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        QChatDiagnosticEntry[] scope = suppressed
            .Where(entry => entry.EventName.Equals("group-passive-scope-skipped", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        QChatDiagnosticEntry? latestCooldown = cooldown.LastOrDefault();
        QChatDiagnosticEntry? latestProactive = entries.LastOrDefault(entry =>
            entry.EventName.Equals("group-buffered-proactive", StringComparison.OrdinalIgnoreCase));
        QChatDiagnosticEntry? latestMediaChanceAllowed = entries.LastOrDefault(entry =>
            entry.EventName.Equals("group-passive-media-chance-allowed", StringComparison.OrdinalIgnoreCase));
        QChatDiagnosticEntry? latestQuietMode = entries.LastOrDefault(entry =>
            entry.EventName.Equals("qchat-quiet-mode-enabled", StringComparison.OrdinalIgnoreCase)
            || entry.EventName.Equals("qchat-quiet-mode-disabled", StringComparison.OrdinalIgnoreCase)
            || entry.EventName.Equals("qchat-quiet-mode-restored", StringComparison.OrdinalIgnoreCase)
            || entry.EventName.Equals("qchat-quiet-mode-restore-skipped", StringComparison.OrdinalIgnoreCase));
        QChatDiagnosticEntry? latestStart = entries.LastOrDefault(entry =>
            entry.EventName.Equals("start", StringComparison.OrdinalIgnoreCase));
        QChatDiagnosticEntry? latestSuppression = suppressed.LastOrDefault();

        return new AgentQChatAntiSpamVisibility(
            entries.Count(entry =>
                entry.EventName.Equals("message-dispatching", StringComparison.OrdinalIgnoreCase)
                && entry.GetStringData("MessageType").Equals("Group", StringComparison.OrdinalIgnoreCase)),
            entries.Count(entry =>
                entry.EventName.Equals("group-buffered", StringComparison.OrdinalIgnoreCase)
                || entry.EventName.Equals("group-buffered-proactive", StringComparison.OrdinalIgnoreCase)),
            suppressed.Length,
            lowInformation.Length,
            cooldown.Length,
            quiet.Length,
            scope.Length,
            entries.Count(entry => entry.EventName.Equals("group-passive-media-chance-allowed", StringComparison.OrdinalIgnoreCase)),
            latestQuietMode?.EventName.Equals("qchat-quiet-mode-enabled", StringComparison.OrdinalIgnoreCase) == true
            || latestQuietMode?.EventName.Equals("qchat-quiet-mode-restored", StringComparison.OrdinalIgnoreCase) == true
               && latestQuietMode.GetBoolData("IsQuietModeEnabled") == true,
            latestQuietMode?.GetStringData("reason") is { Length: > 0 } reason
                ? reason
                : latestQuietMode?.GetStringData("QuietModeReason") ?? "",
            latestStart?.GetStringData("AllowedGroupIds")
            ?? scope.LastOrDefault()?.GetStringData("AllowedGroupIds")
            ?? "",
            latestCooldown?.GetIntData("cooldownSeconds") ?? 0,
            latestCooldown?.GetDoubleData("elapsedSeconds"),
            latestProactive?.GetDoubleData("EffectiveProactiveChatProbability")
            ?? latestProactive?.GetDoubleData("ProactiveChatProbability"),
            latestMediaChanceAllowed?.GetDoubleData("MediaOnlyPassiveGroupReplyProbability"),
            latestSuppression == null ? "" : GetAntiSpamSuppressionReason(latestSuppression.EventName),
            suppressed
                .Select(entry => GetAntiSpamSuppressionReason(entry.EventName))
                .Where(reason => reason.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            BuildQChatGroupDecisionVisibility(entries));
    }

    static IReadOnlyList<AgentQChatGroupDecisionVisibility> BuildQChatGroupDecisionVisibility(
        IReadOnlyList<QChatDiagnosticEntry> entries)
    {
        return entries
            .Where(entry => entry.EventName.Equals("group-decision", StringComparison.OrdinalIgnoreCase))
            .TakeLast(10)
            .Reverse()
            .Select(entry => new AgentQChatGroupDecisionVisibility(
                entry.Timestamp,
                entry.GetLongData("GroupId") ?? 0,
                entry.GetLongData("UserId") ?? 0,
                entry.GetStringData("Decision"),
                entry.GetStringData("Reason"),
                entry.GetBoolData("IsMentionedOrWoken") ?? false,
                entry.GetBoolData("IsGroupEnabled") ?? false,
                entry.GetDoubleData("SocialAttentionProbability"),
                entry.GetIntData("CooldownRemainingSeconds"),
                entry.GetIntData("ActiveSoftAttentionRemainingSeconds"),
                entry.GetStringData("RawMessage")))
            .ToArray();
    }

    static string GetAntiSpamSuppressionReason(string eventName)
    {
        if (eventName.Equals("group-filtered", StringComparison.OrdinalIgnoreCase))
            return "policy";
        if (eventName.Equals("group-passive-low-information-skipped", StringComparison.OrdinalIgnoreCase))
            return "low-information";
        if (eventName.Equals("group-passive-throttled", StringComparison.OrdinalIgnoreCase))
            return "cooldown";
        if (eventName.Equals("group-passive-social-attention-skipped", StringComparison.OrdinalIgnoreCase))
            return "social-attention";
        if (eventName.Equals("group-active-soft-attention-skipped", StringComparison.OrdinalIgnoreCase))
            return "active-soft-attention-expired";
        if (eventName.Equals("group-passive-scope-skipped", StringComparison.OrdinalIgnoreCase))
            return "scope";
        if (eventName.Equals("qchat-quiet-message-suppressed", StringComparison.OrdinalIgnoreCase))
            return "quiet-mode";
        return "";
    }

    static QChatDiagnosticEntry? TryParseQChatDiagnosticEntry(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            DateTimeOffset? timestamp = null;
            if (root.TryGetProperty("timestamp", out JsonElement timestampElement)
                && timestampElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(timestampElement.GetString(), out DateTimeOffset parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }

            string eventName = root.TryGetProperty("eventName", out JsonElement eventNameElement)
                ? eventNameElement.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(eventName))
                return null;

            string detail = root.TryGetProperty("detail", out JsonElement detailElement)
                ? detailElement.GetString() ?? ""
                : "";

            Dictionary<string, string> data = new(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("data", out JsonElement dataElement)
                && dataElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in dataElement.EnumerateObject())
                    data[property.Name] = property.Value.ToString();
            }

            return new QChatDiagnosticEntry(timestamp, eventName, detail, data);
        }
        catch
        {
            return null;
        }
    }

    static bool IsQChatFailureEvent(string eventName)
    {
        return eventName.Equals("qchat-send-failed", StringComparison.OrdinalIgnoreCase)
               || eventName.Equals("model-dispatch-failed", StringComparison.OrdinalIgnoreCase)
               || eventName.Equals("connect-failed", StringComparison.OrdinalIgnoreCase);
    }

    sealed record QChatDiagnosticEntry(
        DateTimeOffset? Timestamp,
        string EventName,
        string Detail,
        IReadOnlyDictionary<string, string> Data)
    {
        public string GetStringData(string key)
        {
            return Data.TryGetValue(key, out string? value) ? value : "";
        }

        public int? GetIntData(string key)
        {
            return int.TryParse(GetStringData(key), out int value) ? value : null;
        }

        public long? GetLongData(string key)
        {
            return long.TryParse(GetStringData(key), out long value) ? value : null;
        }

        public double? GetDoubleData(string key)
        {
            return double.TryParse(GetStringData(key), out double value) ? value : null;
        }

        public bool? GetBoolData(string key)
        {
            return bool.TryParse(GetStringData(key), out bool value) ? value : null;
        }
    }

    static IReadOnlyList<AgentStreamingPolicyVisibility> BuildStreamingPolicyVisibility()
    {
        return [
            ToStreamingVisibility("QQ group", "group chat", StreamingOutputPolicy.QqGroupText),
            ToStreamingVisibility("QQ private", "private chat", StreamingOutputPolicy.QqPrivateText),
            ToStreamingVisibility("DeskPet/UI", "local UI", StreamingOutputPolicy.Token)
        ];
    }

    static AgentStreamingPolicyVisibility ToStreamingVisibility(string name, string target, StreamingOutputPolicy policy)
    {
        return new AgentStreamingPolicyVisibility(
            name,
            target,
            policy.Mode,
            policy.MinBufferedCharacters,
            policy.MaxBufferedCharacters);
    }

    static long? ToMilliseconds(TimeSpan? value)
    {
        return value == null ? null : (long)Math.Round(value.Value.TotalMilliseconds);
    }

    static string FormatAgentSeconds(int? seconds)
    {
        return seconds == null ? "unknown" : $"{Math.Max(0, seconds.Value)}s";
    }

    static string FormatAgentProbability(double? probability)
    {
        return probability == null ? "unknown" : $"{probability.Value:P0}";
    }

    static string TrimAgentDiagnostic(string value, int maxLength)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    static bool IsExternalActionAuditEntry(AgentAuditLogEntry entry)
    {
        return entry.Action.StartsWith("qq.", StringComparison.OrdinalIgnoreCase)
               || entry.Action.StartsWith("qzone.", StringComparison.OrdinalIgnoreCase)
               || entry.Action.StartsWith("github.", StringComparison.OrdinalIgnoreCase)
               || entry.Action.StartsWith("workspace.", StringComparison.OrdinalIgnoreCase)
               || entry.Action.StartsWith("maintenance.", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsBlockedGatewayAuditEntry(AgentAuditLogEntry entry)
    {
        return entry.Succeeded == false
               && (entry.Error?.Contains("Blocked", StringComparison.OrdinalIgnoreCase) == true
                   || entry.Error?.Contains("Owner confirmation required", StringComparison.OrdinalIgnoreCase) == true);
    }

    AgentTaskState? EnsureMaintenanceTask(AgentMaintenanceProposal? proposal)
    {
        if (proposal == null)
            return null;

        AgentTaskState? existing = tasks.GetTasks()
            .Where(task => task.Status is AgentTaskStatus.Planned or AgentTaskStatus.Running)
            .FirstOrDefault(task => task.Events.Any(taskEvent =>
                taskEvent.Detail.Contains(proposal.Id, StringComparison.OrdinalIgnoreCase)));
        if (existing != null)
            return existing;

        AgentTaskState created = tasks.CreateTask(
            "agent-control-ui",
            $"Resolve maintenance proposal: {proposal.Title}",
            [
                $"Review maintenance proposal {proposal.Id}",
                "Prepare owner-confirmed workspace proposal",
                "Run focused verification",
                "Record repair evidence",
                "Archive maintenance proposal"
            ]);
        tasks.RecordProgress(
            created.Id,
            "agent-control-ui",
            $"Linked maintenance proposal {proposal.Id}");
        auditLog.Record(
            "agent.maintenance.task.created",
            "agent-control-ui",
            $"proposal={proposal.Id}; task={created.Id}",
            AgentAuditRiskLevel.Low,
            true);
        return tasks.GetTask(created.Id);
    }

    public AgentControlCenterSelfCheckLoopResult? TryAutomaticSelfCheck(
        ChatRuntimeState runtimeState,
        string characterName,
        string sourceSessionId)
    {
        AgentControlCenterConfig config = EnsureConfiguration();
        if (config.AllowAutomaticMaintenanceInspection == false)
        {
            lastAutomaticSelfCheckSkipReason = "disabled";
            return null;
        }
        if (runtimeState.IsChatting)
        {
            lastAutomaticSelfCheckSkipReason = "chatting";
            return null;
        }

        DateTimeOffset now = clock();
        TimeSpan interval = TimeSpan.FromMinutes(Math.Max(1, config.MaintenanceInspectionIntervalMinutes));
        if (lastAutomaticSelfCheckAt.HasValue && now - lastAutomaticSelfCheckAt.Value < interval)
        {
            lastAutomaticSelfCheckSkipReason = "interval";
            return null;
        }

        lastAutomaticSelfCheckAt = now;
        lastAutomaticSelfCheckSkipReason = "none";
        AgentMaintenanceInspectionResult? maintenanceInspection = TryAutomaticMaintenanceInspection(runtimeState);
        AgentControlCenterSnapshot snapshot = BuildSnapshot(runtimeState, characterName);
        AgentControlCenterSelfCheckItem[] meaningfulItems = snapshot.SelfCheck.Items
            .Where(item => item.Category.Equals("ready", StringComparison.OrdinalIgnoreCase) == false)
            .ToArray();
        IReadOnlyList<AgentControlCenterSelfCheckActionResult> autonomousActions =
            ApplyAutomaticSelfCheckActions(meaningfulItems, runtimeState);
        string resultText = FormatSelfCheckForAgent(snapshot.SelfCheck);
        if (autonomousActions.Count > 0)
        {
            StringBuilder builder = new(resultText);
            builder.AppendLine();
            builder.AppendLine("Autonomous actions applied:");
            foreach (AgentControlCenterSelfCheckActionResult action in autonomousActions)
                builder.AppendLine($"- {action.ActionId}: {action.Message}");
            resultText = builder.ToString();
        }
        AgentBackgroundTaskResult backgroundResult = AgentBackgroundTaskResult.Completed(
            $"agent-self-check-{now:yyyyMMddHHmmss}",
            "agent-self-check",
            string.IsNullOrWhiteSpace(sourceSessionId) ? "agent:control-center" : sourceSessionId.Trim(),
            resultText,
            now);
        RecordBackgroundTaskResult(backgroundResult);

        string fingerprint = BuildSelfCheckFingerprint(meaningfulItems);
        TimeSpan duplicateCooldown = TimeSpan.FromMinutes(Math.Max(1, config.MaintenanceDuplicateCooldownMinutes));
        bool duplicateWake = string.IsNullOrWhiteSpace(fingerprint) == false
                             && fingerprint.Equals(lastAutomaticSelfCheckWakeFingerprint, StringComparison.Ordinal)
                             && lastAutomaticSelfCheckWakeAt.HasValue
                             && now - lastAutomaticSelfCheckWakeAt.Value < duplicateCooldown;
        bool wakeRecommended = meaningfulItems.Length > 0 && duplicateWake == false;
        AgentEvent? wakeEvent = null;
        if (wakeRecommended)
        {
            lastAutomaticSelfCheckWakeFingerprint = fingerprint;
            lastAutomaticSelfCheckWakeAt = now;
            wakeEvent = backgroundResult.ToWakeEvent();
            AgentRunSession runSession = AgentRunSession.Start(wakeEvent, StreamingOutputPolicy.Token, now);
            runSession.MarkFirstContent(now);
            runSession.RecordToolStep("agent-self-check", $"{meaningfulItems.Length} meaningful item(s)", now);
            AgentOwnerNotificationPlan notificationPlan = BuildOwnerNotificationPlan(
                snapshot,
                sourceSessionId,
                sourceSessionId.StartsWith("qq:group:", StringComparison.OrdinalIgnoreCase) ? sourceSessionId : null);
            runSession.RecordToolStep(
                "owner-notification-plan",
                notificationPlan.ShouldNotifyOwner ? notificationPlan.TargetSessionId : "no owner notification needed",
                now);
            runSession.Complete(now);
            RecordAgentRunSession(runSession);
        }

        auditLog.Record(
            "agent.self_check.loop",
            "agent",
            $"items={snapshot.SelfCheck.Items.Count}; meaningful={meaningfulItems.Length}; autonomous_actions={autonomousActions.Count}; wake={wakeRecommended}",
            AgentAuditRiskLevel.Low,
            true);

        return new AgentControlCenterSelfCheckLoopResult(
            snapshot.SelfCheck,
            backgroundResult,
            wakeRecommended,
            wakeEvent,
            maintenanceInspection);
    }

    IReadOnlyList<AgentControlCenterSelfCheckActionResult> ApplyAutomaticSelfCheckActions(
        IEnumerable<AgentControlCenterSelfCheckItem> items,
        ChatRuntimeState runtimeState)
    {
        List<AgentControlCenterSelfCheckActionResult> results = [];
        AgentControlCenterConfig config = EnsureConfiguration();
        foreach (AgentControlCenterSelfCheckItem item in items)
        {
            if (item.CanAgentHandleAutonomously == false
                || item.RiskLevel != AgentAuditRiskLevel.Low
                || item.Category.Equals("qq-output-volume", StringComparison.OrdinalIgnoreCase) == false
                || string.Equals(item.ActionId, "reduce-proactive-intensity", StringComparison.OrdinalIgnoreCase) == false
                || config.ProactiveChatIntensity <= 0)
            {
                continue;
            }

            results.Add(ApplySelfCheckAction(item.ActionId!, runtimeState, "agent"));
        }

        return results;
    }

    AgentControlCenterSelfCheckSchedulerStatus BuildSelfCheckSchedulerStatus(AgentControlCenterConfig config)
    {
        TimeSpan interval = TimeSpan.FromMinutes(Math.Max(1, config.MaintenanceInspectionIntervalMinutes));
        return new AgentControlCenterSelfCheckSchedulerStatus(
            lastAutomaticSelfCheckAt,
            lastAutomaticSelfCheckWakeAt,
            lastAutomaticSelfCheckAt?.Add(interval),
            lastAutomaticSelfCheckSkipReason);
    }

    static Type? ResolveType(string fullName)
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false, ignoreCase: false))
            .FirstOrDefault(type => type != null);
    }

    static string NormalizeQChatPolicyMode(string mode)
    {
        string normalized = string.IsNullOrWhiteSpace(mode)
            ? "balanced"
            : mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "silent" => "silent",
            "mention-only" => "mention-only",
            "mentiononly" => "mention-only",
            "balanced" => "balanced",
            "active" => "active",
            _ => ""
        };
    }

    static void ApplyQChatPolicyMode(object config, string mode)
    {
        switch (mode)
        {
            case "silent":
                SetPropertyValue(config, "AllowGroupMemberChat", false);
                SetPropertyValue(config, "AllowGroupMemberMentions", true);
                SetPropertyValue(config, "AllowProactiveGroupChat", false);
                SetPropertyValue(config, "ProactiveChatProbability", 0f);
                break;
            case "mention-only":
                SetPropertyValue(config, "AllowGroupMemberChat", true);
                SetPropertyValue(config, "AllowGroupMemberMentions", true);
                SetPropertyValue(config, "AllowProactiveGroupChat", false);
                SetPropertyValue(config, "ProactiveChatProbability", 0f);
                break;
            case "active":
                SetPropertyValue(config, "AllowGroupMemberChat", true);
                SetPropertyValue(config, "AllowGroupMemberMentions", true);
                SetPropertyValue(config, "AllowProactiveGroupChat", true);
                SetPropertyValue(config, "ProactiveChatProbability", 0.3f);
                break;
            default:
                SetPropertyValue(config, "AllowGroupMemberChat", true);
                SetPropertyValue(config, "AllowGroupMemberMentions", true);
                SetPropertyValue(config, "AllowProactiveGroupChat", true);
                SetPropertyValue(config, "ProactiveChatProbability", 0.15f);
                break;
        }
    }

    static AgentQChatPolicySnapshot BuildQChatPolicySnapshot(string characterRoot, object config, string mode)
    {
        return new AgentQChatPolicySnapshot(
            characterRoot,
            GetPropertyValue(config, "AllowedGroupIds")?.ToString() ?? "",
            mode,
            Convert.ToInt32(GetPropertyValue(config, "PassiveGroupReplyCooldownSeconds") ?? 0),
            Convert.ToSingle(GetPropertyValue(config, "ProactiveChatProbability") ?? 0f),
            Convert.ToSingle(GetPropertyValue(config, "MediaOnlyPassiveGroupReplyProbability") ?? 0f),
            Convert.ToBoolean(GetPropertyValue(config, "AllowGroupMemberChat") ?? false),
            Convert.ToBoolean(GetPropertyValue(config, "AllowGroupMemberMentions") ?? false),
            Convert.ToBoolean(GetPropertyValue(config, "AllowMentionOutsideAllowedGroups") ?? true),
            Convert.ToBoolean(GetPropertyValue(config, "AllowProactiveGroupChat") ?? false));
    }

    static string InferQChatPolicyMode(object config)
    {
        bool allowGroupMemberChat = Convert.ToBoolean(GetPropertyValue(config, "AllowGroupMemberChat") ?? false);
        bool allowProactiveGroupChat = Convert.ToBoolean(GetPropertyValue(config, "AllowProactiveGroupChat") ?? false);
        float probability = Convert.ToSingle(GetPropertyValue(config, "ProactiveChatProbability") ?? 0f);

        if (allowGroupMemberChat == false)
            return "silent";
        if (allowProactiveGroupChat == false || probability <= 0f)
            return "mention-only";
        return probability >= 0.3f ? "active" : "balanced";
    }

    static string NormalizeAllowedGroupIds(string allowedGroupIds)
    {
        return string.Join(
            ',',
            ParseAllowedGroupIds(allowedGroupIds));
    }

    static IReadOnlyList<string> ParseAllowedGroupIds(string allowedGroupIds)
    {
        return (allowedGroupIds ?? "")
            .Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    static int ClampPassiveCooldownSeconds(int seconds) => Math.Clamp(seconds, 0, 600);

    static float ClampProbability(float probability, float max)
    {
        if (float.IsNaN(probability) || float.IsInfinity(probability))
            return 0f;
        return Math.Clamp(probability, 0f, max);
    }

    static object? GetPropertyValue(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName)?.GetValue(target);
    }

    static void SetPropertyValue(object target, string propertyName, object value)
    {
        System.Reflection.PropertyInfo? property = target.GetType().GetProperty(propertyName);
        if (property == null || property.CanWrite == false)
            throw new InvalidOperationException($"QChat configuration key is unavailable: {propertyName}");

        object converted = property.PropertyType == typeof(float)
            ? Convert.ToSingle(value)
            : property.PropertyType == typeof(int)
                ? Convert.ToInt32(value)
                : property.PropertyType == typeof(bool)
                    ? Convert.ToBoolean(value)
                    : value.ToString() ?? "";
        property.SetValue(target, converted);
    }

    AgentControlCenterConfig EnsureConfiguration()
    {
        return Configuration ??= new AgentControlCenterConfig();
    }

    bool CanApplyDirectly(AgentControlCenterConfig config, string key)
    {
        if (config.AllowAgentLowRiskSelfConfiguration == false)
            return false;

        return ParseKeyList(config.LowRiskConfigurationKeys).Contains(key, StringComparer.OrdinalIgnoreCase)
               && ParseKeyList(config.ProtectedConfigurationKeys).Contains(key, StringComparer.OrdinalIgnoreCase) == false;
    }

    AgentAuditRiskLevel GetConfigurationRisk(string key)
    {
        AgentControlCenterConfig config = EnsureConfiguration();
        return CanApplyDirectly(config, key) ? AgentAuditRiskLevel.Low : AgentAuditRiskLevel.High;
    }

    static IReadOnlyList<string> ParseKeyList(string value)
    {
        return value.Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string FormatProactiveChatStatus(AgentControlCenterConfig config)
    {
        string enabledText = config.AllowProactiveChat ? "enabled" : "disabled";
        return $"{enabledText} mode={GetProactiveChatModeName(config.ProactiveChatIntensity)} intensity={config.ProactiveChatIntensity}";
    }

    public static string GetProactiveChatModeName(int intensity)
    {
        return intensity switch
        {
            <= 0 => "安静",
            1 => "低调",
            2 => "平衡",
            <= 4 => "活跃",
            _ => "高活跃"
        };
    }

    public static string GetProactiveChatModeDescription(int intensity)
    {
        return intensity switch
        {
            <= 0 => "只响应主人或明确 @，不主动插话",
            1 => "低频回应，适合群聊降噪",
            2 => "极少量自然插话，默认推荐",
            <= 4 => "更愿意参与对话，但仍受冷却限制",
            _ => "测试或小群使用，刷屏风险较高"
        };
    }

    static string GetConfigValue(AgentControlCenterConfig config, string key)
    {
        System.Reflection.PropertyInfo? property = typeof(AgentControlCenterConfig).GetProperty(key);
        object? value = property?.GetValue(config);
        return value?.ToString() ?? "";
    }

    static AgentRiskLevel ToAgentRiskLevel(AgentAuditRiskLevel riskLevel) => riskLevel switch
    {
        AgentAuditRiskLevel.High => AgentRiskLevel.High,
        AgentAuditRiskLevel.Medium => AgentRiskLevel.Medium,
        _ => AgentRiskLevel.Low
    };

    static void ApplyConfigValue(AgentControlCenterConfig config, string key, string requestedValue)
    {
        System.Reflection.PropertyInfo? property = typeof(AgentControlCenterConfig).GetProperty(key);
        if (property == null || property.CanWrite == false)
            throw new InvalidOperationException($"Unknown configuration key: {key}");

        object value = property.PropertyType == typeof(bool)
            ? bool.Parse(requestedValue)
            : property.PropertyType == typeof(int)
                ? ClampIntegerConfiguration(key, int.Parse(requestedValue))
                : requestedValue;
        property.SetValue(config, value);
    }

    static int ClampIntegerConfiguration(string key, int requestedValue)
    {
        return key.EndsWith("Minutes", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(requestedValue, 1, 1440)
            : Math.Clamp(requestedValue, 0, 10);
    }

    static string FormatConfigAuditDetail(string key, string requestedValue, string reason)
    {
        return $"key={key}; value={requestedValue}; reason={reason}";
    }

    static AgentControlCenterSelfCheckActionResult ToSelfCheckActionResult(
        string actionId,
        AgentConfigurationChangeResult configResult)
    {
        return new AgentControlCenterSelfCheckActionResult(
            configResult.Applied,
            configResult.RequiresOwnerConfirmation,
            actionId,
            configResult.Key,
            configResult.Message,
            configResult.Proposal);
    }

    static string BuildSelfCheckFingerprint(IEnumerable<AgentControlCenterSelfCheckItem> items)
    {
        return string.Join("|", items
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Summary, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.Category}:{item.Summary}:{item.ActionId}"));
    }

    void PersistConfiguration(AgentControlCenterConfig config)
    {
        configurationSystem?.SetConfiguration(typeof(AgentControlCenterService), config);
    }

    static string EscapeXmlAttribute(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
