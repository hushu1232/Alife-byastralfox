using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV43CrossModuleValueScoreTests
{
    [Test]
    public void AdoptedAlignedAdvisoryScoresOneHundredAndIsEligible()
    {
        DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
            NewInput(DataAgentV43OperatorDisposition.Adopted, beforeMs: 1000, afterMs: 0));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, result.ReasonCode);
            Assert.That(result.PacketScore, Is.EqualTo(25));
            Assert.That(result.ReplayAlignmentScore, Is.EqualTo(25));
            Assert.That(result.ManifestScore, Is.EqualTo(20));
            Assert.That(result.OperatorScore, Is.EqualTo(20));
            Assert.That(result.ReviewTimeScore, Is.EqualTo(10));
            Assert.That(result.TotalScore, Is.EqualTo(100));
            Assert.That(result.Status, Is.EqualTo(DataAgentV43ValueStatus.ProvenUseful));
            Assert.That(result.ProductionShadowEligible, Is.True);
            Assert.That(result.AllowsExecution, Is.False);
            Assert.That(result.AllowsStateWrite, Is.False);
            Assert.That(result.AllowsVisibleText, Is.False);
            Assert.That(result.DefaultResultChanged, Is.False);
        });
    }

    [Test]
    public void UsefulAdvisoryUsesProportionalReviewReduction()
    {
        DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
            NewInput(DataAgentV43OperatorDisposition.Useful, beforeMs: 1000, afterMs: 500));

        Assert.That(result.ReviewTimeScore, Is.EqualTo(5));
        Assert.That(result.TotalScore, Is.EqualTo(85));
        Assert.That(result.Status, Is.EqualTo(DataAgentV43ValueStatus.ProvenUseful));
        Assert.That(result.ProductionShadowEligible, Is.True);
    }

    [TestCase(DataAgentV43OperatorDisposition.Rejected)]
    [TestCase(DataAgentV43OperatorDisposition.NotReviewed)]
    public void RejectedOrUnreviewedAdvisoryIsUnproven(DataAgentV43OperatorDisposition disposition)
    {
        DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
            NewInput(disposition, beforeMs: 1000, afterMs: 0));

        Assert.That(result.Status, Is.EqualTo(DataAgentV43ValueStatus.Unproven));
        Assert.That(result.OperatorScore, Is.Zero);
        Assert.That(result.ReviewTimeScore, Is.Zero);
        Assert.That(result.ProductionShadowEligible, Is.False);
    }

    [Test]
    public void EvidenceWithoutReplayAlignmentIsOnlyPromising()
    {
        DataAgentV42OperatorEvidencePacket packet = AcceptedPacket() with { ReplayDiffGatePassed = false };
        DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
            NewInput(DataAgentV43OperatorDisposition.Useful, beforeMs: 1000, afterMs: 500) with { Packet = packet });

        Assert.That(result.TotalScore, Is.EqualTo(60));
        Assert.That(result.Status, Is.EqualTo(DataAgentV43ValueStatus.Promising));
        Assert.That(result.ProductionShadowEligible, Is.False);
    }

    [Test]
    public void InvalidCapabilitiesFailClosedWithZeroScore()
    {
        DataAgentV43CrossModuleValueInput baseline = NewInput(DataAgentV43OperatorDisposition.Adopted, 1000, 0);
        IReadOnlyList<string>[] invalid =
        [
            ["unknown.capability"],
            ["qchat.intent_hint", "qchat.intent_hint"],
            ["qchat.intent_hint", "memory.candidate_summary", "browser.task_plan", "desktop.task_plan", "emotion.expression_hint", "deskpet.expression_hint", "extra"]
        ];

        foreach (IReadOnlyList<string> capabilities in invalid)
        {
            DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
                baseline with { CapabilityNames = capabilities });
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Status, Is.EqualTo(DataAgentV43ValueStatus.Rejected));
            Assert.That(result.TotalScore, Is.Zero);
            Assert.That(result.ProductionShadowEligible, Is.False);
        }
    }

    [TestCase(-1, 0)]
    [TestCase(3600001, 0)]
    [TestCase(100, 101)]
    public void InvalidReviewTimingFailsClosed(int beforeMs, int afterMs)
    {
        DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
            NewInput(DataAgentV43OperatorDisposition.Adopted, beforeMs, afterMs));

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.TotalScore, Is.Zero);
        Assert.That(result.Status, Is.EqualTo(DataAgentV43ValueStatus.Rejected));
    }

    [Test]
    public void NonacceptedV42PacketCannotBeScored()
    {
        DataAgentV42OperatorEvidencePacket packet = AcceptedPacket() with
        {
            Accepted = false,
            Status = DataAgentV42OperatorEvidenceStatus.Fallback,
            FallbackRequired = true
        };
        DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
            NewInput(DataAgentV43OperatorDisposition.Adopted, 1000, 0) with { Packet = packet });

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.TotalScore, Is.Zero);
        Assert.That(result.ProductionShadowEligible, Is.False);
    }

    [Test]
    public void FormatterEmitsSafeDeterministicScoreOnly()
    {
        DataAgentV43CrossModuleValueResult result = DataAgentV43CrossModuleValueEvaluator.Evaluate(
            NewInput(DataAgentV43OperatorDisposition.Adopted, 1000, 0));

        string text = DataAgentV43CrossModuleValueFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("cross_module_value_score=v4.3"));
            Assert.That(text, Does.Contain("status=proven_useful"));
            Assert.That(text, Does.Contain("operator_disposition=adopted"));
            Assert.That(text, Does.Contain("total_score=100"));
            Assert.That(text, Does.Contain("production_shadow_eligible=true"));
            Assert.That(text, Does.Contain("allows_execution=false"));
            Assert.That(text, Does.Not.Contain("bounded operator summary"));
            Assert.That(text, Does.Not.Contain("replay_report"));
        });
    }

    [Test]
    public void ArtifactWriterPersistsOnlyFormattedScore()
    {
        string output = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            DataAgentV43CrossModuleValueResult value = DataAgentV43CrossModuleValueEvaluator.Evaluate(
                NewInput(DataAgentV43OperatorDisposition.Adopted, 1000, 0));
            DataAgentV43CrossModuleValueArtifactWriteResult write =
                DataAgentV43CrossModuleValueArtifactWriter.Write(output, value);
            string body = File.ReadAllText(write.FilePath);

            Assert.That(write.Written, Is.True, write.ReasonCode);
            Assert.That(write.FileName, Is.EqualTo("dataagent-v4.3-cross-module-value-score.txt"));
            Assert.That(body, Is.EqualTo(DataAgentV43CrossModuleValueFormatter.Format(value)));
            Assert.That(body, Does.Not.Contain(output));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    static DataAgentV43CrossModuleValueInput NewInput(
        DataAgentV43OperatorDisposition disposition,
        int beforeMs,
        int afterMs) =>
        new(
            Packet: AcceptedPacket(),
            CapabilityNames: ["qchat.intent_hint", "memory.candidate_summary"],
            OperatorDisposition: disposition,
            ReviewBeforeMs: beforeMs,
            ReviewAfterMs: afterMs);

    static DataAgentV42OperatorEvidencePacket AcceptedPacket() =>
        new(
            Accepted: true,
            ReasonCode: "v4_2_operator_evidence_accepted",
            ContractVersion: "v4.2",
            SourceBaseline: "v4.1",
            Status: DataAgentV42OperatorEvidenceStatus.Accepted,
            AdvisoryKind: "diagnostic_summary",
            ContextBudgetPassed: true,
            ContractValidationPassed: true,
            ReplayDiffGatePassed: true,
            FallbackRequired: false,
            OperatorRequired: false,
            DefaultResultChanged: false,
            AgentAdvisoryOnly: true,
            CSharpValidationAuthority: true,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            ReasonCodes: ["v4_2_operator_evidence_accepted"],
            EvidenceRefs: ["replay_report:v4.1"],
            SafeSummary: "bounded operator summary");
}
