# DataAgent V2.4 Evidence Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a lightweight DataAgent Evidence Pack that turns route, orchestration trace, checkpoint, and audit facts into a stable diagnostic and interview-ready report.

**Architecture:** Keep the Evidence Pack observational only: it consumes `DataAgentOrchestrationResult` plus optional audit records and never executes SQL, routes tools, or mutates sessions. Add a compact model, builder, and formatter under `Alife.Function.DataAgent`, then enforce the capability through tests and readiness gates.

**Tech Stack:** C#/.NET 9, NUnit, existing DataAgent models, `DataAgentContextFieldSanitizer`, PowerShell readiness scripts.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePack.cs`
  - Immutable record for the evidence report.

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs`
  - Converts orchestration result plus optional `DataAgentAuditRecord` and `DataAgentToolBrokerAuditRecord` lists into a pack.

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs`
  - Emits a stable `[data_agent_evidence_pack]` block with sanitized values.

- Create `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs`
  - Focused builder and formatter tests for accepted, route-denied, terminal, and sanitized output.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Build accepted, route-denied, and terminal Evidence Pack examples.
  - Add required check `DataAgentEvidencePackPresent`.

- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Expect one more core readiness check.
  - Assert `DataAgentEvidencePackPresent` appears in runtime readiness and script output.

- Modify `tools/check-dataagent-readiness.ps1`
  - Add marker check for Evidence Pack model, builder, formatter, runtime readiness evidence, and tests.
  - Increase `$expectedRequired` from `69` to `70`.

---

### Task 1: Evidence Pack Model And Accepted/Denied Builder

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePack.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs`

- [ ] **Step 1: Write failing builder tests for accepted and route-denied evidence**

Create `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs` with this initial content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentEvidencePackTests
{
    [Test]
    public void BuilderBuildsAcceptedQueryEvidence()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 2, true, true, false),
            new DataAgentToolRouteContext(
                true,
                "dataagent_analysis_continue",
                true,
                true,
                "route-1",
                "analysis_continue",
                "route_allowed",
                "session-1"));
        DataAgentAuditRecord audit = new(
            "Which documents describe DataAgent?",
            "document_index",
            "{}",
            "SELECT path FROM document_index LIMIT 20",
            true,
            string.Empty,
            2,
            TimeSpan.FromMilliseconds(12),
            DateTimeOffset.UnixEpoch);
        DataAgentToolBrokerAuditRecord toolAudit = new(
            "session-1",
            "dataagent_analysis_continue",
            true,
            "route_allowed",
            "route allowed",
            DateTimeOffset.UnixEpoch);

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result, [audit], [toolAudit]);

        Assert.Multiple(() =>
        {
            Assert.That(pack.SessionId, Is.EqualTo("session-1"));
            Assert.That(pack.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(pack.TurnCount, Is.EqualTo(2));
            Assert.That(pack.RoutePresent, Is.True);
            Assert.That(pack.RouteTool, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(pack.RouteAllowed, Is.True);
            Assert.That(pack.RouteAllowsQuery, Is.True);
            Assert.That(pack.RouteReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(pack.Trace, Is.EqualTo("RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded"));
            Assert.That(pack.ExecutedSql, Is.True);
            Assert.That(pack.Terminal, Is.False);
            Assert.That(pack.CanContinue, Is.True);
            Assert.That(pack.CanSummarize, Is.True);
            Assert.That(pack.AuditValidated, Is.True);
            Assert.That(pack.AuditDataset, Is.EqualTo("document_index"));
            Assert.That(pack.AuditRowCount, Is.EqualTo(2));
            Assert.That(pack.AuditRejectedReason, Is.Empty);
            Assert.That(pack.ToolBrokerAuditAllowed, Is.True);
            Assert.That(pack.ToolBrokerAuditReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(pack.SafetySummary, Is.EqualTo("route_allowed;read_only_sql_executed;checkpoint_active"));
            Assert.That(pack.InterviewSummary, Does.Contain("governed read-only query"));
        });
    }

    [Test]
    public void BuilderBuildsRouteDeniedEvidenceWithoutSql()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Rejected,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 1, true, true, false),
            DataAgentToolRouteContext.Missing("dataagent_analysis_continue"));

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(pack.SessionId, Is.EqualTo("session-1"));
            Assert.That(pack.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(pack.RoutePresent, Is.False);
            Assert.That(pack.RouteAllowed, Is.False);
            Assert.That(pack.RouteAllowsQuery, Is.False);
            Assert.That(pack.RouteReasonCode, Is.EqualTo("tool_route_required"));
            Assert.That(pack.Trace, Is.EqualTo("RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded"));
            Assert.That(pack.ExecutedSql, Is.False);
            Assert.That(pack.AuditValidated, Is.False);
            Assert.That(pack.AuditDataset, Is.Empty);
            Assert.That(pack.ToolBrokerAuditAllowed, Is.False);
            Assert.That(pack.ToolBrokerAuditReasonCode, Is.Empty);
            Assert.That(pack.SafetySummary, Is.EqualTo("route_rejected;sql_not_executed;checkpoint_unchanged"));
            Assert.That(pack.InterviewSummary, Does.Contain("rejected before SQL execution"));
        });
    }

    static DataAgentOrchestrationResult Result(
        DataAgentAnalysisSessionStatus responseStatus,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        DataAgentOrchestrationCheckpoint checkpoint,
        DataAgentToolRouteContext? routeContext)
    {
        DataAgentAnalysisResponse response = new(
            checkpoint.SessionId,
            responseStatus,
            DataAgentAnalysisTurnIntent.Continue,
            null,
            string.Empty,
            string.Empty,
            responseStatus != DataAgentAnalysisSessionStatus.Rejected,
            responseStatus == DataAgentAnalysisSessionStatus.Rejected ? "tool_route_required" : string.Empty);

        return new DataAgentOrchestrationResult(
            checkpoint.SessionId,
            responseStatus,
            steps,
            checkpoint,
            response,
            routeContext);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail because types do not exist**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests" -v:minimal
```

Expected: FAIL with compiler errors for missing `DataAgentEvidencePack` and `DataAgentEvidencePackBuilder`.

- [ ] **Step 3: Add the evidence pack model**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePack.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentEvidencePack(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    int TurnCount,
    bool RoutePresent,
    string RouteTool,
    bool RouteAllowed,
    bool RouteAllowsQuery,
    string RouteReasonCode,
    string Trace,
    bool ExecutedSql,
    bool Terminal,
    bool CanContinue,
    bool CanSummarize,
    bool AuditValidated,
    string AuditDataset,
    int AuditRowCount,
    string AuditRejectedReason,
    bool ToolBrokerAuditAllowed,
    string ToolBrokerAuditReasonCode,
    string SafetySummary,
    string InterviewSummary);
```

- [ ] **Step 4: Add the minimal builder implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentEvidencePackBuilder
{
    public DataAgentEvidencePack Build(
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentAuditRecord>? queryAudit = null,
        IReadOnlyList<DataAgentToolBrokerAuditRecord>? toolBrokerAudit = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        DataAgentAuditRecord? latestAudit = queryAudit?.LastOrDefault();
        DataAgentToolBrokerAuditRecord? latestToolAudit = FindToolBrokerAudit(result, toolBrokerAudit);
        bool executedSql = result.Steps.Any(step => step.ExecutedSql);
        string trace = BuildTrace(result.Steps);
        string routeReasonCode = result.RouteContext?.ReasonCode ?? string.Empty;

        return new DataAgentEvidencePack(
            result.SessionId,
            result.Checkpoint.SessionStatus,
            result.Checkpoint.TurnCount,
            result.RouteContext?.Present == true,
            result.RouteContext?.ToolName ?? string.Empty,
            result.RouteContext?.AllowsTool == true,
            result.RouteContext?.AllowsQuery == true,
            routeReasonCode,
            trace,
            executedSql,
            result.Checkpoint.Terminal,
            result.Checkpoint.CanContinue,
            result.Checkpoint.CanSummarize,
            latestAudit?.Validated == true,
            latestAudit?.Dataset ?? string.Empty,
            latestAudit?.RowCount ?? 0,
            latestAudit?.RejectedReason ?? string.Empty,
            latestToolAudit?.Allowed == true,
            latestToolAudit?.ReasonCode ?? string.Empty,
            BuildSafetySummary(result, executedSql),
            BuildInterviewSummary(result, executedSql, routeReasonCode));
    }

    static DataAgentToolBrokerAuditRecord? FindToolBrokerAudit(
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentToolBrokerAuditRecord>? records)
    {
        if (records is null || records.Count == 0)
            return null;

        string routeTool = result.RouteContext?.ToolName ?? string.Empty;
        return records.LastOrDefault(record =>
            string.Equals(record.SessionId, result.SessionId, StringComparison.Ordinal) ||
            string.Equals(record.ToolName, routeTool, StringComparison.Ordinal));
    }

    static string BuildTrace(IEnumerable<DataAgentOrchestrationStep> steps)
    {
        return string.Join(">", steps.Select(step => $"{step.Node}:{step.Status}"));
    }

    static string BuildSafetySummary(DataAgentOrchestrationResult result, bool executedSql)
    {
        bool routeRejected = result.Steps.Any(step =>
            step.Node == DataAgentOrchestrationNodeKind.RouteGate &&
            step.Status == DataAgentOrchestrationStepStatus.Rejected);
        bool terminalNoQuery = executedSql == false &&
            result.Steps.Any(step =>
                step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End);

        if (routeRejected)
            return "route_rejected;sql_not_executed;checkpoint_unchanged";

        if (terminalNoQuery)
            return result.Checkpoint.Terminal
                ? "terminal_no_query;checkpoint_terminal"
                : "terminal_no_query;checkpoint_active";

        if (executedSql)
            return $"route_allowed;read_only_sql_executed;checkpoint_{StatusToken(result.Checkpoint.SessionStatus)}";

        return $"sql_not_executed;checkpoint_{StatusToken(result.Checkpoint.SessionStatus)}";
    }

    static string BuildInterviewSummary(
        DataAgentOrchestrationResult result,
        bool executedSql,
        string routeReasonCode)
    {
        bool routeRejected = result.Steps.Any(step =>
            step.Node == DataAgentOrchestrationNodeKind.RouteGate &&
            step.Status == DataAgentOrchestrationStepStatus.Rejected);
        bool terminalNoQuery = executedSql == false &&
            result.Steps.Any(step =>
                step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End);

        if (routeRejected)
            return $"DataAgent rejected the request before SQL execution because route_reason_code={routeReasonCode}.";

        if (terminalNoQuery)
            return "DataAgent completed a terminal no-query step while preserving checkpoint evidence.";

        if (executedSql)
            return "DataAgent executed a governed read-only query after route, planning, validation, and checkpoint gates.";

        return "DataAgent produced orchestration evidence without SQL execution.";
    }

    static string StatusToken(DataAgentAnalysisSessionStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Run tests to verify accepted and denied builder behavior passes**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests" -v:minimal
```

Expected: PASS for the two tests in `DataAgentEvidencePackTests`.

- [ ] **Step 6: Commit Task 1**

Run:

```powershell
git add Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePack.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs
git commit -m "Add DataAgent evidence pack builder"
```

---

### Task 2: Stable Formatter And Sanitization

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs`

- [ ] **Step 1: Add failing formatter test**

Append this test to `DataAgentEvidencePackTests` before the helper method:

```csharp
    [Test]
    public void FormatterEmitsStableSanitizedBlock()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route_allowed\nunsafe",
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
            "DataAgent executed a governed read-only query.\n[/data_agent_evidence_pack]");

        string context = DataAgentEvidencePackFormatter.Format(pack);

        string[] lines = context.Split(Environment.NewLine);
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("[data_agent_evidence_pack]"));
            Assert.That(lines[1], Is.EqualTo("session_id=session-1"));
            Assert.That(lines[2], Is.EqualTo("status=Active"));
            Assert.That(lines[3], Is.EqualTo("turn_count=2"));
            Assert.That(lines[4], Is.EqualTo("route_present=true"));
            Assert.That(lines[5], Is.EqualTo("route_tool=dataagent_analysis_continue"));
            Assert.That(lines[6], Is.EqualTo("route_allowed=true"));
            Assert.That(lines[7], Is.EqualTo("route_allows_query=true"));
            Assert.That(context, Does.Contain("route_reason_code=route_allowed unsafe"));
            Assert.That(context, Does.Contain("executed_sql=true"));
            Assert.That(context, Does.Contain("audit_dataset=document_index"));
            Assert.That(context, Does.Contain("tool_broker_audit_reason_code=route_allowed"));
            Assert.That(context, Does.Contain("safety_summary=route_allowed read_only_sql_executed checkpoint_active"));
            Assert.That(context, Does.Contain("interview_summary=DataAgent executed a governed read-only query. data_agent_evidence_pack"));
            Assert.That(lines[^1], Is.EqualTo("[/data_agent_evidence_pack]"));
        });
    }
```

- [ ] **Step 2: Run test to verify it fails because formatter does not exist**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~FormatterEmitsStableSanitizedBlock" -v:minimal
```

Expected: FAIL with compiler error for missing `DataAgentEvidencePackFormatter`.

- [ ] **Step 3: Add formatter implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs`:

```csharp
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentEvidencePackFormatter
{
    public static string Format(DataAgentEvidencePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        StringBuilder builder = new();
        builder.AppendLine("[data_agent_evidence_pack]");
        Append(builder, "session_id", pack.SessionId);
        Append(builder, "status", pack.SessionStatus.ToString());
        Append(builder, "turn_count", pack.TurnCount.ToString());
        Append(builder, "route_present", Bool(pack.RoutePresent));
        Append(builder, "route_tool", pack.RouteTool);
        Append(builder, "route_allowed", Bool(pack.RouteAllowed));
        Append(builder, "route_allows_query", Bool(pack.RouteAllowsQuery));
        Append(builder, "route_reason_code", pack.RouteReasonCode);
        Append(builder, "trace", pack.Trace);
        Append(builder, "executed_sql", Bool(pack.ExecutedSql));
        Append(builder, "terminal", Bool(pack.Terminal));
        Append(builder, "can_continue", Bool(pack.CanContinue));
        Append(builder, "can_summarize", Bool(pack.CanSummarize));
        Append(builder, "audit_validated", Bool(pack.AuditValidated));
        Append(builder, "audit_dataset", pack.AuditDataset);
        Append(builder, "audit_row_count", pack.AuditRowCount.ToString());
        Append(builder, "audit_rejected_reason", pack.AuditRejectedReason);
        Append(builder, "tool_broker_audit_allowed", Bool(pack.ToolBrokerAuditAllowed));
        Append(builder, "tool_broker_audit_reason_code", pack.ToolBrokerAuditReasonCode);
        Append(builder, "safety_summary", pack.SafetySummary);
        Append(builder, "interview_summary", pack.InterviewSummary);
        builder.Append("[/data_agent_evidence_pack]");
        return builder.ToString();
    }

    static void Append(StringBuilder builder, string key, string value)
    {
        builder.Append(key);
        builder.Append('=');
        builder.AppendLine(DataAgentContextFieldSanitizer.Sanitize(value));
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
```

- [ ] **Step 4: Run formatter test to verify it passes**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~FormatterEmitsStableSanitizedBlock" -v:minimal
```

Expected: PASS.

- [ ] **Step 5: Run all Evidence Pack tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests" -v:minimal
```

Expected: PASS for all `DataAgentEvidencePackTests`.

- [ ] **Step 6: Commit Task 2**

Run:

```powershell
git add Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs
git commit -m "Add DataAgent evidence pack formatter"
```

---

### Task 3: Terminal No-Query Evidence

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs`

- [ ] **Step 1: Add failing terminal evidence test**

Append this test to `DataAgentEvidencePackTests` before the helper method:

```csharp
    [Test]
    public void BuilderBuildsTerminalNoQueryEvidence()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Ended,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.End, DataAgentOrchestrationStepStatus.Succeeded, "terminal_end", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Ended, "document_index", 3, false, false, true),
            new DataAgentToolRouteContext(
                true,
                "dataagent_analysis_end",
                true,
                false,
                "route-end",
                "analysis_end",
                "route_allowed",
                "session-1"));

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(pack.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(pack.RouteTool, Is.EqualTo("dataagent_analysis_end"));
            Assert.That(pack.RouteAllowed, Is.True);
            Assert.That(pack.RouteAllowsQuery, Is.False);
            Assert.That(pack.Trace, Is.EqualTo("End:Succeeded>Checkpoint:Succeeded"));
            Assert.That(pack.ExecutedSql, Is.False);
            Assert.That(pack.Terminal, Is.True);
            Assert.That(pack.CanContinue, Is.False);
            Assert.That(pack.CanSummarize, Is.False);
            Assert.That(pack.SafetySummary, Is.EqualTo("terminal_no_query;checkpoint_terminal"));
            Assert.That(pack.InterviewSummary, Does.Contain("terminal no-query"));
        });
    }
```

- [ ] **Step 2: Run terminal test to verify current behavior**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~BuilderBuildsTerminalNoQueryEvidence" -v:minimal
```

Expected: PASS if Task 1 already implemented terminal summary logic correctly. If it fails, the failure should show an incorrect `SafetySummary` or `InterviewSummary`.

- [ ] **Step 3: Apply minimal builder correction if the terminal test fails**

If Step 2 fails, replace `BuildSafetySummary` and `BuildInterviewSummary` in `DataAgentEvidencePackBuilder.cs` with the versions from Task 1 Step 4. These versions explicitly check:

```csharp
step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End
```

and return:

```csharp
"terminal_no_query;checkpoint_terminal"
```

for terminal checkpoints.

- [ ] **Step 4: Run all Evidence Pack tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests" -v:minimal
```

Expected: PASS for accepted, route-denied, formatter, and terminal tests.

- [ ] **Step 5: Commit Task 3**

Run:

```powershell
git add Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs
git commit -m "Cover DataAgent terminal evidence packs"
```

---

### Task 4: Runtime Readiness Evidence

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Add failing readiness test expectations**

Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`:

Change:

```csharp
Assert.That(checks, Has.Count.EqualTo(55));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(56));
```

Add this assertion near the other DataAgent analysis checks:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEvidencePackPresent"));
DataAgentReadinessCheck evidencePackCheck = checks.Single(check => check.Name == "DataAgentEvidencePackPresent");
Assert.That(evidencePackCheck.Detail, Does.Contain("accepted=true"));
Assert.That(evidencePackCheck.Detail, Does.Contain("denied=true"));
Assert.That(evidencePackCheck.Detail, Does.Contain("terminal=true"));
```

- [ ] **Step 2: Run readiness test to verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~CoreReadinessChecksAllPass" -v:minimal
```

Expected: FAIL because the check count is still 55 and `DataAgentEvidencePackPresent` is absent.

- [ ] **Step 3: Add runtime Evidence Pack checks to DataAgentReadiness**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after `terminalRouteSummaryContext` is built in the analysis readiness section, add:

```csharp
DataAgentEvidencePackBuilder evidencePackBuilder = new();
DataAgentEvidencePack acceptedEvidencePack = evidencePackBuilder.Build(
    orchestrationStart,
    [
        new DataAgentAuditRecord(
            "Which documents describe DataAgent?",
            "document_index",
            "{}",
            "SELECT path FROM document_index LIMIT 20",
            true,
            string.Empty,
            1,
            TimeSpan.FromMilliseconds(1),
            DateTimeOffset.UtcNow)
    ],
    [
        new DataAgentToolBrokerAuditRecord(
            orchestrationStart.SessionId,
            "dataagent_analysis_start",
            true,
            "route_allowed",
            "route allowed",
            DateTimeOffset.UtcNow)
    ]);
DataAgentEvidencePack deniedEvidencePack = evidencePackBuilder.Build(orchestrationDeniedContinue);
DataAgentEvidencePack terminalEvidencePack = evidencePackBuilder.Build(terminalRouteSummary);
string acceptedEvidencePackContext = DataAgentEvidencePackFormatter.Format(acceptedEvidencePack);
string deniedEvidencePackContext = DataAgentEvidencePackFormatter.Format(deniedEvidencePack);
string terminalEvidencePackContext = DataAgentEvidencePackFormatter.Format(terminalEvidencePack);

bool acceptedEvidenceReady =
    acceptedEvidencePack.ExecutedSql &&
    acceptedEvidencePack.RouteAllowed &&
    acceptedEvidencePack.AuditValidated &&
    acceptedEvidencePackContext.Contains("[data_agent_evidence_pack]", StringComparison.Ordinal) &&
    acceptedEvidencePackContext.Contains("safety_summary=route_allowed read_only_sql_executed checkpoint_active", StringComparison.Ordinal);

bool deniedEvidenceReady =
    deniedEvidencePack.ExecutedSql == false &&
    deniedEvidencePack.Trace.Contains("RouteGate:Rejected", StringComparison.Ordinal) &&
    deniedEvidencePack.SafetySummary == "route_rejected;sql_not_executed;checkpoint_unchanged" &&
    deniedEvidencePackContext.Contains("executed_sql=false", StringComparison.Ordinal);

bool terminalEvidenceReady =
    terminalEvidencePack.ExecutedSql == false &&
    terminalEvidencePack.Trace.Contains("Summarize:Succeeded", StringComparison.Ordinal) &&
    terminalEvidencePack.SafetySummary.Contains("terminal_no_query", StringComparison.Ordinal) &&
    terminalEvidencePackContext.Contains("terminal=false", StringComparison.Ordinal);

checks.Add(acceptedEvidenceReady && deniedEvidenceReady && terminalEvidenceReady
    ? Pass("DataAgentEvidencePackPresent", "accepted=true;denied=true;terminal=true")
    : Fail("DataAgentEvidencePackPresent", $"accepted={acceptedEvidenceReady};denied={deniedEvidenceReady};terminal={terminalEvidenceReady}"));
```

- [ ] **Step 4: Run readiness test to verify it passes**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~CoreReadinessChecksAllPass" -v:minimal
```

Expected: PASS and `DataAgentEvidencePackPresent` present.

- [ ] **Step 5: Commit Task 4**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "Add DataAgent evidence pack readiness"
```

---

### Task 5: PowerShell Readiness Gate

**Files:**
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Add failing script expectations**

Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`.

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, change:

```csharp
"  Summary: 69 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 70 required passed, 0 required missing"
```

Add:

```csharp
Assert.That(result.StandardOutput, Does.Contain("DataAgentEvidencePackPresent"));
```

In `ReadinessScriptProtectsV23RouteGateContract`, change:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 69"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 70"));
```

- [ ] **Step 2: Run script tests to verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~ReadinessScript" -v:minimal
```

Expected: FAIL because the script still reports 69 required checks and does not include `DataAgentEvidencePackPresent`.

- [ ] **Step 3: Add PowerShell marker gate**

In `tools/check-dataagent-readiness.ps1`, add this check in the `"Analysis"` group after `TerminalRouteDoesNotQuery`:

```powershell
    New-Check -Group "Analysis" -Name "DataAgentEvidencePackPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePack.cs" @("DataAgentEvidencePack", "SafetySummary", "InterviewSummary")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs" @("DataAgentEvidencePackBuilder", "route_rejected;sql_not_executed;checkpoint_unchanged", "terminal_no_query")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs" @("[data_agent_evidence_pack]", "interview_summary", "DataAgentContextFieldSanitizer")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentEvidencePackPresent", "acceptedEvidencePack", "deniedEvidencePack", "terminalEvidencePack")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs" @("BuilderBuildsAcceptedQueryEvidence", "BuilderBuildsRouteDeniedEvidenceWithoutSql", "BuilderBuildsTerminalNoQueryEvidence", "FormatterEmitsStableSanitizedBlock"))) -Detail "DataAgent evidence pack model, builder, formatter, tests, and runtime readiness"
```

Change:

```powershell
$expectedRequired = 69
```

to:

```powershell
$expectedRequired = 70
```

- [ ] **Step 4: Run script tests to verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~ReadinessScript" -v:minimal
```

Expected: PASS.

- [ ] **Step 5: Run the readiness script directly**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 70 required passed, 0 required missing
```

- [ ] **Step 6: Commit Task 5**

Run:

```powershell
git add tools/check-dataagent-readiness.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "Require DataAgent evidence pack readiness"
```

---

### Task 6: Final Verification And Integration

**Files:**
- Verify all changed files.
- No new production behavior should be added in this task.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass, with existing environment-gated live PostgreSQL skip preserved.

- [ ] **Step 2: Run DataAgent readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 70 required passed, 0 required missing
```

- [ ] **Step 3: Run QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 43 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

Expected: all test projects pass; existing live/environment tests remain skipped.

- [ ] **Step 5: Run diff hygiene check**

Run:

```powershell
git diff --check
```

Expected: exit code 0.

- [ ] **Step 6: Review git status and log**

Run:

```powershell
git status --short --branch
git log --oneline -8
```

Expected: clean working tree on the V2.4 branch after final commits.

- [ ] **Step 7: Request final code review**

Use `superpowers:requesting-code-review` with:

```text
DESCRIPTION: DataAgent V2.4 Evidence Pack model, builder, formatter, readiness gate, and tests.
PLAN_OR_REQUIREMENTS: docs/superpowers/specs/2026-06-30-dataagent-v2.4-evidence-pack-design.md and docs/superpowers/plans/2026-06-30-dataagent-v2.4-evidence-pack.md
BASE_SHA: commit before Task 1
HEAD_SHA: current branch HEAD
```

Expected: no Critical or Important findings before merge.

- [ ] **Step 8: Finish branch**

Use `superpowers:finishing-a-development-branch`.

Preferred completion for this project:

```text
1. Merge back to master locally
```

Then push only to:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

Do not use `D:\FOXD`.

---

## Plan Self-Review

- Spec coverage: Tasks 1-3 cover the Evidence Pack model, builder, formatter, accepted, route-denied, terminal, and sanitized output requirements. Tasks 4-5 cover required readiness gates. Task 6 covers verification, review, merge, and upload constraints.
- Open-ended text scan: no task uses vague fill-in language; each code-bearing task includes exact snippets and exact commands.
- Type consistency: the plan consistently uses `DataAgentEvidencePack`, `DataAgentEvidencePackBuilder`, `DataAgentEvidencePackFormatter`, `DataAgentAuditRecord`, `DataAgentToolBrokerAuditRecord`, and existing orchestration model names.
- Scope control: no UI, LangGraph, live PostgreSQL dependency, state-machine migration, or Tool Broker replacement is introduced.
