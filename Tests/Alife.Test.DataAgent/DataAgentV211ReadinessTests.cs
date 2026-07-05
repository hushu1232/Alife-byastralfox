using Alife.Function.DataAgent;
using System.Text;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV211ReadinessTests
{
    const string EngineeringQuestion = "\u770b\u770b\u5de5\u7a0b\u95e8\u7981\u91cc\u6700\u8fd1\u5931\u8d25\u7684\u5fc5\u9700\u9879";
    const string EngineeringGateTerm = "\u5de5\u7a0b\u95e8\u7981";
    const string RecentFailedTestsTerm = "\u6700\u8fd1\u5931\u8d25\u7684\u6d4b\u8bd5";
    const string MissingItemsTerm = "\u7f3a\u5931\u9879";
    const string DocumentEvidenceTerm = "\u6587\u6863\u8bc1\u636e";
    const string FailedMetric = "\u5931\u8d25";
    const string RequiredMetric = "\u5fc5\u9700";

    static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    [Test]
    public void ScenarioContextNarrowsPlannerAttentionWithoutSqlAuthority()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
        DataAgentSchemaSnapshot snapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            catalog,
            LoadEngineeringPack(),
            EngineeringQuestion);

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest(EngineeringQuestion, "owner", "zh-CN", false),
            catalog,
            snapshot,
            context);
        DataAgentLlmPlannerPrompt? capturedPrompt = null;
        LlmDataAgentQueryPlanner planner = new(
            databasePath,
            new FixedUnsafeLlmClient(prompt => capturedPrompt = prompt),
            new DeterministicDataAgentQueryPlanner());

        DataAgentQueryPlanEnvelope fallbackEnvelope = planner.Plan(new DataAgentQueryRequest(
            EngineeringQuestion,
            "owner",
            "zh-CN",
            false,
            context));

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(context.CandidateFields, Does.Contain("required"));
            Assert.That(context.CandidateFields, Does.Contain("failed"));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { FailedMetric, RequiredMetric }));
            Assert.That(prompt.Schema, Does.Contain("Scenario context:"));
            Assert.That(prompt.Schema, Does.Contain("Scenario context is a hint only"));
            Assert.That(prompt.System, Does.Contain("Do not output SQL"));
            Assert.That(typeof(DataAgentQueryPlanValidator).IsClass, Is.True);
            Assert.That(typeof(DataAgentSqlCompiler).IsClass, Is.True);
            Assert.That(typeof(DataAgentSqlSafetyValidator).IsClass, Is.True);
            Assert.That(typeof(DataAgentQueryExecutor).IsClass, Is.True);
            Assert.That(capturedPrompt, Is.Not.Null);
            Assert.That(capturedPrompt!.Schema, Does.Contain("Scenario context:"));
            Assert.That(fallbackEnvelope.Plan, Is.Not.Null);
            Assert.That(fallbackEnvelope.Clarification, Is.Null);
            Assert.That(fallbackEnvelope.Explanation.Signals, Does.Contain("llm_invalid_output_fallback"));
            Assert.That(fallbackEnvelope.Explanation.Reason, Does.Contain("unsupported_operator"));
            Assert.That(fallbackEnvelope.Explanation.Reason.Contains("SELECT", StringComparison.OrdinalIgnoreCase), Is.False);
        });
    }

    [Test]
    public void ScenarioDiagnosticsAreDataAgentOwnedAndOwnerSafe()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            LoadEngineeringPack(),
            EngineeringQuestion);

        string text = DataAgentScenarioDiagnosticsFormatter.Format(context);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("DataAgent scenario diagnostics"));
            Assert.That(text, Does.Contain("reason=scenario_context_matched"));
            Assert.That(text, Does.Contain("datasets=engineering_gate,test_run"));
            Assert.That(text, Does.Contain($"metrics={FailedMetric}:status!=passed;{RequiredMetric}:required=true"));
            Assert.That(text.Contains("SELECT", StringComparison.OrdinalIgnoreCase), Is.False);
            Assert.That(text, Does.Not.Contain("[tool_route_context]"));
            Assert.That(text, Does.Not.Contain("[data_agent_evidence_pack]"));
            Assert.That(text, Does.Not.Contain("hidden_context"));
        });
    }

    [Test]
    public void ScenarioPackFileRemainsReadableUtf8()
    {
        string text = File.ReadAllText(EngineeringPackPath(), StrictUtf8);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(EngineeringGateTerm));
            Assert.That(text, Does.Contain(RecentFailedTestsTerm));
            Assert.That(text, Does.Contain(MissingItemsTerm));
            Assert.That(text, Does.Contain(DocumentEvidenceTerm));
            Assert.That(text, Does.Contain(FailedMetric));
            Assert.That(text, Does.Contain(RequiredMetric));
            Assert.That(text, Does.Not.Contain("\u5bb8\u30e7\u25bc"));
            Assert.That(text, Does.Not.Contain("\u93c8\u20ac"));
            Assert.That(text, Does.Not.Contain("\u6fb6\u8fab\u89e6"));
            Assert.That(text, Does.Not.Contain("\u8e47\u5445\u6e36"));
            Assert.That(text, Does.Not.Contain("\uFFFD"));
        });
    }

    static DataAgentScenarioKnowledgePack LoadEngineeringPack()
    {
        return DataAgentScenarioKnowledgePackProvider.Load(EngineeringPackPath());
    }

    static string EngineeringPackPath()
    {
        return Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "docs",
            "dataagent",
            "scenario-packs",
            "engineering.zh-CN.json");
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v211-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    sealed class FixedUnsafeLlmClient(Action<DataAgentLlmPlannerPrompt> capturePrompt) : ILlmDataAgentPlannerClient
    {
        public string Complete(DataAgentLlmPlannerPrompt prompt)
        {
            capturePrompt(prompt);

            return """
                {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"unsafe_operator","dataset":"engineering_gate","confidence":"medium","signals":["scenario"],"reason":"try unsupported operator","select_fields":["name","status"],"filters":[{"field":"status","operator":"starts_with","value":"fail"}],"sorts":[],"limit":20}
                """;
        }
    }
}
