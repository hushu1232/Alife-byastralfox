using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeShadowComparisonTests
{
    [Test]
    public void CompareReturnsMatchWhenOutcomeShapeMatches()
    {
        DataAgentGraphHandshakeOutcome baseline = Outcome(
            DataAgentGraphHandshakeStatus.Disabled,
            "sidecar_disabled",
            fallbackRequired: true);
        DataAgentGraphHandshakeOutcome sidecar = Outcome(
            DataAgentGraphHandshakeStatus.Disabled,
            "sidecar_disabled",
            fallbackRequired: true);

        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(baseline, sidecar);

        Assert.Multiple(() =>
        {
            Assert.That(comparison.Status, Is.EqualTo(DataAgentGraphHandshakeShadowComparisonStatus.Match));
            Assert.That(comparison.DefaultResultChanged, Is.False);
            Assert.That(comparison.DeterministicReasonCode, Is.EqualTo("sidecar_disabled"));
            Assert.That(comparison.SidecarReasonCode, Is.EqualTo("sidecar_disabled"));
        });
    }

    [Test]
    public void CompareTreatsAcceptedSidecarAsAdvisoryDifferenceWithoutChangingDefault()
    {
        DataAgentGraphHandshakeOutcome baseline = Outcome(
            DataAgentGraphHandshakeStatus.Disabled,
            "sidecar_disabled",
            fallbackRequired: true);
        DataAgentGraphHandshakeOutcome sidecar = Outcome(
            DataAgentGraphHandshakeStatus.Accepted,
            "accepted",
            fallbackRequired: true);

        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(baseline, sidecar);

        Assert.Multiple(() =>
        {
            Assert.That(comparison.Status, Is.EqualTo(DataAgentGraphHandshakeShadowComparisonStatus.AcceptedAdvisoryDifference));
            Assert.That(comparison.DefaultResultChanged, Is.False);
            Assert.That(comparison.ReasonCode, Is.EqualTo("accepted_advisory_difference"));
        });
    }

    [TestCase("sql_authority_requested")]
    [TestCase("checkpoint_mutation_requested")]
    [TestCase("visible_text_requested")]
    [TestCase("unknown_tool")]
    public void CompareMapsForbiddenAuthorityRejection(string reasonCode)
    {
        DataAgentGraphHandshakeOutcome baseline = Outcome(
            DataAgentGraphHandshakeStatus.Disabled,
            "sidecar_disabled",
            fallbackRequired: true);
        DataAgentGraphHandshakeOutcome sidecar = Outcome(
            DataAgentGraphHandshakeStatus.Rejected,
            reasonCode,
            fallbackRequired: true);

        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(baseline, sidecar);

        Assert.Multiple(() =>
        {
            Assert.That(comparison.Status, Is.EqualTo(DataAgentGraphHandshakeShadowComparisonStatus.RejectedAuthorityClaim));
            Assert.That(comparison.ReasonCode, Is.EqualTo("rejected_authority_claim"));
            Assert.That(comparison.DefaultResultChanged, Is.False);
        });
    }

    [Test]
    public void CompareMapsInvalidSchema()
    {
        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                Outcome(DataAgentGraphHandshakeStatus.Invalid, "invalid_response_schema", fallbackRequired: true));

        Assert.Multiple(() =>
        {
            Assert.That(comparison.Status, Is.EqualTo(DataAgentGraphHandshakeShadowComparisonStatus.InvalidSchema));
            Assert.That(comparison.ReasonCode, Is.EqualTo("invalid_schema"));
            Assert.That(comparison.DefaultResultChanged, Is.False);
        });
    }

    [TestCase(DataAgentGraphHandshakeStatus.Timeout, "sidecar_timeout")]
    [TestCase(DataAgentGraphHandshakeStatus.Unavailable, "sidecar_unavailable")]
    public void CompareMapsTimeoutAndUnavailableAsTransportFailure(
        DataAgentGraphHandshakeStatus status,
        string reasonCode)
    {
        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                Outcome(status, reasonCode, fallbackRequired: true));

        Assert.Multiple(() =>
        {
            Assert.That(comparison.Status, Is.EqualTo(DataAgentGraphHandshakeShadowComparisonStatus.TimeoutOrTransportFailure));
            Assert.That(comparison.ReasonCode, Is.EqualTo("timeout_or_transport_failure"));
            Assert.That(comparison.DefaultResultChanged, Is.False);
        });
    }

    [Test]
    public void CompareMapsFallbackOnlyOutcomes()
    {
        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "graph_sidecar_fallback_used", fallbackRequired: true));

        Assert.Multiple(() =>
        {
            Assert.That(comparison.Status, Is.EqualTo(DataAgentGraphHandshakeShadowComparisonStatus.FallbackUsed));
            Assert.That(comparison.ReasonCode, Is.EqualTo("fallback_used"));
            Assert.That(comparison.DefaultResultChanged, Is.False);
        });
    }

    [Test]
    public void FormatEmitsStableDiagnosticLines()
    {
        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                Outcome(DataAgentGraphHandshakeStatus.Accepted, "accepted", fallbackRequired: true));

        string formatted = DataAgentGraphHandshakeShadowComparisonFormatter.Format(comparison);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("graph_shadow_status=accepted_advisory_difference"));
            Assert.That(formatted, Does.Contain("default_result_changed=false"));
            Assert.That(formatted, Does.Contain("deterministic_reason=sidecar_disabled"));
            Assert.That(formatted, Does.Contain("sidecar_reason=accepted"));
            Assert.That(formatted, Does.Not.Contain("SELECT"));
            Assert.That(formatted, Does.Not.Contain("hidden_context"));
        });
    }

    [Test]
    public void ReportSummarizesMultipleComparisonsWithoutChangingDefaultResult()
    {
        DataAgentGraphHandshakeShadowComparison acceptedDifference =
            DataAgentGraphHandshakeShadowComparer.Compare(
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                Outcome(DataAgentGraphHandshakeStatus.Accepted, "accepted", fallbackRequired: true));
        DataAgentGraphHandshakeShadowComparison rejectedAuthority =
            DataAgentGraphHandshakeShadowComparer.Compare(
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                Outcome(DataAgentGraphHandshakeStatus.Rejected, "sql_authority_requested", fallbackRequired: true));

        DataAgentGraphHandshakeShadowComparisonReport report =
            DataAgentGraphHandshakeShadowComparisonReport.Create(
                "v3.12-owner-readiness-shadow",
                [acceptedDifference, rejectedAuthority]);

        Assert.Multiple(() =>
        {
            Assert.That(report.ReplayId, Is.EqualTo("v3.12-owner-readiness-shadow"));
            Assert.That(report.ComparisonCount, Is.EqualTo(2));
            Assert.That(report.DefaultResultChanged, Is.False);
            Assert.That(report.Passed, Is.True);
            Assert.That(report.StatusCounts["accepted_advisory_difference"], Is.EqualTo(1));
            Assert.That(report.StatusCounts["rejected_authority_claim"], Is.EqualTo(1));
        });
    }

    [Test]
    public void ReportFormatterEmitsStableMarkdownSummary()
    {
        DataAgentGraphHandshakeShadowComparison comparison =
            DataAgentGraphHandshakeShadowComparer.Compare(
                Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true),
                Outcome(DataAgentGraphHandshakeStatus.Accepted, "accepted", fallbackRequired: true));
        DataAgentGraphHandshakeShadowComparisonReport report =
            DataAgentGraphHandshakeShadowComparisonReport.Create("v3.12-owner-readiness-shadow", [comparison]);

        string markdown = DataAgentGraphHandshakeShadowComparisonReportFormatter.FormatMarkdown(report);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("# DataAgent Graph Shadow Comparison: v3.12-owner-readiness-shadow"));
            Assert.That(markdown, Does.Contain("shadow_only=true"));
            Assert.That(markdown, Does.Contain("default_result_changed=false"));
            Assert.That(markdown, Does.Contain("replay_parity_required=true"));
            Assert.That(markdown, Does.Contain("accepted_advisory_difference=1"));
            Assert.That(markdown, Does.Contain("no_sql_authority=true"));
            Assert.That(markdown, Does.Contain("fallback_required=true"));
            Assert.That(markdown, Does.Not.Contain("SELECT"));
            Assert.That(markdown, Does.Not.Contain("hidden_context"));
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
}
