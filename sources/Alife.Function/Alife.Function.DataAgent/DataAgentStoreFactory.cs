namespace Alife.Function.DataAgent;

public sealed record DataAgentStoreOptions(
    string ProviderName,
    string SqlitePath,
    string PostgresConnectionString);

public static class DataAgentStoreFactory
{
    public static IDataAgentStore Create(DataAgentStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string provider = string.IsNullOrWhiteSpace(options.ProviderName)
            ? "sqlite"
            : options.ProviderName.Trim().ToLowerInvariant();

        return provider switch
        {
            "sqlite" => CreateSqlite(options.SqlitePath),
            "postgres" => CreatePostgres(options.PostgresConnectionString),
            _ => throw new InvalidOperationException($"Unsupported DataAgent store provider: {options.ProviderName}")
        };
    }

    public static DataAgentStoreOptions FromEnvironment(string defaultSqlitePath)
    {
        return new DataAgentStoreOptions(
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER") ?? string.Empty,
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH") ?? defaultSqlitePath,
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_CONNECTION") ?? string.Empty);
    }

    static IDataAgentStore CreateSqlite(string sqlitePath)
    {
        if (string.IsNullOrWhiteSpace(sqlitePath))
            throw new InvalidOperationException("ALIFE_DATAAGENT_SQLITE_PATH is required when DataAgent sqlite store is selected.");

        return new SqliteDataAgentStore(sqlitePath);
    }

    static IDataAgentStore CreatePostgres(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ALIFE_DATAAGENT_POSTGRES_CONNECTION is required when DataAgent postgres store is selected.");

        return new PostgresDataAgentStore(connectionString);
    }
}
