namespace Alife.Function.DesktopControl;

public enum DesktopActionStatus
{
    Denied,
    Executed,
    ApprovalRequired,
    Failed
}

public sealed record DesktopActionRequest(
    string ActionName,
    long ActorUserId,
    string AgentId,
    bool IsOwner,
    string Detail = "");

public sealed record DesktopActionResult(
    bool Executed,
    DesktopActionStatus Status,
    DesktopCapabilityRisk Risk,
    string Message);

public interface IDesktopAction
{
    string Name { get; }
    DesktopCapabilityRisk Risk { get; }
    bool Enabled { get; }
    string Summary { get; }
    Task<string> ExecuteAsync(DesktopActionRequest request, CancellationToken cancellationToken = default);
}

public sealed record DesktopActionAuditEntry(
    DateTimeOffset Timestamp,
    string ActionName,
    long ActorUserId,
    string AgentId,
    DesktopCapabilityRisk Risk,
    bool Succeeded,
    string Message);

public interface IDesktopActionAuditSink
{
    void Record(DesktopActionAuditEntry entry);
}

public interface IDesktopActionAuditReader
{
    IReadOnlyList<DesktopActionAuditEntry> GetRecentEntries(int maxCount);
}

public sealed class DesktopActionGateway(
    IEnumerable<IDesktopAction> actions,
    IDesktopActionAuditSink? auditSink = null,
    DesktopCapabilityRegistry? capabilityRegistry = null,
    string allowedAgentId = "xiayu")
{
    readonly Dictionary<string, IDesktopAction> actions = actions.ToDictionary(
        action => action.Name,
        StringComparer.OrdinalIgnoreCase);
    readonly DesktopCapabilityRegistry capabilityRegistry = capabilityRegistry ?? DesktopCapabilityRegistry.CreateDefault();
    readonly string allowedAgentId = allowedAgentId;

    public async Task<DesktopActionResult> ExecuteAsync(
        DesktopActionRequest request,
        CancellationToken cancellationToken = default)
    {
        string actionName = Normalize(request.ActionName);
        DesktopCapabilityRisk risk = DesktopCapabilityRisk.Critical;

        if (request.IsOwner == false)
            return Deny(request, actionName, risk, "desktop_action=denied reason=owner_required");

        if (request.AgentId.Equals(allowedAgentId, StringComparison.OrdinalIgnoreCase) == false)
            return Deny(request, actionName, risk, $"desktop_action=denied reason=agent_not_allowed allowed_agent={allowedAgentId}");

        if (actions.TryGetValue(actionName, out IDesktopAction? action) == false)
            return Deny(request, actionName, risk, "desktop_action=denied reason=unknown_action");

        risk = action.Risk;
        if (action.Enabled == false)
            return Deny(request, actionName, risk, "desktop_action=denied reason=capability_disabled");

        if (risk != DesktopCapabilityRisk.ReadOnly && capabilityRegistry.IsMutationEnabled == false)
            return Deny(request, actionName, risk, "desktop_action=denied desktop_mutation=disabled");

        try
        {
            string message = await action.ExecuteAsync(request, cancellationToken);
            DesktopActionResult result = new(true, DesktopActionStatus.Executed, risk, message);
            Record(request, actionName, risk, succeeded: true, message);
            return result;
        }
        catch (Exception ex)
        {
            string message = $"desktop_action=failed error={ex.GetType().Name}";
            DesktopActionResult result = new(false, DesktopActionStatus.Failed, risk, message);
            Record(request, actionName, risk, succeeded: false, message);
            return result;
        }
    }

    DesktopActionResult Deny(
        DesktopActionRequest request,
        string actionName,
        DesktopCapabilityRisk risk,
        string message)
    {
        DesktopActionResult result = new(false, DesktopActionStatus.Denied, risk, message);
        Record(request, actionName, risk, succeeded: false, message);
        return result;
    }

    void Record(
        DesktopActionRequest request,
        string actionName,
        DesktopCapabilityRisk risk,
        bool succeeded,
        string message)
    {
        auditSink?.Record(new DesktopActionAuditEntry(
            DateTimeOffset.Now,
            actionName,
            request.ActorUserId,
            request.AgentId,
            risk,
            succeeded,
            message));
    }

    static string Normalize(string? actionName)
    {
        return string.IsNullOrWhiteSpace(actionName) ? "unknown" : actionName.Trim();
    }
}
