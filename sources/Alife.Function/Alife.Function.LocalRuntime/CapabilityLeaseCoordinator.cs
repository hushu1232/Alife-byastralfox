namespace Alife.Function.LocalRuntime;

public sealed class CapabilityLeaseCoordinator
{
    private readonly IReadOnlyDictionary<CapabilityKind, SemaphoreSlim> gates = Enum.GetValues<CapabilityKind>().ToDictionary(x => x, _ => new SemaphoreSlim(1, 1));
    public async Task<IAsyncDisposable> AcquireAsync(string accountId, CapabilityKind capability, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        IAsyncDisposable? lease = await TryAcquireAsync(accountId, capability, deadline, cancellationToken);
        return lease ?? throw new TimeoutException("Capability is busy.");
    }
    public async Task<IAsyncDisposable?> TryAcquireAsync(string accountId, CapabilityKind capability, DateTimeOffset deadline, CancellationToken cancellationToken = default)
    {
        TimeSpan wait = deadline - DateTimeOffset.UtcNow; if (wait <= TimeSpan.Zero) return null;
        SemaphoreSlim gate = gates[capability]; if (!await gate.WaitAsync(wait, cancellationToken)) return null;
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable { public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; } }
}
