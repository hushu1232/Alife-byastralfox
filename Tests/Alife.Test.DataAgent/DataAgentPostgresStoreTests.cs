using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentPostgresStoreTests
{
    [Test]
    public void LivePostgresStoreTestIsSkippedWithoutConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Pass("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set; live PostgreSQL test skipped.");

        Assert.That(connectionString, Is.Not.Empty);
    }

    [Test]
    public void LivePostgresStoreInitializesImportsFixturesAndExecutesReadOnlyQuery()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION is not set.");

        IDataAgentStore store = new PostgresDataAgentStore(connectionString);
        store.Initialize();
        store.ImportFixtures();

        DataAgentQueryResult result = store.Query(new DataAgentCompiledSql(
            "SELECT path, title FROM document_index ORDER BY id LIMIT 10",
            []));

        Assert.Multiple(() =>
        {
            Assert.That(store.ProviderName, Is.EqualTo("postgres"));
            Assert.That(result.Rows, Is.Not.Empty);
            Assert.That(result.Rows[0].Keys, Does.Contain("path"));
            Assert.That(result.Rows[0].Keys, Does.Contain("title"));
        });
    }
}
