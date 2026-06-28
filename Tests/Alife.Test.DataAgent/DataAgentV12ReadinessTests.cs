using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV12ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesSchemaAndPlannerExplanationChecks()
    {
        string databasePath = CreateDatabasePath();

        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(checks.Single(check => check.Name == "SchemaSnapshotAvailable").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "CatalogMatchesSqliteSchema").Passed, Is.True);
            Assert.That(checks.Single(check => check.Name == "PlannerExplanationInContext").Passed, Is.True);
        });
    }

    [Test]
    public void StaticReadinessScriptDeclaresV12Markers()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("SchemaSnapshotAvailable"));
            Assert.That(script, Does.Contain("CatalogMatchesSqliteSchema"));
            Assert.That(script, Does.Contain("PlannerExplanationInContext"));
            Assert.That(script, Does.Contain("DataAgentSchemaIntrospector"));
            Assert.That(script, Does.Contain("planner_confidence"));
        });
    }

    [Test]
    public void DataAgentContextIncludesRenderedPlannerExplanationFields()
    {
        string databasePath = CreateDatabasePath();

        DataAgentAnswer answer = new DataAgentService(databasePath).Answer("Which documents describe DataAgent NL2SQL?");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Context, Does.Contain("planner_confidence="));
            Assert.That(answer.Context, Does.Contain("planner_reason="));
            Assert.That(answer.Context, Does.Contain("planner_signals="));
        });
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v12-readiness-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
