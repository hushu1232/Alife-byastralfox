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
    public void AwakeRegistersDataAgentCapabilityProviders()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DataAgentCapabilityRegistry"));
            Assert.That(source, Does.Contain("DataAgentQueryCapabilityProvider"));
            Assert.That(source, Does.Contain("DataAgentAnalysisCapabilityProvider"));
            Assert.That(source, Does.Contain("DataAgentCapabilityRegistrar"));
            Assert.That(source, Does.Contain("provider.Register(registrar)"));
        });
    }

    [Test]
    public void AwakeUsesConfiguredDataAgentStoreBoundary()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("IDataAgentStore"));
            Assert.That(source, Does.Contain("DataAgentStoreFactory.Create"));
            Assert.That(source, Does.Contain("store.Initialize()"));
            Assert.That(source, Does.Contain("store.ImportFixtures()"));
            Assert.That(source, Does.Contain("new(store)"));
        });
    }

    [Test]
    public void ModuleExposesRegisteredProviderAndToolNamesForDiagnostics()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("RegisteredCapabilityProviderNames"));
            Assert.That(source, Does.Contain("RegisteredCapabilityToolNames"));
            Assert.That(source, Does.Contain("capabilityRegistry.ProviderNames"));
            Assert.That(source, Does.Contain("capabilityRegistry.ToolNames"));
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
