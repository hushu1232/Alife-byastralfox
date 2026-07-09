using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV321ManualReplayReportArtifactTests
{
    [Test]
    public void ManualReplayReportArtifactWriterWritesStableOfflineArtifact()
    {
        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create(
                "v3.21-manual-artifact",
                [
                    new DataAgentGraphHandshakeReplayInput(
                        "successful_advisory",
                        Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                        Outcome(DataAgentGraphHandshakeStatus.Accepted, "handshake_accepted", fallbackRequired: false))
                ]);
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v321-manual-artifact");
        string outputPath = Path.Combine(directory, "shadow-replay-report.md");

        DataAgentGraphHandshakeReplayReportArtifact artifact =
            DataAgentGraphHandshakeReplayReportArtifactWriter.Write(report, outputPath);

        string text = File.ReadAllText(outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(artifact.Path, Is.EqualTo(Path.GetFullPath(outputPath)));
            Assert.That(artifact.BytesWritten, Is.EqualTo(new FileInfo(outputPath).Length));
            Assert.That(artifact.ManualOnly, Is.True);
            Assert.That(artifact.StartsRuntime, Is.False);
            Assert.That(artifact.StoresSecrets, Is.False);
            Assert.That(artifact.StoresSql, Is.False);
            Assert.That(artifact.DefaultResultChanged, Is.False);
            Assert.That(text, Does.Contain("manual_replay_report_artifact=true"));
            Assert.That(text, Does.Contain("artifact_writer=true"));
            Assert.That(text, Does.Contain("manual_only=true"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("shadow_replay_report=true"));
            Assert.That(text, Does.Contain("fixture_successful_advisory=accepted_advisory_difference"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void ManualReplayReportArtifactWriterRejectsDirectoryTargets()
    {
        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create("v3.21-directory-target", []);
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v321-directory-target");
        Directory.CreateDirectory(directory);

        Assert.Throws<IOException>(() =>
            DataAgentGraphHandshakeReplayReportArtifactWriter.Write(report, directory));
    }

    [Test]
    public void V321DocumentDeclaresManualArtifactBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.21-manual-replay-report-artifact.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("manual_replay_report_artifact=true"));
            Assert.That(doc, Does.Contain("artifact_writer=true"));
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
