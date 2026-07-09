using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV323ManualAuditBundleTests
{
    [Test]
    public void ManualAuditBundleWriterWritesStableBundleManifest()
    {
        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create(
                "v3.23-audit-bundle",
                [
                    new DataAgentGraphHandshakeReplayInput(
                        "successful_advisory",
                        Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                        Outcome(DataAgentGraphHandshakeStatus.Accepted, "handshake_accepted", fallbackRequired: false)),
                    new DataAgentGraphHandshakeReplayInput(
                        "rejected_authority",
                        Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                        Outcome(DataAgentGraphHandshakeStatus.Rejected, "sql_authority_requested", fallbackRequired: true)),
                    new DataAgentGraphHandshakeReplayInput(
                        "timeout_fallback",
                        Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                        Outcome(DataAgentGraphHandshakeStatus.Timeout, "sidecar_timeout", fallbackRequired: true))
                ]);
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v323-audit-bundle");
        string artifactPath = Path.Combine(directory, "shadow-replay-report.md");
        string indexPath = Path.Combine(directory, "shadow-replay-report.index.md");
        string bundlePath = Path.Combine(directory, "manual-audit-bundle.md");
        DataAgentGraphHandshakeReplayReportArtifact artifact =
            DataAgentGraphHandshakeReplayReportArtifactWriter.Write(report, artifactPath);
        DataAgentGraphHandshakeReplayReportArtifactIndex index =
            DataAgentGraphHandshakeReplayReportArtifactIndexWriter.Write(report, artifact, indexPath);

        DataAgentGraphHandshakeManualAuditBundle bundle =
            DataAgentGraphHandshakeManualAuditBundleWriter.Write(report, artifact, index, bundlePath);

        string text = File.ReadAllText(bundlePath);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(bundlePath), Is.True);
            Assert.That(bundle.Path, Is.EqualTo(Path.GetFullPath(bundlePath)));
            Assert.That(bundle.ReplayReportArtifactPath, Is.EqualTo(Path.GetFullPath(artifactPath)));
            Assert.That(bundle.ArtifactIndexPath, Is.EqualTo(Path.GetFullPath(indexPath)));
            Assert.That(bundle.ReplayId, Is.EqualTo("v3.23-audit-bundle"));
            Assert.That(bundle.ComparisonCount, Is.EqualTo(3));
            Assert.That(bundle.EvidenceItemCount, Is.EqualTo(5));
            Assert.That(bundle.DefaultResultChanged, Is.False);
            Assert.That(bundle.ManualOnly, Is.True);
            Assert.That(bundle.StartsRuntime, Is.False);
            Assert.That(bundle.StoresSecrets, Is.False);
            Assert.That(bundle.StoresSql, Is.False);
            Assert.That(bundle.StoresHiddenContext, Is.False);
            Assert.That(text, Does.Contain("manual_audit_bundle=true"));
            Assert.That(text, Does.Contain("bundle_writer=true"));
            Assert.That(text, Does.Contain("source_versions=v3.18-v3.22"));
            Assert.That(text, Does.Contain("manual_only=true"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("replay_id=v3.23-audit-bundle"));
            Assert.That(text, Does.Contain("comparison_count=3"));
            Assert.That(text, Does.Contain("evidence_item_count=5"));
            Assert.That(text, Does.Contain("includes_smoke_result_artifact=true"));
            Assert.That(text, Does.Contain("includes_replay_fixture_pack=true"));
            Assert.That(text, Does.Contain("includes_shadow_replay_report=true"));
            Assert.That(text, Does.Contain("includes_manual_replay_report_artifact=true"));
            Assert.That(text, Does.Contain("includes_manual_artifact_index=true"));
            Assert.That(text, Does.Contain("replay_report_artifact_path=shadow-replay-report.md"));
            Assert.That(text, Does.Contain("artifact_index_path=shadow-replay-report.index.md"));
            Assert.That(text, Does.Contain("accepted_advisory_difference=1"));
            Assert.That(text, Does.Contain("rejected_authority_claim=1"));
            Assert.That(text, Does.Contain("timeout_or_transport_failure=1"));
            Assert.That(text, Does.Not.Contain(TestContext.CurrentContext.WorkDirectory));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void ManualAuditBundleWriterSanitizesUnsafeReplayIdAndPathTokens()
    {
        DataAgentGraphHandshakeReplayReport report =
            DataAgentGraphHandshakeReplayReportConsolidator.Create(
                "unsafe SELECT hidden_context bearer secret",
                []);
        DataAgentGraphHandshakeReplayReportArtifact artifact = new(
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "unsafe SELECT artifact.md"),
            BytesWritten: 12,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            DefaultResultChanged: false);
        DataAgentGraphHandshakeReplayReportArtifactIndex index = new(
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "unsafe bearer index.md"),
            artifact.Path,
            report.ReplayId,
            ComparisonCount: 0,
            DefaultResultChanged: false,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
        string bundlePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "unsafe-bundle.md");

        DataAgentGraphHandshakeManualAuditBundleWriter.Write(report, artifact, index, bundlePath);
        string text = File.ReadAllText(bundlePath);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("replay_id=redacted"));
            Assert.That(text, Does.Contain("replay_report_artifact_path=redacted"));
            Assert.That(text, Does.Contain("artifact_index_path=redacted"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void V323DocumentDeclaresManualAuditBundleBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.23-manual-audit-bundle.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("manual_audit_bundle=true"));
            Assert.That(doc, Does.Contain("bundle_writer=true"));
            Assert.That(doc, Does.Contain("source_versions=v3.18-v3.22"));
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
