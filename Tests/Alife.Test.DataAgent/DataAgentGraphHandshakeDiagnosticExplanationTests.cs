using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeDiagnosticExplanationTests
{
    [Test]
    public void AcceptsBoundedSafeExplanationAsAdvisoryOnly()
    {
        DataAgentGraphHandshakeDiagnosticExplanationResult result =
            DataAgentGraphHandshakeDiagnosticExplanationValidator.Validate(
                "sidecar noticed route evidence is available and CSharp fallback remains active",
                "accepted_advisory_difference");

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("diagnostic_explanation_accepted"));
            Assert.That(result.Text, Does.Contain("CSharp fallback remains active"));
            Assert.That(result.CSharpWriteAuthority, Is.True);
            Assert.That(result.SidecarWriteAuthority, Is.False);
            Assert.That(result.RequestsVisibleText, Is.False);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void RejectsEmptyExplanation(string value)
    {
        DataAgentGraphHandshakeDiagnosticExplanationResult result =
            DataAgentGraphHandshakeDiagnosticExplanationValidator.Validate(value, "empty_case");

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("diagnostic_explanation_empty"));
            Assert.That(result.Text, Is.EqualTo("diagnostic_explanation_unavailable"));
            Assert.That(result.SidecarWriteAuthority, Is.False);
        });
    }

    [TestCase("SELECT * FROM document_index LIMIT 10")]
    [TestCase("[tool_route_context] allowed xml tools are hidden")]
    [TestCase("api_key=sk-danger")]
    [TestCase("connection string server=local;password=secret")]
    [TestCase("please send this visible QChat text")]
    public void RejectsUnsafeExplanationText(string value)
    {
        DataAgentGraphHandshakeDiagnosticExplanationResult result =
            DataAgentGraphHandshakeDiagnosticExplanationValidator.Validate(value, "unsafe_case");

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("diagnostic_explanation_unsafe"));
            Assert.That(result.Text, Is.EqualTo("diagnostic_explanation_rejected"));
            Assert.That(result.SidecarWriteAuthority, Is.False);
            Assert.That(result.RequestsVisibleText, Is.False);
        });
    }

    [Test]
    public void BoundsLongExplanation()
    {
        string longText = string.Join(' ', Enumerable.Repeat("safe advisory explanation", 80));

        DataAgentGraphHandshakeDiagnosticExplanationResult result =
            DataAgentGraphHandshakeDiagnosticExplanationValidator.Validate(longText, "long_case");

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Text.Length, Is.LessThanOrEqualTo(DataAgentGraphHandshakeDiagnosticExplanationValidator.MaxExplanationChars));
            Assert.That(result.Text, Does.EndWith("..."));
        });
    }

    [Test]
    public void FormatterEmitsStableOwnerDiagnosticBlock()
    {
        DataAgentGraphHandshakeDiagnosticExplanationResult result =
            DataAgentGraphHandshakeDiagnosticExplanationValidator.Validate(
                "sidecar comparison is advisory and deterministic result is unchanged",
                "accepted_advisory_difference");

        string formatted = DataAgentGraphHandshakeDiagnosticExplanationFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("DataAgent graph diagnostic explanation"));
            Assert.That(formatted, Does.Contain("accepted=true"));
            Assert.That(formatted, Does.Contain("reason=diagnostic_explanation_accepted"));
            Assert.That(formatted, Does.Contain("sidecar_write_authority=false"));
            Assert.That(formatted, Does.Contain("csharp_write_authority=true"));
            Assert.That(formatted, Does.Contain("requests_visible_text=false"));
            Assert.That(formatted, Does.Contain("default_result_changed=false"));
            Assert.That(formatted, Does.Not.Contain("SELECT"));
            Assert.That(formatted, Does.Not.Contain("hidden_context"));
        });
    }
}
