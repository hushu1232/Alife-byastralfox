namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentModuleAnalysisRegistrationTests
{
    [Test]
    public void AwakeRegistersQueryAndAnalysisHandlers()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("new DataAgentToolHandler(service, Poke)"));
            Assert.That(source, Does.Contain("RegisterHandlerWithoutDocument(xmlHandler)"));
            Assert.That(source, Does.Contain("new InMemoryDataAgentAnalysisSessionStore()"));
            Assert.That(source, Does.Contain("new DataAgentAnalysisService(service, analysisSessionStore)"));
            Assert.That(source, Does.Contain("new DataAgentAnalysisToolHandler(analysisService, Poke)"));
            Assert.That(source, Does.Contain("RegisterHandlerWithoutDocument(analysisXmlHandler)"));
        });
    }

    [Test]
    public void AwakePromptIncludesAnalysisToolContractAndDocument()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("dataagent_query"));
            Assert.That(source, Does.Contain("dataagent_analysis_start"));
            Assert.That(source, Does.Contain("dataagent_analysis_continue"));
            Assert.That(source, Does.Contain("dataagent_analysis_summarize"));
            Assert.That(source, Does.Contain("dataagent_analysis_end"));
            Assert.That(source, Does.Contain("Summarize and end analysis actions do not execute SQL"));
            Assert.That(source, Does.Contain("If there is no active DataAgent analysis session, do not call continue, summarize, or end."));
            Assert.That(source, Does.Contain("{analysisXmlHandler.FunctionDocument()}"));
        });
    }

    static string ReadModuleSource()
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "Sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentModuleService.cs"));
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Sources")) &&
                File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
