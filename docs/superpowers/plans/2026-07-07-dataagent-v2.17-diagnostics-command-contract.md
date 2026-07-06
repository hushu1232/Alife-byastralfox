# DataAgent V2.17 Diagnostics Command Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a QChat-local DataAgent diagnostics command contract and route access policy, owner command detection, diagnostics rendering, and ingress tests through that shared contract.

**Architecture:** Keep DataAgent runtime behavior unchanged. Add one small string-only parser in the QChat project, then replace duplicated `/dataagent diag|diagnostics evidence|trace|progress|graph` checks in QChat with parsed topics. Preserve QChat as a string consumer and keep `DataAgentDataQueryGraph*` model types out of QChat.

**Tech Stack:** .NET 9, C#, NUnit, PowerShell readiness scripts, existing QChat diagnostics services.

---

## Scope Check

This plan covers one subsystem: QChat owner diagnostics command ingress for DataAgent diagnostics. It does not add LangGraph runtime behavior, Python sidecars, SQL execution, PostgreSQL changes, DataAgent orchestration changes, desktop/browser/RAG graph nodes, or new model-facing tools.

## File Structure

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs`
  - Defines `QChatDataAgentDiagnosticsTopic`.
  - Parses `/dataagent diag|diagnostics <topic>` commands.
  - Parses `/qchat diag|diagnostics dataagent <topic>` suffixes.
  - Strips copied menu descriptions and fails closed on unknown topics.

- Create: `Tests/Alife.Test.QChat/QChatDataAgentDiagnosticsCommandContractTests.cs`
  - Table-driven parser tests for all supported topics and command forms.
  - Boundary tests for unknown prefixes, unknown topics, null, and empty text.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs`
  - Delegate `/dataagent` diagnostics recognition to `QChatDataAgentDiagnosticsCommandContract`.
  - Preserve broad `/qchat` owner-only behavior.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
  - Delegate `/dataagent` diagnostics recognition to `QChatDataAgentDiagnosticsCommandContract`.
  - Leave `/qchat`, help aliases, approval commands, status commands, recall, and natural status logic unchanged.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
  - Dispatch DataAgent diagnostics through `QChatDataAgentDiagnosticsTopic`.
  - Reuse existing text builders for evidence, trace, progress, and graph.
  - Keep unknown `/dataagent` commands unhandled and unknown `/qchat` commands on the existing root-menu fallback.

- Modify: `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`
  - Cover every supported `/dataagent` diagnostics variant at the access policy boundary.

- Modify: `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`
  - Cover copied menu descriptions and strict unknown `/dataagent` command handling.

- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
  - Add explicit copied menu and unknown DataAgent diagnostics tests around the shared parser path.

- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Add full ingress test proving non-owner `/dataagent diag graph` is dropped before model dispatch and reply.

- Modify: `tools/check-qchat-engineering-map.ps1`
  - Add a required QChat engineering-map marker for the shared command contract.
  - Increase expected required checks from `60` to `61`.

- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Assert the new required engineering-map marker and expected count.
  - Assert production consumers use the shared command contract.

- Create: `docs/dataagent/dataagent-v2.17-diagnostics-command-contract.md`
  - Short developer note for V2.17 closure behavior and V3 handoff.

---

### Task 1: Add The Shared Command Contract

**Files:**
- Create: `Tests/Alife.Test.QChat/QChatDataAgentDiagnosticsCommandContractTests.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs`

- [ ] **Step 1: Write the failing contract tests**

Create `Tests/Alife.Test.QChat/QChatDataAgentDiagnosticsCommandContractTests.cs` with this content:

```csharp
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
```

- [ ] **Step 2: Run the contract tests and verify they fail before implementation**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDataAgentDiagnosticsCommandContractTests" -v:minimal
```

Expected: FAIL at compile time because `QChatDataAgentDiagnosticsCommandContract` and `QChatDataAgentDiagnosticsTopic` do not exist.

- [ ] **Step 3: Add the minimal production contract**

Create `sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs` with this content:

```csharp
using System;
using System.Collections.Generic;

namespace Alife.Function.QChat;

public enum QChatDataAgentDiagnosticsTopic
{
    Evidence,
    Trace,
    Progress,
    Graph
}

public static class QChatDataAgentDiagnosticsCommandContract
{
    const string DataAgentPrefix = "/dataagent";
    const string DiagPrefix = "diag";
    const string DiagnosticsPrefix = "diagnostics";
    const string QChatDataAgentDiagPrefix = "diag dataagent ";
    const string QChatDataAgentDiagnosticsPrefix = "diagnostics dataagent ";

    public static IReadOnlyList<string> SupportedDataAgentCommandSuffixes { get; } =
    [
        "diag evidence",
        "diagnostics evidence",
        "diag trace",
        "diagnostics trace",
        "diag progress",
        "diagnostics progress",
        "diag graph",
        "diagnostics graph"
    ];

    public static bool TryParseDataAgentCommand(string? text, out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = default;
        string trimmed = text?.TrimStart() ?? string.Empty;
        if (trimmed.StartsWith(DataAgentPrefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        if (trimmed.Length <= DataAgentPrefix.Length ||
            char.IsWhiteSpace(trimmed[DataAgentPrefix.Length]) == false)
        {
            return false;
        }

        return TryParseDataAgentCommandSuffix(trimmed[DataAgentPrefix.Length..].Trim(), out topic);
    }

    public static bool TryParseDataAgentCommandSuffix(string? command, out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = default;
        string normalized = StripCopiedMenuDescription(command?.Trim() ?? string.Empty);

        if (TryRemoveCommandPrefix(normalized, DiagPrefix, out string diagTopic))
            return TryParseTopic(diagTopic, out topic);

        if (TryRemoveCommandPrefix(normalized, DiagnosticsPrefix, out string diagnosticsTopic))
            return TryParseTopic(diagnosticsTopic, out topic);

        return false;
    }

    public static bool TryParseQChatDataAgentDiagnosticsCommandSuffix(
        string? command,
        out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = default;
        string normalized = StripCopiedMenuDescription(command?.Trim() ?? string.Empty);

        if (normalized.StartsWith(QChatDataAgentDiagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = $"{DiagPrefix} {normalized[QChatDataAgentDiagPrefix.Length..].Trim()}";
            return TryParseDataAgentCommandSuffix(suffix, out topic);
        }

        if (normalized.StartsWith(QChatDataAgentDiagnosticsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = $"{DiagnosticsPrefix} {normalized[QChatDataAgentDiagnosticsPrefix.Length..].Trim()}";
            return TryParseDataAgentCommandSuffix(suffix, out topic);
        }

        return false;
    }

    static bool TryRemoveCommandPrefix(string command, string prefix, out string suffix)
    {
        suffix = string.Empty;
        if (command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        if (command.Length <= prefix.Length ||
            char.IsWhiteSpace(command[prefix.Length]) == false)
        {
            return false;
        }

        suffix = command[prefix.Length..].Trim();
        return true;
    }

    static bool TryParseTopic(string topicText, out QChatDataAgentDiagnosticsTopic topic)
    {
        topic = topicText.ToLowerInvariant() switch
        {
            "evidence" => QChatDataAgentDiagnosticsTopic.Evidence,
            "trace" => QChatDataAgentDiagnosticsTopic.Trace,
            "progress" => QChatDataAgentDiagnosticsTopic.Progress,
            "graph" => QChatDataAgentDiagnosticsTopic.Graph,
            _ => default
        };

        return topicText.Equals("evidence", StringComparison.OrdinalIgnoreCase) ||
               topicText.Equals("trace", StringComparison.OrdinalIgnoreCase) ||
               topicText.Equals("progress", StringComparison.OrdinalIgnoreCase) ||
               topicText.Equals("graph", StringComparison.OrdinalIgnoreCase);
    }

    static string StripCopiedMenuDescription(string command)
    {
        int descriptionStart = command.IndexOf(" - ", StringComparison.Ordinal);
        return descriptionStart >= 0 ? command[..descriptionStart].TrimEnd() : command;
    }
}
```

- [ ] **Step 4: Run the contract tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDataAgentDiagnosticsCommandContractTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatDataAgentDiagnosticsCommandContract.cs Tests\Alife.Test.QChat\QChatDataAgentDiagnosticsCommandContractTests.cs
git commit -m "Add QChat DataAgent diagnostics command contract"
```

---

### Task 2: Wire Access Policy And Owner Command Detection

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`

- [ ] **Step 1: Expand access policy tests for every DataAgent diagnostics variant**

In `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`, use focused ASCII-only edits. Do not replace the whole file, because the lower non-command text cases contain existing mojibake strings that should not be touched in this task.

Add this static command table immediately after the class opening line:

```csharp
    static readonly string[] DataAgentDiagnosticCommands =
    [
        "/dataagent diag evidence",
        "/dataagent diagnostics evidence",
        "/dataagent diag evidence - DataAgent evidence diagnostics",
        "/dataagent diag trace",
        "/dataagent diagnostics trace",
        "/dataagent diag progress",
        "/dataagent diagnostics progress",
        "/dataagent diag graph",
        "/dataagent diagnostics graph",
        "/dataagent diag graph - DataAgent DataQueryGraph dry-run diagnostics",
        "  /DATAAGENT diag evidence  "
    ];
```

Replace the `[TestCase(...)]` attributes above `OwnerDataAgentDiagnosticCommandIsAllowed` with:

```csharp
    [TestCaseSource(nameof(DataAgentDiagnosticCommands))]
```

Keep this existing method body unchanged:

```csharp
    public void OwnerDataAgentDiagnosticCommandIsAllowed(string text)
    {
        QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
            new QChatCommandAccessContext(text, QChatSenderRole.Owner));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.AllowOwnerCommand));
            Assert.That(decision.Reason, Is.EqualTo("owner_qchat_command"));
        });
    }
```

Remove these three old non-owner DataAgent diagnostic methods:

```text
NonOwnerDataAgentDiagEvidenceCommandIsDroppedSilently
NonOwnerDataAgentDiagnosticsEvidenceCommandIsDroppedSilently
NonOwnerDataAgentDiagGraphCommandIsDroppedSilently
```

Add these two table-driven methods where the removed methods were:

```csharp
    [TestCaseSource(nameof(DataAgentDiagnosticCommands))]
    public void NonOwnerPrivateGuestDataAgentDiagnosticCommandIsDroppedSilently(string text)
    {
        QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
            new QChatCommandAccessContext(text, QChatSenderRole.PrivateGuest));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.DropSilently));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_qchat_command"));
        });
    }

    [TestCaseSource(nameof(DataAgentDiagnosticCommands))]
    public void NonOwnerGroupMemberDataAgentDiagnosticCommandIsDroppedSilently(string text)
    {
        QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
            new QChatCommandAccessContext(text, QChatSenderRole.GroupMember));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.DropSilently));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_qchat_command"));
        });
    }
```

Add the unknown-topic case to the existing `UnknownDataAgentCommandsPassThrough` attributes:

```csharp
    [TestCase("/dataagent diag unknown")]
```
- [ ] **Step 2: Expand owner command service command table**

In `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`, replace the `IsDiagnosticsCommandMatchesOnlyQChatAndDataAgentDiagnosticsCommands` test case block with:

```csharp
    [TestCase("/qchat", true)]
    [TestCase("/qchat route", true)]
    [TestCase("  /QCHAT identity  ", true)]
    [TestCase("/dataagent diag evidence", true)]
    [TestCase("/dataagent diag evidence - DataAgent evidence diagnostics", true)]
    [TestCase("/dataagent diagnostics evidence", true)]
    [TestCase("/dataagent diag trace", true)]
    [TestCase("/dataagent diagnostics trace", true)]
    [TestCase("/dataagent diagnostics trace - DataAgent trace diagnostics", true)]
    [TestCase("/dataagent diag progress", true)]
    [TestCase("/dataagent diagnostics progress", true)]
    [TestCase("/dataagent diag graph", true)]
    [TestCase("/dataagent diagnostics graph", true)]
    [TestCase("/dataagent diag graph - DataAgent DataQueryGraph dry-run diagnostics", true)]
    [TestCase("/qchatx route", false)]
    [TestCase("/dataagent", false)]
    [TestCase("/dataagent nope", false)]
    [TestCase("/dataagent diag unknown", false)]
    [TestCase("/dataagent diagnostics", false)]
    [TestCase("/dataagentx diag evidence", false)]
    [TestCase("/dataagent/diag evidence", false)]
    [TestCase("hello /qchat route", false)]
    public void IsDiagnosticsCommandMatchesOnlyQChatAndDataAgentDiagnosticsCommands(string text, bool expected)
    {
        Assert.That(QChatOwnerCommandService.IsDiagnosticsCommand(text.Trim()), Is.EqualTo(expected));
    }
```

- [ ] **Step 3: Run the focused tests before refactor**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatOwnerCommandServiceTests" -v:minimal
```

Expected: PASS with `Failed: 0`. These are characterization tests for current behavior before replacing duplicated matching code.

- [ ] **Step 4: Refactor `QChatCommandAccessPolicy` to use the shared contract**

Replace the full content of `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs` with:

```csharp
using System;

namespace Alife.Function.QChat;

public enum QChatCommandAccessAction
{
    NotCommand,
    AllowOwnerCommand,
    DropSilently
}

public sealed record QChatCommandAccessContext(
    string? PlainText,
    QChatSenderRole SenderRole);

public sealed record QChatCommandAccessDecision(
    QChatCommandAccessAction Action,
    string Reason);

public static class QChatCommandAccessPolicy
{
    const string QChatPrefix = "/qchat";

    public static QChatCommandAccessDecision Evaluate(QChatCommandAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsOwnerDiagnosticCommand(context.PlainText) == false)
            return new QChatCommandAccessDecision(
                QChatCommandAccessAction.NotCommand,
                "not_qchat_command");

        if (context.SenderRole == QChatSenderRole.Owner)
            return new QChatCommandAccessDecision(
                QChatCommandAccessAction.AllowOwnerCommand,
                "owner_qchat_command");

        return new QChatCommandAccessDecision(
            QChatCommandAccessAction.DropSilently,
            "non_owner_qchat_command");
    }

    public static bool IsQChatCommand(string? text)
    {
        return IsCommandWithPrefix(text, QChatPrefix);
    }

    public static bool IsOwnerDiagnosticCommand(string? text)
    {
        return IsCommandWithPrefix(text, QChatPrefix) ||
               QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand(text, out _);
    }

    static bool IsCommandWithPrefix(string? text, string prefix)
    {
        string trimmed = text?.TrimStart() ?? string.Empty;
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        return trimmed.Length == prefix.Length ||
               char.IsWhiteSpace(trimmed[prefix.Length]);
    }
}
```

- [ ] **Step 5: Refactor `QChatOwnerCommandService.IsDiagnosticsCommand` to use the shared contract**

In `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`, replace the `IsDiagnosticsCommand` method with:

```csharp
    public static bool IsDiagnosticsCommand(string text)
    {
        const string qchatPrefix = "/qchat";
        if (text.StartsWith(qchatPrefix, StringComparison.OrdinalIgnoreCase) &&
            (text.Length == qchatPrefix.Length || char.IsWhiteSpace(text[qchatPrefix.Length])))
        {
            return true;
        }

        return QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand(text, out _);
    }
```

Then remove the private `StripCopiedMenuDescription` method from the same file:

```csharp
    static string StripCopiedMenuDescription(string command)
    {
        int descriptionStart = command.IndexOf(" - ", StringComparison.Ordinal);
        return descriptionStart >= 0 ? command[..descriptionStart].TrimEnd() : command;
    }
```

- [ ] **Step 6: Run the focused tests after refactor**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatDataAgentDiagnosticsCommandContractTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 7: Commit Task 2**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatCommandAccessPolicy.cs sources\Alife.Function\Alife.Function.QChat\QChatOwnerCommandService.cs Tests\Alife.Test.QChat\QChatCommandAccessPolicyTests.cs Tests\Alife.Test.QChat\QChatOwnerCommandServiceTests.cs
git commit -m "Use shared DataAgent diagnostics command contract"
```

---

### Task 3: Route Diagnostics Rendering Through Parsed Topics

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`

- [ ] **Step 1: Add diagnostics service parser boundary tests**

In `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`, add these tests near the existing DataAgent diagnostics tests:

```csharp
    [TestCase("/dataagent diag graph - DataAgent DataQueryGraph dry-run diagnostics")]
    [TestCase("/qchat diagnostics dataagent trace - DataAgent trace diagnostics")]
    public void TryHandleDataAgentDiagnosticsStripsCopiedMenuDescription(string command)
    {
        QChatDiagnosticsRuntimeState state = new(
            RecentDataAgentTrace: "DataAgent trace diagnostics\ntrace_marker=copied_menu",
            RecentDataAgentGraph: "DataQueryGraph dry-run\ngraph_marker=copied_menu");

        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            command,
            CreateRoute(),
            CreateProfile(),
            state);

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.True);
            Assert.That(result.Text, Does.Contain("copied_menu"));
        });
    }

    [TestCase("/dataagent diag unknown")]
    [TestCase("/dataagent diagnostics graph extra")]
    [TestCase("/dataagent")]
    public void TryHandleUnknownDataAgentDiagnosticsCommandReturnsUnhandled(string command)
    {
        QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
            command,
            CreateRoute(),
            CreateProfile(),
            new QChatDiagnosticsRuntimeState());

        Assert.Multiple(() =>
        {
            Assert.That(result.Handled, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }
```

- [ ] **Step 2: Run the diagnostics tests before refactor**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests" -v:minimal
```

Expected: PASS with `Failed: 0`. These tests preserve current visible behavior before replacing duplicated command strings.

- [ ] **Step 3: Replace the `/dataagent` branch in `QChatDiagnosticsService.TryHandle`**

In `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`, replace this block:

```csharp
        if (dataAgentCommand)
            return command.ToLowerInvariant() switch
            {
                "diag evidence" or "diagnostics evidence" => Handled(BuildDataAgentEvidenceDiagnosticsText(runtimeState, route)),
                "diag trace" or "diagnostics trace" => Handled(BuildDataAgentTraceDiagnosticsText(runtimeState, route)),
                "diag progress" or "diagnostics progress" => Handled(BuildDataAgentProgressDiagnosticsText(runtimeState, route)),
                "diag graph" or "diagnostics graph" => Handled(BuildDataAgentGraphDiagnosticsText(runtimeState, route)),
                _ => new QChatDiagnosticsResult(false, string.Empty)
            };
```

with:

```csharp
        if (dataAgentCommand)
        {
            return QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommandSuffix(command, out QChatDataAgentDiagnosticsTopic topic)
                ? Handled(BuildDataAgentDiagnosticsText(topic, runtimeState, route))
                : new QChatDiagnosticsResult(false, string.Empty);
        }

        if (QChatDataAgentDiagnosticsCommandContract.TryParseQChatDataAgentDiagnosticsCommandSuffix(command, out QChatDataAgentDiagnosticsTopic qchatDataAgentTopic))
            return Handled(BuildDataAgentDiagnosticsText(qchatDataAgentTopic, runtimeState, route));
```

- [ ] **Step 4: Remove duplicated QChat DataAgent switch arms**

In the large `/qchat` command switch in `QChatDiagnosticsService.TryHandle`, remove these arms:

```csharp
            "diag dataagent evidence" or "diagnostics dataagent evidence" => Handled(BuildDataAgentEvidenceDiagnosticsText(runtimeState, route)),
            "diag dataagent trace" or "diagnostics dataagent trace" => Handled(BuildDataAgentTraceDiagnosticsText(runtimeState, route)),
            "diag dataagent progress" or "diagnostics dataagent progress" => Handled(BuildDataAgentProgressDiagnosticsText(runtimeState, route)),
            "diag dataagent graph" or "diagnostics dataagent graph" => Handled(BuildDataAgentGraphDiagnosticsText(runtimeState, route)),
```

- [ ] **Step 5: Add a topic dispatch helper**

In `QChatDiagnosticsService.cs`, add this helper immediately after `Handled`:

```csharp
    static string BuildDataAgentDiagnosticsText(
        QChatDataAgentDiagnosticsTopic topic,
        QChatDiagnosticsRuntimeState runtimeState,
        QChatAgentRoute route)
    {
        return topic switch
        {
            QChatDataAgentDiagnosticsTopic.Evidence => BuildDataAgentEvidenceDiagnosticsText(runtimeState, route),
            QChatDataAgentDiagnosticsTopic.Trace => BuildDataAgentTraceDiagnosticsText(runtimeState, route),
            QChatDataAgentDiagnosticsTopic.Progress => BuildDataAgentProgressDiagnosticsText(runtimeState, route),
            QChatDataAgentDiagnosticsTopic.Graph => BuildDataAgentGraphDiagnosticsText(runtimeState, route),
            _ => throw new InvalidOperationException($"Unknown DataAgent diagnostics topic: {topic}")
        };
    }
```

- [ ] **Step 6: Run focused diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatDataAgentDiagnosticsCommandContractTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 7: Commit Task 3**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatDiagnosticsService.cs Tests\Alife.Test.QChat\QChatDiagnosticsServiceTests.cs
git commit -m "Dispatch QChat DataAgent diagnostics through parsed topics"
```

---

### Task 4: Add QChat Adapter Ingress Hardening Test

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add non-owner `/dataagent diag graph` ingress test**

In `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`, add this test near `OwnerCanReadRecentDataAgentGraphDiagnosticsRecordedOnService`:

```csharp
    [Test]
    public async Task NonOwnerPrivateDataAgentGraphDiagnosticDropsBeforeModelDispatch()
    {
        await WithIsolatedQChatDiagnosticsAsync(async storageRoot =>
        {
            FakeOneBotRuntime runtime = new();
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowPrivateGuestChat = true,
                EnableBalancedTextStreaming = false
            });
            int dispatchCount = 0;
            service.InboundChatDispatcher = _ =>
            {
                dispatchCount++;
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                UserId = 2002,
                SelfId = 999,
                RawMessage = "/dataagent diag graph"
            });

            string diagnostics = await WaitForQChatCommandDroppedDiagnosticAsync(storageRoot);
            string pending = GetPendingPokeText(service);
            Assert.Multiple(() =>
            {
                Assert.That(dispatchCount, Is.Zero);
                Assert.That(runtime.PrivateMessages, Is.Empty);
                Assert.That(runtime.GroupMessages, Is.Empty);
                Assert.That(pending, Does.Not.Contain("/dataagent"));
                Assert.That(diagnostics, Does.Contain("\"eventName\":\"qchat-command-dropped\""));
                Assert.That(diagnostics, Does.Not.Contain("\"eventName\":\"event-filtered\""));
            });
        });
    }
```

- [ ] **Step 2: Run the adapter test**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~NonOwnerPrivateDataAgentGraphDiagnosticDropsBeforeModelDispatch|FullyQualifiedName~OwnerCanReadRecentDataAgentGraphDiagnosticsRecordedOnService" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 3: Commit Task 4**

Run:

```powershell
git add Tests\Alife.Test.QChat\QChatServiceAdapterTests.cs
git commit -m "Harden QChat DataAgent graph diagnostics ingress"
```

---

### Task 5: Add QChat Engineering-Map Gate

**Files:**
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Add the required engineering-map check**

In `tools/check-qchat-engineering-map.ps1`, add this `Add-Check` immediately after the existing `DataAgent DataQueryGraph owner diagnostics` check:

```powershell
Add-Check -Group "Harness" -Name "DataAgent diagnostics command contract" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs" -Patterns @("QChatDataAgentDiagnosticsCommandContract", "QChatDataAgentDiagnosticsTopic", "SupportedDataAgentCommandSuffixes", "TryParseDataAgentCommand", "TryParseDataAgentCommandSuffix", "TryParseQChatDataAgentDiagnosticsCommandSuffix") -AlsoPath "sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs" -AlsoPatterns @("QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentDataQueryGraph")
```

Then change:

```powershell
$expectedRequired = 60
```

to:

```powershell
$expectedRequired = 61
```

- [ ] **Step 2: Add engineering-map tests**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add this test after `DataQueryGraphOwnerDiagnosticsCheckRequiresStringBridgeAndQChatBoundary`:

```csharp
    [Test]
    public void DataAgentDiagnosticsCommandContractCheckRequiresSharedParserAndQChatBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent diagnostics command contract");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("QChatDataAgentDiagnosticsCommandContract"));
            Assert.That(declaration, Does.Contain("QChatDataAgentDiagnosticsTopic"));
            Assert.That(declaration, Does.Contain("SupportedDataAgentCommandSuffixes"));
            Assert.That(declaration, Does.Contain("TryParseDataAgentCommand"));
            Assert.That(declaration, Does.Contain("TryParseDataAgentCommandSuffix"));
            Assert.That(declaration, Does.Contain("TryParseQChatDataAgentDiagnosticsCommandSuffix"));
            Assert.That(declaration, Does.Contain("QChatCommandAccessPolicy.cs"));
            Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraph"));
        });
    }
```

Add this test after `QChatDoesNotDirectlyImportDataAgentBoundaryTypes`:

```csharp
    [Test]
    public void DataAgentDiagnosticsCommandConsumersUseSharedContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string qchatRoot = Path.Combine(repoRoot, "sources", "Alife.Function", "Alife.Function.QChat");
        string accessPolicy = File.ReadAllText(Path.Combine(qchatRoot, "QChatCommandAccessPolicy.cs"));
        string ownerCommands = File.ReadAllText(Path.Combine(qchatRoot, "QChatOwnerCommandService.cs"));
        string diagnosticsService = File.ReadAllText(Path.Combine(qchatRoot, "QChatDiagnosticsService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(accessPolicy, Does.Contain("QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand"));
            Assert.That(ownerCommands, Does.Contain("QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand"));
            Assert.That(diagnosticsService, Does.Contain("QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommandSuffix"));
            Assert.That(diagnosticsService, Does.Contain("QChatDataAgentDiagnosticsCommandContract.TryParseQChatDataAgentDiagnosticsCommandSuffix"));
            Assert.That(diagnosticsService, Does.Contain("BuildDataAgentDiagnosticsText"));
        });
    }
```

In `QChatEngineeringMapDefaultModeExitsZeroAndPrintsSummary`, change the expected summary line to:

```csharp
                "Summary: 61 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

In `QChatEngineeringMapScriptProtectsRequiredCheckCount`, change:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 60"));
```

to:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 61"));
```

- [ ] **Step 3: Run engineering-map tests and script**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: PASS with `Failed: 0`.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 61 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Commit Task 5**

Run:

```powershell
git add tools\check-qchat-engineering-map.ps1 Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Add QChat DataAgent diagnostics command contract readiness"
```

---

### Task 6: Add V2.17 Developer Note

**Files:**
- Create: `docs/dataagent/dataagent-v2.17-diagnostics-command-contract.md`

- [ ] **Step 1: Create the developer note**

Create `docs/dataagent/dataagent-v2.17-diagnostics-command-contract.md` with this content:

````markdown
# DataAgent V2.17 Diagnostics Command Contract

V2.17 is a QChat ingress hardening release for DataAgent owner diagnostics.

It centralizes the supported DataAgent diagnostics command vocabulary in one QChat-local parser:

```text
/dataagent diag evidence
/dataagent diagnostics evidence
/dataagent diag trace
/dataagent diagnostics trace
/dataagent diag progress
/dataagent diagnostics progress
/dataagent diag graph
/dataagent diagnostics graph
```

QChat diagnostics aliases remain supported:

```text
/qchat diag dataagent evidence
/qchat diagnostics dataagent evidence
/qchat diag dataagent trace
/qchat diagnostics dataagent trace
/qchat diag dataagent progress
/qchat diagnostics dataagent progress
/qchat diag dataagent graph
/qchat diagnostics dataagent graph
```

The command contract is intentionally string-only. It returns QChat-local diagnostics topics and does not reference DataAgent graph model types.

## Safety Boundary

- QChat remains a string-only consumer of DataAgent diagnostics.
- DataAgent remains the owner of QueryPlan, SQL validation, SQL compilation, SQL safety, read-only execution, checkpoint persistence, evidence, trace, progress, and DataQueryGraph dry-run projection.
- Non-owner diagnostics commands are dropped before model dispatch and do not receive a visible denial reply.
- Unknown `/dataagent` commands fail closed and are not treated as owner diagnostics commands.
- QChat must not import `DataAgentDataQueryGraph*` types.

## V3 Handoff

V2.17 closes the V2 line unless verification exposes a real boundary gap.

V3.0 can start after V2.17 is implemented and verified. The first V3 milestone should connect any LangGraph sidecar behind the existing C# contract, keep SQL authority in C#, and expose only scoped node manifests to reduce attention dilution and random tool choice.
````

- [ ] **Step 2: Run documentation and boundary checks**

Run:

```powershell
rg -n "DataAgentDataQueryGraph" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 61 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 3: Commit Task 6**

Run:

```powershell
git add docs\dataagent\dataagent-v2.17-diagnostics-command-contract.md
git commit -m "Document DataAgent V2.17 diagnostics command contract"
```

---

### Task 7: Final Verification

**Files:**
- Verify all files changed in Tasks 1-6.

- [ ] **Step 1: Check the full staged and unstaged diff**

Run:

```powershell
git status --short --branch
git diff --stat
```

Expected:

- Branch shows the V2.17 commits ahead of `alife-byastralfox/master`.
- No unstaged implementation changes remain after all task commits.

- [ ] **Step 2: Run focused QChat tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDataAgentDiagnosticsCommandContractTests|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 3: Run QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 61 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run QChat DataQueryGraph boundary scan**

Run:

```powershell
rg -n "DataAgentDataQueryGraph" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches.

- [ ] **Step 5: Run restore**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
```

Expected: restore completes with exit code `0`.

- [ ] **Step 6: Run build**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal -m:1
```

Expected: build completes with exit code `0` and `0 Error(s)`.

- [ ] **Step 7: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 8: Record final status**

Run:

```powershell
git status --short --branch
git log --oneline -8
```

Expected:

- Working tree is clean.
- Recent commits include the V2.17 implementation commits.

---

## Self-Review

Spec coverage:

- Shared QChat-local command contract: Task 1.
- Access policy delegates `/dataagent` diagnostics matching: Task 2.
- Owner command service delegates `/dataagent` diagnostics matching: Task 2.
- Diagnostics service dispatches through parsed topics: Task 3.
- Supported vocabulary remains unchanged: Task 1 tests and Task 3 tests.
- Unknown `/dataagent` commands fail closed: Task 1, Task 2, and Task 3 tests.
- Non-owner `/dataagent diag graph` drops before model dispatch: Task 4.
- QChat omits `DataAgentDataQueryGraph*` types: Task 5, Task 6, and Task 7.
- QChat engineering-map gate: Task 5.
- Developer note: Task 6.
- Focused and full verification: Task 7.

Placeholder scan:

- The plan contains no unresolved placeholder markers.
- The plan contains no empty future-work markers.
- Every code-changing step includes exact file paths and code snippets.
- Every test command includes expected outcome.

Type consistency:

- The enum name is `QChatDataAgentDiagnosticsTopic` in tests and production.
- The parser type is `QChatDataAgentDiagnosticsCommandContract` in tests, production, engineering-map script, and engineering-map tests.
- The parser methods are consistently named `TryParseDataAgentCommand`, `TryParseDataAgentCommandSuffix`, and `TryParseQChatDataAgentDiagnosticsCommandSuffix`.
- The diagnostics dispatch helper is consistently named `BuildDataAgentDiagnosticsText`.
