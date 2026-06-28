using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class LlmDataAgentPlannerPromptFormatterTests
{
    [Test]
    public void FormatIncludesApprovedSchemaAndJsonContract()
    {
        string databasePath = CreateDatabasePath();
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
            catalog,
            snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(prompt.System, Does.Contain("Do not output SQL"));
            Assert.That(prompt.System, Does.Contain("JSON"));
            Assert.That(prompt.Contract, Does.Contain("\"type\":\"plan\""));
            Assert.That(prompt.Contract, Does.Contain("\"type\":\"clarification\""));
            Assert.That(prompt.Schema, Does.Contain("document_index"));
            Assert.That(prompt.Schema, Does.Contain("path"));
            Assert.That(prompt.Schema, Does.Contain("summary"));
            Assert.That(prompt.Schema, Does.Not.Contain("sqlite_master"));
            Assert.That(prompt.User, Does.Contain("Which documents describe DataAgent NL2SQL?"));
            Assert.That(prompt.User, Does.Contain("Role: developer"));
            Assert.That(prompt.User, Does.Contain("Locale: en-US"));
            Assert.That(prompt.User, Does.Contain("AllowLiveSources: False"));
        });
    }

    [Test]
    public void FormatRejectsMismatchedSchemaSnapshot()
    {
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = new(
            [new DataAgentDatasetSchema("document_index", ["path"], ["path"], true, false)],
            false);

        Assert.Throws<InvalidOperationException>(() => new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
            catalog,
            snapshot));
    }

    [Test]
    public void FormatOmitsUnmatchedDatasetsFromSchemaText()
    {
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = new(
            [
                new DataAgentDatasetSchema("document_index", ["path", "title"], ["path", "title"], true, true),
                new DataAgentDatasetSchema("engineering_gate", ["name"], [], false, false)
            ],
            true);

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
            catalog,
            snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(prompt.Schema, Does.Contain("document_index"));
            Assert.That(prompt.Schema, Does.Not.Contain("engineering_gate"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-llm-prompt-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        return databasePath;
    }
}
