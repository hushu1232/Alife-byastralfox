using Alife.Function.Browser;
using Alife.Function.LocalRuntime;
using Xunit;

namespace Alife.Test.Browser;

public sealed class LocalBrowserCapabilityAdapterTests
{
    [Fact]
    public async Task Browser_rejects_public_debug_endpoint_without_starting_process()
    {
        FakeProcessHost process = new();
        LocalBrowserCapabilityAdapter adapter = new(new Uri("http://192.0.2.8:9222"), process);
        AdapterReadiness readiness = await adapter.EnsureReadyAsync(DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);
        Assert.Equal(SafeReasonCode.ConfigurationRejected, readiness.Reason);
        Assert.Equal(0, process.StartCalls);
    }
    private sealed class FakeProcessHost : ILocalCapabilityProcessHost { public int StartCalls { get; private set; } public Task StartAsync(CancellationToken cancellationToken) { StartCalls++; return Task.CompletedTask; } }
}
