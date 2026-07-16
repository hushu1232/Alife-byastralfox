using Alife.Function.LocalRuntime;

namespace Alife.Test.LocalRuntime;

internal sealed record ScenarioSlotStatus(LocalAccountHealth Health);
internal sealed class LocalDualAccountProductionFixture
{
    private readonly Dictionary<string, SqliteDurableTaskStore> stores;
    private readonly Dictionary<string, List<string>> taskIds = new() { ["account-a"] = [], ["account-b"] = [] };
    private readonly Dictionary<string, LocalAccountHealth> health = new() { ["account-a"] = LocalAccountHealth.Healthy, ["account-b"] = LocalAccountHealth.Healthy };
    private readonly Dictionary<string, int> failures = new() { ["account-a"] = 0, ["account-b"] = 0 };
    public LocalDualAccountProductionFixture(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "dual-account-scenario", Guid.NewGuid().ToString("N"));
        stores = new() { ["account-a"] = new("account-a", Path.Combine(root, "account-a")), ["account-b"] = new("account-b", Path.Combine(root, "account-b")) };
    }
    public async Task EnqueueAsync(string accountId, CapabilityKind capability)
    {
        DurableTaskItem item = await stores[accountId].EnqueueAsync(new DurableTaskRequest(accountId, capability, DateTimeOffset.UtcNow.AddMinutes(1), true)); taskIds[accountId].Add(item.Id);
    }
    public Task FailBusinessProbeAsync(string accountId, int consecutiveFailures) { failures[accountId] = consecutiveFailures; return Task.CompletedTask; }
    public Task RunSupervisorCycleAsync() { foreach (string id in failures.Keys) if (failures[id] >= 3) health[id] = LocalAccountHealth.Draining; return Task.CompletedTask; }
    public ScenarioSlotStatus Status(string accountId) => new(health[accountId]);
    public IReadOnlyList<string> TaskAccountIds(string accountId) => taskIds[accountId].Select(id => stores[accountId].GetAsync(id).GetAwaiter().GetResult()!.AccountId).ToList();
}
