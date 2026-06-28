namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV15ReadinessTests
{
    static readonly string[] RequiredChecks =
    [
        "AnalysisToolHandlerPresent",
        "AnalysisToolsRegisteredInModule",
        "AnalysisTerminalToolsDoNotQuery",
        "ToolCapabilityManifestPresent",
        "ToolCapabilityRouterPresent",
        "ToolExecutionGatePresent"
    ];

    [Test]
    public void StaticReadinessScriptContainsAllV15ToolBrokerMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
                Assert.That(script, Does.Contain(checkName), checkName);

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityManifest.cs"));
            Assert.That(script, Does.Contain("ToolCapabilityManifest"));
            Assert.That(script, Does.Contain("ToolCapabilityDomain"));
            Assert.That(script, Does.Contain("ToolCapabilityPrecondition"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs"));
            Assert.That(script, Does.Contain("ToolCapabilityRouter"));
            Assert.That(script, Does.Contain("Route"));
            Assert.That(script, Does.Contain("dataagent_analysis_continue"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs"));
            Assert.That(script, Does.Contain("CurrentRoute"));
            Assert.That(script, Does.Contain("tool_not_allowed_in_current_route"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs"));
            Assert.That(script, Does.Contain("DataAgentAnalysisToolHandler"));
            Assert.That(script, Does.Contain("dataagent_analysis_start"));
            Assert.That(script, Does.Contain("dataagent_analysis_continue"));
            Assert.That(script, Does.Contain("dataagent_analysis_summarize"));
            Assert.That(script, Does.Contain("dataagent_analysis_end"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs"));
            Assert.That(script, Does.Contain("InMemoryDataAgentAnalysisSessionStore"));
            Assert.That(script, Does.Contain("analysisXmlHandler.FunctionDocument"));

            Assert.That(script, Does.Contain("Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs"));
            Assert.That(script, Does.Contain("SummarizeUsesAnalysisServiceAndDoesNotCallAnswerBoundary"));
            Assert.That(script, Does.Contain("EndUsesAnalysisServiceAndDoesNotCallAnswerBoundary"));
            Assert.That(script, Does.Contain("foreach ($group in @(\"Core\", \"Schema\", \"Safety\", \"Query\", \"Context\", \"Planner\", \"Tool\", \"ToolBroker\", \"Analysis\"))"));
        });
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }
}
