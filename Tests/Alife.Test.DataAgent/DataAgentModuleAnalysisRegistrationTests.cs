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
            Assert.That(source, Does.Contain("new DataAgentAnalysisToolHandler(analysisService, PublishAnalysisContext)"));
            Assert.That(source, Does.Contain("RegisterHandlerWithoutDocument(analysisXmlHandler)"));
        });
    }

    [Test]
    public void AwakePromptDefersAnalysisToolDocumentsToToolBrokerRoute()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("PublishAnalysisContext"));
            Assert.That(source, Does.Contain("UpdateDataAgentAnalysisRouteSessionFromContext"));
            Assert.That(source, Does.Contain("Only use DataAgent XML tools when they appear in current [tool_route_context]"));
            Assert.That(source, Does.Not.Contain("{xmlHandler.FunctionDocument()}"));
            Assert.That(source, Does.Not.Contain("{analysisXmlHandler.FunctionDocument()}"));
        });
    }

    static string ReadModuleSource()
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentModuleService.cs"));
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "sources")) &&
                File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
