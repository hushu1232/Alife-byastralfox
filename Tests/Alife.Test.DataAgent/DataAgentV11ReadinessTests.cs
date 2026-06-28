using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV11ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesPlannerAndToolChecks()
    {
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(NewDatabasePath());
        string[] names = checks.Select(check => check.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(checks, Has.Count.EqualTo(22));
            Assert.That(checks.All(check => check.Passed), Is.True, string.Join(Environment.NewLine, checks.Select(check => $"{check.Name}:{check.Detail}")));
            Assert.That(names, Does.Contain("PlannerInterfacePresent"));
            Assert.That(names, Does.Contain("DeterministicPlannerPassesFixtures"));
            Assert.That(names, Does.Contain("ServiceUsesInjectedPlanner"));
            Assert.That(names, Does.Contain("UnsafePlannerOutputRejected"));
            Assert.That(names, Does.Contain("ToolHandlerReturnsDataAgentContext"));
        });
    }

    [Test]
    public void ReadinessScriptPrintsV11Markers()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("IDataAgentQueryPlanner"));
            Assert.That(script, Does.Contain("DeterministicDataAgentQueryPlanner"));
            Assert.That(script, Does.Contain("DataAgentToolHandler"));
            Assert.That(script, Does.Contain("DataAgentModuleService"));
            Assert.That(script, Does.Contain("dataagent_query"));
        });
    }

    [Test]
    public void EngineeringMapDeclaresPlannerToolIntegrationAsRequired()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "check-qchat-engineering-map.ps1"));
        string declaration = FindAddCheckDeclaration(script, "DataAgent planner/tool integration");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("PlannerInterfacePresent"));
            Assert.That(declaration, Does.Contain("ToolHandlerReturnsDataAgentContext"));
            Assert.That(declaration, Does.Contain("dataagent_query"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v11-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string FindAddCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("Add-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("Add-Check", nameIndex + marker.Length, StringComparison.Ordinal);
        return next < 0
            ? script[start..]
            : script[start..next];
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
