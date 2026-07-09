using Alife.Function.DataAgent;
using System.Text.Json;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV319ReplayFixturePackTests
{
    static readonly string[] ExpectedFixtureIds =
    [
        "successful_advisory",
        "rejected_authority",
        "timeout_fallback",
        "invalid_schema"
    ];

    [Test]
    public void ReplayFixturePackContainsExpectedScenarios()
    {
        string fixtureDirectory = FixtureDirectory();

        Assert.Multiple(() =>
        {
            foreach (string fixtureId in ExpectedFixtureIds)
            {
                string path = Path.Combine(fixtureDirectory, $"{fixtureId}.json");
                Assert.That(File.Exists(path), Is.True, fixtureId);
                string json = File.ReadAllText(path);
                Assert.That(json, Does.Contain("\"replay_fixture_pack\": true"), fixtureId);
                Assert.That(json, Does.Contain("\"default_result_changed\": false"), fixtureId);
                Assert.That(json, Does.Contain("\"no_sql_authority\": true"), fixtureId);
                Assert.That(json, Does.Contain("\"fallback_required\": true"), fixtureId);
                Assert.That(json, Does.Not.Contain("SELECT"), fixtureId);
                Assert.That(json, Does.Not.Contain("hidden_context"), fixtureId);
                Assert.That(json, Does.Not.Contain("bearer"), fixtureId);
                Assert.That(json, Does.Not.Contain("secret"), fixtureId);
            }
        });
    }

    [Test]
    public void ReplayFixturesClassifyIntoExpectedShadowComparisonCategories()
    {
        Dictionary<string, string> expectedReasonCodes = new(StringComparer.Ordinal)
        {
            ["successful_advisory"] = "accepted_advisory_difference",
            ["rejected_authority"] = "rejected_authority_claim",
            ["timeout_fallback"] = "timeout_or_transport_failure",
            ["invalid_schema"] = "invalid_schema"
        };

        Assert.Multiple(() =>
        {
            foreach ((string fixtureId, string expectedReasonCode) in expectedReasonCodes)
            {
                DataAgentGraphHandshakeShadowComparison comparison = CompareFixture(fixtureId);

                Assert.That(comparison.ReasonCode, Is.EqualTo(expectedReasonCode), fixtureId);
                Assert.That(comparison.DefaultResultChanged, Is.False, fixtureId);
            }
        });
    }

    [Test]
    public void V319DocumentDeclaresReplayFixturePackBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.19-replay-fixture-pack.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("replay_fixture_pack=true"));
            Assert.That(doc, Does.Contain("successful_advisory=true"));
            Assert.That(doc, Does.Contain("rejected_authority=true"));
            Assert.That(doc, Does.Contain("timeout_fallback=true"));
            Assert.That(doc, Does.Contain("invalid_schema=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
        });
    }

    static DataAgentGraphHandshakeShadowComparison CompareFixture(string fixtureId)
    {
        string path = Path.Combine(FixtureDirectory(), $"{fixtureId}.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        DataAgentGraphHandshakeOutcome deterministic = NewOutcome(root.GetProperty("deterministic"));
        DataAgentGraphHandshakeOutcome sidecar = NewOutcome(root.GetProperty("sidecar"));

        return DataAgentGraphHandshakeShadowComparer.Compare(deterministic, sidecar);
    }

    static DataAgentGraphHandshakeOutcome NewOutcome(JsonElement element)
    {
        string statusText = element.GetProperty("status").GetString() ?? "Invalid";
        DataAgentGraphHandshakeStatus status = Enum.Parse<DataAgentGraphHandshakeStatus>(statusText);
        string reasonCode = element.GetProperty("reason_code").GetString() ?? "reason_missing";
        bool fallbackRequired = element.GetProperty("fallback_required").GetBoolean();

        return new DataAgentGraphHandshakeOutcome(
            status,
            reasonCode,
            fallbackRequired,
            Request: null,
            Response: null,
            new DataAgentGraphHandshakeValidationResult(status == DataAgentGraphHandshakeStatus.Accepted, reasonCode));
    }

    static string FixtureDirectory()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        return Path.Combine(repoRoot, "Tests", "Alife.Test.DataAgent", "Fixtures", "DataAgent", "V319Replay");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
