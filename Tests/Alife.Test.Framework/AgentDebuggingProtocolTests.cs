using Alife.Function.MessageFilter;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public class AgentDebuggingProtocolTests
{
    [Test]
    public void CreateIssuePacketClassifiesQqInternalLabelLeakWithoutKeepingFullTranscript()
    {
        string ownerMessage = "QQ 群里刚才出现了 [QQ Zone proactive] rejected: Proactive suggestion must be confirmed before execution. "
                              + new string('x', 500);

        AgentDebugIssuePacket packet = AgentDebuggingProtocol.CreateIssuePacket(ownerMessage);

        Assert.Multiple(() =>
        {
            Assert.That(packet.IssueType, Is.EqualTo("qq-visible-output-leak"));
            Assert.That(packet.Surface, Is.EqualTo("qq"));
            Assert.That(packet.Goal, Does.Contain("internal labels"));
            Assert.That(packet.Constraints, Does.Contain("token-efficient"));
            Assert.That(packet.Constraints, Does.Contain("persona-aware"));
            Assert.That(packet.CandidateSubsystems, Does.Contain("qzone"));
            Assert.That(packet.NextStep, Does.Contain("debug map"));
            Assert.That(string.Join("\n", packet.KnownEvidence), Does.Contain("[QQ Zone proactive]"));
            Assert.That(string.Join("\n", packet.KnownEvidence).Length, Is.LessThan(180));
        });
    }

    [Test]
    public void FormatLocationFailureReportNamesCheckedScopeCandidatesMissingEvidenceAndNextStep()
    {
        AgentLocationFailureReport report = new(
            KnownSymptom: "QQ visible output contains internal labels.",
            Checked: [
                new AgentLocationCheck("QChatService.SendTextOrMediaMessageAsync", "sanitizer is already on the direct send path")
            ],
            Candidates: [
                new AgentLocationCandidate("QZoneService.Report*", "high", "formats [QQ Zone ...] before feedback")
            ],
            MissingEvidence: [
                "sample message or correlation id"
            ],
            NextLowTokenStep: "add a failing QZone report feedback test");

        string formatted = AgentDebuggingProtocol.FormatLocationFailureReport(report);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("Location status: not unique yet."));
            Assert.That(formatted, Does.Contain("QChatService.SendTextOrMediaMessageAsync"));
            Assert.That(formatted, Does.Contain("QZoneService.Report*"));
            Assert.That(formatted, Does.Contain("sample message or correlation id"));
            Assert.That(formatted, Does.Contain("add a failing QZone report feedback test"));
            Assert.That(formatted, Does.Not.Contain("fixed"));
            Assert.That(formatted, Does.Not.Contain("completed"));
        });
    }

    [Test]
    public void FormatPersonaAwareHypothesisKeepsOwnerToneWithoutHidingEvidence()
    {
        AgentPersonaContract persona = AgentPersonaContract.XiayuOwnerEngineering;

        string message = AgentDebuggingProtocol.FormatHypothesis(
            persona,
            "QZoneService.Report*",
            "it formats [QQ Zone ...] before model-facing feedback",
            "add a failing behavior test");

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.StartWith("术术"));
            Assert.That(message, Does.Contain("QZoneService.Report*"));
            Assert.That(message, Does.Contain("[QQ Zone ...]"));
            Assert.That(message, Does.Contain("failing behavior test"));
            Assert.That(message.Length, Is.LessThan(220));
        });
    }
}
