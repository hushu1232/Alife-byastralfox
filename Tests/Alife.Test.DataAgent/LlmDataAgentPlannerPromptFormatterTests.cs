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
            Assert.That(prompt.Contract, Does.Contain("\"planner_name\":\"LlmDataAgentQueryPlanner\""));
            Assert.That(prompt.Contract, Does.Contain("\"select_fields\""));
            Assert.That(prompt.Contract, Does.Contain("\"sorts\""));
            Assert.That(prompt.Contract, Does.Contain("\"clarification_question\""));
            Assert.That(prompt.Contract, Does.Contain("\"clarification_options\""));
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

    [Test]
    public void FormatIncludesScenarioContextWhenMatched()
    {
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = CreateSchemaSnapshot();
        DataAgentScenarioContext context = CreateMatchedScenarioContext(catalog);

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Show required failed engineering gates.", "developer", "en-US", false),
            catalog,
            snapshot,
            context);

        Assert.Multiple(() =>
        {
            Assert.That(prompt.Schema, Does.Contain("Scenario context:"));
            Assert.That(prompt.Schema, Does.Contain("scenario: engineering_readiness"));
            Assert.That(prompt.Schema, Does.Contain("reason_code: scenario_context_matched"));
            Assert.That(prompt.Schema, Does.Contain("candidate_datasets: engineering_gate, test_run"));
            Assert.That(prompt.Schema, Does.Contain("candidate_fields: name, status, required, evidence_path, suite_name, failed"));
            Assert.That(prompt.Schema, Does.Contain("\u5de5\u7a0b\u95e8\u7981 -> engineering_gate(name,status,required,evidence_path)"));
            Assert.That(prompt.Schema, Does.Contain("\u5931\u8d25: status != passed"));
            Assert.That(prompt.Schema, Does.Contain("\u5fc5\u9700: required = true"));
            Assert.That(prompt.Schema, Does.Contain("Scenario context is a hint only; use only approved schema fields and operators."));
            Assert.That(prompt.Schema, Does.Contain("Do not output SQL."));
            Assert.That(prompt.Contract, Does.Not.Contain("Scenario context:"));
        });
    }

    [Test]
    public void FormatOmitsScenarioContextWhenNoMatch()
    {
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = CreateSchemaSnapshot();
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            catalog,
            CreateEngineeringPack(),
            "unmatched question");

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
            catalog,
            snapshot,
            context);

        Assert.That(prompt.Schema, Does.Not.Contain("Scenario context:"));
    }

    [Test]
    public void FormatDoesNotEmitRawSqlInScenarioContext()
    {
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = CreateSchemaSnapshot();
        DataAgentScenarioContext context = new(
            "engineering_readiness\r\nSELECT * FROM query_audit;",
            "zh-CN",
            [
                new DataAgentScenarioTermMatch(
                    "\u5de5\u7a0b\u95e8\u7981; DROP TABLE engineering_gate",
                    "engineering_gate",
                    ["name", "status; DELETE FROM query_audit"],
                    "SELECT")
            ],
            [
                new DataAgentScenarioMetricMatch(
                    "\u5931\u8d25; DELETE FROM engineering_gate",
                    "status",
                    "!=",
                    "passed; DROP TABLE test_run")
            ],
            ["engineering_gate; SELECT * FROM engineering_gate"],
            ["name; DROP TABLE engineering_gate"],
            DataAgentScenarioContext.ReasonMatched);

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("Show required failed engineering gates.", "developer", "en-US", false),
            catalog,
            snapshot,
            context);

        string scenarioSection = ExtractScenarioSection(prompt.Schema);
        string scenarioDynamicText = RemoveScenarioSafetyText(scenarioSection);

        Assert.Multiple(() =>
        {
            Assert.That(scenarioSection, Does.Not.Contain("SELECT").IgnoreCase);
            Assert.That(scenarioSection, Does.Not.Contain("DELETE").IgnoreCase);
            Assert.That(scenarioSection, Does.Not.Contain("DROP").IgnoreCase);
            Assert.That(scenarioDynamicText, Does.Not.Contain(";"));
            Assert.That(scenarioSection, Does.Not.Contain("\nSELECT"));
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

    static DataAgentSchemaSnapshot CreateSchemaSnapshot()
    {
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        string databasePath = CreateDatabasePath();

        return new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
    }

    static DataAgentScenarioContext CreateMatchedScenarioContext(DataAgentCatalog catalog)
    {
        return new DataAgentScenarioContextBuilder().Build(
            catalog,
            CreateEngineeringPack(),
            "\u5de5\u7a0b\u95e8\u7981 \u6700\u8fd1\u5931\u8d25\u7684\u6d4b\u8bd5 \u5931\u8d25 \u5fc5\u9700");
    }

    static DataAgentScenarioKnowledgePack CreateEngineeringPack()
    {
        return new DataAgentScenarioKnowledgePack(
            "engineering_readiness",
            "zh-CN",
            [
                new DataAgentScenarioTerm(
                    "\u5de5\u7a0b\u95e8\u7981",
                    [],
                    "engineering_gate",
                    ["name", "status", "required", "evidence_path"]),
                new DataAgentScenarioTerm(
                    "\u6700\u8fd1\u5931\u8d25\u7684\u6d4b\u8bd5",
                    [],
                    "test_run",
                    ["suite_name", "failed"])
            ],
            [
                new DataAgentScenarioMetric("\u5931\u8d25", "status", "!=", "passed"),
                new DataAgentScenarioMetric("\u5fc5\u9700", "required", "=", true)
            ]);
    }

    static string ExtractScenarioSection(string schema)
    {
        int start = schema.IndexOf("Scenario context:", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));

        return schema[start..];
    }

    static string RemoveScenarioSafetyText(string scenarioSection)
    {
        return scenarioSection
            .Replace(
                "Scenario context is a hint only; use only approved schema fields and operators.",
                string.Empty,
                StringComparison.Ordinal)
            .Replace("Do not output SQL.", string.Empty, StringComparison.Ordinal);
    }
}
