using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV42OperatorEvidencePacketTests
{
    [Test]
    public void AcceptedSourcesCreateAcceptedEvidencePacket()
    {
        DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(
            NewInput(AcceptedIntegration(), AcceptedEnvelope()));

        Assert.Multiple(() =>
        {
            Assert.That(packet.Accepted, Is.True, packet.ReasonCode);
            Assert.That(packet.Status, Is.EqualTo(DataAgentV42OperatorEvidenceStatus.Accepted));
            Assert.That(packet.ContractVersion, Is.EqualTo("v4.2"));
            Assert.That(packet.SourceBaseline, Is.EqualTo("v4.1"));
            Assert.That(packet.ContextBudgetPassed, Is.True);
            Assert.That(packet.ContractValidationPassed, Is.True);
            Assert.That(packet.ReplayDiffGatePassed, Is.True);
            Assert.That(packet.FallbackRequired, Is.False);
            Assert.That(packet.OperatorRequired, Is.False);
            Assert.That(packet.DefaultResultChanged, Is.False);
            Assert.That(packet.AgentAdvisoryOnly, Is.True);
            Assert.That(packet.CSharpValidationAuthority, Is.True);
            Assert.That(packet.StoresSecrets, Is.False);
            Assert.That(packet.StoresSql, Is.False);
            Assert.That(packet.StoresHiddenContext, Is.False);
            Assert.That(packet.ReasonCodes, Is.Unique);
        });
    }

    [Test]
    public void ContractRejectionCreatesRejectedPacket()
    {
        DataAgentRealLangGraphManualShadowResult integration = AcceptedIntegration() with
        {
            Accepted = false,
            ReasonCode = "real_langgraph_manual_shadow_boundary_violation",
            FallbackRequired = true,
            OperatorRequired = true,
            ReasonCodes = ["real_langgraph_manual_shadow_boundary_violation"]
        };

        DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(
            NewInput(integration, AcceptedEnvelope()));

        Assert.Multiple(() =>
        {
            Assert.That(packet.Accepted, Is.False);
            Assert.That(packet.Status, Is.EqualTo(DataAgentV42OperatorEvidenceStatus.Rejected));
            Assert.That(packet.ReasonCode, Is.EqualTo("v4_2_operator_evidence_contract_rejected"));
            Assert.That(packet.FallbackRequired, Is.True);
            Assert.That(packet.OperatorRequired, Is.True);
            Assert.That(packet.SafeSummary, Is.Empty);
            Assert.That(packet.EvidenceRefs, Is.Empty);
        });
    }

    [Test]
    public void RuntimeUnavailableCreatesFallbackPacket()
    {
        DataAgentRealLangGraphManualShadowResult integration = AcceptedIntegration() with
        {
            Accepted = false,
            ReasonCode = "real_langgraph_manual_runtime_unavailable",
            FallbackRequired = true,
            OperatorRequired = true,
            ReasonCodes = ["real_langgraph_manual_runtime_unavailable"]
        };

        DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(
            NewInput(integration, AcceptedEnvelope()));

        Assert.Multiple(() =>
        {
            Assert.That(packet.Status, Is.EqualTo(DataAgentV42OperatorEvidenceStatus.Fallback));
            Assert.That(packet.ReasonCode, Is.EqualTo("v4_2_operator_evidence_fallback"));
            Assert.That(packet.FallbackRequired, Is.True);
            Assert.That(packet.DefaultResultChanged, Is.False);
        });
    }

    [TestCase("SELECT secret FROM hidden_context")]
    [TestCase("Bearer token-value")]
    [TestCase("D:\\private\\evidence.txt")]
    public void UnsafeSummaryIsRejected(string summary)
    {
        DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(
            NewInput(AcceptedIntegration(), AcceptedEnvelope()) with { SafeSummary = summary });

        Assert.Multiple(() =>
        {
            Assert.That(packet.Accepted, Is.False);
            Assert.That(packet.Status, Is.EqualTo(DataAgentV42OperatorEvidenceStatus.Rejected));
            Assert.That(packet.ReasonCode, Is.EqualTo("v4_2_operator_evidence_unsafe_input"));
            Assert.That(packet.SafeSummary, Is.Empty);
        });
    }

    [Test]
    public void RejectedBudgetCannotCreateAcceptedPacket()
    {
        DataAgentRealLangGraphManualShadowContextEnvelope envelope = AcceptedEnvelope() with
        {
            Accepted = false,
            ReasonCode = "manual_shadow_context_unsafe_text",
            ReasonCodes = ["manual_shadow_context_unsafe_text"]
        };

        DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(
            NewInput(AcceptedIntegration(), envelope));

        Assert.That(packet.Status, Is.EqualTo(DataAgentV42OperatorEvidenceStatus.Rejected));
        Assert.That(packet.ContextBudgetPassed, Is.False);
    }

    [Test]
    public void InvalidMetadataFailsClosed()
    {
        DataAgentV42OperatorEvidenceInput baseline = NewInput(AcceptedIntegration(), AcceptedEnvelope());
        DataAgentV42OperatorEvidenceInput[] invalid =
        [
            baseline with { AdvisoryKind = "unknown" },
            baseline with { SafeSummary = new string('x', 321) },
            baseline with { EvidenceRefs = Enumerable.Range(0, 9).Select(index => $"ref:{index}").ToArray() },
            baseline with { EvidenceRefs = ["C:\\private\\artifact.txt"] }
        ];

        foreach (DataAgentV42OperatorEvidenceInput input in invalid)
        {
            DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(input);
            Assert.That(packet.Accepted, Is.False);
            Assert.That(packet.SafeSummary, Is.Empty);
            Assert.That(packet.EvidenceRefs, Is.Empty);
        }
    }

    [Test]
    public void FormatterEmitsOnlySafePacketFields()
    {
        DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(
            NewInput(AcceptedIntegration(), AcceptedEnvelope()));

        string text = DataAgentV42OperatorEvidencePacketFormatter.Format(packet);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("operator_evidence_packet=v4.2"));
            Assert.That(text, Does.Contain("status=accepted"));
            Assert.That(text, Does.Contain("safe_summary=bounded operator summary"));
            Assert.That(text, Does.Contain("evidence_refs=replay_report:v4.1"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Not.Contain("fixture-v4-2"));
            Assert.That(text, Does.Not.Contain("SourceReplayId"));
        });
    }

    [Test]
    public void ArtifactWriterPersistsOnlyFormattedPacket()
    {
        string output = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            DataAgentV42OperatorEvidencePacket packet = DataAgentV42OperatorEvidencePacketBuilder.Build(
                NewInput(AcceptedIntegration(), AcceptedEnvelope()));

            DataAgentV42OperatorEvidenceArtifactWriteResult result =
                DataAgentV42OperatorEvidenceArtifactWriter.Write(output, packet);
            string body = File.ReadAllText(result.FilePath);

            Assert.Multiple(() =>
            {
                Assert.That(result.Written, Is.True, result.ReasonCode);
                Assert.That(result.FileName, Is.EqualTo("dataagent-v4.2-operator-evidence-packet.txt"));
                Assert.That(body, Is.EqualTo(DataAgentV42OperatorEvidencePacketFormatter.Format(packet)));
                Assert.That(body, Does.Not.Contain(output));
                Assert.That(body, Does.Not.Contain("fixture-v4-2"));
            });
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Test]
    public void ArtifactWriterFailsClosedForMissingInput()
    {
        DataAgentV42OperatorEvidenceArtifactWriteResult result =
            DataAgentV42OperatorEvidenceArtifactWriter.Write(string.Empty, null);

        Assert.That(result.Written, Is.False);
        Assert.That(result.FilePath, Is.Empty);
    }

    static DataAgentV42OperatorEvidenceInput NewInput(
        DataAgentRealLangGraphManualShadowResult integration,
        DataAgentRealLangGraphManualShadowContextEnvelope envelope) =>
        new(
            integration,
            envelope,
            "diagnostic_summary",
            "bounded operator summary",
            ["replay_report:v4.1"]);

    static DataAgentRealLangGraphManualShadowResult AcceptedIntegration() =>
        new(
            Accepted: true,
            ReasonCode: "real_langgraph_manual_shadow_integration_accepted",
            SourceBaseline: "v3.28",
            SourceReplayId: "fixture-v4-2",
            ContextLayerCount: 3,
            ManualOnly: true,
            OperatorStartedRuntime: true,
            LoopbackOnly: true,
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false,
            FallbackRequired: false,
            OperatorRequired: false,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes:
            [
                "real_langgraph_manual_shadow_integration_accepted",
                "langgraph_manual_shadow_advisory_accepted",
                "harness_replay_diff_gate_passed"
            ]);

    static DataAgentRealLangGraphManualShadowContextEnvelope AcceptedEnvelope() =>
        new(
            Accepted: true,
            ReasonCode: "manual_shadow_context_budget_ready",
            MaxEnvelopeChars: 1200,
            MaxLayerChars: 400,
            TotalIncludedChars: 120,
            LayerCount: 3,
            Layers: [],
            DefaultResultChanged: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: ["manual_shadow_context_budget_ready"]);
}
