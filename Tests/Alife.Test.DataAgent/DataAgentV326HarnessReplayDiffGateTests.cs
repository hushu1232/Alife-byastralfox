using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV326HarnessReplayDiffGateTests
{
    [Test]
    public void DiffGatePassesAcceptedAdvisoryWhenReplayEvidenceContainsReason()
    {
        DataAgentGraphHandshakeReplayReport report = NewReplayReport();
        DataAgentLangGraphManualShadowResult advisory = AcceptedAdvisory("timeout_or_transport_failure");

        DataAgentHarnessReplayDiffGateResult result =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(report, advisory));

        Assert.Multiple(() =>
        {
            Assert.That(result.GatePassed, Is.True, result.ReasonCode);
            Assert.That(result.ReasonCode, Is.EqualTo("harness_replay_diff_gate_passed"));
            Assert.That(result.ReplayEvidenceMatched, Is.True);
            Assert.That(result.AdvisoryReasonMatched, Is.True);
            Assert.That(result.FallbackRequired, Is.False);
            Assert.That(result.OperatorRequired, Is.False);
            Assert.That(result.AgentAdvisoryOnly, Is.True);
            Assert.That(result.HarnessExecutionAuthority, Is.True);
            Assert.That(result.CSharpValidationAuthority, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.InstallsDependencies, Is.False);
            Assert.That(result.CallsSidecar, Is.False);
            Assert.That(result.StoresSecrets, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
        });
    }

    [Test]
    public void DiffGateRequiresFallbackWhenManualShadowAdvisoryWasRejected()
    {
        DataAgentLangGraphManualShadowResult advisory = RejectedAdvisory();

        DataAgentHarnessReplayDiffGateResult result =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(NewReplayReport(), advisory));

        Assert.Multiple(() =>
        {
            Assert.That(result.GatePassed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("advisory_forbidden_authority_claimed"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.CallsSidecar, Is.False);
        });
    }

    [Test]
    public void DiffGateFailsClosedWhenReplayReportChangedDefaultResult()
    {
        DataAgentGraphHandshakeReplayReport report = NewReplayReport(defaultResultChanged: true);

        DataAgentHarnessReplayDiffGateResult result =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(report, AcceptedAdvisory("timeout_or_transport_failure")));

        Assert.Multiple(() =>
        {
            Assert.That(result.GatePassed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("harness_replay_default_result_changed"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
        });
    }

    [Test]
    public void DiffGateRequiresOperatorWhenAdvisoryReasonIsAbsentFromReplayEvidence()
    {
        DataAgentHarnessReplayDiffGateResult result =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(NewReplayReport(), AcceptedAdvisory("invalid_schema")));

        Assert.Multiple(() =>
        {
            Assert.That(result.GatePassed, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("harness_replay_diff_reason_mismatch"));
            Assert.That(result.ReplayEvidenceMatched, Is.True);
            Assert.That(result.AdvisoryReasonMatched, Is.False);
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
        });
    }

    [Test]
    public void DiffGateFormatsCompactSafeAuditPacket()
    {
        DataAgentHarnessReplayDiffGateResult result =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(NewReplayReport(), AcceptedAdvisory("timeout_or_transport_failure")));

        string text = DataAgentHarnessReplayDiffGateFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("harness_replay_diff_gate=true"));
            Assert.That(text, Does.Contain("agent_advisory_contract=v3.24"));
            Assert.That(text, Does.Contain("real_langgraph_manual_shadow_provider=true"));
            Assert.That(text, Does.Contain("harness_execution_authority=true"));
            Assert.That(text, Does.Contain("csharp_validation_authority=true"));
            Assert.That(text, Does.Contain("agent_advisory_only=true"));
            Assert.That(text, Does.Contain("gate_only=true"));
            Assert.That(text, Does.Contain("operator_decides=true"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("calls_sidecar=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void V326DocumentDeclaresHarnessReplayDiffGateBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.26-harness-replay-diff-gate.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("harness_replay_diff_gate=true"));
            Assert.That(doc, Does.Contain("agent_advisory_contract=v3.24"));
            Assert.That(doc, Does.Contain("real_langgraph_manual_shadow_provider=true"));
            Assert.That(doc, Does.Contain("harness_execution_authority=true"));
            Assert.That(doc, Does.Contain("csharp_validation_authority=true"));
            Assert.That(doc, Does.Contain("agent_advisory_only=true"));
            Assert.That(doc, Does.Contain("gate_only=true"));
            Assert.That(doc, Does.Contain("operator_decides=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("calls_sidecar=false"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
            Assert.That(doc, Does.Contain("stores_hidden_context=false"));
        });
    }

    static DataAgentGraphHandshakeReplayReport NewReplayReport(bool defaultResultChanged = false)
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
            defaultResultChanged);

        DataAgentGraphHandshakeReplayFixtureResult fixture = new("timeout_fallback", comparison);
        return new DataAgentGraphHandshakeReplayReport(
            "v3.26-harness-replay-diff-gate",
            [fixture],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["timeout_or_transport_failure"] = 1
            },
            ComparisonCount: 1,
            defaultResultChanged,
            Passed: defaultResultChanged == false);
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
            RunId: "v3.26-diff-gate",
            Task: "compare manual LangGraph advisory to replay evidence",
            CurrentState: "manual shadow advisory captured and replay report available",
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
            AdvisoryId: "lg-manual-v326",
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
