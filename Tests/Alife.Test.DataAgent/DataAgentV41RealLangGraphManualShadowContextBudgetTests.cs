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

    [Test]
    public void BuilderRejectsUnsafeContextWithoutLeakingText()
    {
        DataAgentRealLangGraphManualShadowContextEnvelope envelope =
            DataAgentRealLangGraphManualShadowContextBudgetBuilder.Build(
                [
                    new("layer_1_route", "fixture=ok"),
                    new("layer_2_evidence", "SELECT * FROM hidden_context WHERE bearer='secret'"),
                    new("layer_3_excerpt", "bounded_failure_excerpt=timeout")
                ],
                NewOptions());

        string formatted = DataAgentRealLangGraphManualShadowContextBudgetFormatter.Format(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Accepted, Is.False);
            Assert.That(envelope.ReasonCode, Is.EqualTo("manual_shadow_context_unsafe_text"));
            Assert.That(formatted, Does.Contain("accepted=false"));
            Assert.That(formatted, Does.Contain("reason_code=manual_shadow_context_unsafe_text"));
            Assert.That(formatted, Does.Not.Contain("SELECT"));
            Assert.That(formatted, Does.Not.Contain("bearer"));
            Assert.That(formatted, Does.Not.Contain("'secret'"));
            Assert.That(formatted, Does.Not.Contain("FROM hidden_context"));
        });
    }

    [Test]
    public void BuilderRejectsMissingRequiredLayer()
    {
        DataAgentRealLangGraphManualShadowContextEnvelope envelope =
            DataAgentRealLangGraphManualShadowContextBudgetBuilder.Build(
                [
                    new("layer_1_route", "fixture=ok"),
                    new("layer_3_excerpt", "bounded_failure_excerpt=timeout")
                ],
                NewOptions());

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Accepted, Is.False);
            Assert.That(envelope.ReasonCode, Is.EqualTo("manual_shadow_context_required_layer_missing"));
            Assert.That(envelope.LayerCount, Is.EqualTo(0));
            Assert.That(envelope.TotalIncludedChars, Is.EqualTo(0));
        });
    }

    [Test]
    public void BuilderTruncatesLayerTextWithinBudget()
    {
        DataAgentRealLangGraphManualShadowContextEnvelope envelope =
            DataAgentRealLangGraphManualShadowContextBudgetBuilder.Build(
                [
                    new("layer_1_route", new string('r', 80)),
                    new("layer_2_evidence", new string('e', 80)),
                    new("layer_3_excerpt", new string('x', 80))
                ],
                new DataAgentRealLangGraphManualShadowContextBudgetOptions(
                    MaxEnvelopeChars: 90,
                    MaxLayerChars: 40,
                    RequiredLayerNames: ["layer_1_route", "layer_2_evidence", "layer_3_excerpt"]));

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Accepted, Is.True);
            Assert.That(envelope.TotalIncludedChars, Is.LessThanOrEqualTo(90));
            Assert.That(envelope.Layers.Any(layer => layer.Truncated), Is.True);
            Assert.That(envelope.Layers.All(layer => layer.IncludedChars <= 40), Is.True);
        });
    }

    [Test]
    public void FormatterEmitsCompactSafeBudgetPacket()
    {
        DataAgentRealLangGraphManualShadowContextEnvelope envelope =
            DataAgentRealLangGraphManualShadowContextBudgetBuilder.Build(NewLayers(), NewOptions());

        string formatted = DataAgentRealLangGraphManualShadowContextBudgetFormatter.Format(envelope);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("[dataagent_v4_1_context_budget]"));
            Assert.That(formatted, Does.Contain("manual_shadow_context_budget=true"));
            Assert.That(formatted, Does.Contain("accepted=true"));
            Assert.That(formatted, Does.Contain("reason_code=manual_shadow_context_budget_ready"));
            Assert.That(formatted, Does.Contain("layer_count=3"));
            Assert.That(formatted, Does.Contain("default_result_changed=false"));
            Assert.That(formatted, Does.Contain("stores_secrets=false"));
            Assert.That(formatted, Does.Contain("stores_sql=false"));
            Assert.That(formatted, Does.Contain("stores_hidden_context=false"));
            Assert.That(formatted, Does.Not.Contain("SELECT"));
            Assert.That(formatted, Does.Not.Contain("bearer"));
        });
    }

    static DataAgentRealLangGraphManualShadowContextBudgetOptions NewOptions() =>
        new(
            MaxEnvelopeChars: 260,
            MaxLayerChars: 120,
            RequiredLayerNames:
            [
                "layer_1_route",
                "layer_2_evidence",
                "layer_3_excerpt"
            ]);

    static DataAgentRealLangGraphManualShadowContextLayer[] NewLayers() =>
    [
        new("layer_1_route", "fixture=v4.0-owner-readiness-analysis;route=allowed;node=plan"),
        new("layer_2_evidence", "reason_code=timeout_or_transport_failure;evidence_ref=replay_report:v3.20-shadow-replay-report"),
        new("layer_3_excerpt", "bounded_failure_excerpt=timeout_or_transport_failure")
    ];
}
