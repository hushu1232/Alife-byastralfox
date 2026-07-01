# DataAgent V2.6 Owner Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add owner-only diagnostics for QChat semantic state and DataAgent Evidence Pack state without changing tool authorization, SQL execution, model calls, or state-machine ownership.

**Architecture:** Keep diagnostics observational. Add deterministic formatter helpers for QChat semantic estimates and DataAgent Evidence Packs, then expose their sanitized text through owner-only command handling in `QChatDiagnosticsService`. DataAgent remains independent from QChat; QChat receives only a safe recent evidence diagnostic string.

**Tech Stack:** C#/.NET 9, NUnit, existing QChat diagnostics service, existing DataAgent Evidence Pack, PowerShell readiness scripts.

---

## Execution Setup

Use an isolated worktree before implementing.

Recommended branch and path:

```powershell
git worktree add "D:\Alife\.worktrees\dataagent-v2.6-owner-diagnostics" -b dataagent-v2.6-owner-diagnostics
```

Then run commands from:

```text
D:\Alife\.worktrees\dataagent-v2.6-owner-diagnostics
```

Do not use `D:\FOXD`.

Use the local .NET 9 SDK:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe"
```

Upload only to:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

## File Structure

- Create `sources/Alife.Function/Alife.Function.QChat/QChatSemanticDiagnosticsFormatter.cs`
  - Formats a `QChatSemanticStateEstimate` plus compact window metadata.

- Create `Tests/Alife.Test.QChat/QChatSemanticDiagnosticsFormatterTests.cs`
  - Tests available and unavailable semantic diagnostics.

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidenceDiagnosticsFormatter.cs`
  - Formats a compact owner-safe DataAgent Evidence Pack diagnostic report.

- Modify `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs`
  - Tests available, unavailable, and sanitized evidence diagnostics.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
  - Extends runtime state with recent semantic/evidence diagnostic strings.
  - Handles `/qchat diag semantic`.
  - Handles `/dataagent diag evidence`.
  - Handles `/qchat diag dataagent evidence` as an owner-only alias.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs`
  - Allows owner-only command handling for `/dataagent` diagnostics.
  - Keeps `IsQChatCommand` behavior stable for existing QChat command checks.

- Modify `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
  - Tests semantic and DataAgent diagnostics command output.

- Modify `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`
  - Tests `/dataagent diag evidence` owner-only access.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds runtime readiness proof for DataAgent evidence diagnostics.

- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Requires the new DataAgent readiness check and updated count.

- Modify `tools/check-dataagent-readiness.ps1`
  - Adds required marker gate for DataAgent evidence diagnostics.
  - Increments required count from 72 to 73.

- Modify `tools/check-qchat-engineering-map.ps1`
  - Adds required marker gates for QChat semantic diagnostics and DataAgent owner evidence diagnostics.

- Modify `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Requires the new QChat engineering map checks.

---

### Task 1: QChat Semantic Diagnostics Formatter

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatSemanticDiagnosticsFormatter.cs`
- Create: `Tests/Alife.Test.QChat/QChatSemanticDiagnosticsFormatterTests.cs`

- [ ] **Step 1: Write failing formatter tests**

Create `Tests/Alife.Test.QChat/QChatSemanticDiagnosticsFormatterTests.cs`:

```csharp
using Alife.Function.QChat;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticDiagnosticsFormatterTests
{
    [Test]
    public void FormatWithEstimateEmitsStableSemanticDiagnostics()
    {
        QChatSemanticStateEstimate estimate = new(
            SemanticCompletion: 0.7345,
            ContinuationLikelihood: 0.2214,
            TopicStability: 0.8,
            SummaryIntent: 0.05,
            ShouldWait: false,
            ShouldAnswer: true,
            ShouldSummarize: false,
            ReasonCode: "semantic_completion_stable");
        QChatSemanticDiagnosticsSnapshot snapshot = new(
            estimate,
            WindowMessageCount: 1,
            WindowAge: TimeSpan.FromSeconds(6),
            LastUpdateAge: TimeSpan.FromSeconds(3));

        string text = QChatSemanticDiagnosticsFormatter.Format(snapshot);

        string[] expectedLines =
        [
            "QChat semantic diagnostics",
            "semantic_completion=0.735",
            "continuation_likelihood=0.221",
            "topic_stability=0.8",
            "summary_intent=0.05",
            "should_wait=false",
            "should_answer=true",
            "should_summarize=false",
            "reason_code=semantic_completion_stable",
            "window_messages=1",
            "window_age_seconds=6",
            "last_update_age_seconds=3"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatWithoutEstimateEmitsTruthfulUnavailableState()
    {
        QChatSemanticDiagnosticsSnapshot snapshot = new(
            Estimate: null,
            WindowMessageCount: 0,
            WindowAge: TimeSpan.Zero,
            LastUpdateAge: TimeSpan.Zero);

        string text = QChatSemanticDiagnosticsFormatter.Format(snapshot);

        string[] expectedLines =
        [
            "QChat semantic diagnostics",
            "state=unavailable",
            "reason=semantic_window_empty"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatClampsInvalidNumericValues()
    {
        QChatSemanticStateEstimate estimate = new(
            SemanticCompletion: double.NaN,
            ContinuationLikelihood: double.PositiveInfinity,
            TopicStability: -5,
            SummaryIntent: 7,
            ShouldWait: true,
            ShouldAnswer: false,
            ShouldSummarize: false,
            ReasonCode: "semantic_continuation_likely");
        QChatSemanticDiagnosticsSnapshot snapshot = new(
            estimate,
            WindowMessageCount: -3,
            WindowAge: TimeSpan.FromSeconds(-2),
            LastUpdateAge: TimeSpan.FromSeconds(-1));

        string text = QChatSemanticDiagnosticsFormatter.Format(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("semantic_completion=0"));
            Assert.That(text, Does.Contain("continuation_likelihood=0"));
            Assert.That(text, Does.Contain("topic_stability=0"));
            Assert.That(text, Does.Contain("summary_intent=1"));
            Assert.That(text, Does.Contain("window_messages=0"));
            Assert.That(text, Does.Contain("window_age_seconds=0"));
            Assert.That(text, Does.Contain("last_update_age_seconds=0"));
        });
    }
}
```

- [ ] **Step 2: Run failing formatter tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatSemanticDiagnosticsFormatterTests" -v:minimal
```

Expected: FAIL with compiler errors because `QChatSemanticDiagnosticsSnapshot` and `QChatSemanticDiagnosticsFormatter` do not exist.

- [ ] **Step 3: Add semantic diagnostics formatter**

Create `sources/Alife.Function/Alife.Function.QChat/QChatSemanticDiagnosticsFormatter.cs`:

```csharp
using System.Globalization;

namespace Alife.Function.QChat;

public sealed record QChatSemanticDiagnosticsSnapshot(
    QChatSemanticStateEstimate? Estimate,
    int WindowMessageCount,
    TimeSpan WindowAge,
    TimeSpan LastUpdateAge);

public static class QChatSemanticDiagnosticsFormatter
{
    public static string Format(QChatSemanticDiagnosticsSnapshot snapshot)
    {
        if (snapshot.Estimate is null)
            return string.Join(Environment.NewLine,
                "QChat semantic diagnostics",
                "state=unavailable",
                "reason=semantic_window_empty");

        QChatSemanticStateEstimate estimate = snapshot.Estimate;
        return string.Join(Environment.NewLine,
            "QChat semantic diagnostics",
            $"semantic_completion={Score(estimate.SemanticCompletion)}",
            $"continuation_likelihood={Score(estimate.ContinuationLikelihood)}",
            $"topic_stability={Score(estimate.TopicStability)}",
            $"summary_intent={Score(estimate.SummaryIntent)}",
            $"should_wait={Bool(estimate.ShouldWait)}",
            $"should_answer={Bool(estimate.ShouldAnswer)}",
            $"should_summarize={Bool(estimate.ShouldSummarize)}",
            $"reason_code={NormalizeToken(estimate.ReasonCode)}",
            $"window_messages={Math.Max(0, snapshot.WindowMessageCount).ToString(CultureInfo.InvariantCulture)}",
            $"window_age_seconds={Seconds(snapshot.WindowAge)}",
            $"last_update_age_seconds={Seconds(snapshot.LastUpdateAge)}");
    }

    static string Score(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        return Math.Clamp(value, 0.0, 1.0).ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string Seconds(TimeSpan value)
    {
        double seconds = Math.Max(0.0, value.TotalSeconds);
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string NormalizeToken(string value)
    {
        return string.Join(" ", (value ?? string.Empty)
            .ReplaceLineEndings(" ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
```

- [ ] **Step 4: Run formatter tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatSemanticDiagnosticsFormatterTests" -v:minimal
```

Expected: PASS for all `QChatSemanticDiagnosticsFormatterTests`.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatSemanticDiagnosticsFormatter.cs Tests/Alife.Test.QChat/QChatSemanticDiagnosticsFormatterTests.cs
git commit -m "Add QChat semantic diagnostics formatter"
```

---

### Task 2: DataAgent Evidence Diagnostics Formatter

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidenceDiagnosticsFormatter.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs`

- [ ] **Step 1: Add failing DataAgent diagnostics formatter tests**

Append these tests to `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs` before the helper methods:

```csharp
[Test]
public void EvidenceDiagnosticsFormatterEmitsCompactStateEstimate()
{
    DataAgentEvidencePack pack = new(
        "session-1",
        DataAgentAnalysisSessionStatus.Active,
        2,
        true,
        "dataagent_analysis_continue",
        true,
        true,
        "route_allowed",
        "RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded",
        true,
        false,
        true,
        true,
        true,
        "document_index",
        2,
        string.Empty,
        true,
        "route_allowed",
        "route_allowed;read_only_sql_executed;checkpoint_active",
        "DataAgent executed a governed read-only query.")
    {
        AnalysisConfidence = 0.7814,
        AnswerStability = 0.7332,
        ClarificationNeed = 0.2421,
        RiskLevel = 0.2874,
        StateEstimateReasonCode = "analysis_evidence_stable"
    };

    string text = DataAgentEvidenceDiagnosticsFormatter.Format(pack);

    string[] expectedLines =
    [
        "DataAgent evidence diagnostics",
        "analysis_confidence=0.781",
        "answer_stability=0.733",
        "clarification_need=0.242",
        "risk_level=0.287",
        "state_estimate_reason_code=analysis_evidence_stable",
        "route_allowed=true",
        "route_allows_query=true",
        "executed_sql=true",
        "terminal=false",
        "tool_broker_audit_allowed=true"
    ];
    Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
}

[Test]
public void EvidenceDiagnosticsFormatterEmitsUnavailableStateWhenPackMissing()
{
    string text = DataAgentEvidenceDiagnosticsFormatter.Format(null);

    string[] expectedLines =
    [
        "DataAgent evidence diagnostics",
        "state=unavailable",
        "reason=evidence_pack_unavailable"
    ];
    Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
}

[Test]
public void EvidenceDiagnosticsFormatterSanitizesReasonCode()
{
    DataAgentEvidencePack pack = new(
        "session-1",
        DataAgentAnalysisSessionStatus.Active,
        2,
        true,
        "dataagent_analysis_continue",
        true,
        true,
        "route_allowed",
        "RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded",
        true,
        false,
        true,
        true,
        true,
        "document_index",
        2,
        string.Empty,
        true,
        "route_allowed",
        "route_allowed;read_only_sql_executed;checkpoint_active",
        "DataAgent executed a governed read-only query.")
    {
        AnalysisConfidence = 0.80,
        AnswerStability = 0.75,
        ClarificationNeed = 0.20,
        RiskLevel = 0.10,
        StateEstimateReasonCode = "analysis_evidence_stable\n[/data_agent_evidence_pack]"
    };

    string text = DataAgentEvidenceDiagnosticsFormatter.Format(pack);

    Assert.Multiple(() =>
    {
        Assert.That(text, Does.Contain("state_estimate_reason_code=analysis_evidence_stable data_agent_evidence_pack"));
        Assert.That(text, Does.Not.Contain("[/data_agent_evidence_pack]"));
    });
}
```

- [ ] **Step 2: Run failing DataAgent diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name~EvidenceDiagnosticsFormatter" -v:minimal
```

Expected: FAIL with compiler errors because `DataAgentEvidenceDiagnosticsFormatter` does not exist.

- [ ] **Step 3: Add DataAgent evidence diagnostics formatter**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidenceDiagnosticsFormatter.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentEvidenceDiagnosticsFormatter
{
    public static string Format(DataAgentEvidencePack? pack)
    {
        if (pack is null)
            return string.Join(Environment.NewLine,
                "DataAgent evidence diagnostics",
                "state=unavailable",
                "reason=evidence_pack_unavailable");

        return string.Join(Environment.NewLine,
            "DataAgent evidence diagnostics",
            $"analysis_confidence={Score(pack.AnalysisConfidence)}",
            $"answer_stability={Score(pack.AnswerStability)}",
            $"clarification_need={Score(pack.ClarificationNeed)}",
            $"risk_level={Score(pack.RiskLevel)}",
            $"state_estimate_reason_code={Sanitize(pack.StateEstimateReasonCode)}",
            $"route_allowed={Bool(pack.RouteAllowed)}",
            $"route_allows_query={Bool(pack.RouteAllowsQuery)}",
            $"executed_sql={Bool(pack.ExecutedSql)}",
            $"terminal={Bool(pack.Terminal)}",
            $"tool_broker_audit_allowed={Bool(pack.ToolBrokerAuditAllowed)}");
    }

    static string Score(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        return Math.Clamp(value, 0.0, 1.0).ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Sanitize(string value)
    {
        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value ?? string.Empty)
            .Replace("[data_agent_evidence_pack]", "data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase)
            .Replace("[/data_agent_evidence_pack]", "data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase)
            .Replace("(/data_agent_evidence_pack)", "data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase);

        return CollapseWhitespace(sanitized);
    }

    static string CollapseWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool previousWasWhiteSpace = false;

        foreach (char current in value)
        {
            if (char.IsWhiteSpace(current))
            {
                if (previousWasWhiteSpace == false)
                    builder.Append(' ');

                previousWasWhiteSpace = true;
                continue;
            }

            builder.Append(current);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim();
    }
}
```

- [ ] **Step 4: Run DataAgent diagnostics formatter tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name~EvidenceDiagnosticsFormatter" -v:minimal
```

Expected: PASS for all `EvidenceDiagnosticsFormatter*` tests.

- [ ] **Step 5: Commit Task 2**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidenceDiagnosticsFormatter.cs Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs
git commit -m "Add DataAgent evidence diagnostics formatter"
```

---

### Task 3: Owner-Only Command Access For `/dataagent`

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs`
- Modify: `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`

- [ ] **Step 1: Add failing owner-only access tests**

Add these tests to `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`:

```csharp
[TestCase("/dataagent diag evidence")]
[TestCase("  /DATAAGENT diag evidence  ")]
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

[TestCase(QChatSenderRole.PrivateGuest)]
[TestCase(QChatSenderRole.GroupMember)]
public void NonOwnerDataAgentDiagnosticCommandIsDroppedSilently(QChatSenderRole role)
{
    QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
        new QChatCommandAccessContext("/dataagent diag evidence", role));

    Assert.Multiple(() =>
    {
        Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.DropSilently));
        Assert.That(decision.Reason, Is.EqualTo("non_owner_qchat_command"));
    });
}

[Test]
public void DataAgentWordsWithoutCommandPrefixPassThrough()
{
    QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
        new QChatCommandAccessContext("dataagent diag evidence", QChatSenderRole.GroupMember));

    Assert.Multiple(() =>
    {
        Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.NotCommand));
        Assert.That(decision.Reason, Is.EqualTo("not_qchat_command"));
    });
}
```

- [ ] **Step 2: Run failing command access tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatCommandAccessPolicyTests" -v:minimal
```

Expected: FAIL because `/dataagent diag evidence` is currently treated as `NotCommand`.

- [ ] **Step 3: Update command access policy**

Modify `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs`.

Replace:

```csharp
const string Prefix = "/qchat";
```

with:

```csharp
const string QChatPrefix = "/qchat";
const string DataAgentPrefix = "/dataagent";
```

Replace the first branch in `Evaluate`:

```csharp
if (IsQChatCommand(context.PlainText) == false)
```

with:

```csharp
if (IsOwnerDiagnosticCommand(context.PlainText) == false)
```

Replace `IsQChatCommand` with these methods:

```csharp
public static bool IsQChatCommand(string? text)
{
    return IsCommandWithPrefix(text, QChatPrefix);
}

public static bool IsOwnerDiagnosticCommand(string? text)
{
    return IsCommandWithPrefix(text, QChatPrefix) ||
           IsCommandWithPrefix(text, DataAgentPrefix);
}

static bool IsCommandWithPrefix(string? text, string prefix)
{
    string trimmed = text?.TrimStart() ?? string.Empty;
    if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
        return false;

    return trimmed.Length == prefix.Length ||
           char.IsWhiteSpace(trimmed[prefix.Length]);
}
```

This preserves `IsQChatCommand` for existing persona-frame behavior while letting the owner command gate recognize `/dataagent`.

- [ ] **Step 4: Run command access tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatCommandAccessPolicyTests" -v:minimal
```

Expected: PASS for all `QChatCommandAccessPolicyTests`.

- [ ] **Step 5: Commit Task 3**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs
git commit -m "Allow owner-only DataAgent diagnostics commands"
```

---

### Task 4: QChat Diagnostics Command Integration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`

- [ ] **Step 1: Add failing QChat diagnostics command tests**

Add these tests to `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs` after `TryHandleToolBrokerDiagnosticsShowsRecentRouteStateForOwner`:

```csharp
[Test]
public void TryHandleSemanticDiagnosticsShowsRecentEstimateForOwner()
{
    string semanticText = QChatSemanticDiagnosticsFormatter.Format(new QChatSemanticDiagnosticsSnapshot(
        new QChatSemanticStateEstimate(
            SemanticCompletion: 0.7345,
            ContinuationLikelihood: 0.2214,
            TopicStability: 0.8,
            SummaryIntent: 0.05,
            ShouldWait: false,
            ShouldAnswer: true,
            ShouldSummarize: false,
            ReasonCode: "semantic_completion_stable"),
        WindowMessageCount: 1,
        WindowAge: TimeSpan.FromSeconds(6),
        LastUpdateAge: TimeSpan.FromSeconds(3)));
    QChatDiagnosticsRuntimeState state = new(RecentSemanticEstimate: semanticText);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag semantic",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("QChat semantic diagnostics"));
        Assert.That(result.Text, Does.Contain("semantic_completion=0.735"));
        Assert.That(result.Text, Does.Contain("continuation_likelihood=0.221"));
        Assert.That(result.Text, Does.Contain("should_answer=true"));
        Assert.That(result.Text, Does.Contain("reason_code=semantic_completion_stable"));
    });
}

[Test]
public void TryHandleSemanticDiagnosticsReturnsUnavailableWhenNoEstimateExists()
{
    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag semantic",
        CreateRoute(),
        CreateProfile(),
        new QChatDiagnosticsRuntimeState());

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("QChat semantic diagnostics"));
        Assert.That(result.Text, Does.Contain("state=unavailable"));
        Assert.That(result.Text, Does.Contain("reason=semantic_window_empty"));
    });
}

[Test]
public void TryHandleDataAgentEvidenceDiagnosticsShowsRecentEvidenceForOwner()
{
    string evidenceText = string.Join(Environment.NewLine,
        "DataAgent evidence diagnostics",
        "analysis_confidence=0.781",
        "answer_stability=0.733",
        "clarification_need=0.242",
        "risk_level=0.287",
        "state_estimate_reason_code=analysis_evidence_stable",
        "route_allowed=true",
        "route_allows_query=true",
        "executed_sql=true",
        "terminal=false",
        "tool_broker_audit_allowed=true");
    QChatDiagnosticsRuntimeState state = new(RecentDataAgentEvidence: evidenceText);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag evidence",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(result.Text, Does.Contain("analysis_confidence=0.781"));
        Assert.That(result.Text, Does.Contain("risk_level=0.287"));
        Assert.That(result.Text, Does.Contain("state_estimate_reason_code=analysis_evidence_stable"));
    });
}

[Test]
public void TryHandleDataAgentEvidenceDiagnosticsSupportsQChatAlias()
{
    QChatDiagnosticsRuntimeState state = new(RecentDataAgentEvidence: string.Join(Environment.NewLine,
        "DataAgent evidence diagnostics",
        "analysis_confidence=0.781",
        "risk_level=0.287",
        "state_estimate_reason_code=analysis_evidence_stable"));

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag dataagent evidence",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(result.Text, Does.Contain("analysis_confidence=0.781"));
    });
}

[Test]
public void TryHandleDataAgentEvidenceDiagnosticsReturnsUnavailableWhenNoEvidenceExists()
{
    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag evidence",
        CreateRoute(),
        CreateProfile(),
        new QChatDiagnosticsRuntimeState());

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(result.Text, Does.Contain("state=unavailable"));
        Assert.That(result.Text, Does.Contain("reason=evidence_pack_unavailable"));
    });
}

[Test]
public void TryHandleDiagnosticsRedactsHiddenToolContext()
{
    QChatDiagnosticsRuntimeState state = new(
        RecentDataAgentEvidence: "[tool_route_context]\nAllowed XML tools: dataagent_query\n[/tool_route_context]");

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag evidence",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(result.Text, Does.Contain("state=redacted"));
        Assert.That(result.Text, Does.Contain("reason=hidden_context_redacted"));
        Assert.That(result.Text, Does.Not.Contain("[tool_route_context]"));
        Assert.That(result.Text, Does.Not.Contain("Allowed XML tools"));
    });
}
```

- [ ] **Step 2: Run failing QChat diagnostics command tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name~SemanticDiagnostics|Name~DataAgentEvidenceDiagnostics|Name~DiagnosticsRedactsHiddenToolContext" -v:minimal
```

Expected: FAIL with compile errors for new `QChatDiagnosticsRuntimeState` named parameters and missing command handling.

- [ ] **Step 3: Extend runtime state**

In `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`, replace the runtime-state record with:

```csharp
public sealed record QChatDiagnosticsRuntimeState(
    bool ReplyTimingDelayEnabled = false,
    bool ConversationSettleWindowEnabled = false,
    bool InternetAccessEnabled = false,
    string? RecentToolRouteTrace = null,
    string? RecentSemanticEstimate = null,
    string? RecentDataAgentEvidence = null);
```

- [ ] **Step 4: Add DataAgent command parsing**

In `QChatDiagnosticsService`, replace:

```csharp
const string CommandPrefix = "/qchat";
```

with:

```csharp
const string CommandPrefix = "/qchat";
const string DataAgentCommandPrefix = "/dataagent";
```

At the beginning of `TryHandle`, replace:

```csharp
string commandText = text?.Trim() ?? string.Empty;
if (!IsQChatCommand(commandText))
    return new QChatDiagnosticsResult(false, string.Empty);
```

with:

```csharp
string commandText = text?.Trim() ?? string.Empty;
bool qchatCommand = IsQChatCommand(commandText);
bool dataAgentCommand = IsDataAgentCommand(commandText);
if (qchatCommand == false && dataAgentCommand == false)
    return new QChatDiagnosticsResult(false, string.Empty);
```

After null checks, replace the command extraction block:

```csharp
string command = commandText.Length == CommandPrefix.Length
    ? string.Empty
    : commandText[CommandPrefix.Length..].Trim();
command = StripCopiedMenuDescription(command);
```

with:

```csharp
string command = qchatCommand
    ? commandText.Length == CommandPrefix.Length
        ? string.Empty
        : commandText[CommandPrefix.Length..].Trim()
    : commandText.Length == DataAgentCommandPrefix.Length
        ? string.Empty
        : commandText[DataAgentCommandPrefix.Length..].Trim();
command = StripCopiedMenuDescription(command);

if (dataAgentCommand)
    return command.ToLowerInvariant() switch
    {
        "diag evidence" or "diagnostics evidence" => Handled(BuildDataAgentEvidenceDiagnosticsText(runtimeState)),
        _ => Handled(BuildDataAgentEvidenceDiagnosticsText(runtimeState))
    };
```

Add this method after `IsQChatCommand`:

```csharp
static bool IsDataAgentCommand(string text)
{
    if (!text.StartsWith(DataAgentCommandPrefix, StringComparison.OrdinalIgnoreCase))
        return false;

    return text.Length == DataAgentCommandPrefix.Length || char.IsWhiteSpace(text[DataAgentCommandPrefix.Length]);
}
```

- [ ] **Step 5: Add switch cases and helper methods**

In the QChat command switch, add these cases before `"diag" or "diagnostics"`:

```csharp
"diag semantic" or "diagnostics semantic" => Handled(BuildSemanticDiagnosticsText(runtimeState)),
"diag dataagent evidence" or "diagnostics dataagent evidence" => Handled(BuildDataAgentEvidenceDiagnosticsText(runtimeState)),
```

Add these helper methods after `BuildToolBrokerText`:

```csharp
static string BuildSemanticDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState)
{
    string sanitized = SanitizeDiagnosticText(
        runtimeState.RecentSemanticEstimate,
        "QChat semantic diagnostics");
    return string.IsNullOrWhiteSpace(sanitized)
        ? QChatSemanticDiagnosticsFormatter.Format(new QChatSemanticDiagnosticsSnapshot(null, 0, TimeSpan.Zero, TimeSpan.Zero))
        : sanitized;
}

static string BuildDataAgentEvidenceDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState)
{
    string sanitized = SanitizeDiagnosticText(
        runtimeState.RecentDataAgentEvidence,
        "DataAgent evidence diagnostics");
    return string.IsNullOrWhiteSpace(sanitized)
        ? string.Join(Environment.NewLine,
            "DataAgent evidence diagnostics",
            "state=unavailable",
            "reason=evidence_pack_unavailable")
        : sanitized;
}

static string SanitizeDiagnosticText(string? text, string title)
{
    if (string.IsNullOrWhiteSpace(text))
        return string.Empty;

    if (ContainsHiddenDiagnosticContext(text))
        return string.Join(Environment.NewLine,
            title,
            "state=redacted",
            "reason=hidden_context_redacted");

    string normalized = text.ReplaceLineEndings(Environment.NewLine).Trim();
    return normalized.Length <= 900
        ? normalized
        : normalized[..900] + "...";
}

static bool ContainsHiddenDiagnosticContext(string text)
{
    return text.Contains("[tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("[/tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("[data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("[/data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("Allowed XML tools", StringComparison.OrdinalIgnoreCase);
}
```

Update `BuildDiagnosticsMenuText` so it includes the new commands:

```csharp
public static string BuildDiagnosticsMenuText()
{
    return string.Join(Environment.NewLine,
        "璇婃柇鎸囦护锛?,
        "/qchat route - 鏌ョ湅褰撳墠浼氳瘽璺敱",
        "/qchat identity - 鏌ョ湅褰撳墠 agent 韬唤",
        "/qchat profile - 鏌ョ湅妯″瀷銆佷汉璁俱€佽蹇嗛厤缃?,
        "/qchat status - 鏌ョ湅鍦ㄧ嚎鍜屽洖澶嶇獥鍙ｇ姸鎬?,
        "/qchat diag semantic - QChat semantic state diagnostics",
        "/dataagent diag evidence - DataAgent evidence diagnostics",
        "",
        "璇存槑锛?,
        "璇婃柇淇℃伅鍙粰涓讳汉璐﹀彿寮€鏀撅紝鐢ㄦ潵鎺掓煡 QQ 閾捐矾銆?);
}
```

Keep the existing mojibake text style in this method if the file already uses it around this menu. Do not refactor unrelated menu strings.

- [ ] **Step 6: Run QChat diagnostics command tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name~SemanticDiagnostics|Name~DataAgentEvidenceDiagnostics|Name~DiagnosticsRedactsHiddenToolContext|Name=TryHandleSecondLevelMenuReturnsChineseUsage" -v:minimal
```

Expected: PASS for the new diagnostics command tests and existing second-level menu test.

- [ ] **Step 7: Commit Task 4**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs
git commit -m "Expose owner diagnostics commands"
```

---

### Task 5: Readiness And Engineering Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Add failing DataAgent readiness expectations**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, change:

```csharp
Assert.That(checks, Has.Count.EqualTo(58));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(59));
```

Add this assertion near the existing `DataAgentAnalysisStateEstimatorPresent` assertion:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEvidenceDiagnosticsPresent"));
```

Update the summary expectation from:

```csharp
"  Summary: 72 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 73 required passed, 0 required missing"
```

Update the script contract assertion from:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 72"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 73"));
```

Add a readiness script contract test:

```csharp
[Test]
public void ReadinessScriptProtectsV26EvidenceDiagnosticsContract()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "DataAgentEvidenceDiagnosticsPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentEvidenceDiagnosticsFormatter.cs"));
        Assert.That(declaration, Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(declaration, Does.Contain("state_estimate_reason_code"));
        Assert.That(declaration, Does.Contain("EvidenceDiagnosticsFormatterEmitsCompactStateEstimate"));
    });
}
```

- [ ] **Step 2: Add failing QChat engineering map expectations**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add these required checks:

```csharp
"QChat semantic diagnostics",
"DataAgent owner evidence diagnostics"
```

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update the QChat engineering map summary from:

```csharp
"Summary: 45 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

to:

```csharp
"Summary: 47 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

- [ ] **Step 3: Run failing readiness tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: FAIL until runtime readiness and scripts are updated.

- [ ] **Step 4: Add DataAgent runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after the existing `DataAgentAnalysisStateEstimatorPresent` check, add:

```csharp
string evidenceDiagnostics = DataAgentEvidenceDiagnosticsFormatter.Format(acceptedEvidencePack);
bool evidenceDiagnosticsReady =
    evidenceDiagnostics.Contains("DataAgent evidence diagnostics", StringComparison.Ordinal) &&
    evidenceDiagnostics.Contains("analysis_confidence=", StringComparison.Ordinal) &&
    evidenceDiagnostics.Contains("risk_level=", StringComparison.Ordinal) &&
    evidenceDiagnostics.Contains("state_estimate_reason_code=analysis_evidence_stable", StringComparison.Ordinal) &&
    evidenceDiagnostics.Contains("[data_agent_evidence_pack]", StringComparison.Ordinal) == false &&
    evidenceDiagnostics.Contains("[tool_route_context]", StringComparison.OrdinalIgnoreCase) == false;
checks.Add(evidenceDiagnosticsReady
    ? Pass("DataAgentEvidenceDiagnosticsPresent", "owner_diag=true;analysis_confidence=true;risk_level=true")
    : Fail("DataAgentEvidenceDiagnosticsPresent", $"owner_diag=false;diagnostics={evidenceDiagnostics.ReplaceLineEndings(" ")}"));
```

- [ ] **Step 5: Update DataAgent readiness script**

In `tools/check-dataagent-readiness.ps1`, add this `New-Check` after `DataAgentAnalysisStateEstimatorPresent`:

```powershell
New-Check -Group "Analysis" -Name "DataAgentEvidenceDiagnosticsPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidenceDiagnosticsFormatter.cs" @("DataAgentEvidenceDiagnosticsFormatter", "DataAgent evidence diagnostics", "state_estimate_reason_code")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs" @("EvidenceDiagnosticsFormatterEmitsCompactStateEstimate", "EvidenceDiagnosticsFormatterEmitsUnavailableStateWhenPackMissing", "EvidenceDiagnosticsFormatterSanitizesReasonCode")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentEvidenceDiagnosticsPresent", "owner_diag=true", "analysis_confidence=true", "risk_level=true"))) -Detail "DataAgent owner evidence diagnostics markers"
```

Change:

```powershell
$expectedRequired = 72
```

to:

```powershell
$expectedRequired = 73
```

- [ ] **Step 6: Update QChat engineering map script**

In `tools/check-qchat-engineering-map.ps1`, add these checks near the existing QChat owner Tool Broker diagnostics check:

```powershell
Add-Check -Group "Harness" -Name "QChat semantic diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("RecentSemanticEstimate", "diag semantic", "BuildSemanticDiagnosticsText")
Add-Check -Group "Harness" -Name "DataAgent owner evidence diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("DataAgentCommandPrefix", "diag evidence", "RecentDataAgentEvidence", "BuildDataAgentEvidenceDiagnosticsText")
```

- [ ] **Step 7: Run readiness tests and scripts**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent Summary: 73 required passed, 0 required missing
QChat Summary: 47 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 8: Commit Task 5**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Require owner diagnostics readiness gates"
```

---

### Task 6: Focused Regression Sweep

**Files:**
- Verify all V2.6 changed files.

- [ ] **Step 1: Run focused QChat diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatSemanticDiagnosticsFormatterTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: PASS.

- [ ] **Step 2: Run focused DataAgent diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 3: Run DataAgent full test project**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: PASS with the existing live PostgreSQL test skipped when `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is not set.

- [ ] **Step 4: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent Summary: 73 required passed, 0 required missing
QChat Summary: 47 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 5: Run diff hygiene**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: `git diff --check` exits 0, and branch status contains only V2.6 owner diagnostics changes.

---

### Task 7: Final Verification, Review, Merge, And Upload

**Files:**
- Verify repository-wide behavior after V2.6.

- [ ] **Step 1: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

Expected: exit code 0. Existing live/environment-gated tests may remain skipped.

- [ ] **Step 2: Run final diff and branch checks**

Run:

```powershell
git diff --check
git status --short --branch
git log --oneline --decorate -8
```

Expected: diff check exits 0 and the feature branch is clean.

- [ ] **Step 3: Request code review before merge**

Use `superpowers:requesting-code-review` with:

```text
DESCRIPTION: DataAgent/QChat V2.6 owner-only diagnostics for semantic estimates and Evidence Pack state.
PLAN_OR_REQUIREMENTS: docs/superpowers/plans/2026-07-01-dataagent-v2.6-owner-diagnostics.md
BASE_SHA: master before creating dataagent-v2.6-owner-diagnostics
HEAD_SHA: current feature branch HEAD
```

Required review focus:

- Diagnostics do not execute SQL.
- Diagnostics do not call tools or `XmlFunctionCaller`.
- Diagnostics do not call the model.
- Diagnostics do not mutate QChat or DataAgent state.
- `/dataagent diag evidence` is owner-only.
- QChat does not reference the DataAgent project directly.

- [ ] **Step 4: Fix Critical or Important review findings**

If review reports Critical or Important findings, use `superpowers:receiving-code-review` and fix them with TDD before proceeding.

Expected: no unresolved Critical or Important findings before merge.

- [ ] **Step 5: Merge to master**

From `D:\Alife`, run:

```powershell
git status --short --branch
git merge dataagent-v2.6-owner-diagnostics
```

Expected: merge succeeds, preferably fast-forward.

- [ ] **Step 6: Run post-merge verification on master**

Run from `D:\Alife`:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatSemanticDiagnosticsFormatterTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
git diff --check
```

Expected:

```text
DataAgent readiness: 73 required passed, 0 required missing
QChat engineering map: 47 required passed, 0 required missing, 0 optional present, 0 optional missing
Full solution: exit code 0
```

- [ ] **Step 7: Push to GitHub**

Run:

```powershell
git push alife-byastralfox master
git ls-remote alife-byastralfox refs/heads/master
```

Expected: remote `refs/heads/master` points to the merged V2.6 commit.

- [ ] **Step 8: Clean up feature worktree**

After successful push and remote verification, run:

```powershell
git worktree remove "D:\Alife\.worktrees\dataagent-v2.6-owner-diagnostics"
git worktree prune
git branch -d dataagent-v2.6-owner-diagnostics
```

If Windows reports `Filename too long`, first verify the exact target path:

```powershell
Resolve-Path -LiteralPath "D:\Alife\.worktrees\dataagent-v2.6-owner-diagnostics"
git worktree list
```

Then remove only that verified residual directory with the long-path prefix:

```powershell
Remove-Item -LiteralPath "\\?\D:\Alife\.worktrees\dataagent-v2.6-owner-diagnostics" -Recurse -Force
git worktree prune
git branch -d dataagent-v2.6-owner-diagnostics
```

---

## Plan Self-Review

- Spec coverage: the plan covers `/qchat diag semantic`, `/dataagent diag evidence`, owner-only access, formatter contracts, unavailable states, sanitization, readiness gates, engineering map gates, verification, merge, upload, and cleanup.
- Open-item scan: every task contains exact file paths, code snippets, commands, and expected outcomes.
- Type consistency: `QChatSemanticDiagnosticsSnapshot`, `QChatSemanticDiagnosticsFormatter`, `DataAgentEvidenceDiagnosticsFormatter`, `RecentSemanticEstimate`, and `RecentDataAgentEvidence` are defined before later tasks reference them.
- Boundary check: QChat does not reference the DataAgent project. DataAgent formats evidence diagnostics; QChat displays only injected diagnostic text. No task authorizes tools, executes SQL, calls the model, or mutates state.
