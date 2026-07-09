using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV322ManualArtifactIndexTests
{
    [Test]
    public void ManualArtifactIndexWriterWritesStableManifestForArtifact()
    {
        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create(
                "v3.22-indexed-replay",
                [
                    new DataAgentGraphHandshakeReplayInput(
                        "successful_advisory",
                        Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                        Outcome(DataAgentGraphHandshakeStatus.Accepted, "handshake_accepted", fallbackRequired: false)),
                    new DataAgentGraphHandshakeReplayInput(
                        "timeout_fallback",
                        Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                        Outcome(DataAgentGraphHandshakeStatus.Timeout, "sidecar_timeout", fallbackRequired: true))
                ]);
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v322-artifact-index");
        string artifactPath = Path.Combine(directory, "shadow-replay-report.md");
        string indexPath = Path.Combine(directory, "shadow-replay-report.index.md");
        DataAgentGraphHandshakeReplayReportArtifact artifact =
            DataAgentGraphHandshakeReplayReportArtifactWriter.Write(report, artifactPath);

        DataAgentGraphHandshakeReplayReportArtifactIndex index =
            DataAgentGraphHandshakeReplayReportArtifactIndexWriter.Write(report, artifact, indexPath);

        string text = File.ReadAllText(indexPath);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(indexPath), Is.True);
            Assert.That(index.Path, Is.EqualTo(Path.GetFullPath(indexPath)));
            Assert.That(index.ArtifactPath, Is.EqualTo(Path.GetFullPath(artifactPath)));
            Assert.That(index.ReplayId, Is.EqualTo("v3.22-indexed-replay"));
            Assert.That(index.ComparisonCount, Is.EqualTo(2));
            Assert.That(index.DefaultResultChanged, Is.False);
            Assert.That(index.ManualOnly, Is.True);
            Assert.That(index.StartsRuntime, Is.False);
            Assert.That(index.StoresSecrets, Is.False);
            Assert.That(index.StoresSql, Is.False);
            Assert.That(text, Does.Contain("manual_artifact_index=true"));
            Assert.That(text, Does.Contain("manifest_writer=true"));
            Assert.That(text, Does.Contain("manual_only=true"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Contain("replay_id=v3.22-indexed-replay"));
            Assert.That(text, Does.Contain("comparison_count=2"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("accepted_advisory_difference=1"));
            Assert.That(text, Does.Contain("timeout_or_transport_failure=1"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void ManualArtifactIndexWriterSanitizesUnsafeReplayId()
    {
        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create(
                "unsafe SELECT hidden_context bearer secret",
                []);
        DataAgentGraphHandshakeReplayReportArtifact artifact = new(
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifact.md"),
            BytesWritten: 12,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            DefaultResultChanged: false);
        string indexPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "unsafe-index.md");

        DataAgentGraphHandshakeReplayReportArtifactIndexWriter.Write(report, artifact, indexPath);
        string text = File.ReadAllText(indexPath);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("replay_id=redacted"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void V322DocumentDeclaresManualArtifactIndexBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.22-manual-artifact-index.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("manual_artifact_index=true"));
            Assert.That(doc, Does.Contain("manifest_writer=true"));
            Assert.That(doc, Does.Contain("manual_only=true"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
            Assert.That(doc, Does.Contain("stores_hidden_context=false"));
        });
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
