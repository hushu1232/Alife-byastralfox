# DataAgent V2.3 Tool Broker Route Orchestrator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the real Tool Broker route decision feed `DataAgentOrchestrationRequest` and route evidence context instead of hard-coded `RouteAllowsQuery: true`.

**Architecture:** Add a small DataAgent-owned route context model and accessor boundary. The runtime accessor adapts `XmlFunctionExecutionPolicy.CurrentRoute` into sanitized DataAgent route facts; the XML policy remains the first fail-closed gate, and `DataAgentAnalysisService` remains the state machine owner.

**Tech Stack:** C#/.NET 9, NUnit, PowerShell readiness harness, existing Alife FunctionCaller XML runtime.

---

## Scope Check

This plan implements one subsystem: Tool Broker route decision integration into DataAgent analysis orchestration. It does not introduce LangGraph, live PostgreSQL requirements, UI streaming, or new SQL datasets.

## File Structure

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs`
  - Owns the DataAgent-safe route context record, missing-route defaults, fakeable accessor interface, missing accessor, and runtime XML-policy adapter.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs`
  - Adds route context to requests and results without breaking existing constructor calls.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs`
  - Appends sanitized `route_*` fields when route context is present.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
  - Allows terminal calls to optionally carry route context evidence.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
  - Propagates request route context into results while preserving route-gate and state-machine behavior.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
  - Consumes route context accessor for start, continue, summarize, and end.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
  - Passes the accessor into the handler while keeping existing optional constructor behavior.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Wires the real accessor using `functionService.ExecutionPolicy`.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds required V2.3 runtime readiness evidence.
- Modify `tools/check-dataagent-readiness.ps1`
  - Adds required file-marker gates for V2.3.
- Modify tests under `Tests/Alife.Test.DataAgent`
  - Adds focused tests for accessor, handler, context provider, orchestrator propagation, module wiring, and readiness counts.

---

### Task 1: Add DataAgent Route Context Boundary

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentToolRouteContextAccessorTests.cs`

- [ ] **Step 1: Write failing accessor tests**

Create `Tests/Alife.Test.DataAgent/DataAgentToolRouteContextAccessorTests.cs`:

```csharp
using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentToolRouteContextAccessorTests
{
    [Test]
    public void MissingAccessorReturnsFailClosedContext()
    {
        DataAgentToolRouteContext context = MissingDataAgentToolRouteContextAccessor.Instance.Get(
            "dataagent_analysis_start",
            null);

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.False);
            Assert.That(context.ToolName, Is.EqualTo("dataagent_analysis_start"));
            Assert.That(context.AllowsTool, Is.False);
            Assert.That(context.AllowsQuery, Is.False);
            Assert.That(context.RouteId, Is.Empty);
            Assert.That(context.Intent, Is.Empty);
            Assert.That(context.ReasonCode, Is.EqualTo("tool_route_required"));
            Assert.That(context.RouteSessionId, Is.Empty);
        });
    }

    [Test]
    public void XmlPolicyAccessorReturnsAllowedContextForCurrentRoute()
    {
        XmlFunctionExecutionPolicy policy = new();
        policy.CurrentRoute = new ToolRouteDecision(
            "route-1",
            ToolCapabilityDomain.DataAgent,
            "analysis_start",
            ["dataagent_analysis_start"],
            [],
            new ToolRouteState(string.Empty, string.Empty, true, true, true),
            "route_allowed",
            "route_allowed");
        XmlPolicyDataAgentToolRouteContextAccessor accessor = new(policy);

        DataAgentToolRouteContext context = accessor.Get("dataagent_analysis_start", null);

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.True);
            Assert.That(context.ToolName, Is.EqualTo("dataagent_analysis_start"));
            Assert.That(context.AllowsTool, Is.True);
            Assert.That(context.AllowsQuery, Is.True);
            Assert.That(context.RouteId, Is.EqualTo("route-1"));
            Assert.That(context.Intent, Is.EqualTo("analysis_start"));
            Assert.That(context.ReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(context.RouteSessionId, Is.Empty);
        });
    }

    [Test]
    public void XmlPolicyAccessorReturnsFailClosedContextWhenRouteIsMissing()
    {
        XmlPolicyDataAgentToolRouteContextAccessor accessor = new(new XmlFunctionExecutionPolicy());

        DataAgentToolRouteContext context = accessor.Get("dataagent_analysis_continue", "session-1");

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.False);
            Assert.That(context.AllowsTool, Is.False);
            Assert.That(context.AllowsQuery, Is.False);
            Assert.That(context.ReasonCode, Is.EqualTo("tool_route_required"));
            Assert.That(context.RouteSessionId, Is.Empty);
        });
    }

    [Test]
    public void XmlPolicyAccessorRejectsSessionScopedMismatchForDefenseInDepth()
    {
        XmlFunctionExecutionPolicy policy = new();
        policy.CurrentRoute = new ToolRouteDecision(
            "route-2",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            ["dataagent_analysis_continue"],
            [],
            new ToolRouteState("session-allowed", "Active", true, true, true),
            "route_allowed",
            "route_allowed");
        XmlPolicyDataAgentToolRouteContextAccessor accessor = new(policy);

        DataAgentToolRouteContext context = accessor.Get("dataagent_analysis_continue", "session-other");

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.True);
            Assert.That(context.AllowsTool, Is.False);
            Assert.That(context.AllowsQuery, Is.False);
            Assert.That(context.RouteId, Is.EqualTo("route-2"));
            Assert.That(context.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(context.ReasonCode, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(context.RouteSessionId, Is.EqualTo("session-allowed"));
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentToolRouteContextAccessorTests -v:minimal
```

Expected: FAIL because `DataAgentToolRouteContext`, `IDataAgentToolRouteContextAccessor`, `MissingDataAgentToolRouteContextAccessor`, and `XmlPolicyDataAgentToolRouteContextAccessor` do not exist.

- [ ] **Step 3: Add the route context model and accessors**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs`:

```csharp
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed record DataAgentToolRouteContext(
    bool Present,
    string ToolName,
    bool AllowsTool,
    bool AllowsQuery,
    string RouteId,
    string Intent,
    string ReasonCode,
    string RouteSessionId)
{
    public const string MissingRouteReasonCode = "tool_route_required";
    public const string ToolNotAllowedReasonCode = "tool_not_allowed_in_current_route";
    public const string SessionNotAllowedReasonCode = "tool_session_not_allowed_in_current_route";

    public static DataAgentToolRouteContext Missing(string toolName)
    {
        return new DataAgentToolRouteContext(
            false,
            toolName,
            false,
            false,
            string.Empty,
            string.Empty,
            MissingRouteReasonCode,
            string.Empty);
    }
}

public interface IDataAgentToolRouteContextAccessor
{
    DataAgentToolRouteContext Get(string toolName, string? sessionId);
}

public sealed class MissingDataAgentToolRouteContextAccessor : IDataAgentToolRouteContextAccessor
{
    public static MissingDataAgentToolRouteContextAccessor Instance { get; } = new();

    MissingDataAgentToolRouteContextAccessor()
    {
    }

    public DataAgentToolRouteContext Get(string toolName, string? sessionId)
    {
        return DataAgentToolRouteContext.Missing(toolName);
    }
}

public sealed class XmlPolicyDataAgentToolRouteContextAccessor(XmlFunctionExecutionPolicy executionPolicy)
    : IDataAgentToolRouteContextAccessor
{
    public DataAgentToolRouteContext Get(string toolName, string? sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        ToolRouteDecision? route = executionPolicy.CurrentRoute;
        if (route is null)
            return DataAgentToolRouteContext.Missing(toolName);

        bool routeAllowsTool = route.Allows(toolName);
        bool sessionAllowed = IsSessionAllowed(toolName, sessionId, route.State.ActiveDataAgentSessionId);
        bool allowed = routeAllowsTool && sessionAllowed;
        string reasonCode = allowed
            ? route.ReasonCode
            : routeAllowsTool
                ? DataAgentToolRouteContext.SessionNotAllowedReasonCode
                : DataAgentToolRouteContext.ToolNotAllowedReasonCode;

        return new DataAgentToolRouteContext(
            true,
            toolName,
            allowed,
            allowed,
            route.RouteId,
            route.Intent,
            reasonCode,
            route.State.ActiveDataAgentSessionId);
    }

    static bool IsSessionAllowed(string toolName, string? requestedSessionId, string routeSessionId)
    {
        if (IsSessionScopedDataAgentTool(toolName) == false)
            return true;

        if (string.IsNullOrWhiteSpace(routeSessionId))
            return false;

        if (string.IsNullOrWhiteSpace(requestedSessionId))
            return false;

        return string.Equals(requestedSessionId, routeSessionId, StringComparison.Ordinal);
    }

    static bool IsSessionScopedDataAgentTool(string toolName)
    {
        return string.Equals(toolName, "dataagent_analysis_continue", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "dataagent_analysis_summarize", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "dataagent_analysis_end", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run accessor tests to verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentToolRouteContextAccessorTests -v:minimal
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentToolRouteContext.cs Tests\Alife.Test.DataAgent\DataAgentToolRouteContextAccessorTests.cs
git commit -m "Add DataAgent tool route context accessor"
```

---

### Task 2: Add Route Evidence To Orchestration Models And Context

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentOrchestrationContextProviderTests.cs`

- [ ] **Step 1: Write failing context-provider test**

Append this test to `DataAgentOrchestrationContextProviderTests`:

```csharp
[Test]
public void BuildAppendsSanitizedRouteEvidenceWhenPresent()
{
    DataAgentToolRouteContext routeContext = new(
        true,
        "dataagent_analysis_continue",
        true,
        true,
        "route\nunsafe",
        "analysis_continue",
        "route_allowed",
        "session-1");
    DataAgentOrchestrationResult result = Result(
        "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]",
        [
            new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
            new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
        ],
        new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 2, true, true, false),
        routeContext);

    string context = DataAgentOrchestrationContextProvider.Build(result);

    Assert.Multiple(() =>
    {
        Assert.That(context, Does.Contain("route_present=true"));
        Assert.That(context, Does.Contain("route_tool=dataagent_analysis_continue"));
        Assert.That(context, Does.Contain("route_allows_tool=true"));
        Assert.That(context, Does.Contain("route_allows_query=true"));
        Assert.That(context, Does.Contain("route_id=route unsafe"));
        Assert.That(context, Does.Contain("route_intent=analysis_continue"));
        Assert.That(context, Does.Contain("route_reason_code=route_allowed"));
        Assert.That(context, Does.Contain("route_session_id=session-1"));
    });
}
```

Update the private `Result` helper signature in the same file:

```csharp
static DataAgentOrchestrationResult Result(
    string context,
    IReadOnlyList<DataAgentOrchestrationStep> steps,
    DataAgentOrchestrationCheckpoint checkpoint,
    DataAgentToolRouteContext? routeContext = null)
```

And update its return statement:

```csharp
return new DataAgentOrchestrationResult(
    checkpoint.SessionId,
    checkpoint.SessionStatus,
    steps,
    checkpoint,
    response,
    routeContext);
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentOrchestrationContextProviderTests -v:minimal
```

Expected: FAIL because `DataAgentOrchestrationResult` has no route context and no `route_*` fields are emitted.

- [ ] **Step 3: Extend orchestration request and result models**

Modify `DataAgentOrchestrationModels.cs`:

```csharp
public sealed record DataAgentOrchestrationRequest(
    string CallerId,
    string Input,
    string? SessionId,
    bool RouteAllowsQuery,
    DataAgentToolRouteContext? RouteContext = null);

public sealed record DataAgentOrchestrationResult(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    IReadOnlyList<DataAgentOrchestrationStep> Steps,
    DataAgentOrchestrationCheckpoint Checkpoint,
    DataAgentAnalysisResponse Response,
    DataAgentToolRouteContext? RouteContext = null);
```

- [ ] **Step 4: Append route fields in context provider**

In `DataAgentOrchestrationContextProvider.Build`, after checkpoint fields, add:

```csharp
if (result.RouteContext is not null)
{
    builder.AppendLine($"route_present={ToLowerBool(result.RouteContext.Present)}");
    builder.AppendLine($"route_tool={Sanitize(result.RouteContext.ToolName)}");
    builder.AppendLine($"route_allows_tool={ToLowerBool(result.RouteContext.AllowsTool)}");
    builder.AppendLine($"route_allows_query={ToLowerBool(result.RouteContext.AllowsQuery)}");
    builder.AppendLine($"route_id={Sanitize(result.RouteContext.RouteId)}");
    builder.AppendLine($"route_intent={Sanitize(result.RouteContext.Intent)}");
    builder.AppendLine($"route_reason_code={Sanitize(result.RouteContext.ReasonCode)}");
    builder.AppendLine($"route_session_id={Sanitize(result.RouteContext.RouteSessionId)}");
}
```

- [ ] **Step 5: Run context-provider tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentOrchestrationContextProviderTests -v:minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 2**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentOrchestrationModels.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentOrchestrationContextProvider.cs Tests\Alife.Test.DataAgent\DataAgentOrchestrationContextProviderTests.cs
git commit -m "Add DataAgent route evidence context"
```

---

### Task 3: Make Query-Producing Analysis Tool Calls Consume Route Context

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`

- [ ] **Step 1: Update handler tests to expect real route context**

Add this fake accessor inside `DataAgentAnalysisToolHandlerTests`:

```csharp
sealed class RecordingRouteContextAccessor(DataAgentToolRouteContext routeContext) : IDataAgentToolRouteContextAccessor
{
    public List<(string ToolName, string? SessionId)> Requests { get; } = [];

    public DataAgentToolRouteContext Get(string toolName, string? sessionId)
    {
        Requests.Add((toolName, sessionId));
        return routeContext with { ToolName = toolName };
    }
}
```

In `StartCallsOrchestratorAndPublishesOrchestratedContext`, create and pass an allowed accessor:

```csharp
RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
    true,
    "dataagent_analysis_start",
    true,
    true,
    "route-1",
    "analysis_start",
    "route_allowed",
    string.Empty));
DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);
```

Add assertions:

```csharp
Assert.That(routeAccessor.Requests, Is.EqualTo(new[] { ("dataagent_analysis_start", (string?)null) }));
Assert.That(orchestrator.StartRequests[0].RouteContext?.RouteId, Is.EqualTo("route-1"));
Assert.That(context, Does.Contain("route_reason_code=route_allowed"));
```

In `ContinueCallsOrchestratorAndPublishesOrchestratedContext`, create and pass:

```csharp
RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
    true,
    "dataagent_analysis_continue",
    true,
    true,
    "route-2",
    "analysis_continue",
    "route_allowed",
    "session-1"));
DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);
```

Add assertions:

```csharp
Assert.That(routeAccessor.Requests, Is.EqualTo(new[] { ("dataagent_analysis_continue", (string?)"session-1") }));
Assert.That(orchestrator.ContinueRequests[0].RouteContext?.RouteSessionId, Is.EqualTo("session-1"));
Assert.That(context, Does.Contain("route_session_id=session-1"));
```

Add this new test:

```csharp
[Test]
public void StartWithoutRouteContextFailsClosedAtRequestBoundary()
{
    RecordingOrchestrator orchestrator = CreateOrchestrator();
    DataAgentAnalysisToolHandler handler = new(orchestrator);

    handler.Start("xiayu", "Which documents describe DataAgent?");

    Assert.Multiple(() =>
    {
        Assert.That(orchestrator.StartRequests, Has.Count.EqualTo(1));
        Assert.That(orchestrator.StartRequests[0].RouteAllowsQuery, Is.False);
        Assert.That(orchestrator.StartRequests[0].RouteContext?.Present, Is.False);
        Assert.That(orchestrator.StartRequests[0].RouteContext?.ReasonCode, Is.EqualTo("tool_route_required"));
    });
}
```

- [ ] **Step 2: Run handler tests to verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentAnalysisToolHandlerTests -v:minimal
```

Expected: FAIL because the handler constructor does not accept a route accessor and still hard-codes route permission.

- [ ] **Step 3: Update handler implementation**

Replace the class constructor line in `DataAgentAnalysisToolHandler.cs` with:

```csharp
public sealed class DataAgentAnalysisToolHandler(
    IDataAgentAnalysisOrchestrator orchestrator,
    Action<string>? resultPublisher = null,
    IDataAgentToolRouteContextAccessor? routeContextAccessor = null)
{
    readonly IDataAgentToolRouteContextAccessor routeContextAccessor =
        routeContextAccessor ?? MissingDataAgentToolRouteContextAccessor.Instance;
```

In `Start`, replace the request build with:

```csharp
DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_start", null);
DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
    callerId,
    goalOrQuestion,
    null,
    routeContext.AllowsQuery,
    routeContext));
```

In `Continue`, replace the request build with:

```csharp
DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_continue", sessionId);
DataAgentOrchestrationResult result = orchestrator.Continue(new DataAgentOrchestrationRequest(
    "local",
    question,
    sessionId,
    routeContext.AllowsQuery,
    routeContext));
```

Leave `Summarize` and `End` calling the one-argument orchestrator methods in this task. Task 4 extends the orchestrator interface and then wires terminal route context in one compiling change.

- [ ] **Step 4: Run handler tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentAnalysisToolHandlerTests -v:minimal
```

Expected: PASS. At this point start and continue are route-derived; explicit summarize and end still use the existing one-argument orchestrator methods.

- [ ] **Step 5: Commit Task 3 after Task 4 compiles**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentAnalysisToolHandler.cs Tests\Alife.Test.DataAgent\DataAgentAnalysisToolHandlerTests.cs
git commit -m "Route DataAgent analysis handler through tool context"
```

---

### Task 4: Propagate Route Context Through Orchestrator Results And Terminal Calls

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`

- [ ] **Step 1: Write failing orchestrator propagation tests**

Add this helper to `DataAgentAnalysisOrchestratorTests`:

```csharp
static DataAgentToolRouteContext AllowedRoute(string toolName, string? sessionId = null)
{
    return new DataAgentToolRouteContext(
        true,
        toolName,
        true,
        true,
        "route-test",
        "analysis_continue",
        "route_allowed",
        sessionId ?? string.Empty);
}
```

Add this test:

```csharp
[Test]
public void StartResultPreservesRouteContext()
{
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ => AcceptedAnswer(),
        new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero));
    DataAgentToolRouteContext routeContext = AllowedRoute("dataagent_analysis_start");

    DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Which documents describe DataAgent?",
        null,
        routeContext.AllowsQuery,
        routeContext));

    Assert.That(result.RouteContext, Is.EqualTo(routeContext));
}
```

Add this test:

```csharp
[Test]
public void DeniedContinueResultPreservesRouteContextWithoutMutation()
{
    int answerCalls = 0;
    InMemoryDataAgentAnalysisSessionStore store = new();
    DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
        store,
        _ =>
        {
            answerCalls++;
            return AcceptedAnswer();
        },
        new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero));
    DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "Which documents describe DataAgent?",
        null,
        true,
        AllowedRoute("dataagent_analysis_start")));
    DataAgentToolRouteContext deniedRoute = new(
        true,
        "dataagent_analysis_continue",
        false,
        false,
        "route-denied",
        "analysis_continue",
        "tool_session_not_allowed_in_current_route",
        "other-session");

    DataAgentOrchestrationResult denied = orchestrator.Continue(new DataAgentOrchestrationRequest(
        "owner",
        "\u7ee7\u7eed",
        start.SessionId,
        deniedRoute.AllowsQuery,
        deniedRoute));

    Assert.Multiple(() =>
    {
        Assert.That(answerCalls, Is.EqualTo(1));
        Assert.That(denied.Response.Accepted, Is.False);
        Assert.That(denied.RouteContext, Is.EqualTo(deniedRoute));
        Assert.That(store.Get(start.SessionId)?.Turns, Has.Count.EqualTo(1));
    });
}
```

Update `SummarizeCallsOrchestratorAndPublishesTerminalContext` to pass a terminal route accessor:

```csharp
RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
    true,
    "dataagent_analysis_summarize",
    true,
    true,
    "route-summary",
    "analysis_summarize",
    "route_allowed",
    "session-1"));
DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);
```

Add assertions to the same test:

```csharp
Assert.That(orchestrator.SummarizeRequests[0].RouteContext?.RouteId, Is.EqualTo("route-summary"));
Assert.That(context, Does.Contain("route_tool=dataagent_analysis_summarize"));
```

Update `EndCallsOrchestratorAndPublishesTerminalContext` to pass a terminal route accessor:

```csharp
RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
    true,
    "dataagent_analysis_end",
    true,
    true,
    "route-end",
    "analysis_end",
    "route_allowed",
    "session-1"));
DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);
```

Add assertions to the same test:

```csharp
Assert.That(orchestrator.EndRequests[0].RouteContext?.RouteId, Is.EqualTo("route-end"));
Assert.That(context, Does.Contain("route_tool=dataagent_analysis_end"));
```

- [ ] **Step 2: Run orchestrator tests to verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentAnalysisOrchestratorTests -v:minimal
```

Expected: FAIL because results do not preserve route context and interface terminal methods do not accept it.

- [ ] **Step 3: Update orchestrator interface**

Modify `IDataAgentAnalysisOrchestrator.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisOrchestrator
{
    DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null);

    DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null);
}
```

Update `RecordingOrchestrator` in `DataAgentAnalysisToolHandlerTests`:

```csharp
public List<(string SessionId, DataAgentToolRouteContext? RouteContext)> SummarizeRequests { get; } = [];
public List<(string SessionId, DataAgentToolRouteContext? RouteContext)> EndRequests { get; } = [];
```

Replace its terminal methods:

```csharp
public DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null)
{
    SummarizeSessionIds.Add(sessionId);
    SummarizeRequests.Add((sessionId, routeContext));
    return results["summarize"] with { RouteContext = routeContext };
}

public DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null)
{
    EndSessionIds.Add(sessionId);
    EndRequests.Add((sessionId, routeContext));
    return results["end"] with { RouteContext = routeContext };
}
```

- [ ] **Step 4: Update orchestrator implementation**

In `Start`, pass route context into rejected and accepted results:

```csharp
if (request.RouteAllowsQuery == false)
    return BuildRejectedResult(
        string.Empty,
        DataAgentAnalysisTurnIntent.NewQuestion,
        RouteDeniedReason,
        Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, RouteDeniedReason, false),
        request.RouteContext);
```

And:

```csharp
return BuildResult(response, steps, request.RouteContext);
```

In `Continue`, pass route context through all request-based branches:

```csharp
return BuildResult(
    missingSessionResponse,
    [
        Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
        Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, missingSessionResponse.RejectedReason, false),
        Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
    ],
    request.RouteContext);
```

```csharp
if (intent == DataAgentAnalysisTurnIntent.Summarize)
    return Summarize(request.SessionId!, request.RouteContext);

if (intent == DataAgentAnalysisTurnIntent.End)
    return End(request.SessionId!, request.RouteContext);
```

```csharp
return BuildRejectedResult(
    request.SessionId!,
    intent,
    RouteDeniedReason,
    Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, RouteDeniedReason, false),
    request.RouteContext);
```

```csharp
return BuildResult(response, steps, request.RouteContext);
```

Change terminal method signatures and build calls:

```csharp
public DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null)
```

```csharp
return BuildResult(
    response,
    [
        Step(
            DataAgentOrchestrationNodeKind.Summarize,
            response.Accepted ? DataAgentOrchestrationStepStatus.Succeeded : DataAgentOrchestrationStepStatus.Rejected,
            response.Accepted ? "terminal_summary" : response.RejectedReason,
            false),
        Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
    ],
    routeContext);
```

In `DataAgentAnalysisToolHandler.Summarize`, replace the orchestrator call with:

```csharp
DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_summarize", sessionId);
DataAgentOrchestrationResult result = orchestrator.Summarize(sessionId, routeContext);
```

In `DataAgentAnalysisToolHandler.End`, replace the orchestrator call with:

```csharp
DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_end", sessionId);
DataAgentOrchestrationResult result = orchestrator.End(sessionId, routeContext);
```

```csharp
public DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null)
```

```csharp
return BuildResult(
    response,
    [
        Step(
            DataAgentOrchestrationNodeKind.End,
            response.Accepted ? DataAgentOrchestrationStepStatus.Succeeded : DataAgentOrchestrationStepStatus.Rejected,
            response.Accepted ? "terminal_end" : response.RejectedReason,
            false),
        Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
    ],
    routeContext);
```

Change helper signatures:

```csharp
DataAgentOrchestrationResult BuildRejectedResult(
    string sessionId,
    DataAgentAnalysisTurnIntent intent,
    string reason,
    DataAgentOrchestrationStep routeStep,
    DataAgentToolRouteContext? routeContext)
```

```csharp
return new DataAgentOrchestrationResult(
    sessionId,
    checkpoint.SessionStatus,
    steps,
    checkpoint,
    response,
    routeContext);
```

```csharp
DataAgentOrchestrationResult BuildResult(
    DataAgentAnalysisResponse response,
    IReadOnlyList<DataAgentOrchestrationStep> steps,
    DataAgentToolRouteContext? routeContext)
```

```csharp
return new DataAgentOrchestrationResult(
    response.SessionId,
    response.Status,
    steps.ToArray(),
    checkpoint,
    response,
    routeContext);
```

- [ ] **Step 5: Run orchestrator and handler tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentAnalysisOrchestratorTests|DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 4**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\IDataAgentAnalysisOrchestrator.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentAnalysisOrchestrator.cs Tests\Alife.Test.DataAgent\DataAgentAnalysisOrchestratorTests.cs Tests\Alife.Test.DataAgent\DataAgentAnalysisToolHandlerTests.cs
git commit -m "Propagate DataAgent route context through orchestration"
```

---

### Task 5: Wire Runtime Accessor Through Capability Registration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Write failing module wiring test**

Add this test to `DataAgentModuleServiceTests`:

```csharp
[Test]
public void AwakeWiresRuntimeToolRouteContextAccessor()
{
    string source = ReadModuleSource();

    Assert.Multiple(() =>
    {
        Assert.That(source, Does.Contain("XmlPolicyDataAgentToolRouteContextAccessor"));
        Assert.That(source, Does.Contain("functionService.ExecutionPolicy"));
        Assert.That(source, Does.Contain("new DataAgentAnalysisCapabilityProvider(analysisOrchestrator, PublishAnalysisContext, routeContextAccessor)"));
    });
}
```

- [ ] **Step 2: Run module tests to verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentModuleServiceTests -v:minimal
```

Expected: FAIL because the module does not construct or pass the route accessor.

- [ ] **Step 3: Update capability provider**

Modify `DataAgentAnalysisCapabilityProvider.cs`:

```csharp
public sealed class DataAgentAnalysisCapabilityProvider(
    IDataAgentAnalysisOrchestrator orchestrator,
    Action<string>? resultPublisher = null,
    IDataAgentToolRouteContextAccessor? routeContextAccessor = null) : IDataAgentCapabilityProvider
{
    public string Name => nameof(DataAgentAnalysisCapabilityProvider);

    public IReadOnlyList<ToolCapabilityManifest> ToolManifests => DataAgentToolCapabilityManifests.Analysis;

    public void Register(IDataAgentCapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterXmlHandlerWithoutStaticDocument(new XmlHandler(new DataAgentAnalysisToolHandler(
            orchestrator,
            resultPublisher,
            routeContextAccessor)));
    }
}
```

- [ ] **Step 4: Update module wiring**

In `DataAgentModuleService.AwakeAsync`, after creating `analysisOrchestrator`, add:

```csharp
IDataAgentToolRouteContextAccessor routeContextAccessor =
    new XmlPolicyDataAgentToolRouteContextAccessor(functionService.ExecutionPolicy);
```

Replace analysis capability registration with:

```csharp
capabilityRegistry.Add(new DataAgentAnalysisCapabilityProvider(analysisOrchestrator, PublishAnalysisContext, routeContextAccessor));
```

- [ ] **Step 5: Run module tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentModuleServiceTests -v:minimal
```

Expected: PASS.

- [ ] **Step 6: Commit Task 5**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentAnalysisCapabilityProvider.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs Tests\Alife.Test.DataAgent\DataAgentModuleServiceTests.cs
git commit -m "Wire runtime Tool Broker route context into DataAgent"
```

---

### Task 6: Add Required Readiness Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Test: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Update readiness tests to expect V2.3 gates**

In `CoreReadinessChecksAllPass`, change:

```csharp
Assert.That(checks, Has.Count.EqualTo(49));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(55));
```

Add these assertions:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("AnalysisHandlerConsumesToolRouteContext"));
Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestrationRequestUsesRuntimeRouteDecision"));
Assert.That(checks.Select(check => check.Name), Does.Contain("RouteMissingRequestFailsClosed"));
Assert.That(checks.Select(check => check.Name), Does.Contain("RouteEvidenceContextPresent"));
Assert.That(checks.Select(check => check.Name), Does.Contain("RouteSessionScopePreserved"));
Assert.That(checks.Select(check => check.Name), Does.Contain("TerminalRouteDoesNotQuery"));
```

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, change:

```csharp
"  Summary: 63 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 69 required passed, 0 required missing"
```

Add output assertions:

```csharp
Assert.That(result.StandardOutput, Does.Contain("AnalysisHandlerConsumesToolRouteContext"));
Assert.That(result.StandardOutput, Does.Contain("RouteEvidenceContextPresent"));
Assert.That(result.StandardOutput, Does.Contain("TerminalRouteDoesNotQuery"));
```

- [ ] **Step 2: Run readiness tests to verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentReadinessTests -v:minimal
```

Expected: FAIL because readiness code and script do not yet emit the six V2.3 gates.

- [ ] **Step 3: Add runtime readiness checks**

In `DataAgentReadiness.cs`, after the existing `OrchestratorRuntimeRouteDeniedFailClosed` check, add:

```csharp
DataAgentToolRouteContext allowedStartRoute = new(
    true,
    "dataagent_analysis_start",
    true,
    true,
    "readiness-route-start",
    "analysis_start",
    "route_allowed",
    string.Empty);
DataAgentToolRouteContext deniedMissingRoute = DataAgentToolRouteContext.Missing("dataagent_analysis_start");
RecordingRouteContextAccessor readinessAllowedRouteAccessor = new(allowedStartRoute);
RecordingRouteContextAccessor readinessMissingRouteAccessor = new(deniedMissingRoute);
RecordingOrchestrator routeRecordingOrchestrator = new(orchestrationStart);
DataAgentAnalysisToolHandler routedHandler = new(routeRecordingOrchestrator, null, readinessAllowedRouteAccessor);
routedHandler.Start("readiness", "Which documents describe DataAgent routes?");
DataAgentAnalysisToolHandler missingRouteHandler = new(routeRecordingOrchestrator, null, readinessMissingRouteAccessor);
missingRouteHandler.Start("readiness", "Which documents describe DataAgent routes?");
string routeEvidenceContext = DataAgentOrchestrationContextProvider.Build(orchestrationStart with { RouteContext = allowedStartRoute });

checks.Add(routeRecordingOrchestrator.StartRequests.Any(request => request.RouteContext?.ReasonCode == "route_allowed")
    ? Pass("AnalysisHandlerConsumesToolRouteContext", "handler requested and forwarded Tool Broker route context")
    : Fail("AnalysisHandlerConsumesToolRouteContext", "handler did not forward route context"));

checks.Add(routeRecordingOrchestrator.StartRequests.First().RouteAllowsQuery &&
           routeRecordingOrchestrator.StartRequests.First().RouteContext == allowedStartRoute
    ? Pass("OrchestrationRequestUsesRuntimeRouteDecision", "RouteAllowsQuery came from route context")
    : Fail("OrchestrationRequestUsesRuntimeRouteDecision", "RouteAllowsQuery was not route-derived"));

checks.Add(routeRecordingOrchestrator.StartRequests.Last().RouteAllowsQuery == false &&
           routeRecordingOrchestrator.StartRequests.Last().RouteContext?.ReasonCode == "tool_route_required"
    ? Pass("RouteMissingRequestFailsClosed", "missing route created fail-closed request")
    : Fail("RouteMissingRequestFailsClosed", "missing route did not fail closed"));

checks.Add(routeEvidenceContext.Contains("route_present=true", StringComparison.Ordinal) &&
           routeEvidenceContext.Contains("route_allows_query=true", StringComparison.Ordinal) &&
           routeEvidenceContext.Contains("route_reason_code=route_allowed", StringComparison.Ordinal)
    ? Pass("RouteEvidenceContextPresent", "route evidence fields emitted")
    : Fail("RouteEvidenceContextPresent", routeEvidenceContext));

checks.Add(new XmlPolicyDataAgentToolRouteContextAccessor(new Alife.Function.Interpreter.XmlFunctionExecutionPolicy
           {
               CurrentRoute = new ToolRouteDecision(
                   "readiness-route-session",
                   ToolCapabilityDomain.DataAgent,
                   "analysis_continue",
                   ["dataagent_analysis_continue"],
                   [],
                   new ToolRouteState("readiness-session", "Active", true, true, true),
                   "route_allowed",
                   "route_allowed")
           }).Get("dataagent_analysis_continue", "other-session").ReasonCode == "tool_session_not_allowed_in_current_route"
    ? Pass("RouteSessionScopePreserved", "session scoped route mismatch fails closed")
    : Fail("RouteSessionScopePreserved", "session scoped mismatch was accepted"));

checks.Add(orchestrationSummary.Steps.Any(step => step.ExecutedSql) == false &&
           orchestrationSummary.Response.Answer is null &&
           orchestrationAnswerCalls == 1
    ? Pass("TerminalRouteDoesNotQuery", "terminal route path avoided query execution")
    : Fail("TerminalRouteDoesNotQuery", $"answerCalls={orchestrationAnswerCalls}"));
```

Add these helper classes inside `DataAgentReadiness` after `FixedLlmClient`:

```csharp
sealed class RecordingRouteContextAccessor(DataAgentToolRouteContext routeContext) : IDataAgentToolRouteContextAccessor
{
    public DataAgentToolRouteContext Get(string toolName, string? sessionId)
    {
        return routeContext with { ToolName = toolName };
    }
}

sealed class RecordingOrchestrator(DataAgentOrchestrationResult result) : IDataAgentAnalysisOrchestrator
{
    public List<DataAgentOrchestrationRequest> StartRequests { get; } = [];

    public DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request)
    {
        StartRequests.Add(request);
        return result with { RouteContext = request.RouteContext };
    }

    public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
    {
        return result with { RouteContext = request.RouteContext };
    }

    public DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null)
    {
        return result with { RouteContext = routeContext };
    }

    public DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null)
    {
        return result with { RouteContext = routeContext };
    }
}
```

- [ ] **Step 4: Add PowerShell readiness gates**

In `tools/check-dataagent-readiness.ps1`, after `OrchestratorRuntimeRouteDeniedFailClosed`, add:

```powershell
    New-Check -Group "Analysis" -Name "AnalysisHandlerConsumesToolRouteContext" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("IDataAgentToolRouteContextAccessor", "routeContextAccessor.Get", "RouteAllowsQuery")) -Detail "analysis handler consumes Tool Broker route context"
    New-Check -Group "Analysis" -Name "OrchestrationRequestUsesRuntimeRouteDecision" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("routeContext.AllowsQuery", "DataAgentOrchestrationRequest")) -and (Test-FileOmitsMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("RouteAllowsQuery: true"))) -Detail "orchestration request route permission is runtime-derived"
    New-Check -Group "Analysis" -Name "RouteMissingRequestFailsClosed" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs" @("MissingRouteReasonCode", "tool_route_required", "MissingDataAgentToolRouteContextAccessor")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs" @("StartWithoutRouteContextFailsClosedAtRequestBoundary", "RouteAllowsQuery, Is.False"))) -Detail "missing route creates fail-closed DataAgent request"
    New-Check -Group "Analysis" -Name "RouteEvidenceContextPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs" @("route_present", "route_allows_query", "route_reason_code", "route_session_id")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentOrchestrationContextProviderTests.cs" @("BuildAppendsSanitizedRouteEvidenceWhenPresent"))) -Detail "orchestration context emits sanitized route evidence"
    New-Check -Group "Analysis" -Name "RouteSessionScopePreserved" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs" @("tool_session_not_allowed_in_current_route", "IsSessionScopedDataAgentTool")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentToolRouteContextAccessorTests.cs" @("XmlPolicyAccessorRejectsSessionScopedMismatchForDefenseInDepth"))) -Detail "session scoped route context remains fail-closed"
    New-Check -Group "Analysis" -Name "TerminalRouteDoesNotQuery" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("Summarize(string sessionId, DataAgentToolRouteContext? routeContext", "End(string sessionId, DataAgentToolRouteContext? routeContext")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("ContinueSummarizeDoesNotRequireRouteAndDoesNotExecuteSql", "ContinueEndDoesNotRequireRouteAndProducesTerminalCheckpoint"))) -Detail "terminal route context does not force query execution"
```

- [ ] **Step 5: Run readiness tests and script**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentReadinessTests -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected: both PASS with `69 required passed, 0 required missing`.

- [ ] **Step 6: Commit Task 6**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs
git commit -m "Add DataAgent V2.3 route readiness gates"
```

---

### Task 7: Run Required Verification Gates

**Files:**
- No source edits unless verification exposes a real failure.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: PASS with all DataAgent tests passing.

- [ ] **Step 2: Run DataAgent readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 69 required passed, 0 required missing
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

Expected: PASS with `Failed: 0`.

- [ ] **Step 5: Run diff hygiene**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` exits 0. `git status --short` shows only intended source, test, readiness, and docs changes before final commit.

- [ ] **Step 6: Commit any verification fixes**

If verification required a code fix, commit it:

```powershell
git add sources Tests tools
git commit -m "Stabilize DataAgent V2.3 route integration"
```

If no verification fix was needed, do not create an empty commit.

---

### Task 8: Prepare Branch For Integration

**Files:**
- No source edits expected.

- [ ] **Step 1: Review commit history**

Run:

```powershell
git log --oneline --decorate -8
```

Expected: the branch contains the design commit, implementation commits, and no unrelated `D:\FOXD` commits.

- [ ] **Step 2: Review final changed files**

Run:

```powershell
git diff --stat master...HEAD
git diff --name-only master...HEAD
```

Expected changed files:

```text
docs/superpowers/specs/2026-06-30-dataagent-v2.3-route-orchestrator-design.md
docs/superpowers/plans/2026-06-30-dataagent-v2.3-route-orchestrator.md
sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs
sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisOrchestrator.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs
tools/check-dataagent-readiness.ps1
Tests/Alife.Test.DataAgent/DataAgentToolRouteContextAccessorTests.cs
Tests/Alife.Test.DataAgent/DataAgentOrchestrationContextProviderTests.cs
Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs
Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs
Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs
Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
```

- [ ] **Step 3: Stop before merge workflow**

Use `superpowers:finishing-a-development-branch` before opening or merging the PR.

Do not use `D:\FOXD`. Push only to `git@github.com:hushu1232/Alife-byastralfox.git` when the user asks for upload or merge.

---

## Self-Review Checklist

- Spec coverage: Tasks 1-6 cover route context accessor, request usage, context evidence, session-scope defense, terminal no-query behavior, runtime wiring, and required readiness gates.
- Type consistency: `DataAgentToolRouteContext`, `IDataAgentToolRouteContextAccessor`, `XmlPolicyDataAgentToolRouteContextAccessor`, `RouteContext`, and `RouteAllowsQuery` names are consistent across tests and implementation steps.
- Verification coverage: Task 7 runs focused DataAgent tests, readiness script, QChat engineering map, full solution, and diff hygiene.
- Scope control: The plan does not introduce LangGraph, PostgreSQL migration, UI streaming, or state-machine migration.
