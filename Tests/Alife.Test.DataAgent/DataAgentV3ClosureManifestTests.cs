using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed partial class DataAgentV3ClosureManifestTests
{
    [Test]
    public void ClosureLedgerContainsEveryV3MilestoneExactlyOnce()
    {
        string ledger = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "dataagent", "dataagent-v3-closure-ledger.md"));
        string[] versions = ledger.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("milestone=v3.", StringComparison.Ordinal))
            .Select(line => line["milestone=".Length..])
            .ToArray();
        string[] expected = Enumerable.Range(0, 29).Select(index => $"v3.{index}").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(versions, Is.EquivalentTo(expected));
            Assert.That(versions, Is.Unique);
            Assert.That(ledger, Does.Contain("v3.5 | RegressionHardening"));
            Assert.That(ledger, Does.Contain("v3.7 | RegressionHardening"));
            Assert.That(ledger, Does.Contain("v3.10 | StaticReadiness"));
            Assert.That(ledger, Does.Contain("v3.28 | FinalFreeze"));
        });
    }

    static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
