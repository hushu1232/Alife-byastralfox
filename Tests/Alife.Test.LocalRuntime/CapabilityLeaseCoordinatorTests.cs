using Alife.Function.LocalRuntime;

namespace Alife.Test.LocalRuntime;

public sealed class CapabilityLeaseCoordinatorTests
{
    [Test]
    public async Task One_browser_lease_cannot_be_preempted()
    {
        CapabilityLeaseCoordinator leases = new();
        await using IAsyncDisposable lease = await leases.AcquireAsync("account-a", CapabilityKind.Browser, DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);

        Assert.That(await leases.TryAcquireAsync("account-b", CapabilityKind.Browser, DateTimeOffset.UtcNow.AddSeconds(1)), Is.Null);
    }
}
