using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV40RealLangGraphManualShadowIntegrationTests
{
    [Test]
    public void IntegrationAcceptsManualLangGraphAdvisoryThroughReplayDiffGate()
    {
        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput());

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, result.ReasonCode);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_shadow_integration_accepted"));
            Assert.That(result.SourceBaseline, Is.EqualTo("v3.28"));
            Assert.That(result.SourceReplayId, Is.EqualTo("v4.0-owner-readiness-analysis"));
            Assert.That(result.ContextLayerCount, Is.EqualTo(3));
            Assert.That(result.ManualOnly, Is.True);
            Assert.That(result.OperatorStartedRuntime, Is.True);
            Assert.That(result.LoopbackOnly, Is.True);
            Assert.That(result.AgentAdvisoryOnly, Is.True);
            Assert.That(result.HarnessExecutionAuthority, Is.True);
            Assert.That(result.CSharpValidationAuthority, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.FallbackRequired, Is.False);
            Assert.That(result.OperatorRequired, Is.False);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.InstallsDependencies, Is.False);
            Assert.That(result.StoresSecrets, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
            Assert.That(result.ReasonCodes, Does.Contain("langgraph_manual_shadow_advisory_accepted"));
            Assert.That(result.ReasonCodes, Does.Contain("harness_replay_diff_gate_passed"));
        });
    }

    [Test]
    public void IntegrationFallsBackWhenManualRuntimeIsUnavailable()
    {
        DataAgentRealLangGraphManualShadowInput input = NewInput() with
        {
            OperatorStartedRuntime = false,
            ManualShadowResult = null,
            DiffGateResult = null
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_runtime_unavailable"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.InstallsDependencies, Is.False);
        });
    }

    [Test]
    public void IntegrationRejectsUnsafeContextAndPreservesFallback()
    {
        DataAgentRealLangGraphManualShadowInput input = NewInput() with
        {
            ContextLayers =
            [
                new DataAgentRealLangGraphManualShadowContextLayer(
                    "layer_3_failure_excerpt",
                    "SELECT * FROM hidden_context WHERE bearer = secret")
            ]
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_shadow_unsafe_context"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
        });
    }

    [Test]
    public void IntegrationFormatterEmitsCompactSafePacket()
    {
        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput());

        string text = DataAgentRealLangGraphManualShadowFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("real_langgraph_manual_shadow_integration=true"));
            Assert.That(text, Does.Contain("source_baseline=v3.28"));
            Assert.That(text, Does.Contain("manual_only=true"));
            Assert.That(text, Does.Contain("operator_started_runtime=true"));
            Assert.That(text, Does.Contain("loopback_only=true"));
            Assert.That(text, Does.Contain("agent_advisory_only=true"));
            Assert.That(text, Does.Contain("harness_execution_authority=true"));
            Assert.That(text, Does.Contain("csharp_validation_authority=true"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("fallback_required=false"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("SELECT * FROM hidden_context"));
        });
    }

    static DataAgentRealLangGraphManualShadowInput NewInput()
    {
        DataAgentLangGraphManualShadowResult advisory = AcceptedAdvisory("timeout_or_transport_failure");
        DataAgentHarnessReplayDiffGateResult diffGate =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(NewReplayReport(), advisory));

        return new DataAgentRealLangGraphManualShadowInput(
            SourceReplayId: "v4.0-owner-readiness-analysis",
            OperatorStartedRuntime: true,
            LoopbackOnly: true,
            RuntimeStartedByAlife: false,
            DependenciesInstalledByAlife: false,
            SidecarCalledByAlife: false,
            ContextLayers:
            [
                new DataAgentRealLangGraphManualShadowContextLayer("layer_1_route", "fixture=v4.0-owner-readiness-analysis;route=allowed;node=plan"),
                new DataAgentRealLangGraphManualShadowContextLayer("layer_2_evidence", "reason_code=timeout_or_transport_failure;evidence_ref=replay_report:v3.20-shadow-replay-report"),
                new DataAgentRealLangGraphManualShadowContextLayer("layer_3_excerpt", "bounded_failure_excerpt=timeout_or_transport_failure")
            ],
            ManualShadowResult: advisory,
            DiffGateResult: diffGate);
    }

    static DataAgentGraphHandshakeReplayReport NewReplayReport()
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

        DataAgentGraphHandshakeReplayFixtureResult fixture = new("timeout_fallback", comparison);
        return new DataAgentGraphHandshakeReplayReport(
            "v4.0-owner-readiness-analysis",
            [fixture],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["timeout_or_transport_failure"] = 1
            },
            ComparisonCount: 1,
            DefaultResultChanged: false,
            Passed: true);
    }

    static DataAgentLangGraphManualShadowResult AcceptedAdvisory(string reasonCode)
    {
        return DataAgentLangGraphManualShadowProvider.Evaluate(NewRequest(reasonCode), NewPayload(NewResponse(reasonCode)));
    }

    static DataAgentAgentAdvisoryRequest NewRequest(string reasonCode)
    {
        return new DataAgentAgentAdvisoryRequest(
            ContractVersion: "v3.24",
            RunId: "v4.0-manual-shadow",
            Task: "summarize replay failure for operator review",
            CurrentState: "manual LangGraph runtime returned advisory packet",
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
            AdvisoryId: "lg-v40-manual",
            Summary: "manual LangGraph advisory matches replay evidence category",
            ReasonCode: reasonCode,
            Confidence: 0.81,
            EvidenceRefs: ["replay_report:v3.20-shadow-replay-report"],
            ProposedNextSteps: ["inspect_loopback", "review_replay_diff"],
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
}
