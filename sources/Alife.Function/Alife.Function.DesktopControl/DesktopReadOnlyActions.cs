namespace Alife.Function.DesktopControl;

public static class DesktopReadOnlyActions
{
    const int MaxRecentAuditEntries = 8;
    const int MaxRecentDraftEntries = 8;
    const int MaxDraftPreviewLength = 80;

    public const string Status = "qchat.desktop.status";
    public const string Health = "qchat.desktop.health";
    public const string Processes = "qchat.desktop.processes";
    public const string Windows = "qchat.desktop.windows";
    public const string Capabilities = "qchat.desktop.capabilities";
    public const string AuditRecent = "qchat.desktop.audit.recent";
    public const string AuditHealth = "qchat.desktop.audit.health";
    public const string RequestDraft = "qchat.desktop.request.draft";
    public const string DraftsRecent = "qchat.desktop.drafts.recent";
    public const string DraftReject = "qchat.desktop.draft.reject";
    public const string DraftApprove = "qchat.desktop.draft.approve";
    public const string DraftExecute = "qchat.desktop.draft.execute";
    public const string JobsRecent = "qchat.desktop.jobs.recent";
    public const string JobDetail = "qchat.desktop.job.detail";
    public const string FilePolicy = "qchat.desktop.file.policy";

    public static IReadOnlyList<IDesktopAction> Create(
        DesktopControlService desktopControl,
        IDesktopActionAuditReader? auditReader = null,
        IDesktopActionDraftSink? draftSink = null,
        IDesktopActionDraftReader? draftReader = null,
        IDesktopActionDraftController? draftController = null,
        IDesktopApprovedDraftExecutor? businessExecutor = null,
        IDesktopBusinessJobReader? jobReader = null)
    {
        ArgumentNullException.ThrowIfNull(desktopControl);
        return
        [
            new DelegateDesktopAction(Status, "read-only desktop status", (_, token) => desktopControl.GetStatusAsync(token)),
            new DelegateDesktopAction(Health, "read-only desktop health", (_, token) => desktopControl.GetStatusAsync(token)),
            new DelegateDesktopAction(Processes, "read-only process summary", (_, token) => desktopControl.GetProcessListAsync(cancellationToken: token)),
            new DelegateDesktopAction(Windows, "read-only window summary", (_, token) => desktopControl.GetWindowListAsync(cancellationToken: token)),
            new DelegateDesktopAction(Capabilities, "enabled read-only desktop capabilities", (_, _) => Task.FromResult(desktopControl.GetCapabilitySummary())),
            new DelegateDesktopAction(AuditRecent, "recent desktop action audit summary", (_, _) => Task.FromResult(FormatRecentAudit(auditReader))),
            new DelegateDesktopAction(AuditHealth, "desktop action audit health summary", (_, _) => Task.FromResult(FormatAuditHealth(auditReader))),
            new DelegateDesktopAction(RequestDraft, "create a pending desktop action draft without execution", (request, _) => Task.FromResult(CreateRequestDraft(request, draftSink))),
            new DelegateDesktopAction(DraftsRecent, "recent desktop action draft summary", (_, _) => Task.FromResult(FormatRecentDrafts(draftReader))),
            new DelegateDesktopAction(DraftReject, "reject a pending desktop action draft without execution", (request, _) => Task.FromResult(UpdateDraftStatus(request, draftController, DesktopActionDraftStatus.Rejected))),
            new DelegateDesktopAction(DraftApprove, "approve a pending desktop action draft without execution", (request, _) => Task.FromResult(UpdateDraftStatus(request, draftController, DesktopActionDraftStatus.Approved))),
            new DelegateDesktopAction(DraftExecute, "queue an approved whitelisted desktop action draft for execution", (request, token) => ExecuteApprovedDraftAsync(request, draftReader, draftController, businessExecutor, token), DesktopCapabilityRisk.Low),
            new DelegateDesktopAction(JobsRecent, "recent desktop business jobs summary", (_, _) => Task.FromResult(FormatRecentJobs(jobReader))),
            new DelegateDesktopAction(JobDetail, "desktop business job detail", (request, _) => Task.FromResult(FormatJobDetail(request, jobReader))),
            new DelegateDesktopAction(FilePolicy, "desktop file access policy summary", (_, _) => Task.FromResult(DesktopFileAccessPolicy.CreateDefault().FormatForOwner()))
        ];
    }

    public static DesktopActionGateway CreateGateway(
        DesktopControlService desktopControl,
        IDesktopActionAuditSink? auditSink = null,
        IDesktopActionAuditReader? auditReader = null,
        IDesktopActionDraftSink? draftSink = null,
        IDesktopActionDraftReader? draftReader = null,
        IDesktopActionDraftController? draftController = null,
        IDesktopApprovedDraftExecutor? businessExecutor = null,
        IDesktopBusinessJobReader? jobReader = null,
        DesktopCapabilityRegistry? capabilityRegistry = null)
    {
        return new DesktopActionGateway(Create(desktopControl, auditReader, draftSink, draftReader, draftController, businessExecutor, jobReader), auditSink, capabilityRegistry);
    }

    static string FormatRecentAudit(IDesktopActionAuditReader? auditReader)
    {
        IReadOnlyList<DesktopActionAuditEntry> entries = auditReader?.GetRecentEntries(MaxRecentAuditEntries) ?? [];
        if (entries.Count == 0)
            return string.Join(Environment.NewLine, "Recent desktop actions:", "none");

        List<string> lines = ["Recent desktop actions:"];
        lines.AddRange(entries.Select(entry =>
            $"{entry.Timestamp:O} {entry.ActionName} risk={entry.Risk} succeeded={FormatBool(entry.Succeeded)} agent={entry.AgentId} actor={entry.ActorUserId}"));
        return string.Join(Environment.NewLine, lines);
    }

    static string FormatAuditHealth(IDesktopActionAuditReader? auditReader)
    {
        IReadOnlyList<DesktopActionAuditEntry> entries = auditReader?.GetRecentEntries(MaxRecentAuditEntries) ?? [];
        int recentFailures = entries.Count(entry => entry.Succeeded == false);
        return string.Join(Environment.NewLine,
            $"desktop_audit={(auditReader == null ? "unavailable" : "available")}",
            "owner_gate=enabled",
            "agent_gate=xiayu_only",
            "desktop_mutation=enabled",
            "shell_execution=disabled",
            $"recent_entries={entries.Count}",
            $"recent_failures={recentFailures}");
    }

    static string CreateRequestDraft(
        DesktopActionRequest request,
        IDesktopActionDraftSink? draftSink)
    {
        if (draftSink == null)
            return "desktop_request=unavailable reason=draft_sink_missing execution=disabled";

        DesktopActionDraftEntry entry = draftSink.CreateDraft(request);
        return $"desktop_request=draft_created id={entry.DraftId} approval_required=true execution=disabled risk=pending_review";
    }

    static string FormatRecentDrafts(IDesktopActionDraftReader? draftReader)
    {
        IReadOnlyList<DesktopActionDraftEntry> drafts = draftReader?.GetRecentDrafts(MaxRecentDraftEntries) ?? [];
        if (drafts.Count == 0)
            return string.Join(Environment.NewLine, "Recent desktop drafts:", "none");

        List<string> lines = ["Recent desktop drafts:"];
        lines.AddRange(drafts.Select(draft =>
            $"{draft.Timestamp:O} {draft.DraftId} status={draft.Status} agent={draft.AgentId} actor={draft.ActorUserId} preview={FormatDraftPreview(draft.RequestedAction)}"));
        return string.Join(Environment.NewLine, lines);
    }

    static string FormatRecentJobs(IDesktopBusinessJobReader? jobReader)
    {
        IReadOnlyList<DesktopBusinessJobEntry> jobs = jobReader?.GetRecentJobs(MaxRecentDraftEntries) ?? [];
        if (jobs.Count == 0)
            return string.Join(Environment.NewLine, "Recent desktop jobs:", "none");

        List<string> lines = ["Recent desktop jobs:"];
        lines.AddRange(jobs.Select(job =>
            $"{job.Timestamp:O} {job.JobId} status={job.Status} draft={job.DraftId} agent={job.AgentId} actor={job.ActorUserId} action={FormatDraftPreview(job.RequestedAction)}"));
        return string.Join(Environment.NewLine, lines);
    }

    static string FormatJobDetail(
        DesktopActionRequest request,
        IDesktopBusinessJobReader? jobReader)
    {
        if (jobReader == null)
            return "desktop_job=unavailable reason=job_reader_missing";

        DesktopBusinessJobEntry? job = jobReader.GetJob(request.Detail);
        if (job == null)
            return "desktop_job=not_found";

        return string.Join(Environment.NewLine,
            $"desktop_job={job.JobId}",
            $"status={job.Status}",
            $"draft={job.DraftId}",
            $"agent={job.AgentId}",
            $"actor={job.ActorUserId}",
            $"action={FormatDraftPreview(job.RequestedAction)}",
            $"message={FormatDraftPreview(job.Message)}");
    }

    static string UpdateDraftStatus(
        DesktopActionRequest request,
        IDesktopActionDraftController? draftController,
        DesktopActionDraftStatus status)
    {
        if (draftController == null)
            return "desktop_draft=unavailable reason=draft_controller_missing execution=disabled";

        DesktopActionDraftUpdateResult result = draftController.UpdateStatus(request, status);
        return result.Message;
    }

    static async Task<string> ExecuteApprovedDraftAsync(
        DesktopActionRequest request,
        IDesktopActionDraftReader? draftReader,
        IDesktopActionDraftController? draftController,
        IDesktopApprovedDraftExecutor? businessExecutor,
        CancellationToken cancellationToken)
    {
        if (draftReader == null || draftController == null)
            return "desktop_execution=unavailable reason=draft_store_missing";
        if (businessExecutor == null)
            return "desktop_execution=unavailable reason=executor_missing";

        DesktopActionDraftEntry? draft = draftReader.GetDraft(request.Detail);
        if (draft == null)
            return "desktop_execution=not_found";
        if (draft.Status == DesktopActionDraftStatus.Executed)
            return "desktop_execution=denied reason=already_executed";
        if (draft.Status != DesktopActionDraftStatus.Approved)
            return $"desktop_execution=denied reason=draft_not_approved status={draft.Status}";

        DesktopBusinessExecutionResult execution = await businessExecutor.ExecuteAsync(draft, cancellationToken);
        if (execution.Success == false)
            return execution.Message;
        if (execution.MarksDraftExecuted == false)
            return execution.Message;

        DesktopActionDraftUpdateResult update = draftController.UpdateStatus(request, DesktopActionDraftStatus.Executed);
        if (update.Success == false)
            return update.Message;

        return execution.Message;
    }

    static string FormatDraftPreview(string value)
    {
        string normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized.Length <= MaxDraftPreviewLength
            ? normalized
            : normalized[..MaxDraftPreviewLength].TrimEnd() + "...";
    }

    static string FormatBool(bool value) => value ? "true" : "false";

    sealed class DelegateDesktopAction(
        string name,
        string summary,
        Func<DesktopActionRequest, CancellationToken, Task<string>> execute,
        DesktopCapabilityRisk risk = DesktopCapabilityRisk.ReadOnly) : IDesktopAction
    {
        public string Name { get; } = name;
        public DesktopCapabilityRisk Risk { get; } = risk;
        public bool Enabled => true;
        public string Summary { get; } = summary;

        public Task<string> ExecuteAsync(
            DesktopActionRequest request,
            CancellationToken cancellationToken = default)
        {
            return execute(request, cancellationToken);
        }
    }
}
