namespace Alife.Function.LocalRuntime;

public sealed class HeavyCapabilityExecutor(CapabilityLeaseCoordinator leases)
{
    public async Task<AdapterExecutionResult> ExecuteAsync(HeavyCapabilityRequest request, IHeavyCapabilityAdapter adapter, CancellationToken cancellationToken)
    {
        if (request.Capability != adapter.Kind) return new AdapterExecutionResult(SafeReasonCode.ConfigurationRejected);
        await using IAsyncDisposable lease = await leases.AcquireAsync(request.AccountId, request.Capability, request.Deadline, cancellationToken);
        if (await adapter.EnsureReadyAsync(request.Deadline, cancellationToken) != AdapterReadiness.Ready) return new AdapterExecutionResult(SafeReasonCode.DependencyUnavailable);
        if (await adapter.GetHealthAsync(cancellationToken) != AdapterHealth.Healthy) return new AdapterExecutionResult(SafeReasonCode.HealthProbeFailed);
        return await adapter.ExecuteAsync(request, cancellationToken);
    }
}
