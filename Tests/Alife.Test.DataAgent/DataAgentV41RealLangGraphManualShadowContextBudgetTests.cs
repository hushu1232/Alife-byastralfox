using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV41RealLangGraphManualShadowContextBudgetTests
{
    [Test]
    public void BuilderCreatesLayeredEnvelopeWithinBudget()
    {
        DataAgentRealLangGraphManualShadowContextEnvelope envelope =
            DataAgentRealLangGraphManualShadowContextBudgetBuilder.Build(
                NewLayers(),
                new DataAgentRealLangGraphManualShadowContextBudgetOptions(
                    MaxEnvelopeChars: 260,
                    MaxLayerChars: 120,
                    RequiredLayerNames:
                    [
                        "layer_1_route",
                        "layer_2_evidence",
                        "layer_3_excerpt"
                    ]));

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Accepted, Is.True, envelope.ReasonCode);
            Assert.That(envelope.ReasonCode, Is.EqualTo("manual_shadow_context_budget_ready"));
            Assert.That(envelope.MaxEnvelopeChars, Is.EqualTo(260));
            Assert.That(envelope.MaxLayerChars, Is.EqualTo(120));
            Assert.That(envelope.TotalIncludedChars, Is.LessThanOrEqualTo(260));
            Assert.That(envelope.LayerCount, Is.EqualTo(3));
            Assert.That(envelope.Layers.Select(layer => layer.Name), Is.EqualTo(new[]
            {
                "layer_1_route",
                "layer_2_evidence",
                "layer_3_excerpt"
            }));
            Assert.That(envelope.StoresSecrets, Is.False);
            Assert.That(envelope.StoresSql, Is.False);
            Assert.That(envelope.StoresHiddenContext, Is.False);
            Assert.That(envelope.DefaultResultChanged, Is.False);
        });
    }

    static DataAgentRealLangGraphManualShadowContextLayer[] NewLayers() =>
    [
        new("layer_1_route", "fixture=v4.0-owner-readiness-analysis;route=allowed;node=plan"),
        new("layer_2_evidence", "reason_code=timeout_or_transport_failure;evidence_ref=replay_report:v3.20-shadow-replay-report"),
        new("layer_3_excerpt", "bounded_failure_excerpt=timeout_or_transport_failure")
    ];
}
