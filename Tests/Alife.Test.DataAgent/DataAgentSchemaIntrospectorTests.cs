using Alife.Function.DataAgent;
using Microsoft.Data.Sqlite;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentSchemaIntrospectorTests
{
    [Test]
    public void SnapshotIncludesEveryDefaultCatalogDataset()
    {
        string databasePath = CreateDatabasePath();

        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(
            DataAgentCatalog.CreateDefault(),
            databasePath).Inspect();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("engineering_gate"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("runtime_readiness_check"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("module_capability"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("test_run"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("document_index"));
            Assert.That(snapshot.Datasets.Select(dataset => dataset.Name), Does.Contain("query_audit"));
        });
    }

    [Test]
    public void InitializedSqliteSchemaMatchesDefaultCatalog()
    {
        string databasePath = CreateDatabasePath();

        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(
            DataAgentCatalog.CreateDefault(),
            databasePath).Inspect();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CatalogMatchesDatabase, Is.True);
            Assert.That(snapshot.Datasets, Has.All.Matches<DataAgentDatasetSchema>(dataset => dataset.ExistsInDatabase));
            Assert.That(snapshot.Datasets, Has.All.Matches<DataAgentDatasetSchema>(dataset => dataset.FieldsMatch));
        });
    }

    [Test]
    public void MissingCatalogTableIsReportedAsMismatch()
    {
        string databasePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "dataagent-schema-introspector-tests",
            $"{Guid.NewGuid():N}.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE engineering_gate (id INTEGER PRIMARY KEY, name TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(
            DataAgentCatalog.CreateDefault(),
            databasePath).Inspect();

        DataAgentDatasetSchema engineeringGate = snapshot.Datasets.Single(dataset => dataset.Name == "engineering_gate");
        DataAgentDatasetSchema documentIndex = snapshot.Datasets.Single(dataset => dataset.Name == "document_index");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CatalogMatchesDatabase, Is.False);
            Assert.That(engineeringGate.ExistsInDatabase, Is.True);
            Assert.That(engineeringGate.FieldsMatch, Is.False);
            Assert.That(documentIndex.ExistsInDatabase, Is.False);
            Assert.That(documentIndex.FieldsMatch, Is.False);
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-schema-introspector-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        return databasePath;
    }
}
