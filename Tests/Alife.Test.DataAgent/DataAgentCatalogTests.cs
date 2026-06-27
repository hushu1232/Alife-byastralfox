namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentCatalogTests
{
    [Test]
    public void DefaultCatalogContainsEngineeringDatasets()
    {
        Alife.Function.DataAgent.DataAgentCatalog catalog =
            Alife.Function.DataAgent.DataAgentCatalog.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.HasDataset("engineering_gate"), Is.True);
            Assert.That(catalog.HasDataset("runtime_readiness_check"), Is.True);
            Assert.That(catalog.HasDataset("module_capability"), Is.True);
            Assert.That(catalog.HasDataset("test_run"), Is.True);
            Assert.That(catalog.HasDataset("document_index"), Is.True);
            Assert.That(catalog.HasDataset("query_audit"), Is.True);
        });
    }
}
