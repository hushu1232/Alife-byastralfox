namespace Alife.Function.DesktopControl;

public static class DesktopReadOnlyActions
{
    const int MaxRecentAuditEntries = 8;

    public const string Status = "qchat.desktop.status";
    public const string Health = "qchat.desktop.health";
    public const string Processes = "qchat.desktop.processes";
    public const string Windows = "qchat.desktop.windows";
    public const string Capabilities = "qchat.desktop.capabilities";
    public const string AuditRecent = "qchat.desktop.audit.recent";
    public const string AuditHealth = "qchat.desktop.audit.health";

    public static IReadOnlyList<IDesktopAction> Create(
        DesktopControlService desktopControl,
        IDesktopActionAuditReader? auditReader = null)
    {
        ArgumentNullException.ThrowIfNull(desktopControl);
        return
        [
            new DelegateDesktopAction(Status, "read-only desktop status", desktopControl.GetStatusAsync),
            new DelegateDesktopAction(Health, "read-only desktop health", desktopControl.GetStatusAsync),
            new DelegateDesktopAction(Processes, "read-only process summary", token => desktopControl.GetProcessListAsync(cancellationToken: token)),
            new DelegateDesktopAction(Windows, "read-only window summary", token => desktopControl.GetWindowListAsync(cancellationToken: token)),
            new DelegateDesktopAction(Capabilities, "enabled read-only desktop capabilities", _ => Task.FromResult(desktopControl.GetCapabilitySummary())),
            new DelegateDesktopAction(AuditRecent, "recent desktop action audit summary", _ => Task.FromResult(FormatRecentAudit(auditReader))),
            new DelegateDesktopAction(AuditHealth, "desktop action audit health summary", _ => Task.FromResult(FormatAuditHealth(auditReader)))
        ];
    }

    public static DesktopActionGateway CreateGateway(
        DesktopControlService desktopControl,
        IDesktopActionAuditSink? auditSink = null,
        IDesktopActionAuditReader? auditReader = null)
    {
        return new DesktopActionGateway(Create(desktopControl, auditReader), auditSink);
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
            "desktop_mutation=disabled",
            "shell_execution=disabled",
            $"recent_entries={entries.Count}",
            $"recent_failures={recentFailures}");
    }

    static string FormatBool(bool value) => value ? "true" : "false";

    sealed class DelegateDesktopAction(
        string name,
        string summary,
        Func<CancellationToken, Task<string>> execute) : IDesktopAction
    {
        public string Name { get; } = name;
        public DesktopCapabilityRisk Risk => DesktopCapabilityRisk.ReadOnly;
        public bool Enabled => true;
        public string Summary { get; } = summary;

        public Task<string> ExecuteAsync(
            DesktopActionRequest request,
            CancellationToken cancellationToken = default)
        {
            return execute(cancellationToken);
        }
    }
}
