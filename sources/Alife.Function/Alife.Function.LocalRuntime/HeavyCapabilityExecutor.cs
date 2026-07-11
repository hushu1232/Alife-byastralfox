namespace Alife.Function.LocalRuntime;

public sealed class HeavyCapabilityExecutor(CapabilityLeaseCoordinator leases)
{
    public async Task<AdapterExecutionResult> ExecuteAsync(HeavyCapabilityRequest request, IHeavyCapabilityAdapter adapter, CancellationToken cancellationToken)
    {
        if (request.Capability != adapter.Kind) return new AdapterExecutionResult(SafeReasonCode.ConfigurationRejected);
        await using IAsyncDisposable lease = await leases.AcquireAsync(request.AccountId, request.Capability, request.Deadline, cancellationToken);
        AdapterReadiness readiness = await adapter.EnsureReadyAsync(request.Deadline, cancellationToken);
        if (!readiness.IsReady) return new AdapterExecutionResult(readiness.Reason);
        if (await adapter.GetHealthAsync(cancellationToken) != AdapterHealth.Healthy) return new AdapterExecutionResult(SafeReasonCode.HealthProbeFailed);
        return await adapter.ExecuteAsync(request, cancellationToken);
    }
}
