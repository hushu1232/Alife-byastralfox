using System;
using System.Linq;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatDecisionTraceTests
{
    [Test]
    public void ToDiagnosticTextFormatsCompactDecisionPath()
    {
        QChatDecisionTrace trace = new(
            TraceId: "trace-001",
            BotId: 2905391496,
            AgentId: "xiayu",
            MessageType: OneBotMessageType.Group,
            SenderRole: QChatSenderRole.Owner,
            IntentKind: QChatIntentKind.RecallMessage,
            IntentCandidate: true,
            IntentConfirmed: true,
            GateDecision: "accepted",
            ReplyDecision: "not_applicable",
            CapabilityDecision: "allowed",
            FinalAction: "recall",
            Reason: "confirmed recall command",
            CreatedAt: DateTimeOffset.Parse("2026-06-21T06:00:00Z"));

        string text = trace.ToDiagnosticText();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("qchat decision:"));
            Assert.That(text, Does.Contain("trace=trace-001"));
            Assert.That(text, Does.Contain("bot=2905391496"));
            Assert.That(text, Does.Contain("agent=xiayu"));
            Assert.That(text, Does.Contain("surface=Group"));
            Assert.That(text, Does.Contain("actor=Owner"));
            Assert.That(text, Does.Contain("intent=RecallMessage"));
            Assert.That(text, Does.Contain("candidate=true"));
            Assert.That(text, Does.Contain("confirmed=true"));
            Assert.That(text, Does.Contain("gate=accepted"));
            Assert.That(text, Does.Contain("reply=not_applicable"));
            Assert.That(text, Does.Contain("capability=allowed"));
            Assert.That(text, Does.Contain("action=recall"));
            Assert.That(text, Does.Contain("reason=confirmed recall command"));
        });
    }

    [Test]
    public void ToDiagnosticTextRecordsQuietModeSuppression()
    {
        QChatDecisionTrace trace = CreateTrace(
            intentKind: QChatIntentKind.GroupWake,
            replyDecision: "suppressed_by_quiet_mode",
            finalAction: "no_visible_reply",
            reason: "quiet mode is active");

        string text = trace.ToDiagnosticText();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("reply=suppressed_by_quiet_mode"));
            Assert.That(text, Does.Contain("action=no_visible_reply"));
            Assert.That(text, Does.Contain("reason=quiet mode is active"));
        });
    }

    [Test]
    public void ToDiagnosticTextRecordsNonOwnerDenial()
    {
        QChatDecisionTrace trace = CreateTrace(
            senderRole: QChatSenderRole.GroupMember,
            intentKind: QChatIntentKind.GroupFileUpload,
            capabilityDecision: "denied:not_owner",
            finalAction: "ignore",
            reason: "actor is not owner");

        string text = trace.ToDiagnosticText();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("actor=GroupMember"));
            Assert.That(text, Does.Contain("intent=GroupFileUpload"));
            Assert.That(text, Does.Contain("capability=denied:not_owner"));
            Assert.That(text, Does.Contain("action=ignore"));
            Assert.That(text, Does.Contain("reason=actor is not owner"));
        });
    }

    [Test]
    public void DecisionTraceDoesNotModelRawUserContent()
    {
        string[] propertyNames = typeof(QChatDecisionTrace)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(propertyNames, Does.Not.Contain("RawMessage"));
            Assert.That(propertyNames, Does.Not.Contain("PlainText"));
            Assert.That(propertyNames, Does.Not.Contain("ReadableText"));
            Assert.That(propertyNames, Does.Not.Contain("MessageText"));
        });
    }

    [Test]
    public void DiagnosticsServiceFormatsDecisionTrace()
    {
        QChatDecisionTrace trace = CreateTrace(finalAction: "reply");

        string text = QChatDiagnosticsService.FormatDecisionTrace(trace);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("qchat decision:"));
            Assert.That(text, Does.Contain("action=reply"));
        });
    }

    static QChatDecisionTrace CreateTrace(
        QChatSenderRole senderRole = QChatSenderRole.Owner,
        QChatIntentKind intentKind = QChatIntentKind.None,
        string gateDecision = "accepted",
        string replyDecision = "allow_reply",
        string capabilityDecision = "allowed",
        string finalAction = "reply",
        string reason = "normal reply")
    {
        return new QChatDecisionTrace(
            TraceId: "trace-test",
            BotId: 2905391496,
            AgentId: "xiayu",
            MessageType: OneBotMessageType.Group,
            SenderRole: senderRole,
            IntentKind: intentKind,
            IntentCandidate: intentKind != QChatIntentKind.None,
            IntentConfirmed: intentKind != QChatIntentKind.None,
            GateDecision: gateDecision,
            ReplyDecision: replyDecision,
            CapabilityDecision: capabilityDecision,
            FinalAction: finalAction,
            Reason: reason,
            CreatedAt: DateTimeOffset.Parse("2026-06-21T06:00:00Z"));
    }
}
