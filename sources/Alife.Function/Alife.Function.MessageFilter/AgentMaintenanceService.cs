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

public sealed record AgentMaintenanceProposal(
    string Id,
    DateTimeOffset CreatedAt,
    string Actor,
    string Title,
    string Evidence,
    IReadOnlyList<string> SuggestedNextSteps,
    AgentAuditRiskLevel RiskLevel,
    bool RequiresOwnerConfirmationForExecution,
    bool CanApplyAutomatically);

public sealed record AgentMaintenanceArchivedProposal(
    AgentMaintenanceProposal Proposal,
    DateTimeOffset ArchivedAt,
    string Actor,
    string Resolution);

public sealed record AgentMaintenanceArchiveResult(
    bool Archived,
    string Message,
    AgentMaintenanceArchivedProposal? ArchivedProposal = null);

public sealed record AgentMaintenanceInspectionResult(
    bool Created,
    string Message,
    AgentMaintenanceProposal? Proposal = null);

public sealed record AgentMaintenanceRepairEvidence(
    string ProposalId,
    DateTimeOffset RecordedAt,
    string Actor,
    string WorkspaceProposalId,
    string VerificationCommandId,
    string VerificationSummary,
    string Notes);

public sealed class AgentMaintenanceProposalPersistenceState
{
    public List<AgentMaintenanceProposal> Pending { get; set; } = [];
    public List<AgentMaintenanceArchivedProposal> Archived { get; set; } = [];
    public List<AgentMaintenanceRepairEvidence> RepairEvidence { get; set; } = [];
}

[Module(
    "Agent Maintenance",
    "Turns runtime issue reports into owner-confirmed self-maintenance proposals without directly changing files or configuration.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -55)]
public class AgentMaintenanceService(
    AgentIssueReportService? issueReports = null,
    AgentAuditLogService? auditLog = null,
    XmlFunctionCaller? functionCaller = null,
    string? proposalStorePath = null,
    Func<DateTimeOffset>? clock = null)
    : InteractiveModule<AgentMaintenanceService>
{
    readonly object proposalGate = new();
    readonly string proposalStorePath = Path.GetFullPath(
        proposalStorePath ?? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-maintenance-proposals.json"));
    readonly AgentIssueReportService issueReports = issueReports ?? new AgentIssueReportService();
    readonly AgentAuditLogService auditLog = auditLog ?? new AgentAuditLogService(
        Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-audit.jsonl"));
    readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.Now);
    readonly Dictionary<string, AgentMaintenanceProposal> proposals = LoadProposals(
        proposalStorePath ?? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-maintenance-proposals.json"));
    readonly Dictionary<string, AgentMaintenanceArchivedProposal> archivedProposals = LoadArchivedProposals(
        proposalStorePath ?? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-maintenance-proposals.json"));
    readonly List<AgentMaintenanceRepairEvidence> repairEvidence = LoadRepairEvidence(
        proposalStorePath ?? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-maintenance-proposals.json"));

    [XmlFunction(FunctionMode.OneShot, name: "agent_maintenance_propose", budgetCost: 4)]
    [Description("Create a self-maintenance proposal from recent runtime errors and failed audit entries. This does not modify files or configuration.")]
    public void ProposeMaintenance(int maxEntries = 8)
    {
        AgentMaintenanceInspectionResult result = InspectIssueReport(
            issueReports.BuildSnapshot(ChatBot.GetRuntimeState(), maxEntries),
            Character.Name,
            duplicateCooldown: TimeSpan.FromHours(2));
        Poke(result.Proposal == null ? result.Message : FormatProposal(result.Proposal));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_maintenance_pending")]
    [Description("List pending self-maintenance proposals.")]
    public void ShowPendingMaintenanceProposals()
    {
        IReadOnlyList<AgentMaintenanceProposal> pending = GetPendingProposals();
        if (pending.Count == 0)
        {
            Poke("No pending agent maintenance proposals.");
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("Pending agent maintenance proposals:");
        foreach (AgentMaintenanceProposal proposal in pending)
            builder.AppendLine($"- {proposal.Id}: {proposal.Title} [{proposal.RiskLevel}]");
        Poke(builder.ToString().TrimEnd());
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_maintenance_archive")]
    [Description("Archive a pending self-maintenance proposal after it has been handled. This only updates local maintenance state and audit log.")]
    public void ArchiveMaintenanceProposal(string id, string resolution = "", string actor = "owner")
    {
        AgentMaintenanceArchiveResult result = ArchiveProposal(id, actor, resolution);
        Poke(result.Message);
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_maintenance_archived")]
    [Description("List recently archived self-maintenance proposals.")]
    public void ShowArchivedMaintenanceProposals()
    {
        IReadOnlyList<AgentMaintenanceArchivedProposal> archived = GetArchivedProposals();
        if (archived.Count == 0)
        {
            Poke("No archived agent maintenance proposals.");
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("Archived agent maintenance proposals:");
        foreach (AgentMaintenanceArchivedProposal item in archived.Take(8))
            builder.AppendLine($"- {item.Proposal.Id}: {item.Proposal.Title} [{item.Proposal.RiskLevel}] resolution={item.Resolution}");
        Poke(builder.ToString().TrimEnd());
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_maintenance_record_repair_evidence")]
    [Description("Record repair evidence for a maintenance proposal, such as linked workspace proposal and verification result. This does not execute changes.")]
    public void RecordRepairEvidenceFromXml(
        string proposalId,
        string workspaceProposalId = "",
        string verificationCommandId = "",
        string verificationSummary = "",
        string notes = "",
        string actor = "agent")
    {
        AgentMaintenanceRepairEvidence evidence = RecordRepairEvidence(
            proposalId,
            workspaceProposalId,
            verificationCommandId,
            verificationSummary,
            actor,
            notes);
        Poke($"Recorded repair evidence for maintenance proposal: {evidence.ProposalId}");
    }

    public AgentMaintenanceProposal ProposeFromIssueReport(AgentIssueReportSnapshot issueReport, string actor)
    {
        string evidence = BuildEvidence(issueReport);
        AgentAuditRiskLevel risk = issueReport.FailedAuditEntries.Any(entry => entry.RiskLevel == AgentAuditRiskLevel.High)
            ? AgentAuditRiskLevel.High
            : AgentAuditRiskLevel.Medium;
        AgentMaintenanceProposal proposal = new(
            Guid.NewGuid().ToString("N"),
            clock(),
            string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim(),
            BuildTitle(issueReport),
            evidence,
            BuildSuggestedSteps(issueReport),
            risk,
            RequiresOwnerConfirmationForExecution: true,
            CanApplyAutomatically: false);

        lock (proposalGate)
        {
            proposals[proposal.Id] = proposal;
        }
        SaveProposals();
        auditLog.Record(
            "agent.maintenance.propose",
            proposal.Actor,
            proposal.Title,
            proposal.RiskLevel,
            succeeded: true);
        return proposal;
    }

    public AgentMaintenanceInspectionResult InspectIssueReport(
        AgentIssueReportSnapshot issueReport,
        string actor,
        TimeSpan duplicateCooldown)
    {
        if (HasActionableIssue(issueReport) == false)
            return new AgentMaintenanceInspectionResult(false, "No actionable maintenance issue was found.");

        TimeSpan cooldown = duplicateCooldown <= TimeSpan.Zero ? TimeSpan.FromHours(2) : duplicateCooldown;
        string title = BuildTitle(issueReport);
        string evidence = BuildEvidence(issueReport);
        AgentMaintenanceProposal? duplicate = FindRecentDuplicate(title, evidence, cooldown);
        if (duplicate != null)
        {
            auditLog.Record(
                "agent.maintenance.inspect.deduplicated",
                string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim(),
                duplicate.Title,
                duplicate.RiskLevel,
                succeeded: true);
            return new AgentMaintenanceInspectionResult(false, "Recent duplicate maintenance proposal already exists.", duplicate);
        }

        AgentMaintenanceProposal proposal = ProposeFromIssueReport(issueReport, actor);
        return new AgentMaintenanceInspectionResult(true, "Maintenance proposal created.", proposal);
    }

    public IReadOnlyList<AgentMaintenanceProposal> GetPendingProposals()
    {
        lock (proposalGate)
        {
            return proposals.Values
                .OrderByDescending(proposal => proposal.CreatedAt)
                .ToArray();
        }
    }

    public IReadOnlyList<AgentMaintenanceArchivedProposal> GetArchivedProposals()
    {
        lock (proposalGate)
        {
            return archivedProposals.Values
                .OrderByDescending(proposal => proposal.ArchivedAt)
                .ToArray();
        }
    }

    public AgentMaintenanceArchiveResult ArchiveProposal(string id, string actor, string resolution = "")
    {
        string normalizedId = (id ?? string.Empty).Trim();
        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "owner" : actor.Trim();
        string normalizedResolution = string.IsNullOrWhiteSpace(resolution) ? "handled" : resolution.Trim();

        AgentMaintenanceProposal proposal;
        AgentMaintenanceArchivedProposal archived;
        lock (proposalGate)
        {
            if (proposals.TryGetValue(normalizedId, out proposal!) == false)
                return new AgentMaintenanceArchiveResult(false, "Maintenance proposal was not found.");

            archived = new AgentMaintenanceArchivedProposal(
                proposal,
                clock(),
                normalizedActor,
                normalizedResolution);
            proposals.Remove(normalizedId);
            archivedProposals[normalizedId] = archived;
        }
        SaveProposals();

        auditLog.Record(
            "agent.maintenance.archived",
            normalizedActor,
            $"{proposal.Title}; resolution={normalizedResolution}",
            proposal.RiskLevel,
            succeeded: true);
        return new AgentMaintenanceArchiveResult(true, "Maintenance proposal archived.", archived);
    }

    public AgentMaintenanceRepairEvidence RecordRepairEvidence(
        string proposalId,
        string workspaceProposalId,
        string verificationCommandId,
        string verificationSummary,
        string actor,
        string notes = "")
    {
        string normalizedProposalId = (proposalId ?? string.Empty).Trim();
        string normalizedActor = string.IsNullOrWhiteSpace(actor) ? "agent" : actor.Trim();

        AgentMaintenanceRepairEvidence evidence = new(
            normalizedProposalId,
            clock(),
            normalizedActor,
            (workspaceProposalId ?? string.Empty).Trim(),
            (verificationCommandId ?? string.Empty).Trim(),
            (verificationSummary ?? string.Empty).Trim(),
            (notes ?? string.Empty).Trim());

        lock (proposalGate)
        {
            if (proposals.ContainsKey(normalizedProposalId) == false
                && archivedProposals.ContainsKey(normalizedProposalId) == false)
                throw new InvalidOperationException("Maintenance proposal was not found.");

            repairEvidence.Add(evidence);
        }
        SaveProposals();

        auditLog.Record(
            "agent.maintenance.repair_evidence",
            normalizedActor,
            $"proposal={normalizedProposalId}; workspace_proposal={evidence.WorkspaceProposalId}; verification={evidence.VerificationCommandId}; {evidence.VerificationSummary}",
            AgentAuditRiskLevel.Low,
            succeeded: true);
        return evidence;
    }

    public IReadOnlyList<AgentMaintenanceRepairEvidence> GetRepairEvidence(string proposalId)
    {
        string normalizedProposalId = (proposalId ?? string.Empty).Trim();
        lock (proposalGate)
        {
            return repairEvidence
                .Where(evidence => string.Equals(evidence.ProposalId, normalizedProposalId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(evidence => evidence.RecordedAt)
                .ToArray();
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<AgentMaintenanceRepairEvidence>> GetRepairEvidenceByProposalId()
    {
        lock (proposalGate)
        {
            return repairEvidence
                .GroupBy(evidence => evidence.ProposalId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<AgentMaintenanceRepairEvidence>)group
                        .OrderByDescending(evidence => evidence.RecordedAt)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string FormatProposal(AgentMaintenanceProposal proposal)
    {
        StringBuilder builder = new();
        builder.AppendLine("Agent maintenance proposal");
        builder.AppendLine($"Id: {proposal.Id}");
        builder.AppendLine($"Title: {proposal.Title}");
        builder.AppendLine($"Risk: {proposal.RiskLevel}");
        builder.AppendLine($"Requires owner confirmation for execution: {proposal.RequiresOwnerConfirmationForExecution}");
        builder.AppendLine($"Can apply automatically: {proposal.CanApplyAutomatically}");
        builder.AppendLine("No files or configuration were changed.");
        builder.AppendLine("Evidence:");
        builder.AppendLine(proposal.Evidence);
        builder.AppendLine("Suggested next steps:");
        foreach (string step in proposal.SuggestedNextSteps)
            builder.AppendLine($"- {step}");
        builder.AppendLine("Apply boundary:");
        builder.AppendLine("- Use workspace_propose_replace for patch previews.");
        builder.AppendLine("- Use confirm execute <workspace_apply_proposal id=\"...\" /> only after owner confirmation.");
        return builder.ToString().TrimEnd();
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    static string BuildTitle(AgentIssueReportSnapshot issueReport)
    {
        if (string.IsNullOrWhiteSpace(issueReport.LastError) == false)
            return TrimLine(issueReport.LastError, 96);
        ModuleHealth? unhealthy = issueReport.UnhealthyModules.FirstOrDefault();
        if (unhealthy != null)
            return $"{unhealthy.Name} is {unhealthy.Status}";
        AgentAuditLogEntry? failed = issueReport.FailedAuditEntries.LastOrDefault();
        return failed == null ? "Review recent agent state" : $"{failed.Action} failed";
    }

    static string BuildEvidence(AgentIssueReportSnapshot issueReport)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Last error: {issueReport.LastError ?? "none"}");
        foreach (ChatRuntimeEvent runtimeEvent in issueReport.RuntimeErrors.TakeLast(3))
            builder.AppendLine($"Runtime error: {runtimeEvent.Detail}");
        foreach (AgentAuditLogEntry entry in issueReport.FailedAuditEntries.TakeLast(3))
            builder.AppendLine($"Failed audit: {entry.Action}; {entry.Detail}; error={entry.Error ?? "none"}");
        foreach (ModuleHealth health in issueReport.UnhealthyModules.Take(3))
            builder.AppendLine($"Unhealthy module: {health.Name}; {health.Status}; {health.Summary}");
        return builder.ToString().TrimEnd();
    }

    static IReadOnlyList<string> BuildSuggestedSteps(AgentIssueReportSnapshot issueReport)
    {
        List<string> steps = [
            "Run agent_issue_report again if the error state may have changed.",
            "Use workspace_search to locate the relevant code path before proposing a patch.",
            "Use workspace_propose_replace to preview an exact code change without modifying files.",
            "Run agent_run for an allowed build/test command after a patch preview or owner-approved change.",
            "Use workspace_apply_proposal only after explicit owner confirmation."
        ];
        if (issueReport.UnhealthyModules.Count > 0)
            steps.Insert(1, "Inspect degraded module health before changing shared code.");
        return steps;
    }

    AgentMaintenanceProposal? FindRecentDuplicate(string title, string evidence, TimeSpan cooldown)
    {
        DateTimeOffset cutoff = clock() - cooldown;
        string fingerprint = BuildDuplicateFingerprint(title, evidence);
        lock (proposalGate)
        {
            return proposals.Values
                       .Concat(archivedProposals.Values.Select(item => item.Proposal))
                       .Where(proposal => proposal.CreatedAt >= cutoff)
                       .FirstOrDefault(proposal =>
                           string.Equals(
                               BuildDuplicateFingerprint(proposal.Title, proposal.Evidence),
                               fingerprint,
                               StringComparison.OrdinalIgnoreCase));
        }
    }

    static bool HasActionableIssue(AgentIssueReportSnapshot issueReport)
    {
        return string.IsNullOrWhiteSpace(issueReport.LastError) == false
               || issueReport.RuntimeErrors.Count > 0
               || issueReport.FailedAuditEntries.Count > 0
               || issueReport.UnhealthyModules.Any(AgentHealthIssueClassifier.IsActionable);
    }

    static string TrimLine(string value, int maxLength)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        if (value.Length <= maxLength)
            return value;
        return value[..(maxLength - 3)] + "...";
    }

    static string BuildDuplicateFingerprint(string title, string evidence)
    {
        string[] lines = evidence.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string[] unhealthyModules = lines
            .Where(line => line.StartsWith("Unhealthy module:", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizeDiagnosticLine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unhealthyModules.Length > 0)
            return "modules:" + string.Join("|", unhealthyModules);

        string[] errors = lines
            .Where(line => line.StartsWith("Last error:", StringComparison.OrdinalIgnoreCase)
                           || line.StartsWith("Runtime error:", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizeDiagnosticLine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (errors.Length > 0)
            return "errors:" + string.Join("|", errors);

        return "title:" + NormalizeDiagnosticText(title);
    }

    static string NormalizeDiagnosticLine(string line)
    {
        int separator = line.IndexOf(':');
        string payload = separator >= 0 ? line[(separator + 1)..] : line;
        return NormalizeDiagnosticText(payload);
    }

    static string NormalizeDiagnosticText(string value)
    {
        string normalized = value.ReplaceLineEndings(" ").Trim();
        while (TryStripExceptionPrefix(normalized, out string stripped))
            normalized = stripped;
        return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static bool TryStripExceptionPrefix(string value, out string stripped)
    {
        stripped = value;
        int separator = value.IndexOf(':');
        if (separator <= 0)
            return false;

        string prefix = value[..separator].Trim();
        if (prefix.EndsWith("Exception", StringComparison.OrdinalIgnoreCase) == false)
            return false;

        stripped = value[(separator + 1)..].Trim();
        return true;
    }

    void SaveProposals()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(proposalStorePath)!);
        AgentMaintenanceProposalPersistenceState state;
        lock (proposalGate)
        {
            state = new AgentMaintenanceProposalPersistenceState
            {
                Pending = proposals.Values
                    .OrderByDescending(proposal => proposal.CreatedAt)
                    .ToList(),
                Archived = archivedProposals.Values
                    .OrderByDescending(proposal => proposal.ArchivedAt)
                    .ToList(),
                RepairEvidence = repairEvidence
                    .OrderByDescending(evidence => evidence.RecordedAt)
                    .ToList()
            };
        }

        File.WriteAllText(proposalStorePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    static Dictionary<string, AgentMaintenanceProposal> LoadProposals(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) == false)
            return new Dictionary<string, AgentMaintenanceProposal>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string json = File.ReadAllText(fullPath);
            AgentMaintenanceProposal[] restored = json.TrimStart().StartsWith("[", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<AgentMaintenanceProposal[]>(json, JsonOptions) ?? []
                : JsonSerializer.Deserialize<AgentMaintenanceProposalPersistenceState>(json, JsonOptions)?.Pending.ToArray() ?? [];
            return ToProposalDictionary(restored);
        }
        catch
        {
            return new Dictionary<string, AgentMaintenanceProposal>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static Dictionary<string, AgentMaintenanceArchivedProposal> LoadArchivedProposals(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) == false)
            return new Dictionary<string, AgentMaintenanceArchivedProposal>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string json = File.ReadAllText(fullPath);
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return new Dictionary<string, AgentMaintenanceArchivedProposal>(StringComparer.OrdinalIgnoreCase);

            AgentMaintenanceArchivedProposal[] restored =
                JsonSerializer.Deserialize<AgentMaintenanceProposalPersistenceState>(json, JsonOptions)?.Archived.ToArray() ?? [];
            return restored
                .Where(item => string.IsNullOrWhiteSpace(item.Proposal.Id) == false)
                .GroupBy(item => item.Proposal.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, AgentMaintenanceArchivedProposal>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static List<AgentMaintenanceRepairEvidence> LoadRepairEvidence(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) == false)
            return [];

        try
        {
            string json = File.ReadAllText(fullPath);
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return [];

            return JsonSerializer.Deserialize<AgentMaintenanceProposalPersistenceState>(json, JsonOptions)?.RepairEvidence
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    static Dictionary<string, AgentMaintenanceProposal> ToProposalDictionary(IEnumerable<AgentMaintenanceProposal> restored)
    {
        return restored
            .Where(proposal => string.IsNullOrWhiteSpace(proposal.Id) == false)
            .GroupBy(
                proposal => BuildDuplicateFingerprint(proposal.Title, proposal.Evidence),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(proposal => proposal.CreatedAt)
                .ThenBy(proposal => proposal.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToDictionary(proposal => proposal.Id, proposal => proposal, StringComparer.OrdinalIgnoreCase);
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
