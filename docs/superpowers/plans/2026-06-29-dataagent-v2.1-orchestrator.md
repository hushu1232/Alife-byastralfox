# DataAgent V2.1 Orchestrator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a native C# DataAgent orchestration layer that makes governed NL2SQL analysis steps, route gating, terminal behavior, and checkpoints observable without introducing LangGraph or changing QChat runtime behavior.

**Architecture:** Add small orchestration model types, an `IDataAgentAnalysisOrchestrator` contract, and a `DataAgentAnalysisOrchestrator` implementation that coordinates the existing `DataAgentAnalysisService` and `IDataAgentAnalysisSessionStore`. The orchestrator records explicit node steps, fails closed before query-producing actions when route policy denies access, and derives checkpoints from the analysis session store.

**Tech Stack:** .NET 9, C# records/enums, NUnit, existing `Alife.Function.DataAgent` services, PowerShell readiness checks.

---

## File Structure

- Create: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs`
  - Owns V2.1 public orchestration enums and records.
- Create: `Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
  - Owns the public orchestrator contract.
- Create: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
  - Coordinates route gating, `DataAgentAnalysisService`, session store snapshots, step construction, and checkpoints.
- Create: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`
  - Tests accepted, denied, terminal, clarification, rejected, and checkpoint behavior.
- Modify: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds required V2.1 orchestration readiness evidence.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Updates required check count and asserts the new V2.1 readiness names.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Adds required file-marker checks for the orchestrator.

The plan intentionally does not modify QChat runtime files, Tool Broker routing code, store providers, PostgreSQL live-test behavior, or the existing `DataAgentService` SQL validation path.

---

### Task 1: Add Orchestration Model RED Tests

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`
- Later create: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs`
- Later create: `Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
- Later create: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`

- [ ] **Step 1: Create the failing orchestrator test file**

Create `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs` with this initial test and helpers:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisOrchestratorTests
{
    [Test]
    public void StartAcceptedAnalysisRecordsQueryNodesAndCheckpoint()
    {
        DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        int answerCalls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
            store,
            _ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            },
            now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: true));

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(result.Response.Accepted, Is.True);
            Assert.That(result.SessionId, Is.Not.Empty);
            Assert.That(result.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationNodeKind.SchemaContext,
                DataAgentOrchestrationNodeKind.Plan,
                DataAgentOrchestrationNodeKind.Validate,
                DataAgentOrchestrationNodeKind.Execute,
                DataAgentOrchestrationNodeKind.Explain,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(result.Steps.Single(step => step.Node == DataAgentOrchestrationNodeKind.Execute).ExecutedSql, Is.True);
            Assert.That(result.Checkpoint.SessionId, Is.EqualTo(result.SessionId));
            Assert.That(result.Checkpoint.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(result.Checkpoint.LastDataset, Is.EqualTo("document_index"));
            Assert.That(result.Checkpoint.TurnCount, Is.EqualTo(1));
            Assert.That(result.Checkpoint.CanContinue, Is.True);
            Assert.That(result.Checkpoint.CanSummarize, Is.True);
            Assert.That(result.Checkpoint.Terminal, Is.False);
        });
    }

    static DataAgentAnalysisOrchestrator Orchestrator(
        IDataAgentAnalysisSessionStore store,
        Func<string, DataAgentAnswer> answer,
        DateTimeOffset now)
    {
        DataAgentAnalysisService analysisService = new(
            answer,
            store,
            new DataAgentFollowUpInterpreter(),
            () => now);

        return new DataAgentAnalysisOrchestrator(
            analysisService,
            store,
            new DataAgentFollowUpInterpreter());
    }

    static DataAgentAnswer AcceptedAnswer(string summary = "Found DataAgent documentation.")
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            summary,
            "[data_agent_context]\nsql_status=validated\nresult_explanation=Found DataAgent documentation.\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_documents",
                "document_index",
                "high",
                ["test"],
                "test accepted answer"));
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests" -v:minimal
```

Expected: compilation fails because `DataAgentAnalysisOrchestrator`, `DataAgentOrchestrationResult`, `DataAgentOrchestrationRequest`, and `DataAgentOrchestrationNodeKind` do not exist.

- [ ] **Step 3: Commit the RED test**

```powershell
git add Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs
git commit -m "test: add DataAgent orchestrator red test"
```

---

### Task 2: Add Orchestration Models And Accepted Start Flow

**Files:**
- Create: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs`
- Create: `Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
- Create: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`

- [ ] **Step 1: Add orchestration models**

Create `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs`:

```csharp
namespace Alife.Function.DataAgent;

public enum DataAgentOrchestrationNodeKind
{
    RouteGate,
    SchemaContext,
    Plan,
    Validate,
    Execute,
    Explain,
    Clarification,
    Summarize,
    End,
    Reject,
    Checkpoint
}

public enum DataAgentOrchestrationStepStatus
{
    Succeeded,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentOrchestrationStep(
    DataAgentOrchestrationNodeKind Node,
    DataAgentOrchestrationStepStatus Status,
    string Reason,
    bool ExecutedSql);

public sealed record DataAgentOrchestrationCheckpoint(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    string LastDataset,
    int TurnCount,
    bool CanContinue,
    bool CanSummarize,
    bool Terminal);

public sealed record DataAgentOrchestrationRequest(
    string CallerId,
    string Input,
    string? SessionId,
    bool RouteAllowsQuery);

public sealed record DataAgentOrchestrationResult(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    IReadOnlyList<DataAgentOrchestrationStep> Steps,
    DataAgentOrchestrationCheckpoint Checkpoint,
    DataAgentAnalysisResponse Response);
```

- [ ] **Step 2: Add the orchestrator contract**

Create `Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisOrchestrator
{
    DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request);
}
```

- [ ] **Step 3: Add minimal orchestrator implementation for accepted start**

Create `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentAnalysisOrchestrator : IDataAgentAnalysisOrchestrator
{
    const string RouteDeniedReason = "tool_route_required";

    readonly DataAgentAnalysisService analysisService;
    readonly IDataAgentAnalysisSessionStore sessionStore;
    readonly DataAgentFollowUpInterpreter followUpInterpreter;

    public DataAgentAnalysisOrchestrator(
        DataAgentAnalysisService analysisService,
        IDataAgentAnalysisSessionStore sessionStore,
        DataAgentFollowUpInterpreter? followUpInterpreter = null)
    {
        ArgumentNullException.ThrowIfNull(analysisService);
        ArgumentNullException.ThrowIfNull(sessionStore);

        this.analysisService = analysisService;
        this.sessionStore = sessionStore;
        this.followUpInterpreter = followUpInterpreter ?? new DataAgentFollowUpInterpreter();
    }

    public DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request)
    {
        ValidateStartRequest(request);

        List<DataAgentOrchestrationStep> steps = [];
        steps.Add(Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false));
        steps.Add(Step(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false));

        DataAgentAnalysisResponse response = analysisService.Start(request.CallerId, request.Input);
        AppendAnswerSteps(steps, response);
        return BuildResult(response, steps);
    }

    public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
    {
        ValidateContinueRequest(request);

        DataAgentAnalysisResponse response = analysisService.Continue(request.SessionId!, request.Input);
        List<DataAgentOrchestrationStep> steps =
        [
            Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false)
        ];
        AppendAnswerSteps(steps, response);
        return BuildResult(response, steps);
    }

    void AppendAnswerSteps(List<DataAgentOrchestrationStep> steps, DataAgentAnalysisResponse response)
    {
        if (response.Answer is null)
        {
            if (response.Intent == DataAgentAnalysisTurnIntent.Summarize)
                steps.Add(Step(DataAgentOrchestrationNodeKind.Summarize, DataAgentOrchestrationStepStatus.Succeeded, "terminal_summary", false));
            else if (response.Intent == DataAgentAnalysisTurnIntent.End)
                steps.Add(Step(DataAgentOrchestrationNodeKind.End, DataAgentOrchestrationStepStatus.Succeeded, "terminal_end", false));
            else
                steps.Add(Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, response.RejectedReason, false));

            steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
            return;
        }

        steps.Add(Step(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "planner_response_received", false));

        if (response.Answer.RejectedReason == "needs_clarification")
        {
            steps.Add(Step(DataAgentOrchestrationNodeKind.Clarification, DataAgentOrchestrationStepStatus.Succeeded, "needs_clarification", false));
            steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
            return;
        }

        if (response.Answer.Validated == false)
        {
            string reason = string.IsNullOrWhiteSpace(response.Answer.RejectedReason)
                ? "answer_rejected"
                : response.Answer.RejectedReason;
            steps.Add(Step(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Rejected, reason, false));
            steps.Add(Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, reason, false));
            steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
            return;
        }

        steps.Add(Step(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false));
        steps.Add(Step(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true));
        steps.Add(Step(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "result_explained", false));
        steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
    }

    DataAgentOrchestrationResult BuildResult(
        DataAgentAnalysisResponse response,
        IReadOnlyList<DataAgentOrchestrationStep> steps)
    {
        DataAgentOrchestrationCheckpoint checkpoint = BuildCheckpoint(response.SessionId, response.Status);
        return new DataAgentOrchestrationResult(
            response.SessionId,
            response.Status,
            steps.ToArray(),
            checkpoint,
            response);
    }

    DataAgentOrchestrationCheckpoint BuildCheckpoint(
        string sessionId,
        DataAgentAnalysisSessionStatus fallbackStatus)
    {
        DataAgentAnalysisSession? session = sessionStore.Get(sessionId);
        DataAgentAnalysisSessionStatus status = session?.Status ?? fallbackStatus;
        int turnCount = session?.Turns.Count ?? 0;
        bool terminal = status is DataAgentAnalysisSessionStatus.Ended or DataAgentAnalysisSessionStatus.Rejected;

        return new DataAgentOrchestrationCheckpoint(
            sessionId,
            status,
            session?.LastDataset ?? string.Empty,
            turnCount,
            CanContinue: terminal == false,
            CanSummarize: terminal == false && turnCount > 0,
            Terminal: terminal);
    }

    static DataAgentOrchestrationStep Step(
        DataAgentOrchestrationNodeKind node,
        DataAgentOrchestrationStepStatus status,
        string reason,
        bool executedSql)
    {
        return new DataAgentOrchestrationStep(node, status, reason, executedSql);
    }

    static void ValidateStartRequest(DataAgentOrchestrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CallerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
    }

    static void ValidateContinueRequest(DataAgentOrchestrationRequest request)
    {
        ValidateStartRequest(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
    }
}
```

- [ ] **Step 4: Run the accepted-start test and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests.StartAcceptedAnalysisRecordsQueryNodesAndCheckpoint" -v:minimal
```

Expected: test passes.

- [ ] **Step 5: Commit accepted start flow**

```powershell
git add Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs
git commit -m "Add DataAgent analysis orchestrator accepted flow"
```

---

### Task 3: Add Route Gate Fail-Closed Behavior

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`
- Modify: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`

- [ ] **Step 1: Add RED test for denied start**

Append this test to `DataAgentAnalysisOrchestratorTests`:

```csharp
[Test]
public void StartRouteDeniedFailsClosedWithoutCallingAnswer()
{
    DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
    int answerCalls = 0;
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ =>
        {
            answerCalls++;
            return AcceptedAnswer();
        },
        now);

    DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Which documents describe DataAgent?",
        null,
        RouteAllowsQuery: false));

    Assert.Multiple(() =>
    {
        Assert.That(answerCalls, Is.Zero);
        Assert.That(result.Response.Accepted, Is.False);
        Assert.That(result.Response.RejectedReason, Is.EqualTo("tool_route_required"));
        Assert.That(result.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Rejected));
        Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
            DataAgentOrchestrationNodeKind.RouteGate,
            DataAgentOrchestrationNodeKind.Reject,
            DataAgentOrchestrationNodeKind.Checkpoint
        }));
        Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
        Assert.That(result.Checkpoint.Terminal, Is.True);
        Assert.That(result.Checkpoint.TurnCount, Is.Zero);
    });
}
```

- [ ] **Step 2: Run denied-start test and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests.StartRouteDeniedFailsClosedWithoutCallingAnswer" -v:minimal
```

Expected: test fails because `Start` currently ignores `RouteAllowsQuery`.

- [ ] **Step 3: Implement denied start**

In `DataAgentAnalysisOrchestrator.Start`, insert this block after request validation and before adding a succeeded route step:

```csharp
if (request.RouteAllowsQuery == false)
    return BuildRejectedResult(
        string.Empty,
        DataAgentAnalysisTurnIntent.NewQuestion,
        RouteDeniedReason,
        Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, RouteDeniedReason, false));
```

Add this helper method to `DataAgentAnalysisOrchestrator`:

```csharp
DataAgentOrchestrationResult BuildRejectedResult(
    string sessionId,
    DataAgentAnalysisTurnIntent intent,
    string reason,
    DataAgentOrchestrationStep routeStep)
{
    DataAgentAnalysisResponse response = new(
        sessionId,
        DataAgentAnalysisSessionStatus.Rejected,
        intent,
        null,
        string.Empty,
        string.Empty,
        false,
        reason);

    DataAgentOrchestrationStep[] steps =
    [
        routeStep,
        Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, reason, false),
        Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
    ];

    return new DataAgentOrchestrationResult(
        sessionId,
        response.Status,
        steps,
        BuildCheckpoint(sessionId, response.Status),
        response);
}
```

- [ ] **Step 4: Run denied-start test and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests.StartRouteDeniedFailsClosedWithoutCallingAnswer" -v:minimal
```

Expected: test passes.

- [ ] **Step 5: Add RED test for denied query-producing continue**

Append this test:

```csharp
[Test]
public void ContinueRouteDeniedForQueryTurnDoesNotExecuteSqlOrMutateSession()
{
    DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
    int answerCalls = 0;
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ =>
        {
            answerCalls++;
            return AcceptedAnswer();
        },
        now);
    DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Which documents describe DataAgent?",
        null,
        RouteAllowsQuery: true));

    DataAgentOrchestrationResult denied = orchestrator.Continue(new DataAgentOrchestrationRequest(
        "owner",
        "\u7ee7\u7eed",
        start.SessionId,
        RouteAllowsQuery: false));
    DataAgentAnalysisSession session = store.Get(start.SessionId)!;

    Assert.Multiple(() =>
    {
        Assert.That(answerCalls, Is.EqualTo(1));
        Assert.That(denied.Response.Accepted, Is.False);
        Assert.That(denied.Response.RejectedReason, Is.EqualTo("tool_route_required"));
        Assert.That(denied.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
        Assert.That(denied.Checkpoint.SessionId, Is.EqualTo(start.SessionId));
        Assert.That(denied.Checkpoint.TurnCount, Is.EqualTo(1));
        Assert.That(session.Turns, Has.Count.EqualTo(1));
    });
}
```

- [ ] **Step 6: Run denied-continue test and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests.ContinueRouteDeniedForQueryTurnDoesNotExecuteSqlOrMutateSession" -v:minimal
```

Expected: test fails because `Continue` currently calls `DataAgentAnalysisService.Continue` even when route is denied.

- [ ] **Step 7: Implement denied query-producing continue**

Replace `Continue` with this implementation:

```csharp
public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
{
    ValidateContinueRequest(request);

    DataAgentAnalysisSession? session = sessionStore.Get(request.SessionId!);
    if (session is null)
    {
        DataAgentAnalysisResponse response = analysisService.Continue(request.SessionId!, request.Input);
        return BuildResult(
            response,
            [
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, response.RejectedReason, false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ]);
    }

    DataAgentAnalysisTurnIntent intent = followUpInterpreter.Interpret(request.Input, session);
    bool queryProducing = intent.ProducesQuery();
    if (request.RouteAllowsQuery == false && queryProducing)
    {
        return BuildRejectedResult(
            request.SessionId!,
            intent,
            RouteDeniedReason,
            Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, RouteDeniedReason, false));
    }

    DataAgentAnalysisResponse response = analysisService.Continue(request.SessionId!, request.Input);
    List<DataAgentOrchestrationStep> steps =
    [
        Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, queryProducing ? "route_allowed" : "terminal_route_not_required", false)
    ];
    AppendAnswerSteps(steps, response);
    return BuildResult(response, steps);
}
```

- [ ] **Step 8: Run route gate tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests.StartRouteDeniedFailsClosedWithoutCallingAnswer|FullyQualifiedName~DataAgentAnalysisOrchestratorTests.ContinueRouteDeniedForQueryTurnDoesNotExecuteSqlOrMutateSession" -v:minimal
```

Expected: both route gate tests pass.

- [ ] **Step 9: Commit route gate behavior**

```powershell
git add Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs
git commit -m "Add DataAgent orchestrator route gate"
```

---

### Task 4: Add Terminal Node Behavior Tests

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`
- Verify: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`

- [ ] **Step 1: Add terminal summarize test**

Append this test:

```csharp
[Test]
public void ContinueSummarizeDoesNotRequireRouteAndDoesNotExecuteSql()
{
    DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
    int answerCalls = 0;
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ =>
        {
            answerCalls++;
            return AcceptedAnswer();
        },
        now);
    DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Which documents describe DataAgent?",
        null,
        RouteAllowsQuery: true));

    DataAgentOrchestrationResult summary = orchestrator.Continue(new DataAgentOrchestrationRequest(
        "owner",
        "\u603b\u7ed3\u4e00\u4e0b",
        start.SessionId,
        RouteAllowsQuery: false));

    Assert.Multiple(() =>
    {
        Assert.That(answerCalls, Is.EqualTo(1));
        Assert.That(summary.Response.Accepted, Is.True);
        Assert.That(summary.Response.Answer, Is.Null);
        Assert.That(summary.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Summarized));
        Assert.That(summary.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
            DataAgentOrchestrationNodeKind.RouteGate,
            DataAgentOrchestrationNodeKind.Summarize,
            DataAgentOrchestrationNodeKind.Checkpoint
        }));
        Assert.That(summary.Steps.Any(step => step.ExecutedSql), Is.False);
        Assert.That(summary.Checkpoint.CanContinue, Is.True);
        Assert.That(summary.Checkpoint.Terminal, Is.False);
    });
}
```

- [ ] **Step 2: Add terminal end test**

Append this test:

```csharp
[Test]
public void ContinueEndDoesNotRequireRouteAndProducesTerminalCheckpoint()
{
    DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
    int answerCalls = 0;
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ =>
        {
            answerCalls++;
            return AcceptedAnswer();
        },
        now);
    DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Which documents describe DataAgent?",
        null,
        RouteAllowsQuery: true));

    DataAgentOrchestrationResult end = orchestrator.Continue(new DataAgentOrchestrationRequest(
        "owner",
        "\u7ed3\u675f",
        start.SessionId,
        RouteAllowsQuery: false));

    Assert.Multiple(() =>
    {
        Assert.That(answerCalls, Is.EqualTo(1));
        Assert.That(end.Response.Accepted, Is.True);
        Assert.That(end.Response.Answer, Is.Null);
        Assert.That(end.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
        Assert.That(end.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
            DataAgentOrchestrationNodeKind.RouteGate,
            DataAgentOrchestrationNodeKind.End,
            DataAgentOrchestrationNodeKind.Checkpoint
        }));
        Assert.That(end.Steps.Any(step => step.ExecutedSql), Is.False);
        Assert.That(end.Checkpoint.CanContinue, Is.False);
        Assert.That(end.Checkpoint.CanSummarize, Is.False);
        Assert.That(end.Checkpoint.Terminal, Is.True);
    });
}
```

- [ ] **Step 3: Run terminal tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests.ContinueSummarizeDoesNotRequireRouteAndDoesNotExecuteSql|FullyQualifiedName~DataAgentAnalysisOrchestratorTests.ContinueEndDoesNotRequireRouteAndProducesTerminalCheckpoint" -v:minimal
```

Expected: both tests pass because Task 2 records `Summarize`/`End` terminal nodes and Task 3 allows non-query terminal intents when `RouteAllowsQuery` is false.

- [ ] **Step 4: Verify all orchestrator tests still pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests" -v:minimal
```

Expected: all orchestrator tests pass.

- [ ] **Step 5: Commit terminal node behavior tests**

```powershell
git add Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs
git commit -m "Add DataAgent orchestrator terminal nodes"
```

---

### Task 5: Add Clarification And Rejected Branch Coverage

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`
- Verify: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`

- [ ] **Step 1: Add clarification test**

Append this test:

```csharp
[Test]
public void ClarificationBranchRecordsClarificationWithoutExecute()
{
    DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ => ClarificationAnswer(),
        now);

    DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Show status",
        null,
        RouteAllowsQuery: true));

    Assert.Multiple(() =>
    {
        Assert.That(result.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
        Assert.That(result.Response.Answer?.RejectedReason, Is.EqualTo("needs_clarification"));
        Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
            DataAgentOrchestrationNodeKind.RouteGate,
            DataAgentOrchestrationNodeKind.SchemaContext,
            DataAgentOrchestrationNodeKind.Plan,
            DataAgentOrchestrationNodeKind.Clarification,
            DataAgentOrchestrationNodeKind.Checkpoint
        }));
        Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
        Assert.That(result.Checkpoint.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
        Assert.That(result.Checkpoint.CanContinue, Is.True);
    });
}
```

Add this helper:

```csharp
static DataAgentAnswer ClarificationAnswer()
{
    return new DataAgentAnswer(
        string.Empty,
        string.Empty,
        0,
        "Which dataset should I use?",
        "[data_agent_context]\nsql_status=needs_clarification\nclarification_question=Which dataset should I use?\n[/data_agent_context]",
        false,
        "needs_clarification",
        new DataAgentPlannerExplanation(
            "TestPlanner",
            "clarify",
            string.Empty,
            "low",
            ["ambiguous_dataset"],
            "ambiguous dataset"));
}
```

- [ ] **Step 2: Add unsafe rejected answer test**

Append this test:

```csharp
[Test]
public void RejectedPlannerOutputRecordsRejectWithoutExecute()
{
    DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ => RejectedAnswer(),
        now);

    DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Use unsafe planner output",
        null,
        RouteAllowsQuery: true));

    Assert.Multiple(() =>
    {
        Assert.That(result.Response.Answer?.Validated, Is.False);
        Assert.That(result.Response.Answer?.RejectedReason, Is.EqualTo("unsupported_operator:starts_with"));
        Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
            DataAgentOrchestrationNodeKind.RouteGate,
            DataAgentOrchestrationNodeKind.SchemaContext,
            DataAgentOrchestrationNodeKind.Plan,
            DataAgentOrchestrationNodeKind.Validate,
            DataAgentOrchestrationNodeKind.Reject,
            DataAgentOrchestrationNodeKind.Checkpoint
        }));
        Assert.That(result.Steps.Single(step => step.Node == DataAgentOrchestrationNodeKind.Validate).Status, Is.EqualTo(DataAgentOrchestrationStepStatus.Rejected));
        Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
    });
}
```

Add this helper:

```csharp
static DataAgentAnswer RejectedAnswer()
{
    return new DataAgentAnswer(
        "engineering_gate",
        string.Empty,
        0,
        "DataAgent query rejected: unsupported_operator:starts_with",
        "[data_agent_context]\nsql_status=rejected\nrejected_reason=unsupported_operator:starts_with\n[/data_agent_context]",
        false,
        "unsupported_operator:starts_with",
        new DataAgentPlannerExplanation(
            "TestPlanner",
            "unsafe",
            "engineering_gate",
            "low",
            ["unsafe_operator"],
            "unsupported operator"));
}
```

- [ ] **Step 3: Run clarification/reject tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests.ClarificationBranchRecordsClarificationWithoutExecute|FullyQualifiedName~DataAgentAnalysisOrchestratorTests.RejectedPlannerOutputRecordsRejectWithoutExecute" -v:minimal
```

Expected: both tests pass because Task 2 records the `Clarification` branch for `needs_clarification` and the `Validate` + `Reject` branch for invalid planner output.

- [ ] **Step 4: Verify all orchestrator tests pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests" -v:minimal
```

Expected: all orchestrator tests pass.

- [ ] **Step 5: Commit branch coverage**

```powershell
git add Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs
git commit -m "Cover DataAgent orchestrator non-query branches"
```

---

### Task 6: Add Readiness Gates

**Files:**
- Modify: `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `tools/check-dataagent-readiness.ps1`

- [ ] **Step 1: Add RED readiness test expectations**

Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`.

Change the core count assertion from:

```csharp
Assert.That(checks, Has.Count.EqualTo(36));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(42));
```

Add these assertions inside `CoreReadinessChecksAllPass`:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentOrchestratorPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorNodeBoundaryPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorCheckpointPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRouteGateFailClosed"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorTerminalNodesDoNotQuery"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorStateMachineTransitions"));
```

Change the readiness script summary expectation from:

```csharp
"  Summary: 50 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 56 required passed, 0 required missing"
```

- [ ] **Step 2: Run readiness tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: readiness tests fail because the six new check names are not implemented yet.

- [ ] **Step 3: Add runtime readiness checks**

In `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after the existing `AnalysisSessionHasNoSqliteBinding` check, add:

```csharp
InMemoryDataAgentAnalysisSessionStore orchestrationStore = new();
int orchestrationAnswerCalls = 0;
DataAgentAnalysisService orchestrationAnalysisService = new(
    question =>
    {
        orchestrationAnswerCalls++;
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            1,
            "orchestrated answer",
            "[data_agent_context]\nsql_status=validated\nresult_explanation=orchestrated answer\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "ReadinessPlanner",
                "orchestrator_readiness",
                "document_index",
                "high",
                ["orchestrator"],
                "readiness orchestrator answer"));
    },
    orchestrationStore);
DataAgentAnalysisOrchestrator orchestrator = new(orchestrationAnalysisService, orchestrationStore);
DataAgentOrchestrationResult orchestrationStart = orchestrator.Start(new DataAgentOrchestrationRequest(
    "readiness",
    "Which documents describe DataAgent?",
    null,
    RouteAllowsQuery: true));
DataAgentOrchestrationResult orchestrationDenied = orchestrator.Start(new DataAgentOrchestrationRequest(
    "readiness",
    "Which documents describe DataAgent?",
    null,
    RouteAllowsQuery: false));
DataAgentOrchestrationResult orchestrationSummary = orchestrator.Continue(new DataAgentOrchestrationRequest(
    "readiness",
    "\u603b\u7ed3\u4e00\u4e0b",
    orchestrationStart.SessionId,
    RouteAllowsQuery: false));

checks.Add(typeof(IDataAgentAnalysisOrchestrator).IsAssignableFrom(typeof(DataAgentAnalysisOrchestrator)) &&
           orchestrationStart.Response.Accepted
    ? Pass("DataAgentOrchestratorPresent", "native DataAgent analysis orchestrator available")
    : Fail("DataAgentOrchestratorPresent", "orchestrator type or accepted flow missing"));

checks.Add(orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.RouteGate) &&
           orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.SchemaContext) &&
           orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Plan) &&
           orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Validate) &&
           orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute) &&
           orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Explain)
    ? Pass("OrchestratorNodeBoundaryPresent", "route/schema/plan/validate/execute/explain nodes recorded")
    : Fail("OrchestratorNodeBoundaryPresent", string.Join(",", orchestrationStart.Steps.Select(step => step.Node))));

checks.Add(orchestrationStart.Checkpoint.SessionId == orchestrationStart.SessionId &&
           orchestrationStart.Checkpoint.TurnCount == 1 &&
           orchestrationStart.Checkpoint.CanContinue &&
           orchestrationStart.Checkpoint.CanSummarize
    ? Pass("OrchestratorCheckpointPresent", "checkpoint includes session state and continuation flags")
    : Fail("OrchestratorCheckpointPresent", orchestrationStart.Checkpoint.ToString()));

checks.Add(orchestrationDenied.Response.Accepted == false &&
           orchestrationDenied.Response.RejectedReason == "tool_route_required" &&
           orchestrationDenied.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute) == false
    ? Pass("OrchestratorRouteGateFailClosed", "route denial prevents query execution")
    : Fail("OrchestratorRouteGateFailClosed", orchestrationDenied.Response.RejectedReason));

checks.Add(orchestrationSummary.Response.Accepted &&
           orchestrationSummary.Response.Answer is null &&
           orchestrationSummary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Summarize) &&
           orchestrationSummary.Steps.Any(step => step.ExecutedSql) == false &&
           orchestrationAnswerCalls == 1
    ? Pass("OrchestratorTerminalNodesDoNotQuery", "summarize terminal node avoided query execution")
    : Fail("OrchestratorTerminalNodesDoNotQuery", $"answerCalls={orchestrationAnswerCalls}"));

checks.Add(orchestrationStart.SessionStatus == DataAgentAnalysisSessionStatus.Active &&
           orchestrationSummary.SessionStatus == DataAgentAnalysisSessionStatus.Summarized
    ? Pass("OrchestratorStateMachineTransitions", $"{orchestrationStart.SessionStatus}->{orchestrationSummary.SessionStatus}")
    : Fail("OrchestratorStateMachineTransitions", $"{orchestrationStart.SessionStatus}->{orchestrationSummary.SessionStatus}"));
```

- [ ] **Step 4: Add script readiness markers**

In `tools/check-dataagent-readiness.ps1`, add these checks in the `Analysis` group near existing analysis checks:

```powershell
New-Check -Group "Analysis" -Name "DataAgentOrchestratorPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("DataAgentAnalysisOrchestrator", "IDataAgentAnalysisOrchestrator", "RouteAllowsQuery")) -Detail "native DataAgent analysis orchestrator markers"
New-Check -Group "Analysis" -Name "OrchestratorNodeBoundaryPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs" @("DataAgentOrchestrationNodeKind", "RouteGate", "SchemaContext", "Execute", "Checkpoint")) -Detail "orchestrator node boundary markers"
New-Check -Group "Analysis" -Name "OrchestratorCheckpointPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs" @("DataAgentOrchestrationCheckpoint", "CanContinue", "CanSummarize", "Terminal")) -Detail "orchestrator checkpoint markers"
New-Check -Group "Analysis" -Name "OrchestratorRouteGateFailClosed" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("tool_route_required", "RouteAllowsQuery == false", "BuildRejectedResult")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("StartRouteDeniedFailsClosedWithoutCallingAnswer", "ContinueRouteDeniedForQueryTurnDoesNotExecuteSqlOrMutateSession"))) -Detail "orchestrator route gate fail-closed markers"
New-Check -Group "Analysis" -Name "OrchestratorTerminalNodesDoNotQuery" -Passed (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("ContinueSummarizeDoesNotRequireRouteAndDoesNotExecuteSql", "ContinueEndDoesNotRequireRouteAndProducesTerminalCheckpoint", "answerCalls")) -Detail "orchestrator terminal node no-query markers"
New-Check -Group "Analysis" -Name "OrchestratorStateMachineTransitions" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("BuildCheckpoint", "CanContinue", "CanSummarize")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("DataAgentAnalysisSessionStatus.Summarized", "DataAgentAnalysisSessionStatus.Ended"))) -Detail "orchestrator state transition markers"
```

- [ ] **Step 5: Run readiness tests and script**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
DataAgentReadinessTests: 0 failed
DataAgent readiness summary: 56 required passed, 0 required missing
```

- [ ] **Step 6: Confirm QChat engineering map stays green**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 43 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 7: Commit readiness gates**

```powershell
git add Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs tools/check-dataagent-readiness.ps1
git commit -m "Require DataAgent V2.1 orchestrator readiness"
```

---

### Task 7: Full Verification And Branch Push

**Files:**
- Verify all changed files.

- [ ] **Step 1: Run all DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected:

```text
Failed: 0
```

Live PostgreSQL test remains skipped unless `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is set.

- [ ] **Step 2: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent readiness: 56 required passed, 0 required missing
QChat engineering map: 43 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 3: Run full solution**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

Expected: all projects pass with `0 failed`.

- [ ] **Step 4: Check git diff**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected:

```text
git diff --check: exit 0
status: only intentional V2.1 branch commits or clean after commit
```

- [ ] **Step 5: Push V2.1 branch**

Run:

```powershell
git push alife-byastralfox dataagent-v2.1-orchestrator
git ls-remote alife-byastralfox refs/heads/dataagent-v2.1-orchestrator
```

Expected: remote `dataagent-v2.1-orchestrator` points at the final V2.1 implementation commit.

## Self-Review Checklist

- Spec coverage: the plan covers orchestration models, orchestrator contract, accepted flow, route fail-closed behavior, terminal no-query nodes, clarification/reject branches, checkpoints, readiness gates, and full verification.
- Scope: the plan does not add LangGraph, QChat runtime changes, live PostgreSQL requirements, front-end streaming, or new datasets.
- Type consistency: all planned code uses `DataAgentOrchestrationRequest`, `DataAgentOrchestrationResult`, `DataAgentOrchestrationStep`, `DataAgentOrchestrationCheckpoint`, `DataAgentOrchestrationNodeKind`, and `DataAgentOrchestrationStepStatus` consistently.
- TDD: each behavior task starts with failing tests or explicit verification of expected RED before implementation.
