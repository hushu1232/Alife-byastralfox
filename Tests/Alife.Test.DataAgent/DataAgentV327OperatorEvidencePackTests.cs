using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV327OperatorEvidencePackTests
{
    [Test]
    public void OperatorEvidencePackSummarizesManualEvidenceChainWithoutRuntimeAuthority()
    {
        DataAgentGraphHandshakeReplayReport report = NewReplayReport();
        DataAgentGraphHandshakeReplayReportArtifact artifact = NewArtifact();
        DataAgentGraphHandshakeReplayReportArtifactIndex index = NewIndex(report, artifact);
        DataAgentGraphHandshakeManualAuditBundle bundle = NewBundle(report, artifact, index);
        DataAgentLangGraphManualShadowResult advisory = AcceptedAdvisory("timeout_or_transport_failure");
        DataAgentHarnessReplayDiffGateResult diffGate =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(report, advisory));

        DataAgentOperatorEvidencePack pack =
            DataAgentOperatorEvidencePackBuilder.Build(report, artifact, index, bundle, advisory, diffGate);

        Assert.Multiple(() =>
        {
            Assert.That(pack.PackId, Is.EqualTo("v3.27-operator-evidence-pack"));
            Assert.That(pack.ReplayId, Is.EqualTo("v3.27-operator-evidence-pack"));
            Assert.That(pack.ComparisonCount, Is.EqualTo(1));
            Assert.That(pack.EvidenceItemCount, Is.EqualTo(5));
            Assert.That(pack.GatePassed, Is.True);
            Assert.That(pack.GateReasonCode, Is.EqualTo("harness_replay_diff_gate_passed"));
            Assert.That(pack.AdvisoryAccepted, Is.True);
            Assert.That(pack.AdvisoryReasonCode, Is.EqualTo("timeout_or_transport_failure"));
            Assert.That(pack.FallbackRequired, Is.False);
            Assert.That(pack.OperatorRequired, Is.False);
            Assert.That(pack.ManualOnly, Is.True);
            Assert.That(pack.AgentAdvisoryOnly, Is.True);
            Assert.That(pack.HarnessExecutionAuthority, Is.True);
            Assert.That(pack.CSharpValidationAuthority, Is.True);
            Assert.That(pack.OperatorDecides, Is.True);
            Assert.That(pack.DefaultResultChanged, Is.False);
            Assert.That(pack.StartsRuntime, Is.False);
            Assert.That(pack.InstallsDependencies, Is.False);
            Assert.That(pack.CallsSidecar, Is.False);
            Assert.That(pack.StoresSecrets, Is.False);
            Assert.That(pack.StoresSql, Is.False);
            Assert.That(pack.StoresHiddenContext, Is.False);
        });
    }

    [Test]
    public void OperatorEvidencePackPreservesFallbackAndOperatorRequiredFromDiffGate()
    {
        DataAgentGraphHandshakeReplayReport report = NewReplayReport();
        DataAgentLangGraphManualShadowResult rejected = RejectedAdvisory();
        DataAgentHarnessReplayDiffGateResult diffGate =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(report, rejected));

        DataAgentOperatorEvidencePack pack =
            DataAgentOperatorEvidencePackBuilder.Build(report, NewArtifact(), NewIndex(report, NewArtifact()), NewBundle(report), rejected, diffGate);

        Assert.Multiple(() =>
        {
            Assert.That(pack.GatePassed, Is.False);
            Assert.That(pack.GateReasonCode, Is.EqualTo("advisory_forbidden_authority_claimed"));
            Assert.That(pack.AdvisoryAccepted, Is.False);
            Assert.That(pack.AdvisoryReasonCode, Is.EqualTo("advisory_forbidden_authority_claimed"));
            Assert.That(pack.FallbackRequired, Is.True);
            Assert.That(pack.OperatorRequired, Is.True);
            Assert.That(pack.DefaultResultChanged, Is.False);
            Assert.That(pack.StartsRuntime, Is.False);
            Assert.That(pack.CallsSidecar, Is.False);
        });
    }

    [Test]
    public void OperatorEvidencePackFormatsCompactSafePacket()
    {
        DataAgentGraphHandshakeReplayReport report = NewReplayReport();
        DataAgentGraphHandshakeReplayReportArtifact artifact = NewArtifact();
        DataAgentGraphHandshakeReplayReportArtifactIndex index = NewIndex(report, artifact);
        DataAgentGraphHandshakeManualAuditBundle bundle = NewBundle(report, artifact, index);
        DataAgentLangGraphManualShadowResult advisory = AcceptedAdvisory("timeout_or_transport_failure");
        DataAgentHarnessReplayDiffGateResult diffGate =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(report, advisory));
        DataAgentOperatorEvidencePack pack =
            DataAgentOperatorEvidencePackBuilder.Build(report, artifact, index, bundle, advisory, diffGate);

        string text = DataAgentOperatorEvidencePackFormatter.Format(pack);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("operator_evidence_pack=true"));
            Assert.That(text, Does.Contain("source_versions=v3.18-v3.26"));
            Assert.That(text, Does.Contain("manual_audit_bundle=true"));
            Assert.That(text, Does.Contain("agent_advisory_contract=v3.24"));
            Assert.That(text, Does.Contain("real_langgraph_manual_shadow_provider=true"));
            Assert.That(text, Does.Contain("harness_replay_diff_gate=true"));
            Assert.That(text, Does.Contain("gate_passed=true"));
            Assert.That(text, Does.Contain("operator_decides=true"));
            Assert.That(text, Does.Contain("agent_advisory_only=true"));
            Assert.That(text, Does.Contain("harness_execution_authority=true"));
            Assert.That(text, Does.Contain("csharp_validation_authority=true"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("manual_only=true"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("calls_sidecar=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Contain("replay_report_artifact_path=shadow-replay-report.md"));
            Assert.That(text, Does.Contain("artifact_index_path=shadow-replay-report.index.md"));
            Assert.That(text, Does.Contain("manual_audit_bundle_path=manual-audit-bundle.md"));
            Assert.That(text, Does.Not.Contain(TestContext.CurrentContext.WorkDirectory));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void OperatorEvidencePackRedactsUnsafeTokens()
    {
        DataAgentGraphHandshakeReplayReport report = NewReplayReport("unsafe SELECT hidden_context bearer secret");
        DataAgentGraphHandshakeReplayReportArtifact artifact = NewArtifact("unsafe SELECT artifact.md");
        DataAgentGraphHandshakeReplayReportArtifactIndex index = NewIndex(report, artifact, "unsafe bearer index.md");
        DataAgentGraphHandshakeManualAuditBundle bundle = NewBundle(report, artifact, index, "unsafe hidden_context bundle.md");
        DataAgentLangGraphManualShadowResult advisory = AcceptedAdvisory("timeout_or_transport_failure");
        DataAgentHarnessReplayDiffGateResult diffGate =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(report, advisory));
        DataAgentOperatorEvidencePack pack =
            DataAgentOperatorEvidencePackBuilder.Build(report, artifact, index, bundle, advisory, diffGate);

        string text = DataAgentOperatorEvidencePackFormatter.Format(pack);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("replay_id=redacted"));
            Assert.That(text, Does.Contain("replay_report_artifact_path=redacted"));
            Assert.That(text, Does.Contain("artifact_index_path=redacted"));
            Assert.That(text, Does.Contain("manual_audit_bundle_path=redacted"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void V327DocumentDeclaresOperatorEvidencePackBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.27-operator-evidence-pack.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("operator_evidence_pack=true"));
            Assert.That(doc, Does.Contain("source_versions=v3.18-v3.26"));
            Assert.That(doc, Does.Contain("manual_audit_bundle=true"));
            Assert.That(doc, Does.Contain("agent_advisory_contract=v3.24"));
            Assert.That(doc, Does.Contain("real_langgraph_manual_shadow_provider=true"));
            Assert.That(doc, Does.Contain("harness_replay_diff_gate=true"));
            Assert.That(doc, Does.Contain("operator_decides=true"));
            Assert.That(doc, Does.Contain("agent_advisory_only=true"));
            Assert.That(doc, Does.Contain("harness_execution_authority=true"));
            Assert.That(doc, Does.Contain("csharp_validation_authority=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("manual_only=true"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("calls_sidecar=false"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
            Assert.That(doc, Does.Contain("stores_hidden_context=false"));
        });
    }

    static DataAgentGraphHandshakeReplayReport NewReplayReport(string replayId = "v3.27-operator-evidence-pack")
    {
        DataAgentGraphHandshakeShadowComparison comparison = new(
            DataAgentGraphHandshakeShadowComparisonStatus.TimeoutOrTransportFailure,
            "timeout_or_transport_failure",
            "sidecar_disabled",
            "timeout",
            DataAgentGraphHandshakeStatus.Disabled,
            DataAgentGraphHandshakeStatus.Timeout,
            DeterministicFallbackRequired: true,
            SidecarFallbackRequired: true,
            DefaultResultChanged: false);

        return new DataAgentGraphHandshakeReplayReport(
            replayId,
            [new DataAgentGraphHandshakeReplayFixtureResult("timeout_fallback", comparison)],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["timeout_or_transport_failure"] = 1
            },
            ComparisonCount: 1,
            DefaultResultChanged: false,
            Passed: true);
    }

    static DataAgentGraphHandshakeReplayReportArtifact NewArtifact(string fileName = "shadow-replay-report.md")
    {
        return new DataAgentGraphHandshakeReplayReportArtifact(
            Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName),
            BytesWritten: 12,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            DefaultResultChanged: false);
    }

    static DataAgentGraphHandshakeReplayReportArtifactIndex NewIndex(
        DataAgentGraphHandshakeReplayReport report,
        DataAgentGraphHandshakeReplayReportArtifact artifact,
        string fileName = "shadow-replay-report.index.md")
    {
        return new DataAgentGraphHandshakeReplayReportArtifactIndex(
            Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName),
            artifact.Path,
            report.ReplayId,
            report.ComparisonCount,
            report.DefaultResultChanged,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }

    static DataAgentGraphHandshakeManualAuditBundle NewBundle(
        DataAgentGraphHandshakeReplayReport? report = null,
        DataAgentGraphHandshakeReplayReportArtifact? artifact = null,
        DataAgentGraphHandshakeReplayReportArtifactIndex? index = null,
        string fileName = "manual-audit-bundle.md")
    {
        report ??= NewReplayReport();
        artifact ??= NewArtifact();
        index ??= NewIndex(report, artifact);

        return new DataAgentGraphHandshakeManualAuditBundle(
            Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName),
            artifact.Path,
            index.Path,
            report.ReplayId,
            report.ComparisonCount,
            EvidenceItemCount: 5,
            report.DefaultResultChanged,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }

    static DataAgentLangGraphManualShadowResult AcceptedAdvisory(string reasonCode)
    {
        return DataAgentLangGraphManualShadowProvider.Evaluate(NewRequest(reasonCode), NewPayload(NewResponse(reasonCode)));
    }

    static DataAgentLangGraphManualShadowResult RejectedAdvisory()
    {
        DataAgentAgentAdvisoryResponse response = NewResponse("timeout_or_transport_failure") with
        {
            ForbiddenAuthorityClaims = ["execute_sql"],
            RequestsExecution = true
        };
        return DataAgentLangGraphManualShadowProvider.Evaluate(NewRequest("timeout_or_transport_failure"), NewPayload(response));
    }

    static DataAgentAgentAdvisoryRequest NewRequest(string reasonCode)
    {
        return new DataAgentAgentAdvisoryRequest(
            ContractVersion: "v3.24",
            RunId: "v3.27-operator-evidence-pack",
            Task: "aggregate manual evidence for operator review",
            CurrentState: "manual evidence chain captured",
            AllowedAdvisoryActions: ["explain_failure", "propose_manual_check", "summarize_artifact"],
            ForbiddenAuthorities: ["start_runtime", "execute_sql", "write_state", "publish_visible_answer", "override_readiness"],
            LastSuccessfulStep: "manual_shadow_capture",
            FailureCategory: reasonCode,
            EvidenceRefs: ["replay_report:v3.20-shadow-replay-report"],
            ArtifactIndexToken: "v3.23-manual-audit-bundle",
            ExpectedResponseSchema: "advisory_id,summary,reason_code,confidence,evidence_refs,proposed_next_steps,forbidden_authority_claims,requires_operator_action",
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false);
    }

    static DataAgentAgentAdvisoryResponse NewResponse(string reasonCode)
    {
        return new DataAgentAgentAdvisoryResponse(
            AdvisoryId: "lg-manual-v327",
            Summary: "manual LangGraph advisory matches replay evidence category",
            ReasonCode: reasonCode,
            Confidence: 0.79,
            EvidenceRefs: ["replay_report:v3.20-shadow-replay-report"],
            ProposedNextSteps: ["inspect_replay_diff"],
            ForbiddenAuthorityClaims: [],
            RequiresOperatorAction: true,
            RequestsExecution: false,
            RequestsStateWrite: false,
            RequestsVisibleText: false,
            DefaultResultChanged: false);
    }

    static DataAgentLangGraphManualShadowPayload NewPayload(DataAgentAgentAdvisoryResponse response)
    {
        return new DataAgentLangGraphManualShadowPayload(
            ProviderName: "langgraph",
            CapturedByOperator: true,
            RuntimeStartedByAlife: false,
            DependenciesInstalledByAlife: false,
            SidecarCalledByAlife: false,
            Advisory: response);
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
