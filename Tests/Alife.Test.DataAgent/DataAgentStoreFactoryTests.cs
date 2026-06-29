using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentStoreFactoryTests
{
    [Test]
    public void CreateDefaultUsesSqliteStore()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.sqlite");

        IDataAgentStore store = DataAgentStoreFactory.Create(new DataAgentStoreOptions(
            ProviderName: string.Empty,
            SqlitePath: databasePath,
            PostgresConnectionString: string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(store, Is.TypeOf<SqliteDataAgentStore>());
            Assert.That(store.ProviderName, Is.EqualTo("sqlite"));
        });
    }

    [Test]
    public void CreateRejectsUnknownProvider()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentStoreFactory.Create(new DataAgentStoreOptions(
                ProviderName: "oracle",
                SqlitePath: "dataagent.sqlite",
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("Unsupported DataAgent store provider"));
    }

    [Test]
    public void CreateRejectsPostgresWithoutConnectionString()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DataAgentStoreFactory.Create(new DataAgentStoreOptions(
                ProviderName: "postgres",
                SqlitePath: string.Empty,
                PostgresConnectionString: string.Empty)))!;

        Assert.That(exception.Message, Does.Contain("ALIFE_DATAAGENT_POSTGRES_CONNECTION"));
    }

    [Test]
    public void FromEnvironmentUsesDefaultSqlitePathWhenProviderIsUnset()
    {
        using EnvironmentScope scope = new();
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.sqlite");

        scope.Set("ALIFE_DATAAGENT_STORE_PROVIDER", null);
        scope.Set("ALIFE_DATAAGENT_SQLITE_PATH", null);
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", null);

        DataAgentStoreOptions options = DataAgentStoreFactory.FromEnvironment(databasePath);
        IDataAgentStore store = DataAgentStoreFactory.Create(options);

        Assert.Multiple(() =>
        {
            Assert.That(options.ProviderName, Is.Empty);
            Assert.That(options.SqlitePath, Is.EqualTo(databasePath));
            Assert.That(options.PostgresConnectionString, Is.Empty);
            Assert.That(store, Is.TypeOf<SqliteDataAgentStore>());
        });
    }

    [Test]
    public void FromEnvironmentUsesSqlitePathOverride()
    {
        using EnvironmentScope scope = new();
        string defaultPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}-default.sqlite");
        string overridePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}-override.sqlite");

        scope.Set("ALIFE_DATAAGENT_STORE_PROVIDER", "sqlite");
        scope.Set("ALIFE_DATAAGENT_SQLITE_PATH", overridePath);
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", null);

        DataAgentStoreOptions options = DataAgentStoreFactory.FromEnvironment(defaultPath);

        Assert.Multiple(() =>
        {
            Assert.That(options.ProviderName, Is.EqualTo("sqlite"));
            Assert.That(options.SqlitePath, Is.EqualTo(overridePath));
            Assert.That(options.PostgresConnectionString, Is.Empty);
        });
    }

    [Test]
    public void FromEnvironmentUsesPostgresProviderAndConnection()
    {
        using EnvironmentScope scope = new();

        scope.Set("ALIFE_DATAAGENT_STORE_PROVIDER", "postgres");
        scope.Set("ALIFE_DATAAGENT_SQLITE_PATH", null);
        scope.Set("ALIFE_DATAAGENT_POSTGRES_CONNECTION", "Host=postgres.example;Database=alife_test");

        DataAgentStoreOptions options = DataAgentStoreFactory.FromEnvironment("fallback.sqlite");
        IDataAgentStore store = DataAgentStoreFactory.Create(options);

        Assert.Multiple(() =>
        {
            Assert.That(options.ProviderName, Is.EqualTo("postgres"));
            Assert.That(options.SqlitePath, Is.EqualTo("fallback.sqlite"));
            Assert.That(options.PostgresConnectionString, Is.EqualTo("Host=postgres.example;Database=alife_test"));
            Assert.That(store, Is.TypeOf<PostgresDataAgentStore>());
        });
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
