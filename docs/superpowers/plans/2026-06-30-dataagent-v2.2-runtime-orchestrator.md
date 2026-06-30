# DataAgent V2.2 Runtime Orchestrator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire DataAgent analysis XML tools through `IDataAgentAnalysisOrchestrator` at runtime and return trace/checkpoint context for start, continue, summarize, end, rejected, and terminal paths.

**Architecture:** Keep `DataAgentAnalysisService` as the state machine owner. Make `DataAgentAnalysisToolHandler` and `DataAgentAnalysisCapabilityProvider` depend on `IDataAgentAnalysisOrchestrator`, then add a focused `DataAgentOrchestrationContextProvider` that formats the existing analysis context plus orchestration trace and checkpoint fields.

**Tech Stack:** C#/.NET 9, NUnit, existing DataAgent service/store/orchestrator contracts, PowerShell readiness scripts.

---

## File Structure

- Modify `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
  - Add explicit `Summarize(string sessionId)` and `End(string sessionId)` runtime methods.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
  - Implement direct terminal methods.
  - Route `Continue` terminal intents to direct terminal methods.
  - Preserve route-denied query fail-closed behavior.
  - Preserve `DataAgentAnalysisService` as the only session state mutator.

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs`
  - Format `DataAgentOrchestrationResult` into returned tool context.
  - Append `orchestration_trace` and checkpoint fields to the existing response context.
  - Use `DataAgentContextFieldSanitizer` for string fields.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
  - Change constructor dependency from `DataAgentAnalysisService` to `IDataAgentAnalysisOrchestrator`.
  - Use `DataAgentOrchestrationContextProvider.Build(result)` for published/returned context.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
  - Change provider dependency from `DataAgentAnalysisService` to `IDataAgentAnalysisOrchestrator`.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Construct one `InMemoryDataAgentAnalysisSessionStore`.
  - Construct `DataAgentAnalysisService`.
  - Construct `DataAgentAnalysisOrchestrator`.
  - Register `DataAgentAnalysisCapabilityProvider` with the orchestrator.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add seven V2.2 runtime required checks.

- Modify `tools/check-dataagent-readiness.ps1`
  - Add seven V2.2 required marker checks under `[Analysis]`.
  - Update markers that previously required terminal handler calls to `service.Summarize`/`service.End`.

- Modify tests under `Tests/Alife.Test.DataAgent`
  - `DataAgentAnalysisToolHandlerTests.cs`
  - `DataAgentAnalysisOrchestratorTests.cs`
  - `DataAgentReadinessTests.cs`
  - `DataAgentModuleServiceTests.cs`
  - `DataAgentModuleAnalysisRegistrationTests.cs`

---

### Task 1: Orchestrator Runtime Terminal Contract

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`

- [ ] **Step 1: Write failing orchestrator terminal tests**

Add or update these assertions in `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs` so terminal flows no longer include `RouteGate` when the orchestrator knows they are terminal:

```csharp
[Test]
public void SummarizeReturnsTerminalTraceWithoutRouteGateOrQuery()
{
    DateTimeOffset now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
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

    DataAgentOrchestrationResult summary = orchestrator.Summarize(start.SessionId);

    Assert.Multiple(() =>
    {
        Assert.That(answerCalls, Is.EqualTo(1));
        Assert.That(summary.Response.Accepted, Is.True);
        Assert.That(summary.Response.Answer, Is.Null);
        Assert.That(summary.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Summarized));
        Assert.That(summary.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
            DataAgentOrchestrationNodeKind.Summarize,
            DataAgentOrchestrationNodeKind.Checkpoint
        }));
        Assert.That(summary.Steps.Any(step => step.ExecutedSql), Is.False);
        Assert.That(summary.Checkpoint.SessionId, Is.EqualTo(start.SessionId));
        Assert.That(summary.Checkpoint.TurnCount, Is.EqualTo(2));
    });
}

[Test]
public void EndReturnsTerminalTraceWithoutRouteGateOrQuery()
{
    DateTimeOffset now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
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

    DataAgentOrchestrationResult end = orchestrator.End(start.SessionId);

    Assert.Multiple(() =>
    {
        Assert.That(answerCalls, Is.EqualTo(1));
        Assert.That(end.Response.Accepted, Is.True);
        Assert.That(end.Response.Answer, Is.Null);
        Assert.That(end.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
        Assert.That(end.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
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

Update the existing `ContinueSummarizeDoesNotRequireRouteAndDoesNotExecuteSql` and `ContinueEndDoesNotRequireRouteAndProducesTerminalCheckpoint` expectations to the same terminal-only node sequences. They should still call `orchestrator.Continue(...)`, but once interpreted as summarize/end, the result should be:

```csharp
Assert.That(summary.Steps.Select(step => step.Node), Is.EqualTo(new[]
{
    DataAgentOrchestrationNodeKind.Summarize,
    DataAgentOrchestrationNodeKind.Checkpoint
}));
```

```csharp
Assert.That(end.Steps.Select(step => step.Node), Is.EqualTo(new[]
{
    DataAgentOrchestrationNodeKind.End,
    DataAgentOrchestrationNodeKind.Checkpoint
}));
```

- [ ] **Step 2: Run the terminal tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests" -v:minimal
```

Expected: FAIL because `IDataAgentAnalysisOrchestrator` has no `Summarize`/`End` methods and `Continue` terminal flows still include `RouteGate`.

- [ ] **Step 3: Extend the interface**

Change `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs` to:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisOrchestrator
{
    DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Summarize(string sessionId);

    DataAgentOrchestrationResult End(string sessionId);
}
```

- [ ] **Step 4: Implement terminal methods in the orchestrator**

In `DataAgentAnalysisOrchestrator.Continue`, replace direct terminal service routing with calls to the new methods:

```csharp
DataAgentAnalysisTurnIntent intent = followUpInterpreter.Interpret(request.Input, session);
if (intent == DataAgentAnalysisTurnIntent.Summarize)
    return Summarize(request.SessionId!);

if (intent == DataAgentAnalysisTurnIntent.End)
    return End(request.SessionId!);

bool queryProducing = intent.ProducesQuery();
```

Add public terminal methods:

```csharp
public DataAgentOrchestrationResult Summarize(string sessionId)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

    DataAgentAnalysisResponse response = analysisService.Summarize(sessionId);
    return BuildResult(
        response,
        [
            Step(DataAgentOrchestrationNodeKind.Summarize, response.Accepted ? DataAgentOrchestrationStepStatus.Succeeded : DataAgentOrchestrationStepStatus.Rejected, response.Accepted ? "terminal_summary" : response.RejectedReason, false),
            Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
        ]);
}

public DataAgentOrchestrationResult End(string sessionId)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

    DataAgentAnalysisResponse response = analysisService.End(sessionId);
    return BuildResult(
        response,
        [
            Step(DataAgentOrchestrationNodeKind.End, response.Accepted ? DataAgentOrchestrationStepStatus.Succeeded : DataAgentOrchestrationStepStatus.Rejected, response.Accepted ? "terminal_end" : response.RejectedReason, false),
            Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
        ]);
}
```

Keep `AppendAnswerSteps` available for query-producing `Start`/`Continue`.

- [ ] **Step 5: Run the orchestrator tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisOrchestratorTests" -v:minimal
```

Expected: PASS for all `DataAgentAnalysisOrchestratorTests`.

- [ ] **Step 6: Commit Task 1**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs
git commit -m "Add DataAgent orchestrator terminal runtime methods"
```

---

### Task 2: Orchestration Context Provider

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentOrchestrationContextProviderTests.cs`

- [ ] **Step 1: Write failing context provider tests**

Create `Tests/Alife.Test.DataAgent/DataAgentOrchestrationContextProviderTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentOrchestrationContextProviderTests
{
    [Test]
    public void BuildAppendsTraceAndCheckpointFieldsToResponseContext()
    {
        DataAgentOrchestrationResult result = Result(
            "[data_agent_analysis_session_context]\nsession_id=session-1\nturn_count=1\n[/data_agent_analysis_session_context]",
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 1, true, true, false));

        string context = DataAgentOrchestrationContextProvider.Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("checkpoint_session_id=session-1"));
            Assert.That(context, Does.Contain("checkpoint_status=Active"));
            Assert.That(context, Does.Contain("checkpoint_turn_count=1"));
            Assert.That(context, Does.Contain("checkpoint_can_continue=true"));
            Assert.That(context, Does.Contain("checkpoint_can_summarize=true"));
            Assert.That(context, Does.Contain("checkpoint_terminal=false"));
        });
    }

    [Test]
    public void BuildReturnsTraceAndCheckpointFieldsWhenResponseContextIsEmpty()
    {
        DataAgentOrchestrationResult result = Result(
            string.Empty,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint(string.Empty, DataAgentAnalysisSessionStatus.Rejected, string.Empty, 0, false, false, true));

        string context = DataAgentOrchestrationContextProvider.Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("checkpoint_status=Rejected"));
            Assert.That(context, Does.Contain("checkpoint_terminal=true"));
            Assert.That(context, Does.Not.Contain("tool_route_required"));
        });
    }

    static DataAgentOrchestrationResult Result(
        string context,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        DataAgentOrchestrationCheckpoint checkpoint)
    {
        DataAgentAnalysisResponse response = new(
            checkpoint.SessionId,
            checkpoint.SessionStatus,
            DataAgentAnalysisTurnIntent.NewQuestion,
            null,
            string.Empty,
            context,
            checkpoint.SessionStatus != DataAgentAnalysisSessionStatus.Rejected,
            checkpoint.SessionStatus == DataAgentAnalysisSessionStatus.Rejected ? "tool_route_required" : string.Empty);

        return new DataAgentOrchestrationResult(
            checkpoint.SessionId,
            checkpoint.SessionStatus,
            steps,
            checkpoint,
            response);
    }
}
```

- [ ] **Step 2: Run provider tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentOrchestrationContextProviderTests" -v:minimal
```

Expected: FAIL because `DataAgentOrchestrationContextProvider` does not exist.

- [ ] **Step 3: Create the context provider**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs`:

```csharp
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentOrchestrationContextProvider
{
    public static string Build(DataAgentOrchestrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StringBuilder builder = new();
        if (string.IsNullOrWhiteSpace(result.Response.Context) == false)
            builder.AppendLine(result.Response.Context.Trim());

        builder.AppendLine($"orchestration_trace={BuildTrace(result.Steps)}");
        builder.AppendLine($"checkpoint_session_id={Sanitize(result.Checkpoint.SessionId)}");
        builder.AppendLine($"checkpoint_status={result.Checkpoint.SessionStatus}");
        builder.AppendLine($"checkpoint_turn_count={result.Checkpoint.TurnCount}");
        builder.AppendLine($"checkpoint_can_continue={ToLowerBool(result.Checkpoint.CanContinue)}");
        builder.AppendLine($"checkpoint_can_summarize={ToLowerBool(result.Checkpoint.CanSummarize)}");
        builder.AppendLine($"checkpoint_terminal={ToLowerBool(result.Checkpoint.Terminal)}");

        return builder.ToString().Trim();
    }

    static string BuildTrace(IEnumerable<DataAgentOrchestrationStep> steps)
    {
        return string.Join(
            ">",
            steps.Select(step => $"{step.Node}:{step.Status}"));
    }

    static string Sanitize(string value)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value);
    }

    static string ToLowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}
```

- [ ] **Step 4: Run provider tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentOrchestrationContextProviderTests" -v:minimal
```

Expected: PASS for both context provider tests.

- [ ] **Step 5: Commit Task 2**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs Tests/Alife.Test.DataAgent/DataAgentOrchestrationContextProviderTests.cs
git commit -m "Add DataAgent orchestration context provider"
```

---

### Task 3: XML Tool Handler Runtime Integration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests for orchestrator dependency**

Update `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs` so tests construct the handler with `IDataAgentAnalysisOrchestrator`.

Add a fake orchestrator:

```csharp
sealed class RecordingOrchestrator : IDataAgentAnalysisOrchestrator
{
    readonly Dictionary<string, DataAgentOrchestrationResult> results;

    public RecordingOrchestrator(Dictionary<string, DataAgentOrchestrationResult> results)
    {
        this.results = results;
    }

    public List<DataAgentOrchestrationRequest> StartRequests { get; } = [];
    public List<DataAgentOrchestrationRequest> ContinueRequests { get; } = [];
    public List<string> SummarizeSessionIds { get; } = [];
    public List<string> EndSessionIds { get; } = [];

    public DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request)
    {
        StartRequests.Add(request);
        return results["start"];
    }

    public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
    {
        ContinueRequests.Add(request);
        return results["continue"];
    }

    public DataAgentOrchestrationResult Summarize(string sessionId)
    {
        SummarizeSessionIds.Add(sessionId);
        return results["summarize"];
    }

    public DataAgentOrchestrationResult End(string sessionId)
    {
        EndSessionIds.Add(sessionId);
        return results["end"];
    }
}
```

Add a reusable result helper:

```csharp
static DataAgentOrchestrationResult OrchestratedResult(
    string sessionId,
    DataAgentAnalysisSessionStatus status,
    DataAgentAnalysisTurnIntent intent,
    IReadOnlyList<DataAgentOrchestrationStep> steps,
    int turnCount,
    string baseContext)
{
    DataAgentAnalysisResponse response = new(
        sessionId,
        status,
        intent,
        null,
        string.Empty,
        baseContext,
        true,
        string.Empty);

    return new DataAgentOrchestrationResult(
        sessionId,
        status,
        steps,
        new DataAgentOrchestrationCheckpoint(
            sessionId,
            status,
            "document_index",
            turnCount,
            CanContinue: status != DataAgentAnalysisSessionStatus.Ended,
            CanSummarize: turnCount > 0 && status != DataAgentAnalysisSessionStatus.Ended,
            Terminal: status == DataAgentAnalysisSessionStatus.Ended),
        response);
}
```

Add or update the start test:

```csharp
[Test]
public void StartCallsOrchestratorAndPublishesOrchestratedContext()
{
    List<string> published = [];
    RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
    {
        ["start"] = OrchestratedResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            1,
            "[data_agent_analysis_session_context]\nsession_id=session-1\ncaller_id=xiayu\n[/data_agent_analysis_session_context]")
    });
    DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add);

    string context = handler.Start("xiayu", "Which documents describe DataAgent?");

    Assert.Multiple(() =>
    {
        Assert.That(orchestrator.StartRequests, Has.Count.EqualTo(1));
        Assert.That(orchestrator.StartRequests[0].CallerId, Is.EqualTo("xiayu"));
        Assert.That(orchestrator.StartRequests[0].Input, Is.EqualTo("Which documents describe DataAgent?"));
        Assert.That(orchestrator.StartRequests[0].SessionId, Is.Null);
        Assert.That(orchestrator.StartRequests[0].RouteAllowsQuery, Is.True);
        Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded"));
        Assert.That(context, Does.Contain("checkpoint_session_id=session-1"));
        Assert.That(published, Is.EqualTo(new[] { context }));
    });
}
```

Add equivalent tests for `Continue`, `Summarize`, and `End`:

```csharp
Assert.That(orchestrator.ContinueRequests[0].SessionId, Is.EqualTo("session-1"));
Assert.That(orchestrator.ContinueRequests[0].Input, Is.EqualTo("continue"));
Assert.That(orchestrator.ContinueRequests[0].RouteAllowsQuery, Is.True);
```

```csharp
Assert.That(orchestrator.SummarizeSessionIds, Is.EqualTo(new[] { "session-1" }));
Assert.That(context, Does.Contain("orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded"));
```

```csharp
Assert.That(orchestrator.EndSessionIds, Is.EqualTo(new[] { "session-1" }));
Assert.That(context, Does.Contain("orchestration_trace=End:Succeeded>Checkpoint:Succeeded"));
```

Keep `AnalysisMethodsAreRegisteredAsXmlFunctions` and blank argument tests, but construct the handler with `RecordingOrchestrator`.

- [ ] **Step 2: Run handler tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: FAIL because `DataAgentAnalysisToolHandler` still requires `DataAgentAnalysisService`.

- [ ] **Step 3: Change the handler dependency**

Replace the constructor declaration in `DataAgentAnalysisToolHandler.cs` with:

```csharp
public sealed class DataAgentAnalysisToolHandler(IDataAgentAnalysisOrchestrator orchestrator, Action<string>? resultPublisher = null)
```

Use orchestrator calls:

```csharp
DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
    callerId,
    goalOrQuestion,
    null,
    RouteAllowsQuery: true));
string context = DataAgentOrchestrationContextProvider.Build(result);
```

```csharp
DataAgentOrchestrationResult result = orchestrator.Continue(new DataAgentOrchestrationRequest(
    "local",
    question,
    sessionId,
    RouteAllowsQuery: true));
string context = DataAgentOrchestrationContextProvider.Build(result);
```

```csharp
DataAgentOrchestrationResult result = orchestrator.Summarize(sessionId);
string context = DataAgentOrchestrationContextProvider.Build(result);
```

```csharp
DataAgentOrchestrationResult result = orchestrator.End(sessionId);
string context = DataAgentOrchestrationContextProvider.Build(result);
```

Keep existing argument validation and publisher behavior.

- [ ] **Step 4: Run handler tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: PASS for all handler tests.

- [ ] **Step 5: Commit Task 3**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs
git commit -m "Route DataAgent analysis tools through orchestrator"
```

---

### Task 4: Module and Capability Provider Wiring

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleAnalysisRegistrationTests.cs`

- [ ] **Step 1: Write failing wiring tests**

In `DataAgentModuleServiceTests.cs`, add an assertion to `AwakeRegistersDataAgentCapabilityProviders` or create a new test:

```csharp
[Test]
public void AwakeConstructsAnalysisOrchestratorForRuntimeTools()
{
    string source = ReadModuleSource();

    Assert.Multiple(() =>
    {
        Assert.That(source, Does.Contain("DataAgentAnalysisOrchestrator"));
        Assert.That(source, Does.Contain("IDataAgentAnalysisOrchestrator"));
        Assert.That(source, Does.Contain("new DataAgentAnalysisOrchestrator"));
        Assert.That(source, Does.Contain("new DataAgentAnalysisCapabilityProvider(analysisOrchestrator"));
    });
}
```

In `DataAgentModuleAnalysisRegistrationTests.cs`, extend `AwakeRegistersCapabilityProviderHandlersIntoFunctionCaller` by actually handling a start call if `XmlFunctionCaller` exposes the same helper already used in nearby tests. If no direct invocation helper exists, keep this test as registration-only and rely on handler tests for call behavior.

- [ ] **Step 2: Run module tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentModuleServiceTests|FullyQualifiedName~DataAgentModuleAnalysisRegistrationTests" -v:minimal
```

Expected: FAIL because module source does not construct `DataAgentAnalysisOrchestrator` for the capability provider.

- [ ] **Step 3: Change the capability provider dependency**

Change `DataAgentAnalysisCapabilityProvider.cs` to:

```csharp
public sealed class DataAgentAnalysisCapabilityProvider(
    IDataAgentAnalysisOrchestrator orchestrator,
    Action<string>? resultPublisher = null) : IDataAgentCapabilityProvider
{
    public string Name => nameof(DataAgentAnalysisCapabilityProvider);

    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => DataAgentToolCapabilityManifests.Analysis;

    public void Register(IDataAgentCapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterXmlHandlerWithoutStaticDocument(new XmlHandler(new DataAgentAnalysisToolHandler(orchestrator, resultPublisher)));
    }
}
```

- [ ] **Step 4: Wire the module to the orchestrator**

In `DataAgentModuleService.AwakeAsync`, replace the analysis provider setup with:

```csharp
InMemoryDataAgentAnalysisSessionStore analysisSessionStore = new InMemoryDataAgentAnalysisSessionStore();
DataAgentAnalysisService analysisService = new DataAgentAnalysisService(service, analysisSessionStore);
IDataAgentAnalysisOrchestrator analysisOrchestrator = new DataAgentAnalysisOrchestrator(
    analysisService,
    analysisSessionStore);
```

Then register:

```csharp
capabilityRegistry.Add(new DataAgentAnalysisCapabilityProvider(analysisOrchestrator, PublishAnalysisContext));
```

- [ ] **Step 5: Run module tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentModuleServiceTests|FullyQualifiedName~DataAgentModuleAnalysisRegistrationTests" -v:minimal
```

Expected: PASS for module wiring tests.

- [ ] **Step 6: Commit Task 4**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs Tests/Alife.Test.DataAgent/DataAgentModuleAnalysisRegistrationTests.cs
git commit -m "Wire DataAgent module analysis tools to orchestrator"
```

---

### Task 5: Runtime Readiness Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Write failing readiness tests**

In `DataAgentReadinessTests.CoreReadinessChecksAllPass`, update the count and assert the seven V2.2 checks:

```csharp
Assert.That(checks, Has.Count.EqualTo(49));
Assert.That(checks.Select(check => check.Name), Does.Contain("AnalysisToolHandlerUsesOrchestrator"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorTraceContextPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorCheckpointContextPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeStartPathCovered"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeContinuePathCovered"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeTerminalPathCovered"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeRouteDeniedFailClosed"));
```

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, update the script summary:

```csharp
Assert.That(GetSummaryLines(result.StandardOutput), Is.EqualTo(new[]
{
    "  Summary: 63 required passed, 0 required missing"
}));
```

Also assert the printed script output includes:

```csharp
Assert.That(result.StandardOutput, Does.Contain("AnalysisToolHandlerUsesOrchestrator"));
Assert.That(result.StandardOutput, Does.Contain("OrchestratorTraceContextPresent"));
Assert.That(result.StandardOutput, Does.Contain("OrchestratorRuntimeRouteDeniedFailClosed"));
```

- [ ] **Step 2: Run readiness tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: FAIL because the seven V2.2 checks are not implemented.

- [ ] **Step 3: Add runtime checks to `DataAgentReadiness.cs`**

After existing orchestrator checks in `DataAgentReadiness.CheckCore`, add checks that use real runtime objects:

```csharp
string orchestrationStartContext = DataAgentOrchestrationContextProvider.Build(orchestrationStart);
DataAgentOrchestrationResult orchestrationRuntimeContinue = orchestrator.Continue(new DataAgentOrchestrationRequest(
    "readiness",
    "\u7ee7\u7eed",
    orchestrationStart.SessionId,
    RouteAllowsQuery: true));
string orchestrationContinueContext = DataAgentOrchestrationContextProvider.Build(orchestrationRuntimeContinue);
string orchestrationSummaryContext = DataAgentOrchestrationContextProvider.Build(orchestrationSummary);
string orchestrationDeniedContinueContext = DataAgentOrchestrationContextProvider.Build(orchestrationDeniedContinue);

checks.Add(typeof(DataAgentAnalysisToolHandler)
        .GetConstructors()
        .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IDataAgentAnalysisOrchestrator)))
    ? Pass("AnalysisToolHandlerUsesOrchestrator", "analysis XML handler depends on IDataAgentAnalysisOrchestrator")
    : Fail("AnalysisToolHandlerUsesOrchestrator", "analysis XML handler can bypass orchestrator"));

checks.Add(orchestrationStartContext.Contains("orchestration_trace=RouteGate:Succeeded", StringComparison.Ordinal) &&
           orchestrationStartContext.Contains("Execute:Succeeded", StringComparison.Ordinal)
    ? Pass("OrchestratorTraceContextPresent", "orchestration trace emitted in runtime context")
    : Fail("OrchestratorTraceContextPresent", orchestrationStartContext));

checks.Add(orchestrationStartContext.Contains("checkpoint_session_id=", StringComparison.Ordinal) &&
           orchestrationStartContext.Contains("checkpoint_can_continue=true", StringComparison.Ordinal) &&
           orchestrationStartContext.Contains("checkpoint_terminal=false", StringComparison.Ordinal)
    ? Pass("OrchestratorCheckpointContextPresent", "checkpoint context emitted")
    : Fail("OrchestratorCheckpointContextPresent", orchestrationStartContext));

checks.Add(orchestrationStart.Response.Accepted &&
           orchestrationStartContext.Contains("[data_agent_analysis_session_context]", StringComparison.Ordinal)
    ? Pass("OrchestratorRuntimeStartPathCovered", "start path returned analysis context and orchestration trace")
    : Fail("OrchestratorRuntimeStartPathCovered", orchestrationStartContext));

checks.Add(orchestrationRuntimeContinue.Response.Accepted &&
           orchestrationRuntimeContinue.Checkpoint.TurnCount == 2 &&
           orchestrationContinueContext.Contains("checkpoint_turn_count=2", StringComparison.Ordinal)
    ? Pass("OrchestratorRuntimeContinuePathCovered", "continue path returned second-turn checkpoint")
    : Fail("OrchestratorRuntimeContinuePathCovered", orchestrationContinueContext));

checks.Add(orchestrationSummary.Response.Accepted &&
           orchestrationSummaryContext.Contains("orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded", StringComparison.Ordinal) &&
           orchestrationSummary.Steps.Any(step => step.ExecutedSql) == false
    ? Pass("OrchestratorRuntimeTerminalPathCovered", "terminal summarize path returned no-query trace")
    : Fail("OrchestratorRuntimeTerminalPathCovered", orchestrationSummaryContext));

checks.Add(orchestrationDeniedContinue.Response.Accepted == false &&
           orchestrationDeniedContinueContext.Contains("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded", StringComparison.Ordinal) &&
           answerCallsAfterDeniedContinue == 1 &&
           turnsAfterDeniedContinue == 1
    ? Pass("OrchestratorRuntimeRouteDeniedFailClosed", "route-denied runtime continue returned rejected trace without mutation")
    : Fail("OrchestratorRuntimeRouteDeniedFailClosed", orchestrationDeniedContinueContext));
```

Important ordering: create `orchestrationRuntimeContinue` before `orchestrationSummary`, because summary transitions the session to `Summarized`.

- [ ] **Step 4: Add script marker checks**

In `tools/check-dataagent-readiness.ps1`, add seven `[Analysis]` checks:

```powershell
New-Check -Group "Analysis" -Name "AnalysisToolHandlerUsesOrchestrator" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("IDataAgentAnalysisOrchestrator", "orchestrator.Start", "orchestrator.Continue", "orchestrator.Summarize", "orchestrator.End")) -Detail "analysis XML tool handler runtime orchestrator markers"
New-Check -Group "Analysis" -Name "OrchestratorTraceContextPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs" @("orchestration_trace", "BuildTrace", "DataAgentOrchestrationStep")) -Detail "orchestration trace context markers"
New-Check -Group "Analysis" -Name "OrchestratorCheckpointContextPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs" @("checkpoint_session_id", "checkpoint_can_continue", "checkpoint_terminal")) -Detail "orchestration checkpoint context markers"
New-Check -Group "Analysis" -Name "OrchestratorRuntimeStartPathCovered" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeStartPathCovered", "orchestrationStartContext", "[data_agent_analysis_session_context]")) -Detail "runtime orchestrator start path markers"
New-Check -Group "Analysis" -Name "OrchestratorRuntimeContinuePathCovered" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeContinuePathCovered", "orchestrationRuntimeContinue", "checkpoint_turn_count=2")) -Detail "runtime orchestrator continue path markers"
New-Check -Group "Analysis" -Name "OrchestratorRuntimeTerminalPathCovered" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeTerminalPathCovered", "Summarize:Succeeded>Checkpoint:Succeeded", "ExecutedSql")) -Detail "runtime orchestrator terminal path markers"
New-Check -Group "Analysis" -Name "OrchestratorRuntimeRouteDeniedFailClosed" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeRouteDeniedFailClosed", "RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded", "turnsAfterDeniedContinue")) -Detail "runtime route-denied fail-closed markers"
```

Update the existing `AnalysisTerminalToolsDoNotQuery` marker to expect orchestrator terminal calls instead of direct service calls:

```powershell
New-Check -Group "Analysis" -Name "AnalysisTerminalToolsDoNotQuery" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("dataagent_analysis_summarize", "dataagent_analysis_end", "orchestrator.Summarize", "orchestrator.End")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs" @("Summarize", "End", "orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded", "orchestration_trace=End:Succeeded>Checkpoint:Succeeded"))) -Detail "terminal analysis tools avoid answer-boundary query calls"
```

- [ ] **Step 5: Run readiness tests and scripts and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
DataAgentReadinessTests: 0 failed
Summary: 63 required passed, 0 required missing
```

- [ ] **Step 6: Commit Task 5**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "Add DataAgent V2.2 runtime readiness gates"
```

---

### Task 6: Final Verification and Branch Readiness

**Files:**
- No planned production edits.
- Verify all touched behavior and repository hygiene.

- [ ] **Step 1: Run DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected:

```text
失败: 0
```

- [ ] **Step 2: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent readiness: 63 required passed, 0 required missing
QChat engineering map: 43 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 3: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

Expected:

```text
失败: 0
```

If the full solution test run fails only with the known DeskPet smoke timeout:

```text
System.TimeoutException: 无法连接到桌宠客户端
```

then rerun the focused DeskPet smoke test once and rerun the full solution test before treating it as a regression.

- [ ] **Step 4: Run diff hygiene**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected:

```text
git diff --check has no output
branch dataagent-v2.2-runtime-orchestrator is clean after commits
```

- [ ] **Step 5: Push the branch**

Run:

```powershell
git push -u alife-byastralfox dataagent-v2.2-runtime-orchestrator
git ls-remote alife-byastralfox refs/heads/dataagent-v2.2-runtime-orchestrator
```

Expected: remote branch points to the local branch HEAD.

---

## Self-Review Checklist

- Spec coverage:
  - XML handler runtime orchestration is covered by Task 3.
  - Explicit terminal orchestrator methods are covered by Task 1.
  - Trace/checkpoint context is covered by Task 2.
  - Module/provider runtime registration is covered by Task 4.
  - Required readiness gates are covered by Task 5.
  - Full verification and push are covered by Task 6.

- Type consistency:
  - `IDataAgentAnalysisOrchestrator.Start/Continue` use `DataAgentOrchestrationRequest`.
  - `IDataAgentAnalysisOrchestrator.Summarize/End` use `string sessionId`.
  - Handler context is produced through `DataAgentOrchestrationContextProvider.Build`.
  - Module provider uses `IDataAgentAnalysisOrchestrator`.

- Risk controls:
  - `DataAgentAnalysisService` remains the state machine owner.
  - No live LLM or live PostgreSQL dependency is added.
  - Terminal paths do not execute SQL.
  - Route-denied query-producing continue remains fail-closed and does not mutate persisted sessions.
