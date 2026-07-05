using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisSessionStoreFactoryTests
{
    [Test]
    public void CreateDefaultUsesInMemorySessionStore()
    {
        IDataAgentAnalysisSessionStore store = DataAgentAnalysisSessionStoreFactory.Create(
            new DataAgentAnalysisSessionStoreOptions(
                ProviderName: string.Empty,
                PostgresConnectionString: string.Empty));

        Assert.That(store, Is.TypeOf<InMemoryDataAgentAnalysisSessionStore>());
    }

    [Test]
    public void CreateSupportsExplicitMemoryProvider()
    {
        IDataAgentAnalysisSessionStore store = DataAgentAnalysisSessionStoreFactory.Create(
            new DataAgentAnalysisSessionStoreOptions(
                ProviderName: "memory",
                PostgresConnectionString: string.Empty));

        Assert.That(store, Is.TypeOf<InMemoryDataAgentAnalysisSessionStore>());
    }

    [Test]
    public void CreateRejectsUnknownProvider()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentAnalysisSessionStoreFactory.Create(new DataAgentAnalysisSessionStoreOptions(
                ProviderName: "redis",
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("Unsupported DataAgent analysis session store provider"));
    }

    [Test]
    public void CreateRejectsPostgresWithoutConnectionString()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentAnalysisSessionStoreFactory.Create(new DataAgentAnalysisSessionStoreOptions(
                ProviderName: "postgres",
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION"));
    }

    [Test]
    public void FromEnvironmentDefaultsToMemoryProvider()
    {
        using EnvironmentScope scope = new();
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER", null);
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION", null);
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", null);

        DataAgentAnalysisSessionStoreOptions options =
            DataAgentAnalysisSessionStoreFactory.FromEnvironment();

        Assert.Multiple(() =>
        {
            Assert.That(options.ProviderName, Is.Empty);
            Assert.That(options.PostgresConnectionString, Is.Empty);
        });
    }

    [Test]
    public void FromEnvironmentUsesDedicatedCheckpointConnectionBeforeSharedPostgresConnection()
    {
        using EnvironmentScope scope = new();
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER", "postgres");
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION", "Host=checkpoint;Database=alife_checkpoint");
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", "Host=query;Database=alife_query");

        DataAgentAnalysisSessionStoreOptions options =
            DataAgentAnalysisSessionStoreFactory.FromEnvironment();

        Assert.Multiple(() =>
        {
            Assert.That(options.ProviderName, Is.EqualTo("postgres"));
            Assert.That(options.PostgresConnectionString, Is.EqualTo("Host=checkpoint;Database=alife_checkpoint"));
        });
    }

    [Test]
    public void FromEnvironmentFallsBackToSharedPostgresConnection()
    {
        using EnvironmentScope scope = new();
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER", "postgres");
        scope.Set("ALIFE_DATAAGENT_ANALYSIS_SESSION_POSTGRES_CONNECTION", null);
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", "Host=shared;Database=alife");

        DataAgentAnalysisSessionStoreOptions options =
            DataAgentAnalysisSessionStoreFactory.FromEnvironment();

        Assert.That(options.PostgresConnectionString, Is.EqualTo("Host=shared;Database=alife"));
    }

    sealed class EnvironmentScope : IDisposable
    {
        readonly Dictionary<string, string?> previousValues = new(StringComparer.Ordinal);

        public void Set(string name, string? value)
        {
            if (previousValues.ContainsKey(name) == false)
                previousValues[name] = Environment.GetEnvironmentVariable(name);

            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in previousValues)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
