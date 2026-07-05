using Alife.Function.DataAgent;
using System.Text;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV211ReadinessTests
{
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
            "看看工程门禁里最近失败的必需项");

        DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
            new DataAgentQueryRequest("看看工程门禁里最近失败的必需项", "owner", "zh-CN", false),
            catalog,
            snapshot,
            context);

        Assert.Multiple(() =>
        {
            Assert.That(context.ReasonCode, Is.EqualTo(DataAgentScenarioContext.ReasonMatched));
            Assert.That(context.CandidateDatasets, Is.EqualTo(new[] { "engineering_gate", "test_run" }));
            Assert.That(context.CandidateFields, Does.Contain("required"));
            Assert.That(context.CandidateFields, Does.Contain("failed"));
            Assert.That(context.Metrics.Select(metric => metric.Name), Is.EqualTo(new[] { "失败", "必需" }));
            Assert.That(prompt.Schema, Does.Contain("Scenario context:"));
            Assert.That(prompt.Schema, Does.Contain("Scenario context is a hint only"));
            Assert.That(prompt.System, Does.Contain("Do not output SQL"));
            Assert.That(typeof(DataAgentQueryPlanValidator).IsClass, Is.True);
            Assert.That(typeof(DataAgentSqlCompiler).IsClass, Is.True);
            Assert.That(typeof(DataAgentSqlSafetyValidator).IsClass, Is.True);
            Assert.That(typeof(DataAgentQueryExecutor).IsClass, Is.True);
        });
    }

    [Test]
    public void ScenarioDiagnosticsAreDataAgentOwnedAndOwnerSafe()
    {
        DataAgentScenarioContext context = new DataAgentScenarioContextBuilder().Build(
            DataAgentCatalog.CreateDefault(),
            LoadEngineeringPack(),
            "看看工程门禁里最近失败的必需项");

        string text = DataAgentScenarioDiagnosticsFormatter.Format(context);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("DataAgent scenario diagnostics"));
            Assert.That(text, Does.Contain("reason=scenario_context_matched"));
            Assert.That(text, Does.Contain("datasets=engineering_gate,test_run"));
            Assert.That(text, Does.Contain("metrics=失败:status!=passed;必需:required=true"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("[tool_route_context]"));
            Assert.That(text, Does.Not.Contain("[data_agent_evidence_pack]"));
            Assert.That(text, Does.Not.Contain("hidden_context"));
        });
    }

    [Test]
    public void ScenarioPackFileRemainsReadableUtf8()
    {
        string text = File.ReadAllText(EngineeringPackPath(), Encoding.UTF8);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("工程门禁"));
            Assert.That(text, Does.Contain("最近失败的测试"));
            Assert.That(text, Does.Contain("缺失项"));
            Assert.That(text, Does.Contain("文档证据"));
            Assert.That(text, Does.Contain("失败"));
            Assert.That(text, Does.Contain("必需"));
            Assert.That(text, Does.Not.Contain("宸ョ▼"));
            Assert.That(text, Does.Not.Contain("鏈€"));
            Assert.That(text, Does.Not.Contain("澶辫触"));
            Assert.That(text, Does.Not.Contain("蹇呴渶"));
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
}
