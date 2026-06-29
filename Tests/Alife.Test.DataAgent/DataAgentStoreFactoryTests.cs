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
}
