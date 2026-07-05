namespace Alife.Function.DataAgent;

public sealed record DataAgentAnalysisSessionStoreOptions(
    string ProviderName,
    string PostgresConnectionString);

public static class DataAgentAnalysisSessionStoreFactory
{
    public static IDataAgentAnalysisSessionStore Create(DataAgentAnalysisSessionStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string provider = string.IsNullOrWhiteSpace(options.ProviderName)
            ? "memory"
            : options.ProviderName.Trim().ToLowerInvariant();

        return provider switch
        {
            "memory" => new InMemoryDataAgentAnalysisSessionStore(),
            "postgres" => CreatePostgres(options.PostgresConnectionString),
            _ => throw new InvalidOperationException(
                $"Unsupported DataAgent analysis session store provider: {options.ProviderName}")
        };
    }

    public static DataAgentAnalysisSessionStoreOptions FromEnvironment()
    {
        string dedicatedPostgresConnection =
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION") ??
            string.Empty;
        string sharedPostgresConnection =
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_CONNECTION") ??
            string.Empty;

        return new DataAgentAnalysisSessionStoreOptions(
            Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER") ?? string.Empty,
            string.IsNullOrWhiteSpace(dedicatedPostgresConnection)
                ? sharedPostgresConnection
                : dedicatedPostgresConnection);
    }

    static IDataAgentAnalysisSessionStore CreatePostgres(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION or ALIFE_DATAAGENT_POSTGRES_CONNECTION is required when DataAgent postgres analysis session store is selected.");
        }

        PostgresDataAgentAnalysisSessionStore store = new(connectionString);
        store.Initialize();
        return store;
    }
}
