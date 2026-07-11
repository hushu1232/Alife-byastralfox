using Alife.Function.LocalRuntime;

namespace Alife.Test.LocalRuntime;

public sealed class SqliteDurableTaskStoreTests
{
    [Test]
    public async Task Restart_requeues_only_nonexpired_retry_safe_work()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        SqliteDurableTaskStore store = new("account-a", root);
        DurableTaskItem unsafeTask = await store.EnqueueAsync(NewTask(retrySafe: false));
        await store.TransitionAsync(unsafeTask.Id, DurableTaskState.Starting, SafeReasonCode.None);
        await store.TransitionAsync(unsafeTask.Id, DurableTaskState.Running, SafeReasonCode.None);

        await new DurableTaskRecovery(store).RecoverAfterSupervisorRestartAsync();

        Assert.That((await store.GetAsync(unsafeTask.Id))!.State, Is.EqualTo(DurableTaskState.Degraded));
    }

    private static DurableTaskRequest NewTask(bool retrySafe) => new("account-a", CapabilityKind.Vision, DateTimeOffset.UtcNow.AddMinutes(1), retrySafe);
}
