using Alife.Framework;
using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentModuleAnalysisRegistrationTests
{
    [Test]
    public async Task AwakeRegistersCapabilityProviderHandlersIntoFunctionCaller()
    {
        XmlFunctionCaller functionCaller = new(NullLogger<XmlFunctionCaller>.Instance);
        DataAgentModuleService service = new(functionCaller);

        await service.AwakeAsync(new AwakeContext
        {
            Character = new Character(),
            Services = new ServiceCollection().BuildServiceProvider(),
            KernelBuilder = Kernel.CreateBuilder(),
            ContextBuilder = new ChatHistoryAgentThread()
        });

        Assert.Multiple(() =>
        {
            Assert.That(functionCaller.CanHandleFunction("dataagent_query"), Is.True);
            Assert.That(functionCaller.CanHandleFunction("dataagent_analysis_start"), Is.True);
            Assert.That(functionCaller.CanHandleFunction("dataagent_analysis_continue"), Is.True);
            Assert.That(functionCaller.CanHandleFunction("dataagent_analysis_summarize"), Is.True);
            Assert.That(functionCaller.CanHandleFunction("dataagent_analysis_end"), Is.True);
            Assert.That(service.RegisteredCapabilityProviderNames, Is.EqualTo(new[]
            {
                nameof(DataAgentQueryCapabilityProvider),
                nameof(DataAgentAnalysisCapabilityProvider)
            }));
            Assert.That(service.RegisteredCapabilityToolNames, Is.EqualTo(new[]
            {
                "dataagent_query",
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
        });
    }

    [Test]
    public void CapabilityRegistrarRejectsNullFunctionService()
    {
        Assert.Throws<ArgumentNullException>(() => new DataAgentCapabilityRegistrar(null!));
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
            Assert.That(source, Does.Not.Contain("<dataagent_query"));
            Assert.That(source, Does.Not.Contain("<dataagent_analysis_start"));
            Assert.That(source, Does.Not.Contain("<dataagent_analysis_continue"));
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
