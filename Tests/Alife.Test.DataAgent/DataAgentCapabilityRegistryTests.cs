using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentCapabilityRegistryTests
{
    [Test]
    public void AddStoresProvidersInRegistrationOrder()
    {
        DataAgentCapabilityRegistry registry = new();

        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));
        registry.Add(new FakeProvider("analysis", [new ToolCapabilityManifest("dataagent_analysis_start", ToolCapabilityDomain.DataAgent, "analysis_start")]));

        Assert.Multiple(() =>
        {
            Assert.That(registry.ProviderNames, Is.EqualTo(new[] { "query", "analysis" }));
            Assert.That(registry.ToolNames, Is.EqualTo(new[] { "dataagent_query", "dataagent_analysis_start" }));
            Assert.That(registry.ToolManifests.Select(manifest => manifest.Intent), Is.EqualTo(new[] { "query", "analysis_start" }));
        });
    }

    [Test]
    public void AddRejectsDuplicateProviderNames()
    {
        DataAgentCapabilityRegistry registry = new();
        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_other", ToolCapabilityDomain.DataAgent, "query")])))!;

        Assert.That(exception.Message, Does.Contain("Duplicate DataAgent capability provider"));
    }

    [Test]
    public void AddRejectsDuplicateToolNames()
    {
        DataAgentCapabilityRegistry registry = new();
        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new FakeProvider("analysis", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "analysis")])))!;

        Assert.That(exception.Message, Does.Contain("Duplicate DataAgent tool capability"));
    }

    [Test]
    public void AddRejectsBlankProviderAndToolNames()
    {
        DataAgentCapabilityRegistry registry = new();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() =>
                registry.Add(new FakeProvider(" ", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")])));
            Assert.Throws<ArgumentException>(() =>
                registry.Add(new FakeProvider("query", [new ToolCapabilityManifest(" ", ToolCapabilityDomain.DataAgent, "query")])));
        });
    }

    sealed class FakeProvider(string name, IReadOnlyList<ToolCapabilityManifest> toolManifests)
        : IDataAgentCapabilityProvider
    {
        public string Name => name;
        public IReadOnlyList<ToolCapabilityManifest> ToolManifests => toolManifests;
        public void Register(IDataAgentCapabilityRegistrar registrar) { }
    }
}
