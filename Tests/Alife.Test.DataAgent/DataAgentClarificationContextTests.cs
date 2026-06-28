using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentClarificationContextTests
{
    [Test]
    public void BuildClarificationContextIncludesPlannerAndOptions()
    {
        DataAgentPlannerExplanation explanation = new(
            "LlmDataAgentQueryPlanner",
            "clarify_ambiguous_query",
            string.Empty,
            "low",
            ["ambiguous_time_range", "ambiguous_metric"],
            "question has no time range or metric");
        DataAgentClarificationRequest clarification = new(
            "Do you want the last 7 days, last 30 days, or all history?",
            ["last 7 days", "last 30 days", "all history"],
            "question does not specify metric or time range");

        string context = DataAgentContextProvider.BuildClarification(
            "How active has the project been recently?",
            clarification,
            explanation);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("sql_status=needs_clarification"));
            Assert.That(context, Does.Contain("planner=LlmDataAgentQueryPlanner"));
            Assert.That(context, Does.Contain("planner_confidence=low"));
            Assert.That(context, Does.Contain("planner_signals=ambiguous_time_range, ambiguous_metric"));
            Assert.That(context, Does.Contain("clarification_question=Do you want the last 7 days, last 30 days, or all history?"));
            Assert.That(context, Does.Contain("clarification_options=last 7 days, last 30 days, all history"));
        });
    }

    [Test]
    public void ServiceReturnsClarificationWithoutSqlExecution()
    {
        string databasePath = CreateDatabasePath();
        DataAgentService service = new(databasePath, new ClarifyingPlanner());

        DataAgentAnswer answer = service.Answer("How active has the project been recently?");
        IReadOnlyList<DataAgentAuditRecord> audit = new DataAgentAuditLog(databasePath).ReadAll();

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.False);
            Assert.That(answer.Dataset, Is.Empty);
            Assert.That(answer.Sql, Is.Empty);
            Assert.That(answer.RowCount, Is.EqualTo(0));
            Assert.That(answer.RejectedReason, Is.EqualTo("needs_clarification"));
            Assert.That(answer.Context, Does.Contain("sql_status=needs_clarification"));
            Assert.That(answer.Context, Does.Contain("clarification_question="));
            Assert.That(audit, Has.Count.EqualTo(1));
            Assert.That(audit[0].Validated, Is.False);
            Assert.That(audit[0].GeneratedSql, Is.Empty);
            Assert.That(audit[0].RejectedReason, Is.EqualTo("needs_clarification"));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-clarification-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }

    sealed class ClarifyingPlanner : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            return new DataAgentQueryPlanEnvelope(
                null,
                new DataAgentPlannerExplanation(
                    nameof(LlmDataAgentQueryPlanner),
                    "clarify_ambiguous_query",
                    string.Empty,
                    "low",
                    ["ambiguous_time_range", "ambiguous_metric"],
                    "question has no time range or metric"),
                new DataAgentClarificationRequest(
                    "Do you want the last 7 days, last 30 days, or all history?",
                    ["last 7 days", "last 30 days", "all history"],
                    "question does not specify metric or time range"));
        }
    }
}
