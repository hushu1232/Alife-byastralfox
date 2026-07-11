using Alife.Function.LocalRuntime;

namespace Alife.Test.LocalRuntime;

public sealed class LocalDualAccountProductionScenarioTests
{
    [Test]
    public async Task Account_a_restart_does_not_stop_b_or_migrate_b_task()
    {
        LocalDualAccountProductionFixture fixture = new(TestContext.CurrentContext.WorkDirectory);
        await fixture.EnqueueAsync("account-b", CapabilityKind.Vision);
        await fixture.FailBusinessProbeAsync("account-a", consecutiveFailures: 3);
        await fixture.RunSupervisorCycleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Status("account-a").Health, Is.EqualTo(LocalAccountHealth.Draining));
            Assert.That(fixture.Status("account-b").Health, Is.EqualTo(LocalAccountHealth.Healthy));
            Assert.That(fixture.TaskAccountIds("account-b"), Is.All.EqualTo("account-b"));
        });
    }
}
