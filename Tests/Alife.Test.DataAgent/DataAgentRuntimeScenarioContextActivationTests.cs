using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentRuntimeScenarioContextActivationTests
{
    const string EngineeringQuestion = "\u770b\u770b\u5de5\u7a0b\u95e8\u7981\u91cc\u6700\u8fd1\u5931\u8d25\u7684\u5fc5\u9700\u9879";

    [Test]
    public void AnswerBuildsScenarioContextBeforeCallingPlanner()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        RecordingPlanner planner = new(new DataAgentQueryPlan(
            "engineering_gate",
            "runtime_scenario_activation",
            ["name", "status", "required"],
            [new DataAgentFilter("required", "=", true)],
            [],
            20));
        DataAgentService service = new(
            databasePath,
            planner,
            new DataAgentScenarioContextProvider(EngineeringPackPath()));

        DataAgentAnswer answer = service.Answer(EngineeringQuestion);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            Assert.That(planner.Requests, Has.Count.EqualTo(1));
            DataAgentScenarioContext? context = planner.Requests.Single().ScenarioContext;
            Assert.That(context, Is.Not.Null);
            Assert.That(context!.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(context.CandidateFields, Does.Contain("required"));
            Assert.That(context.CandidateFields, Does.Contain("failed"));
        });
    }

    [Test]
    public void AnswerContinuesWhenScenarioPackIsUnavailable()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        RecordingPlanner planner = new(new DataAgentQueryPlan(
            "document_index",
            "runtime_scenario_pack_unavailable",
            ["path", "title"],
            [],
            [],
            20));
        string missingPack = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}-missing-pack.json");
        DataAgentService service = new(
            databasePath,
            planner,
            new DataAgentScenarioContextProvider(missingPack));

        DataAgentAnswer answer = service.Answer("Which documents describe DataAgent NL2SQL?");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            DataAgentScenarioContext? context = planner.Requests.Single().ScenarioContext;
            Assert.That(context, Is.Not.Null);
            Assert.That(context!.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonPackUnavailable));
            Assert.That(context.HasMatches, Is.False);
        });
    }

    [Test]
    public void LlmPlannerReceivesRuntimeScenarioContextFromService()
    {
        string databasePath = NewDatabasePath();
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        DataAgentLlmPlannerPrompt? capturedPrompt = null;
        LlmDataAgentQueryPlanner planner = new(
            databasePath,
            new CapturingInvalidLlmClient(prompt => capturedPrompt = prompt),
            new DeterministicDataAgentQueryPlanner());
        DataAgentService service = new(
            databasePath,
            planner,
            new DataAgentScenarioContextProvider(EngineeringPackPath()));

        DataAgentAnswer answer = service.Answer(EngineeringQuestion);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Validated, Is.True);
            Assert.That(answer.PlannerExplanation.Signals, Does.Contain("llm_invalid_output_fallback"));
            Assert.That(capturedPrompt, Is.Not.Null);
            Assert.That(capturedPrompt!.Schema, Does.Contain("Scenario context:"));
            Assert.That(capturedPrompt.Schema, Does.Contain("engineering_gate"));
            Assert.That(capturedPrompt.Schema, Does.Contain("test_run"));
            Assert.That(capturedPrompt.System, Does.Contain("Do not output SQL"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v212-runtime-scenario-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
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

    sealed class RecordingPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
    {
        public List<DataAgentQueryRequest> Requests { get; } = [];

        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            Requests.Add(request);
            return new DataAgentQueryPlanEnvelope(
                plan,
                new DataAgentPlannerExplanation(
                    nameof(RecordingPlanner),
                    plan.Intent,
                    plan.Dataset,
                    "medium",
                    ["runtime_scenario_context"],
                    "recorded runtime scenario context"));
        }
    }

    sealed class CapturingInvalidLlmClient(Action<DataAgentLlmPlannerPrompt> capturePrompt) : ILlmDataAgentPlannerClient
    {
        public string Complete(DataAgentLlmPlannerPrompt prompt)
        {
            capturePrompt(prompt);
            return """
                {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_operator","dataset":"engineering_gate","confidence":"medium","signals":["scenario"],"reason":"bad operator","select_fields":["name"],"filters":[{"field":"status","operator":"starts_with","value":"fail"}],"sorts":[],"limit":20}
                """;
        }
    }
}
