using Microsoft.Data.Sqlite;

namespace Alife.Function.LocalRuntime;

public sealed record DurableTaskRequest(string AccountId, CapabilityKind Capability, DateTimeOffset DeadlineUtc, bool RetrySafe);
public sealed record DurableTaskItem(string Id, string AccountId, CapabilityKind Capability, DateTimeOffset DeadlineUtc, bool RetrySafe, DurableTaskState State, SafeReasonCode Reason);

public sealed class SqliteDurableTaskStore
{
    private readonly string accountId;
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);
    public SqliteDurableTaskStore(string accountId, string runtimeRoot)
    {
        this.accountId = accountId;
        string folder = Path.Combine(runtimeRoot, "local-production"); Directory.CreateDirectory(folder);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "queue.db") }.ToString();
        using SqliteConnection connection = new(connectionString); connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS tasks(id TEXT PRIMARY KEY, account_id TEXT NOT NULL, capability INTEGER NOT NULL, deadline TEXT NOT NULL, retry_safe INTEGER NOT NULL, state INTEGER NOT NULL, reason INTEGER NOT NULL)"; command.ExecuteNonQuery();
    }
    public async Task<DurableTaskItem> EnqueueAsync(DurableTaskRequest request)
    {
        if (request.AccountId != accountId) throw new InvalidOperationException("Cross-account task access is forbidden.");
        DurableTaskItem item = new(Guid.NewGuid().ToString("N"), accountId, request.Capability, request.DeadlineUtc, request.RetrySafe, DurableTaskState.Queued, SafeReasonCode.None);
        await gate.WaitAsync(); try { using SqliteConnection c = new(connectionString); c.Open(); using SqliteCommand q = c.CreateCommand(); q.CommandText = "INSERT INTO tasks VALUES($id,$account,$capability,$deadline,$retry,$state,$reason)"; q.Parameters.AddWithValue("$id", item.Id); q.Parameters.AddWithValue("$account", accountId); q.Parameters.AddWithValue("$capability", (int)item.Capability); q.Parameters.AddWithValue("$deadline", item.DeadlineUtc.ToString("O")); q.Parameters.AddWithValue("$retry", item.RetrySafe ? 1 : 0); q.Parameters.AddWithValue("$state", (int)item.State); q.Parameters.AddWithValue("$reason", (int)item.Reason); q.ExecuteNonQuery(); } finally { gate.Release(); } return item;
    }
    public async Task<DurableTaskItem?> GetAsync(string id) { await gate.WaitAsync(); try { using SqliteConnection c = new(connectionString); c.Open(); using SqliteCommand q = c.CreateCommand(); q.CommandText = "SELECT account_id,capability,deadline,retry_safe,state,reason FROM tasks WHERE id=$id"; q.Parameters.AddWithValue("$id", id); using SqliteDataReader r = q.ExecuteReader(); return r.Read() ? new DurableTaskItem(id, r.GetString(0), (CapabilityKind)r.GetInt32(1), DateTimeOffset.Parse(r.GetString(2)), r.GetInt32(3) != 0, (DurableTaskState)r.GetInt32(4), (SafeReasonCode)r.GetInt32(5)) : null; } finally { gate.Release(); } }
    public async Task TransitionAsync(string id, DurableTaskState next, SafeReasonCode reason) { DurableTaskItem current = await GetAsync(id) ?? throw new InvalidOperationException("Task not found."); if (!Allowed(current.State, next)) throw new InvalidOperationException("Illegal task transition."); await UpdateAsync(id, next, reason); }
    internal async Task RecoverAsync() { foreach (DurableTaskItem item in await NonTerminalAsync()) await UpdateAsync(item.Id, item.RetrySafe && item.DeadlineUtc > DateTimeOffset.UtcNow ? DurableTaskState.Queued : DurableTaskState.Degraded, item.RetrySafe && item.DeadlineUtc > DateTimeOffset.UtcNow ? SafeReasonCode.None : SafeReasonCode.RestartRecoveryRequired); }
    private async Task<List<DurableTaskItem>> NonTerminalAsync() { List<DurableTaskItem> result=[]; foreach (DurableTaskState state in new[]{DurableTaskState.Starting,DurableTaskState.Ready,DurableTaskState.Running}) { using SqliteConnection c=new(connectionString); c.Open(); using SqliteCommand q=c.CreateCommand(); q.CommandText="SELECT id FROM tasks WHERE state=$state"; q.Parameters.AddWithValue("$state",(int)state); using SqliteDataReader r=q.ExecuteReader(); while(r.Read()) result.Add((await GetAsync(r.GetString(0)))!); } return result; }
    private async Task UpdateAsync(string id, DurableTaskState state, SafeReasonCode reason) { await gate.WaitAsync(); try { using SqliteConnection c=new(connectionString); c.Open(); using SqliteCommand q=c.CreateCommand(); q.CommandText="UPDATE tasks SET state=$state,reason=$reason WHERE id=$id AND account_id=$account"; q.Parameters.AddWithValue("$state",(int)state); q.Parameters.AddWithValue("$reason",(int)reason); q.Parameters.AddWithValue("$id",id); q.Parameters.AddWithValue("$account",accountId); q.ExecuteNonQuery(); } finally { gate.Release(); } }
    private static bool Allowed(DurableTaskState from, DurableTaskState to) => (from,to) is (DurableTaskState.Queued,DurableTaskState.Starting) or (DurableTaskState.Starting,DurableTaskState.Ready) or (DurableTaskState.Starting,DurableTaskState.Running) or (DurableTaskState.Starting,DurableTaskState.Degraded) or (DurableTaskState.Running,DurableTaskState.Degraded) or (DurableTaskState.Ready,DurableTaskState.Running);
}
public sealed class DurableTaskRecovery(SqliteDurableTaskStore store) { public Task RecoverAfterSupervisorRestartAsync() => store.RecoverAsync(); }
