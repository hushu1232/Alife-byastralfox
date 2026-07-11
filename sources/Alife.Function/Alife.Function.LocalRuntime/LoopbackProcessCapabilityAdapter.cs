namespace Alife.Function.LocalRuntime;

public class LoopbackProcessCapabilityAdapter(CapabilityKind kind, Uri endpoint, ILocalCapabilityProcessHost process) : IHeavyCapabilityAdapter
{
    public CapabilityKind Kind => kind;
    public async Task<AdapterReadiness> EnsureReadyAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        if (!endpoint.IsLoopback) return new(false, SafeReasonCode.ConfigurationRejected);
        if (DateTimeOffset.UtcNow >= deadline) return new(false, SafeReasonCode.DeadlineExceeded);
        await process.StartAsync(cancellationToken); return AdapterReadiness.Ready;
    }
    public Task<AdapterHealth> GetHealthAsync(CancellationToken cancellationToken) => Task.FromResult(AdapterHealth.Healthy);
    public Task<AdapterExecutionResult> ExecuteAsync(HeavyCapabilityRequest request, CancellationToken cancellationToken) => Task.FromResult(new AdapterExecutionResult(SafeReasonCode.None));
    public Task DrainAsync(DateTimeOffset deadline, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopIfIdleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public SafeCapabilityStatus GetSafeStatus() => new("ready", SafeReasonCode.None);
}
