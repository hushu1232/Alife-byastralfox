using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentCapabilityProviderTests
{
    [Test]
    public void QueryProviderDeclaresAndRegistersQueryTool()
    {
        RecordingRegistrar registrar = new();
        DataAgentQueryCapabilityProvider provider = new(new DataAgentService(CreateDatabasePath()));

        provider.Register(registrar);

        Assert.Multiple(() =>
        {
            Assert.That(provider.Name, Is.EqualTo(nameof(DataAgentQueryCapabilityProvider)));
            Assert.That(provider.ToolManifests.Select(manifest => manifest.Name), Is.EqualTo(new[] { "dataagent_query" }));
            Assert.That(provider.ToolManifests.Single().StateEffect, Is.EqualTo(ToolStateEffect.ReadsData));
            Assert.That(registrar.FunctionNames, Is.EqualTo(new[] { "dataagent_query" }));
        });
    }

    [Test]
    public void AnalysisProviderDeclaresAndRegistersAnalysisTools()
    {
        RecordingRegistrar registrar = new();
        DataAgentAnalysisService analysisService = new(
            new DataAgentService(CreateDatabasePath()),
            new InMemoryDataAgentAnalysisSessionStore());
        DataAgentAnalysisCapabilityProvider provider = new(analysisService);

        provider.Register(registrar);

        Assert.Multiple(() =>
        {
            Assert.That(provider.Name, Is.EqualTo(nameof(DataAgentAnalysisCapabilityProvider)));
            Assert.That(provider.ToolManifests.Select(manifest => manifest.Name), Is.EqualTo(new[]
            {
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
            Assert.That(provider.ToolManifests.Single(manifest => manifest.Name == "dataagent_analysis_continue").Preconditions, Does.Contain(ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession));
            Assert.That(registrar.FunctionNames, Is.EqualTo(new[]
            {
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
        });
    }

    [Test]
    public void ProvidersUseSharedToolBrokerManifestSource()
    {
        DataAgentQueryCapabilityProvider query = new(new DataAgentService(CreateDatabasePath()));
        DataAgentAnalysisService analysisService = new(
            new DataAgentService(CreateDatabasePath()),
            new InMemoryDataAgentAnalysisSessionStore());
        DataAgentAnalysisCapabilityProvider analysis = new(analysisService);

        string[] providerTools = query.ToolManifests.Concat(analysis.ToolManifests)
            .Select(manifest => manifest.Name)
            .ToArray();
        string[] sharedTools = DataAgentToolCapabilityManifests.Create()
            .Select(manifest => manifest.Name)
            .ToArray();

        Assert.That(providerTools, Is.EqualTo(sharedTools));
    }

    static string CreateDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-capability-provider-tests");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);
        return databasePath;
    }

    sealed class RecordingRegistrar : IDataAgentCapabilityRegistrar
    {
        readonly List<string> functionNames = [];

        public IReadOnlyList<string> FunctionNames => functionNames;

        public void RegisterXmlHandlerWithoutStaticDocument(XmlHandler handler, params string[] plainAreas)
        {
            functionNames.AddRange(handler.Functions.Select(function => function.Name));
        }
    }
}
