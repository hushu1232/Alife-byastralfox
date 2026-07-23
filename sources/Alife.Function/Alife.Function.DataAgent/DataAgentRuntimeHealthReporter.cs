using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed class DataAgentRuntimeHealthReporter
{
    const int SnapshotVersion = 1;
    const string SnapshotFileName = "runtime-health.json";
    const string EvidencePath = "runtime-health.json";
    const string Endpoint = "loopback";
    static readonly ConcurrentDictionary<string, DataAgentRuntimeHealthReporter> Reporters = new(StringComparer.OrdinalIgnoreCase);

    readonly object gate = new();
    readonly string accountId;
    readonly string databasePath;
    readonly string snapshotPath;
    readonly Dictionary<string, DataAgentRuntimeHealthEvent> latestByComponent = new(StringComparer.Ordinal);

    DataAgentRuntimeHealthReporter(string storageRoot, string accountId)
    {
        this.accountId = accountId;
        databasePath = Path.Combine(storageRoot, "DataAgent", "dataagent.sqlite");
        snapshotPath = Path.Combine(storageRoot, SnapshotFileName);
        LoadSnapshotIfValid();
    }

    public string AccountId => accountId;

    public static DataAgentRuntimeHealthReporter Create(string storageRoot, string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        if (DataAgentRuntimeHealthEvent.IsAllowedAccountId(accountId) == false)
            throw new ArgumentOutOfRangeException(nameof(accountId));

        string normalizedRoot = Path.GetFullPath(storageRoot);
        return Reporters.GetOrAdd(
            string.Concat(normalizedRoot, "|", accountId),
            _ => new DataAgentRuntimeHealthReporter(normalizedRoot, accountId));
    }

    public static DataAgentRuntimeHealthReporter? TryCreate(string storageRoot, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
            return null;

        try
        {
            string inferredAccountId = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageRoot)));
            if (DataAgentRuntimeHealthEvent.IsAllowedAccountId(accountId) &&
                DataAgentRuntimeHealthEvent.IsAllowedAccountId(inferredAccountId) &&
                string.Equals(accountId, inferredAccountId, StringComparison.Ordinal) == false)
            {
                return null;
            }

            string resolvedAccountId = DataAgentRuntimeHealthEvent.IsAllowedAccountId(accountId)
                ? accountId!
                : inferredAccountId;
            return DataAgentRuntimeHealthEvent.IsAllowedAccountId(resolvedAccountId)
                ? Create(storageRoot, resolvedAccountId)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Report(DataAgentRuntimeHealthEvent healthEvent)
    {
        ArgumentNullException.ThrowIfNull(healthEvent);
        if (string.Equals(healthEvent.AccountId, accountId, StringComparison.Ordinal) == false)
            return;

        lock (gate)
        {
            if (latestByComponent.TryGetValue(healthEvent.Component, out DataAgentRuntimeHealthEvent? previous) &&
                previous.State == healthEvent.State &&
                string.Equals(previous.ReasonCode, healthEvent.ReasonCode, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                latestByComponent[healthEvent.Component] = healthEvent;
                InsertAudit(healthEvent);
                WriteSnapshot();
            }
            catch
            {
                if (previous == null)
                    latestByComponent.Remove(healthEvent.Component);
                else
                    latestByComponent[healthEvent.Component] = previous;
                // Runtime health reporting is observability only and must not interrupt QQ handling.
            }
        }
    }

    public IReadOnlyList<DataAgentRuntimeHealthEvent> ReadAudit()
    {
        try
        {
            DataAgentSchemaInitializer.Initialize(databasePath);
            using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT capability, account, status, failure_reason
                FROM runtime_readiness_check
                WHERE account = $account AND endpoint = $endpoint AND evidence_path = $evidencePath
                ORDER BY id ASC
                """;
            command.Parameters.AddWithValue("$account", accountId);
            command.Parameters.AddWithValue("$endpoint", Endpoint);
            command.Parameters.AddWithValue("$evidencePath", EvidencePath);

            using SqliteDataReader reader = command.ExecuteReader();
            List<DataAgentRuntimeHealthEvent> result = [];
            while (reader.Read())
            {
                if (TryCreateEvent(reader.GetString(1), reader.GetString(0), reader.GetString(2), reader.GetString(3), out DataAgentRuntimeHealthEvent? healthEvent))
                    result.Add(healthEvent!);
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    void InsertAudit(DataAgentRuntimeHealthEvent healthEvent)
    {
        DataAgentSchemaInitializer.Initialize(databasePath);
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runtime_readiness_check (capability, account, endpoint, status, required, failure_reason, last_checked_at, evidence_path)
            VALUES ($capability, $account, $endpoint, $status, $required, $failureReason, $checkedAt, $evidencePath)
            """;
        command.Parameters.AddWithValue("$capability", healthEvent.Component);
        command.Parameters.AddWithValue("$account", healthEvent.AccountId);
        command.Parameters.AddWithValue("$endpoint", Endpoint);
        command.Parameters.AddWithValue("$status", ToSnapshotHealth(healthEvent.State));
        command.Parameters.AddWithValue("$required", 1);
        command.Parameters.AddWithValue("$failureReason", healthEvent.ReasonCode);
        command.Parameters.AddWithValue("$checkedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$evidencePath", EvidencePath);
        command.ExecuteNonQuery();
    }

    void LoadSnapshotIfValid()
    {
        try
        {
            if (File.Exists(snapshotPath) == false)
                return;

            RuntimeHealthSnapshot? snapshot = JsonSerializer.Deserialize<RuntimeHealthSnapshot>(File.ReadAllText(snapshotPath));
            if (snapshot is null || snapshot.Version != SnapshotVersion ||
                string.Equals(snapshot.Account, accountId, StringComparison.Ordinal) == false ||
                snapshot.Components is not { Count: > 0 and <= 4 })
            {
                return;
            }

            foreach (RuntimeHealthSnapshotComponent component in snapshot.Components)
            {
                if (latestByComponent.ContainsKey(component.Component) ||
                    TryCreateEvent(snapshot.Account, component.Component, component.Health, component.Reason, out DataAgentRuntimeHealthEvent? healthEvent) == false)
                {
                    latestByComponent.Clear();
                    return;
                }

                latestByComponent.Add(component.Component, healthEvent!);
            }
        }
        catch
        {
            latestByComponent.Clear();
        }
    }

    void WriteSnapshot()
    {
        string? parent = Path.GetDirectoryName(snapshotPath);
        if (string.IsNullOrWhiteSpace(parent) == false)
            Directory.CreateDirectory(parent);

        RuntimeHealthSnapshot snapshot = new(
            SnapshotVersion,
            accountId,
            latestByComponent.Values
                .OrderBy(item => item.Component, StringComparer.Ordinal)
                .Select(item => new RuntimeHealthSnapshotComponent(item.Component, ToSnapshotHealth(item.State), item.ReasonCode))
                .ToList());
        string temporaryPath = snapshotPath + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot));
            File.Move(temporaryPath, snapshotPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    static bool TryCreateEvent(
        string account,
        string component,
        string health,
        string reason,
        out DataAgentRuntimeHealthEvent? healthEvent)
    {
        healthEvent = null;
        if (TryParseSnapshotHealth(health, out DataAgentRuntimeHealthState state) == false)
            return false;

        try
        {
            healthEvent = new DataAgentRuntimeHealthEvent(account, component, state, reason);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool TryParseSnapshotHealth(string value, out DataAgentRuntimeHealthState state)
    {
        state = value switch
        {
            "healthy" => DataAgentRuntimeHealthState.Healthy,
            "degraded" => DataAgentRuntimeHealthState.Degraded,
            "unavailable" => DataAgentRuntimeHealthState.Unavailable,
            _ => default
        };
        return value is "healthy" or "degraded" or "unavailable";
    }

    static string ToSnapshotHealth(DataAgentRuntimeHealthState state) => state switch
    {
        DataAgentRuntimeHealthState.Healthy => "healthy",
        DataAgentRuntimeHealthState.Degraded => "degraded",
        DataAgentRuntimeHealthState.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    sealed record RuntimeHealthSnapshot(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("account")] string Account,
        [property: JsonPropertyName("components")] List<RuntimeHealthSnapshotComponent> Components);

    sealed record RuntimeHealthSnapshotComponent(
        [property: JsonPropertyName("component")] string Component,
        [property: JsonPropertyName("health")] string Health,
        [property: JsonPropertyName("reason")] string Reason);
}
