namespace Alife.Function.LocalRuntime;

public sealed record AdapterReadiness(bool IsReady, SafeReasonCode Reason)
{
    public static AdapterReadiness Ready { get; } = new(true, SafeReasonCode.None);
}
public enum AdapterHealth { Healthy, Unhealthy }
public sealed record HeavyCapabilityRequest(string AccountId, CapabilityKind Capability, DateTimeOffset Deadline);
public sealed record AdapterExecutionResult(SafeReasonCode Reason);
public sealed record SafeCapabilityStatus(string State, SafeReasonCode Reason);
public interface ILocalCapabilityProcessHost { Task StartAsync(CancellationToken cancellationToken); }
public interface IHeavyCapabilityAdapter
{
    CapabilityKind Kind { get; }
    Task<AdapterReadiness> EnsureReadyAsync(DateTimeOffset deadline, CancellationToken cancellationToken);
    Task<AdapterHealth> GetHealthAsync(CancellationToken cancellationToken);
    Task<AdapterExecutionResult> ExecuteAsync(HeavyCapabilityRequest request, CancellationToken cancellationToken);
    Task DrainAsync(DateTimeOffset deadline, CancellationToken cancellationToken);
    Task StopIfIdleAsync(CancellationToken cancellationToken);
    SafeCapabilityStatus GetSafeStatus();
}
