namespace Alife.Function.DesktopControl;

public static class DesktopReadOnlyActions
{
    public const string Status = "qchat.desktop.status";
    public const string Health = "qchat.desktop.health";
    public const string Processes = "qchat.desktop.processes";
    public const string Windows = "qchat.desktop.windows";
    public const string Capabilities = "qchat.desktop.capabilities";

    public static IReadOnlyList<IDesktopAction> Create(DesktopControlService desktopControl)
    {
        ArgumentNullException.ThrowIfNull(desktopControl);
        return
        [
            new DelegateDesktopAction(Status, "read-only desktop status", desktopControl.GetStatusAsync),
            new DelegateDesktopAction(Health, "read-only desktop health", desktopControl.GetStatusAsync),
            new DelegateDesktopAction(Processes, "read-only process summary", token => desktopControl.GetProcessListAsync(cancellationToken: token)),
            new DelegateDesktopAction(Windows, "read-only window summary", token => desktopControl.GetWindowListAsync(cancellationToken: token)),
            new DelegateDesktopAction(Capabilities, "enabled read-only desktop capabilities", _ => Task.FromResult(desktopControl.GetCapabilitySummary()))
        ];
    }

    public static DesktopActionGateway CreateGateway(
        DesktopControlService desktopControl,
        IDesktopActionAuditSink? auditSink = null)
    {
        return new DesktopActionGateway(Create(desktopControl), auditSink);
    }

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
