using Alife.Function.LocalRuntime;

namespace Alife.Test.LocalRuntime;

public sealed class HeavyCapabilityExecutorTests
{
    [Test]
    public async Task Unhealthy_adapter_does_not_execute_user_work()
    {
        FakeAdapter adapter = new(AdapterHealth.Unhealthy);
        HeavyCapabilityExecutor executor = new(new CapabilityLeaseCoordinator());

        AdapterExecutionResult result = await executor.ExecuteAsync(new HeavyCapabilityRequest("account-a", CapabilityKind.Speech, DateTimeOffset.UtcNow.AddSeconds(1)), adapter, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Reason, Is.EqualTo(SafeReasonCode.HealthProbeFailed));
            Assert.That(adapter.ExecuteCalls, Is.Zero);
        });
    }

    private sealed class FakeAdapter(AdapterHealth health) : IHeavyCapabilityAdapter
    {
        public int ExecuteCalls { get; private set; }
        public CapabilityKind Kind => CapabilityKind.Speech;
        public Task<AdapterReadiness> EnsureReadyAsync(DateTimeOffset deadline, CancellationToken cancellationToken) => Task.FromResult(AdapterReadiness.Ready);
        public Task<AdapterHealth> GetHealthAsync(CancellationToken cancellationToken) => Task.FromResult(health);
        public Task<AdapterExecutionResult> ExecuteAsync(HeavyCapabilityRequest request, CancellationToken cancellationToken) { ExecuteCalls++; return Task.FromResult(new AdapterExecutionResult(SafeReasonCode.None)); }
        public Task DrainAsync(DateTimeOffset deadline, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopIfIdleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public SafeCapabilityStatus GetSafeStatus() => new("unavailable", SafeReasonCode.HealthProbeFailed);
    }
}
