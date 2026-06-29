using System.Reflection;
using Alife.Framework;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentModuleServiceTests
{
    [Test]
    public void ModuleServiceHasModuleAttribute()
    {
        ModuleAttribute attribute = typeof(DataAgentModuleService).GetCustomAttribute<ModuleAttribute>()!;

        Assert.Multiple(() =>
        {
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.Name, Is.EqualTo("DataAgent"));
            Assert.That(attribute.DefaultCategory, Is.EqualTo("astralfox-alife/Data Analytics"));
        });
    }

    [Test]
    public void AwakeRegistersDataAgentToolHandler()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("new DataAgentToolHandler(service, Poke)"));
            Assert.That(source, Does.Contain("new XmlHandler"));
            Assert.That(source, Does.Contain("RegisterHandlerWithoutDocument(xmlHandler)"));
        });
    }

    [Test]
    public void AwakeInjectsToolBrokerPromptWithoutStaticToolDocuments()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Prompt("));
            Assert.That(source, Does.Contain("Tool Broker contract"));
            Assert.That(source, Does.Contain("Only use DataAgent XML tools when they appear in current [tool_route_context]"));
            Assert.That(source, Does.Contain("PublishAnalysisContext"));
            Assert.That(source, Does.Not.Contain("{xmlHandler.FunctionDocument()}"));
            Assert.That(source, Does.Not.Contain("{analysisXmlHandler.FunctionDocument()}"));
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
