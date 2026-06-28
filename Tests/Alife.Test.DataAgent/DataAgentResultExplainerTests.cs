using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentResultExplainerTests
{
    [Test]
    public void ExplainAcceptedResultIncludesDatasetRowsSignalsAndSourceBoundary()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent", "nl2sql", "document"],
            "question asks for DataAgent or NL2SQL documentation");

        string result = DataAgentResultExplainer.ExplainAccepted(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            3,
            "DataAgent NL2SQL Design",
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("document_index"));
            Assert.That(result, Does.Contain("3 rows"));
            Assert.That(result, Does.Contain("dataagent, nl2sql, document"));
            Assert.That(result, Does.Contain("local SQLite"));
            Assert.That(result, Does.Not.Contain("\r"));
            Assert.That(result, Does.Not.Contain("\n"));
        });
    }

    [Test]
    public void ExplainAcceptedResultUsesSingularRowWord()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent"],
            "question asks for DataAgent documentation");

        string result = DataAgentResultExplainer.ExplainAccepted(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            1,
            "DataAgent NL2SQL Design",
            explanation);

        Assert.That(result, Does.Contain("1 row"));
        Assert.That(result, Does.Not.Contain("1 rows"));
    }

    [Test]
    public void ContextIncludesResultExplanationWhenProvided()
    {
        DataAgentPlannerExplanation explanation = new(
            "DeterministicDataAgentQueryPlanner",
            "find_dataagent_documents",
            "document_index",
            "high",
            ["dataagent"],
            "question asks for DataAgent documentation");

        string context = DataAgentContextProvider.Build(
            "Which documents describe DataAgent NL2SQL?",
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            1,
            "DataAgent NL2SQL Design",
            new DataAgentQueryResult([
                new Dictionary<string, object?> { ["path"] = "docs/a.md" }
            ]),
            explanation,
            "This query matched document_index and returned 1 row.");

        Assert.That(context, Does.Contain("result_explanation=This query matched document_index and returned 1 row."));
    }

    [Test]
    public void ExplainClarificationResultMentionsClarificationBeforeSqlAndIsSingleLine()
    {
        DataAgentClarificationRequest clarification = new(
            "Which date range should DataAgent use?\r\nChoose one.",
            ["last 7 days", "last 30 days"],
            "question does not specify a date range");

        string result = DataAgentResultExplainer.ExplainClarification(clarification);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("DataAgent needs clarification before it can run a SQL query"));
            Assert.That(result, Does.Contain("Which date range should DataAgent use?"));
            Assert.That(result, Does.Not.Contain("\r"));
            Assert.That(result, Does.Not.Contain("\n"));
        });
    }

    [Test]
    public void ServiceAcceptedAnswerIncludesResultExplanationInContext()
    {
        string databasePath = CreateDatabasePath();
        DataAgentAnswer answer = new DataAgentService(databasePath).Answer("Which documents describe DataAgent NL2SQL?");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            Assert.That(answer.Context, Does.Contain("result_explanation="));
            Assert.That(answer.Context, Does.Contain("local SQLite"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-result-explainer-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }
}
