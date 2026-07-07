using Alife.Function.QChat;
using NUnit.Framework;
using System.Collections.Generic;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatDataAgentDiagnosticsCommandContractTests
{
    static readonly (string Name, QChatDataAgentDiagnosticsTopic Topic)[] Topics =
    [
        ("evidence", QChatDataAgentDiagnosticsTopic.Evidence),
        ("trace", QChatDataAgentDiagnosticsTopic.Trace),
        ("progress", QChatDataAgentDiagnosticsTopic.Progress),
        ("graph", QChatDataAgentDiagnosticsTopic.Graph)
    ];

    static IEnumerable<TestCaseData> DataAgentCommands()
    {
        foreach ((string name, QChatDataAgentDiagnosticsTopic topic) in Topics)
        {
            yield return new TestCaseData($"/dataagent diag {name}", topic);
            yield return new TestCaseData($"/dataagent diagnostics {name}", topic);
            yield return new TestCaseData($"  /DATAAGENT diag {name}  ", topic);
        }
    }

    static IEnumerable<TestCaseData> DataAgentSuffixes()
    {
        foreach ((string name, QChatDataAgentDiagnosticsTopic topic) in Topics)
        {
            yield return new TestCaseData($"diag {name}", topic);
            yield return new TestCaseData($"diagnostics {name}", topic);
            yield return new TestCaseData($"  DIAG {name}  ", topic);
        }
    }

    static IEnumerable<TestCaseData> QChatDataAgentSuffixes()
    {
        foreach ((string name, QChatDataAgentDiagnosticsTopic topic) in Topics)
        {
            yield return new TestCaseData($"diag dataagent {name}", topic);
            yield return new TestCaseData($"diagnostics dataagent {name}", topic);
            yield return new TestCaseData($"  DIAGNOSTICS DATAAGENT {name}  ", topic);
        }
    }

    [TestCaseSource(nameof(DataAgentCommands))]
    public void TryParseDataAgentCommandMatchesSupportedCommands(
        string command,
        QChatDataAgentDiagnosticsTopic expectedTopic)
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand(
            command,
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(topic, Is.EqualTo(expectedTopic));
        });
    }

    [TestCaseSource(nameof(DataAgentSuffixes))]
    public void TryParseDataAgentCommandSuffixMatchesSupportedSuffixes(
        string command,
        QChatDataAgentDiagnosticsTopic expectedTopic)
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommandSuffix(
            command,
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(topic, Is.EqualTo(expectedTopic));
        });
    }

    [TestCaseSource(nameof(QChatDataAgentSuffixes))]
    public void TryParseQChatDataAgentDiagnosticsCommandSuffixMatchesSupportedSuffixes(
        string command,
        QChatDataAgentDiagnosticsTopic expectedTopic)
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseQChatDataAgentDiagnosticsCommandSuffix(
            command,
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(topic, Is.EqualTo(expectedTopic));
        });
    }

    [Test]
    public void TryParseDataAgentCommandStripsCopiedMenuDescription()
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand(
            "/dataagent diag graph - DataAgent DataQueryGraph dry-run diagnostics",
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(topic, Is.EqualTo(QChatDataAgentDiagnosticsTopic.Graph));
        });
    }

    [Test]
    public void TryParseQChatDataAgentDiagnosticsCommandSuffixStripsCopiedMenuDescription()
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseQChatDataAgentDiagnosticsCommandSuffix(
            "diagnostics dataagent trace - DataAgent trace diagnostics",
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(topic, Is.EqualTo(QChatDataAgentDiagnosticsTopic.Trace));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/dataagent")]
    [TestCase("/dataagent nope")]
    [TestCase("/dataagent diag unknown")]
    [TestCase("/dataagent diagnostics")]
    [TestCase("/dataagentx diag evidence")]
    [TestCase("/dataagent/diag evidence")]
    [TestCase("dataagent diag evidence")]
    public void TryParseDataAgentCommandFailsClosedForUnknownText(string? command)
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand(
            command,
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(topic, Is.EqualTo(default(QChatDataAgentDiagnosticsTopic)));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("diag")]
    [TestCase("diagnostics")]
    [TestCase("diag unknown")]
    [TestCase("diagnostics graph extra")]
    [TestCase("dataagent graph")]
    public void TryParseDataAgentCommandSuffixFailsClosedForUnknownText(string? command)
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommandSuffix(
            command,
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(topic, Is.EqualTo(default(QChatDataAgentDiagnosticsTopic)));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("diag")]
    [TestCase("diag dataagent")]
    [TestCase("diag dataagent unknown")]
    [TestCase("diagnostics dataagent graph extra")]
    [TestCase("diag qchat graph")]
    public void TryParseQChatDataAgentDiagnosticsCommandSuffixFailsClosedForUnknownText(string? command)
    {
        bool parsed = QChatDataAgentDiagnosticsCommandContract.TryParseQChatDataAgentDiagnosticsCommandSuffix(
            command,
            out QChatDataAgentDiagnosticsTopic topic);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(topic, Is.EqualTo(default(QChatDataAgentDiagnosticsTopic)));
        });
    }

    [Test]
    public void SupportedDataAgentCommandSuffixesListEveryStableVariant()
    {
        Assert.That(QChatDataAgentDiagnosticsCommandContract.SupportedDataAgentCommandSuffixes, Is.EqualTo(new[]
        {
            "diag evidence",
            "diagnostics evidence",
            "diag trace",
            "diagnostics trace",
            "diag progress",
            "diagnostics progress",
            "diag graph",
            "diagnostics graph"
        }));
    }
}
