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
    public void AddRejectsDuplicateToolNamesWithinProvider()
    {
        DataAgentCapabilityRegistry registry = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new FakeProvider(
                "query",
                [
                    new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query"),
                    new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query_again")
                ])))!;

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

    [Test]
    public void ReturnedCollectionsCannotPolluteRegistryState()
    {
        DataAgentCapabilityRegistry registry = new();
        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));

        IReadOnlyList<IDataAgentCapabilityProvider> providersSnapshot = registry.Providers;
        IReadOnlyList<ToolCapabilityManifest> toolManifestsSnapshot = registry.ToolManifests;

        TryAddProvider(providersSnapshot, new FakeProvider("polluted_provider", [new ToolCapabilityManifest("polluted_from_provider", ToolCapabilityDomain.DataAgent, "polluted")]));
        TryAddManifest(toolManifestsSnapshot, new ToolCapabilityManifest("polluted_tool", ToolCapabilityDomain.DataAgent, "polluted"));

        AssertRegistryState(
            registry,
            providerNames: ["query"],
            toolNames: ["dataagent_query"],
            providerCount: 1,
            toolManifestCount: 1);
    }

    [Test]
    public void FailedAddLeavesStateUnchanged()
    {
        DataAgentCapabilityRegistry registry = new();
        registry.Add(new FakeProvider("query", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]));

        AssertFailedAddLeavesStateUnchanged(
            registry,
            new FakeProvider("query", [new ToolCapabilityManifest("dataagent_other", ToolCapabilityDomain.DataAgent, "query")]));
        AssertFailedAddLeavesStateUnchanged(
            registry,
            new FakeProvider("analysis", [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "analysis")]));
        AssertFailedAddLeavesStateUnchanged(
            registry,
            new FakeProvider(" ", [new ToolCapabilityManifest("dataagent_blank_provider", ToolCapabilityDomain.DataAgent, "blank_provider")]));
        AssertFailedAddLeavesStateUnchanged(
            registry,
            new FakeProvider("blank_tool", [new ToolCapabilityManifest(" ", ToolCapabilityDomain.DataAgent, "blank_tool")]));
        AssertFailedAddLeavesStateUnchanged(
            registry,
            new FakeProvider("null_manifest_list", null));
        AssertFailedAddLeavesStateUnchanged(
            registry,
            new FakeProvider("null_manifest_entry", [null!]));
        AssertFailedAddLeavesStateUnchanged(
            registry,
            new FakeProvider(
                "duplicate_tool_inside_provider",
                [
                    new ToolCapabilityManifest("dataagent_duplicate_inside", ToolCapabilityDomain.DataAgent, "duplicate_inside"),
                    new ToolCapabilityManifest("dataagent_duplicate_inside", ToolCapabilityDomain.DataAgent, "duplicate_inside_again")
                ]));
    }

    [Test]
    public void ProviderNamesUseRegistrationSnapshotWhenProviderMutates()
    {
        MutableProvider provider = new(
            "query",
            [new ToolCapabilityManifest("dataagent_query", ToolCapabilityDomain.DataAgent, "query")]);
        DataAgentCapabilityRegistry registry = new();

        registry.Add(provider);
        provider.Name = "polluted_query";

        AssertRegistryState(
            registry,
            providerNames: ["query"],
            toolNames: ["dataagent_query"],
            providerCount: 1,
            toolManifestCount: 1);
    }

    static void TryAddProvider(IReadOnlyList<IDataAgentCapabilityProvider> providers, IDataAgentCapabilityProvider provider)
    {
        if (providers is not IList<IDataAgentCapabilityProvider> mutableProviders)
        {
            return;
        }

        try
        {
            mutableProviders.Add(provider);
        }
        catch (NotSupportedException)
        {
        }
    }

    static void TryAddManifest(IReadOnlyList<ToolCapabilityManifest> manifests, ToolCapabilityManifest manifest)
    {
        if (manifests is not IList<ToolCapabilityManifest> mutableManifests)
        {
            return;
        }

        try
        {
            mutableManifests.Add(manifest);
        }
        catch (NotSupportedException)
        {
        }
    }

    static void AssertFailedAddLeavesStateUnchanged(
        DataAgentCapabilityRegistry registry,
        IDataAgentCapabilityProvider provider)
    {
        string[] providerNames = registry.ProviderNames.ToArray();
        string[] toolNames = registry.ToolNames.ToArray();
        int providerCount = registry.Providers.Count;
        int toolManifestCount = registry.ToolManifests.Count;

        Assert.Catch(() => registry.Add(provider));

        AssertRegistryState(registry, providerNames, toolNames, providerCount, toolManifestCount);
    }

    static void AssertRegistryState(
        DataAgentCapabilityRegistry registry,
        string[] providerNames,
        string[] toolNames,
        int providerCount,
        int toolManifestCount)
    {
        Assert.Multiple(() =>
        {
            Assert.That(registry.ProviderNames, Is.EqualTo(providerNames));
            Assert.That(registry.ToolNames, Is.EqualTo(toolNames));
            Assert.That(registry.Providers, Has.Count.EqualTo(providerCount));
            Assert.That(registry.ToolManifests, Has.Count.EqualTo(toolManifestCount));
        });
    }

    sealed class FakeProvider(string name, IReadOnlyList<ToolCapabilityManifest>? toolManifests)
        : IDataAgentCapabilityProvider
    {
        public string Name => name;
        public IReadOnlyList<ToolCapabilityManifest> ToolManifests => toolManifests!;
        public void Register(IDataAgentCapabilityRegistrar registrar) { }
    }

    sealed class MutableProvider(string name, IReadOnlyList<ToolCapabilityManifest> toolManifests)
        : IDataAgentCapabilityProvider
    {
        public string Name { get; set; } = name;
        public IReadOnlyList<ToolCapabilityManifest> ToolManifests => toolManifests;
        public void Register(IDataAgentCapabilityRegistrar registrar) { }
    }
}
