using Alife.Function.DataAgent;
using System.Text.Json;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV320ShadowReplayReportTests
{
    static readonly string[] FixtureIds =
    [
        "successful_advisory",
        "rejected_authority",
        "timeout_fallback",
        "invalid_schema"
    ];

    [Test]
    public void ShadowReplayReportConsolidatesV319FixturesIntoStableSummary()
    {
        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create(
                "v3.20-shadow-replay-report",
                FixtureIds.Select(ReadFixture));

        string markdown = DataAgentGraphHandshakeReplayReportFormatter.FormatMarkdown(report);

        Assert.Multiple(() =>
        {
            Assert.That(report.ReplayId, Is.EqualTo("v3.20-shadow-replay-report"));
            Assert.That(report.ComparisonCount, Is.EqualTo(4));
            Assert.That(report.DefaultResultChanged, Is.False);
            Assert.That(report.Passed, Is.True);
            Assert.That(report.StatusCounts["accepted_advisory_difference"], Is.EqualTo(1));
            Assert.That(report.StatusCounts["rejected_authority_claim"], Is.EqualTo(1));
            Assert.That(report.StatusCounts["timeout_or_transport_failure"], Is.EqualTo(1));
            Assert.That(report.StatusCounts["invalid_schema"], Is.EqualTo(1));
            Assert.That(markdown, Does.Contain("shadow_replay_report=true"));
            Assert.That(markdown, Does.Contain("replay_fixture_pack=true"));
            Assert.That(markdown, Does.Contain("source_fixture_pack=v3.19"));
            Assert.That(markdown, Does.Contain("shadow_only=true"));
            Assert.That(markdown, Does.Contain("default_result_changed=false"));
            Assert.That(markdown, Does.Contain("starts_runtime=false"));
            Assert.That(markdown, Does.Contain("stores_secrets=false"));
            Assert.That(markdown, Does.Contain("stores_sql=false"));
            Assert.That(markdown, Does.Contain("stores_hidden_context=false"));
            Assert.That(markdown, Does.Contain("fixture_successful_advisory=accepted_advisory_difference"));
            Assert.That(markdown, Does.Contain("fixture_rejected_authority=rejected_authority_claim"));
            Assert.That(markdown, Does.Contain("fixture_timeout_fallback=timeout_or_transport_failure"));
            Assert.That(markdown, Does.Contain("fixture_invalid_schema=invalid_schema"));
            Assert.That(markdown, Does.Not.Contain("SELECT"));
            Assert.That(markdown, Does.Not.Contain("bearer"));
            Assert.That(markdown, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void ShadowReplayReportSanitizesUnsafeFixtureIds()
    {
        DataAgentGraphHandshakeReplayInput unsafeFixture = new(
            "unsafe SELECT hidden_context bearer secret",
            Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
            Outcome(DataAgentGraphHandshakeStatus.Accepted, "handshake_accepted", fallbackRequired: false));

        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create("unsafe replay", [unsafeFixture]);

        string markdown = DataAgentGraphHandshakeReplayReportFormatter.FormatMarkdown(report);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("fixture_redacted=accepted_advisory_difference"));
            Assert.That(markdown, Does.Not.Contain("SELECT"));
            Assert.That(markdown, Does.Not.Contain("bearer"));
            Assert.That(markdown, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void V320DocumentDeclaresShadowReplayReportBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.20-shadow-replay-report.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("shadow_replay_report=true"));
            Assert.That(doc, Does.Contain("replay_fixture_pack=true"));
            Assert.That(doc, Does.Contain("source_fixture_pack=v3.19"));
            Assert.That(doc, Does.Contain("shadow_only=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
            Assert.That(doc, Does.Contain("stores_hidden_context=false"));
        });
    }

    static DataAgentGraphHandshakeReplayInput ReadFixture(string fixtureId)
    {
        string path = Path.Combine(FixtureDirectory(), $"{fixtureId}.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        return new DataAgentGraphHandshakeReplayInput(
            fixtureId,
            NewOutcome(root.GetProperty("deterministic")),
            NewOutcome(root.GetProperty("sidecar")));
    }

    static DataAgentGraphHandshakeOutcome NewOutcome(JsonElement element)
    {
        string statusText = element.GetProperty("status").GetString() ?? "Invalid";
        DataAgentGraphHandshakeStatus status = Enum.Parse<DataAgentGraphHandshakeStatus>(statusText);
        string reasonCode = element.GetProperty("reason_code").GetString() ?? "reason_missing";
        bool fallbackRequired = element.GetProperty("fallback_required").GetBoolean();

        return Outcome(status, reasonCode, fallbackRequired);
    }

    static DataAgentGraphHandshakeOutcome Outcome(
        DataAgentGraphHandshakeStatus status,
        string reasonCode,
        bool fallbackRequired)
    {
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
