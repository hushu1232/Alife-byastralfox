# DataAgent V3.8 End-to-End Chain Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic offline tests and readiness markers proving the full DataAgent chain from Tool Broker route state through analysis execution and owner diagnostics.

**Architecture:** Keep production behavior unchanged unless a test exposes a real contract gap. Add one focused V3.8 NUnit contract test file, add a test-only QChat project reference so owner diagnostics command formatting can be exercised, and add one dynamic/static readiness marker. The tests use fake or in-memory collaborators and do not start Python, sidecars, QQ, network, PostgreSQL, browser automation, or model calls.

**Tech Stack:** .NET 9, C#, NUnit, existing DataAgent/QChat/FunctionCaller modules, PowerShell readiness script.

---

## File Structure

- Modify: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
  - Add a test-only project reference to `Alife.Function.QChat` so `QChatDiagnosticsService` can be exercised directly.
- Create: `Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs`
  - Owns all V3.8 chain contract tests.
  - Covers Tool Broker routing, XML execution policy session checks, successful analysis execution, route-denied no-execute behavior, active session propagation, diagnostics closure, and QChat owner diagnostics formatting.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Increase dynamic readiness count from `76` to `77`.
  - Increase static readiness expected summary from `91` to `92`.
  - Assert the new `DataAgentEndToEndChainContractPresent` dynamic and static markers.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add the dynamic `DataAgentEndToEndChainContractPresent` readiness check.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Add the static `DataAgentEndToEndChainContractPresent` marker.
  - Increase `$expectedRequired` from `91` to `92`.

Do not modify:

- `sources/Alife.Function/Alife.Function.QChat/**`
- `tools/dataagent-graph-sidecar/**`
- `tools/run-dataagent-graph-sidecar-smoke.ps1`
- Python runtime files
- upload scripts

---

### Task 1: Add The V3.8 Test Harness And Route Boundary Tests

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
- Create: `Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs`

- [ ] **Step 1: Add the test-only QChat project reference**

Modify `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj` so the final project reference item group becomes:

```xml
    <ItemGroup>
        <ProjectReference Include="..\..\Sources\Alife.Function\Alife.Function.DataAgent\Alife.Function.DataAgent.csproj" />
        <ProjectReference Include="..\..\Sources\Alife.Function\Alife.Function.QChat\Alife.Function.QChat.csproj" />
    </ItemGroup>
```

- [ ] **Step 2: Create the V3.8 test file with route and execution-policy tests**

Create `Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs` with this initial content:

```csharp
using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.QChat;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentEndToEndChainContractTests
{
    const string SidecarAuthorityMarker = "sidecar_authority=false";
    const string DefaultTestsLiveRuntimeMarker = "default_tests_live_runtime=false";

    static readonly string[] AllDataAgentTools =
    [
        "dataagent_query",
        "dataagent_analysis_start",
        "dataagent_analysis_continue",
        "dataagent_analysis_summarize",
        "dataagent_analysis_end"
    ];

    [Test]
    public void ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();

        ToolRouteDecision start = router.Route(
            "DataAgent analyze project readiness",
            RouteState(isOwner: true, isPrivate: true, trusted: true));
        ToolRouteDecision active = router.Route(
            "continue DataAgent analysis",
            RouteState(isOwner: true, isPrivate: true, trusted: true, sessionId: "session-a", status: "Active"));
        ToolRouteDecision nonOwner = router.Route(
            "DataAgent analyze project readiness",
            RouteState(isOwner: false, isPrivate: true, trusted: true));
        ToolRouteDecision ownerGroup = router.Route(
            "DataAgent analyze project readiness",
            RouteState(isOwner: true, isPrivate: false, trusted: true));
        ToolRouteDecision untrusted = router.Route(
            "DataAgent analyze project readiness",
            RouteState(isOwner: true, isPrivate: true, trusted: false));
        ToolRouteDecision ordinary = router.Route(
            "continue talking about this idea",
            RouteState(isOwner: true, isPrivate: true, trusted: true, sessionId: "session-a", status: "Active"));

        Assert.Multiple(() =>
        {
            Assert.That(start.AllowedTools, Is.EqualTo(new[] { "dataagent_query", "dataagent_analysis_start" }));
            Assert.That(start.Intent, Is.EqualTo("analysis_start"));
            Assert.That(start.ReasonCode, Is.EqualTo("route_allowed"));

            Assert.That(active.AllowedTools, Is.EqualTo(new[] { "dataagent_query", "dataagent_analysis_continue", "dataagent_analysis_summarize", "dataagent_analysis_end" }));
            Assert.That(active.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(active.State.ActiveDataAgentSessionId, Is.EqualTo("session-a"));

            AssertDataAgentDenied(nonOwner, "owner_private_required");
            AssertDataAgentDenied(ownerGroup, "owner_private_required");
            AssertDataAgentDenied(untrusted, "trusted_runtime_required");
            Assert.That(ordinary.Domain, Is.EqualTo(ToolCapabilityDomain.Chat));
            Assert.That(ordinary.AllowedTools, Is.Empty);
            AssertDataAgentDenied(ordinary, "tool_not_allowed_in_current_route");
        });
    }

    [Test]
    public void XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        XmlFunctionExecutionPolicy policy = new();
        policy.SetGovernedToolNames(router.ToolNames);
        XmlFunction startTool = Function("dataagent_analysis_start");
        XmlFunction continueTool = Function("dataagent_analysis_continue");

        XmlFunctionExecutionDecision missingRoute = policy.TryConsume(startTool);

        ToolRouteDecision startRoute = router.Route(
            "DataAgent analyze project readiness",
            RouteState(isOwner: true, isPrivate: true, trusted: true));
        policy.CurrentRoute = startRoute;
        XmlFunctionExecutionDecision startAllowed = policy.TryConsume(startTool);
        XmlFunctionExecutionDecision continueDeniedOnStartRoute = policy.TryConsume(
            continueTool,
            ContextWithSession("session-a"));

        ToolRouteDecision activeRoute = router.Route(
            "continue DataAgent analysis",
            RouteState(isOwner: true, isPrivate: true, trusted: true, sessionId: "session-a", status: "Active"));
        policy.CurrentRoute = activeRoute;
        XmlFunctionExecutionDecision missingSession = policy.TryConsume(continueTool, ContextWithSession(null));
        XmlFunctionExecutionDecision wrongSession = policy.TryConsume(continueTool, ContextWithSession("session-b"));
        XmlFunctionExecutionDecision matchingSession = policy.TryConsume(continueTool, ContextWithSession("session-a"));

        Assert.Multiple(() =>
        {
            Assert.That(missingRoute.IsAllowed, Is.False);
            Assert.That(missingRoute.Reason, Does.Contain("tool_route_required"));
            Assert.That(startAllowed.IsAllowed, Is.True);
            Assert.That(continueDeniedOnStartRoute.IsAllowed, Is.False);
            Assert.That(continueDeniedOnStartRoute.Reason, Does.Contain("tool_not_allowed_in_current_route"));
            Assert.That(missingSession.IsAllowed, Is.False);
            Assert.That(missingSession.Reason, Does.Contain("tool_session_not_allowed_in_current_route"));
            Assert.That(wrongSession.IsAllowed, Is.False);
            Assert.That(wrongSession.Reason, Does.Contain("tool_session_not_allowed_in_current_route"));
            Assert.That(matchingSession.IsAllowed, Is.True);
        });
    }

    [Test]
    public void OfflineBoundaryMarkersLockNoLiveRuntimeAndNoSidecarAuthority()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "Tests",
            "Alife.Test.DataAgent",
            "DataAgentEndToEndChainContractTests.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(SidecarAuthorityMarker, Is.EqualTo("sidecar_authority=false"));
            Assert.That(DefaultTestsLiveRuntimeMarker, Is.EqualTo("default_tests_live_runtime=false"));
            Assert.That(source, Does.Contain(SidecarAuthorityMarker));
            Assert.That(source, Does.Contain(DefaultTestsLiveRuntimeMarker));
            Assert.That(source, Does.Not.Contain("Invoke-WebRequest"));
            Assert.That(source, Does.Not.Contain("Start-Process"));
            Assert.That(source, Does.Not.Contain("uvicorn"));
            Assert.That(source, Does.Not.Contain("127.0.0.1:8765"));
            Assert.That(source, Does.Not.Contain("EventSource"));
        });
    }

    static ToolRouteState RouteState(
        bool isOwner,
        bool isPrivate,
        bool trusted,
        string sessionId = "",
        string status = "") =>
        new(sessionId, status, isOwner, isPrivate, trusted);

    static void AssertDataAgentDenied(ToolRouteDecision decision, string reasonCode)
    {
        Assert.That(decision.ReasonCode, Is.EqualTo(reasonCode));

        foreach (string toolName in AllDataAgentTools)
        {
            ToolRouteDeniedTool denied = decision.DeniedTools.Single(tool =>
                string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase));
            Assert.That(denied.Reason, Is.EqualTo(reasonCode), toolName);
        }
    }

    static XmlFunction Function(string name) => new()
    {
        Name = name,
        Mode = FunctionMode.OneShot,
        Invoker = (_, _) => Task.CompletedTask
    };

    static XmlContext ContextWithSession(string? sessionId)
    {
        Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
        if (sessionId is not null)
            parameters["sessionid"] = sessionId;

        return new XmlContext
        {
            CallMode = CallMode.OneShot,
            Parameters = parameters
        };
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
```

- [ ] **Step 3: Run the new focused route tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEndToEndChainContractTests.ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface|FullyQualifiedName~DataAgentEndToEndChainContractTests.XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools|FullyQualifiedName~DataAgentEndToEndChainContractTests.OfflineBoundaryMarkersLockNoLiveRuntimeAndNoSidecarAuthority" -v:minimal
```

Expected: compile and test execution, with exact denied-route reason codes asserted for owner/private, trusted-runtime, and ordinary-chat denials.

- [ ] **Step 4: Commit the route boundary test harness**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj Tests\Alife.Test.DataAgent\DataAgentEndToEndChainContractTests.cs
git commit -m "Add DataAgent V3.8 route boundary contract tests"
```

---

### Task 2: Add End-To-End Analysis, Session, And Diagnostics Contract Tests

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs`

- [ ] **Step 1: Add the accepted analysis chain test**

Append this test method inside `DataAgentEndToEndChainContractTests`:

```csharp
[Test]
public void AcceptedAnalysisPublishesSessionStateAndAllDiagnostics()
{
    DateTimeOffset now = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    RecordingDataAgentStore store = new();
    FixedPlanner planner = new(new DataAgentQueryPlan(
        "document_index",
        "find_dataagent_documents",
        ["path", "title"],
        [new DataAgentFilter("tags", "contains", "dataagent")],
        [],
        20));
    DataAgentProgressRecorder progressRecorder = new();
    List<string> progressDiagnostics = [];
    DataAgentProgressDiagnosticsPublisher progressSink = new(
        progressRecorder,
        progressDiagnostics.Add,
        () => now);
    InMemoryDataAgentAnalysisSessionStore sessionStore = new();
    DataAgentService service = new(store, planner);
    DataAgentAnalysisService analysisService = new(service, sessionStore, progressSink, () => now);
    DataAgentAnalysisOrchestrator orchestrator = new(analysisService, sessionStore, progressSink: progressSink, progressClock: () => now);
    List<string> publishedContexts = [];
    List<string> evidenceDiagnostics = [];
    List<string> traceDiagnostics = [];
    List<string> graphDiagnostics = [];
    DataAgentAnalysisToolHandler handler = new(
        orchestrator,
        publishedContexts.Add,
        new FixedRouteContextAccessor(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "tool-capability-router-v0",
            "analysis_start",
            "route_allowed",
            string.Empty)),
        evidenceDiagnostics.Add,
        traceDiagnostics.Add,
        new DataAgentTraceRecorder(),
        () => now,
        graphDiagnostics.Add,
        new DataAgentGraphHandshakeCoordinator(DataAgentGraphHandshakeOptions.Disabled));

    string context = handler.Start("owner", "DataAgent analyze project readiness");
    string sessionId = ReadContextValue(context, "session_id=");
    XmlFunctionCallerShim routeState = new();
    routeState.UpdateDataAgentAnalysisRouteSessionFromContext(context);
    ToolRouteState activeState = routeState.CreateToolRouteState(isOwner: true, isPrivateChat: true);
    QChatDiagnosticsRuntimeState diagnosticsState = new(
        RecentDataAgentEvidence: evidenceDiagnostics.Single(),
        RecentDataAgentTrace: traceDiagnostics.Single(),
        RecentDataAgentProgress: progressDiagnostics.Last(),
        RecentDataAgentGraph: graphDiagnostics.Single());
    QChatAgentRoute route = OwnerPrivateRoute();
    QChatAgentProfile profile = OwnerProfile();

    QChatDiagnosticsResult evidence = QChatDiagnosticsService.TryHandle("/dataagent diag evidence", route, profile, diagnosticsState);
    QChatDiagnosticsResult trace = QChatDiagnosticsService.TryHandle("/dataagent diag trace", route, profile, diagnosticsState);
    QChatDiagnosticsResult progress = QChatDiagnosticsService.TryHandle("/dataagent diag progress", route, profile, diagnosticsState);
    QChatDiagnosticsResult graph = QChatDiagnosticsService.TryHandle("/dataagent diag graph", route, profile, diagnosticsState);

    Assert.Multiple(() =>
    {
        Assert.That(store.QueryCount, Is.EqualTo(1));
        Assert.That(store.AcceptedAudit, Has.Count.EqualTo(1));
        Assert.That(store.RejectedAudit, Is.Empty);
        Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
        Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Succeeded>SchemaContext:Succeeded>Plan:Succeeded>Validate:Succeeded>Execute:Succeeded>Explain:Succeeded>Checkpoint:Succeeded"));
        Assert.That(context, Does.Contain("[data_agent_context]"));
        Assert.That(context, Does.Contain("sql_status=validated"));
        Assert.That(sessionId, Is.Not.Empty);
        Assert.That(publishedContexts, Is.EqualTo(new[] { context }));
        Assert.That(activeState.ActiveDataAgentSessionId, Is.EqualTo(sessionId));
        Assert.That(activeState.HasActiveDataAgentSession, Is.True);
        Assert.That(evidenceDiagnostics.Single(), Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(evidenceDiagnostics.Single(), Does.Contain("executed_sql=true"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("DataAgent trace diagnostics"));
        Assert.That(progressDiagnostics.Last(), Does.Contain("DataAgent progress diagnostics"));
        Assert.That(progressDiagnostics.Last(), Does.Contain("sql=redacted"));
        Assert.That(graphDiagnostics.Single(), Does.Contain("DataQueryGraph"));
        Assert.That(graphDiagnostics.Single(), Does.Contain("graph_sidecar"));
        Assert.That(evidence.Handled, Is.True);
        Assert.That(evidence.Text, Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(trace.Handled, Is.True);
        Assert.That(trace.Text, Does.Contain("DataAgent trace diagnostics"));
        Assert.That(progress.Handled, Is.True);
        Assert.That(progress.Text, Does.Contain("DataAgent progress diagnostics"));
        Assert.That(graph.Handled, Is.True);
        Assert.That(graph.Text, Does.Contain("graph_sidecar"));
        Assert.That(evidence.Text + trace.Text + progress.Text + graph.Text, Does.Not.Contain("sql.execute"));
        Assert.That(evidence.Text + trace.Text + progress.Text + graph.Text, Does.Not.Contain("RequestsVisibleText=True"));
    });
}
```

- [ ] **Step 2: Add the route-denied and terminal no-execute tests**

Append these test methods inside `DataAgentEndToEndChainContractTests`:

```csharp
[Test]
public void RouteDeniedAnalysisDoesNotExecuteSql()
{
    DateTimeOffset now = new(2026, 7, 8, 12, 5, 0, TimeSpan.Zero);
    RecordingDataAgentStore store = new();
    DataAgentService service = new(store, new FixedPlanner(DocumentPlan()));
    InMemoryDataAgentAnalysisSessionStore sessionStore = new();
    DataAgentProgressRecorder progressRecorder = new();
    DataAgentProgressDiagnosticsPublisher progressSink = new(progressRecorder, null, () => now);
    DataAgentAnalysisService analysisService = new(service, sessionStore, progressSink, () => now);
    DataAgentAnalysisOrchestrator orchestrator = new(analysisService, sessionStore, progressSink: progressSink, progressClock: () => now);

    DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "DataAgent analyze project readiness",
        null,
        RouteAllowsQuery: false,
        new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_start",
            false,
            false,
            "tool-capability-router-v0",
            "analysis_start",
            "owner_private_required",
            string.Empty)));

    Assert.Multiple(() =>
    {
        Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
        {
            DataAgentOrchestrationNodeKind.RouteGate,
            DataAgentOrchestrationNodeKind.Reject,
            DataAgentOrchestrationNodeKind.Checkpoint
        }));
        Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
        Assert.That(result.Steps.Any(step => step.ExecutedSql), Is.False);
        Assert.That(result.Response.Accepted, Is.False);
        Assert.That(result.Response.RejectedReason, Is.EqualTo("owner_private_required"));
        Assert.That(store.QueryCount, Is.EqualTo(0));
        Assert.That(store.AcceptedAudit, Is.Empty);
        Assert.That(store.RejectedAudit, Is.Empty);
    });
}

[Test]
public void TerminalAnalysisActionsDoNotExecuteSqlAndRemainSessionScoped()
{
    DateTimeOffset now = new(2026, 7, 8, 12, 10, 0, TimeSpan.Zero);
    RecordingDataAgentStore store = new();
    DataAgentService service = new(store, new FixedPlanner(DocumentPlan()));
    InMemoryDataAgentAnalysisSessionStore sessionStore = new();
    DataAgentAnalysisService analysisService = new(service, sessionStore, clock: () => now);
    DataAgentAnalysisOrchestrator orchestrator = new(analysisService, sessionStore, progressClock: () => now);
    DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
        "owner",
        "DataAgent analyze project readiness",
        null,
        RouteAllowsQuery: true,
        AllowedContext("dataagent_analysis_start", string.Empty)));
    int queryCountAfterStart = store.QueryCount;

    DataAgentOrchestrationResult summary = orchestrator.Summarize(
        start.SessionId,
        AllowedContext("dataagent_analysis_summarize", start.SessionId));
    DataAgentOrchestrationResult end = orchestrator.End(
        start.SessionId,
        AllowedContext("dataagent_analysis_end", start.SessionId));
    DataAgentOrchestrationResult deniedTerminal = orchestrator.Summarize(
        start.SessionId,
        DataAgentToolRouteContext.Missing("dataagent_analysis_summarize"));

    Assert.Multiple(() =>
    {
        Assert.That(queryCountAfterStart, Is.EqualTo(1));
        Assert.That(store.QueryCount, Is.EqualTo(1));
        Assert.That(summary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
        Assert.That(summary.Steps.Any(step => step.ExecutedSql), Is.False);
        Assert.That(end.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
        Assert.That(end.Steps.Any(step => step.ExecutedSql), Is.False);
        Assert.That(deniedTerminal.Steps.First().Status, Is.EqualTo(DataAgentOrchestrationStepStatus.Rejected));
        Assert.That(deniedTerminal.Response.RejectedReason, Is.EqualTo("tool_route_required"));
    });
}
```

- [ ] **Step 3: Add helper types and methods required by the new tests**

Append these helpers at the end of `DataAgentEndToEndChainContractTests`, before the closing brace of the class:

```csharp
static DataAgentQueryPlan DocumentPlan() => new(
    "document_index",
    "find_dataagent_documents",
    ["path", "title"],
    [new DataAgentFilter("tags", "contains", "dataagent")],
    [],
    20);

static DataAgentToolRouteContext AllowedContext(string toolName, string sessionId) => new(
    true,
    toolName,
    true,
    true,
    "tool-capability-router-v0",
    toolName.Contains("start", StringComparison.OrdinalIgnoreCase) ? "analysis_start" : "analysis_continue",
    "route_allowed",
    sessionId);

static string ReadContextValue(string context, string prefix)
{
    foreach (string line in context.Split('\n'))
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            return trimmed[prefix.Length..].Trim();
    }

    return string.Empty;
}

static QChatAgentRoute OwnerPrivateRoute() => new(
    "xiayu",
    10001,
    QChatConversationKind.Private,
    20002,
    20002,
    true,
    "qq:xiayu:10001:private:20002");

static QChatAgentProfile OwnerProfile() => new(
    "xiayu",
    "XiaYu",
    "persona.md",
    "owner",
    "test-model",
    "owner",
    [],
    new QChatAgentCapabilities(
        AllowComputerFileTools: true,
        AllowProjectModification: true,
        AllowRecall: true,
        AllowPoke: true));

sealed class FixedPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
{
    public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
    {
        return new DataAgentQueryPlanEnvelope(
            plan,
            new DataAgentPlannerExplanation(
                nameof(FixedPlanner),
                plan.Intent,
                plan.Dataset,
                "high",
                ["v3_8_chain_contract"],
                "V3.8 chain contract fixed planner"));
    }
}

sealed class FixedRouteContextAccessor(DataAgentToolRouteContext context) : IDataAgentToolRouteContextAccessor
{
    public DataAgentToolRouteContext Get(string toolName, string? sessionId)
    {
        return context with
        {
            ToolName = toolName,
            RouteSessionId = sessionId ?? context.RouteSessionId
        };
    }
}

sealed class XmlFunctionCallerShim
{
    string activeDataAgentSessionId = string.Empty;
    string activeDataAgentSessionStatus = string.Empty;

    public ToolRouteState CreateToolRouteState(bool isOwner, bool isPrivateChat, bool isTrustedRuntime = true)
    {
        return new ToolRouteState(
            activeDataAgentSessionId,
            activeDataAgentSessionStatus,
            isOwner,
            isPrivateChat,
            isTrustedRuntime);
    }

    public void UpdateDataAgentAnalysisRouteSessionFromContext(string context)
    {
        string sessionId = ReadContextValue(context, "session_id=");
        string status = ReadContextValue(context, "status=");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(status))
            return;

        activeDataAgentSessionStatus = status;
        activeDataAgentSessionId = ToolRouteState.IsLiveDataAgentAnalysisStatus(status)
            ? sessionId
            : string.Empty;
    }
}

sealed class RecordingDataAgentStore : IDataAgentStore
{
    readonly List<DataAgentAuditRecord> queryAudit = [];
    readonly List<DataAgentToolBrokerAuditRecord> toolBrokerAudit = [];

    public string ProviderName => "recording";
    public int QueryCount { get; private set; }
    public List<DataAgentAcceptedAuditInput> AcceptedAudit { get; } = [];
    public List<DataAgentRejectedAuditInput> RejectedAudit { get; } = [];

    public void Initialize() {}

    public void ImportFixtures() {}

    public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
    {
        QueryCount++;
        return new DataAgentQueryResult([
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["path"] = "docs/dataagent/dataagent-v3.8.md",
                ["title"] = "DataAgent V3.8 chain contract"
            }
        ]);
    }

    public void RecordAccepted(DataAgentAcceptedAuditInput input)
    {
        AcceptedAudit.Add(input);
        queryAudit.Add(new DataAgentAuditRecord(
            input.Question,
            input.Dataset,
            input.QueryPlanJson,
            input.GeneratedSql,
            true,
            string.Empty,
            input.RowCount,
            input.Elapsed,
            DateTimeOffset.UtcNow));
    }

    public void RecordRejected(DataAgentRejectedAuditInput input)
    {
        RejectedAudit.Add(input);
        queryAudit.Add(new DataAgentAuditRecord(
            input.Question,
            input.Dataset,
            input.QueryPlanJson,
            input.GeneratedSql,
            false,
            input.RejectedReason,
            0,
            input.Elapsed,
            DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit() => queryAudit;

    public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
    {
        toolBrokerAudit.Add(record);
    }

    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit() => toolBrokerAudit;
}
```

- [ ] **Step 4: Run the V3.8 chain tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEndToEndChainContractTests" -v:minimal
```

Expected: all `DataAgentEndToEndChainContractTests` pass. If a diagnostic assertion fails because the formatter uses a slightly different stable header, update only the expected literal to match the existing formatter output and keep the same authority assertions.

- [ ] **Step 5: Commit the end-to-end chain tests**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentEndToEndChainContractTests.cs
git commit -m "Prove DataAgent V3.8 end-to-end chain contract"
```

---

### Task 3: Add Dynamic Readiness Coverage

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`

- [ ] **Step 1: Update failing dynamic readiness assertions**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, change:

```csharp
Assert.That(checks, Has.Count.EqualTo(76));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(77));
```

Then add these assertions immediately after the `GraphHandshakeDevSidecarObservabilityContractPresent` assertions:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEndToEndChainContractPresent"));
DataAgentReadinessCheck endToEndChainCheck = checks.Single(check => check.Name == "DataAgentEndToEndChainContractPresent");
Assert.That(endToEndChainCheck.Detail, Does.Contain("route_boundary=true"));
Assert.That(endToEndChainCheck.Detail, Does.Contain("xml_policy=true"));
Assert.That(endToEndChainCheck.Detail, Does.Contain("session_state=true"));
Assert.That(endToEndChainCheck.Detail, Does.Contain("diagnostics_closure=true"));
Assert.That(endToEndChainCheck.Detail, Does.Contain("route_denied_no_execute=true"));
Assert.That(endToEndChainCheck.Detail, Does.Contain("terminal_no_execute=true"));
Assert.That(endToEndChainCheck.Detail, Does.Contain("sidecar_authority=false"));
Assert.That(endToEndChainCheck.Detail, Does.Contain("default_tests_live_runtime=false"));
```

- [ ] **Step 2: Run dynamic readiness and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.CoreReadinessChecksAllPass" -v:minimal
```

Expected: fail because `DataAgentReadiness.CheckCore` still returns `76` checks and does not include `DataAgentEndToEndChainContractPresent`.

- [ ] **Step 3: Add the dynamic readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, insert this block immediately after the `GraphHandshakeDevSidecarObservabilityContractPresent` check:

```csharp
bool endToEndChainContractReady =
    typeof(DataAgentAnalysisToolHandler).IsClass &&
    typeof(DataAgentAnalysisOrchestrator).IsClass &&
    typeof(DataAgentService).IsClass &&
    typeof(XmlFunctionExecutionPolicy).IsClass &&
    typeof(ToolCapabilityRouter).IsClass &&
    typeof(DataAgentEvidenceDiagnosticsFormatter).IsClass &&
    typeof(DataAgentTraceDiagnosticsFormatter).IsClass &&
    typeof(DataAgentProgressDiagnosticsFormatter).IsClass &&
    typeof(DataAgentDataQueryGraphPilot).IsClass &&
    typeof(DataAgentGraphHandshakeCoordinator).IsClass &&
    File.Exists(Path.Combine(
        FindRepositoryRoot(AppContext.BaseDirectory),
        "Tests",
        "Alife.Test.DataAgent",
        "DataAgentEndToEndChainContractTests.cs"));
checks.Add(endToEndChainContractReady
    ? Pass("DataAgentEndToEndChainContractPresent", "route_boundary=true;xml_policy=true;session_state=true;diagnostics_closure=true;route_denied_no_execute=true;terminal_no_execute=true;sidecar_authority=false;default_tests_live_runtime=false")
    : Fail("DataAgentEndToEndChainContractPresent", $"route_boundary={LowerBool(typeof(ToolCapabilityRouter).IsClass)};xml_policy={LowerBool(typeof(XmlFunctionExecutionPolicy).IsClass)};session_state={LowerBool(typeof(DataAgentAnalysisToolHandler).IsClass)};diagnostics_closure={LowerBool(typeof(DataAgentEvidenceDiagnosticsFormatter).IsClass)};route_denied_no_execute=false;terminal_no_execute=false;sidecar_authority=false;default_tests_live_runtime=false"));
```

- [ ] **Step 4: Run dynamic readiness and verify it passes**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.CoreReadinessChecksAllPass" -v:minimal
```

Expected: pass with `77` checks and `DataAgentEndToEndChainContractPresent`.

- [ ] **Step 5: Commit dynamic readiness**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs
git commit -m "Add DataAgent V3.8 dynamic readiness marker"
```

---

### Task 4: Add Static Readiness Coverage

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `tools/check-dataagent-readiness.ps1`

- [ ] **Step 1: Update failing static readiness assertions**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, change the expected summary in `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary` from:

```csharp
"  Summary: 91 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 92 required passed, 0 required missing"
```

In the same test, add:

```csharp
Assert.That(result.StandardOutput, Does.Contain("DataAgentEndToEndChainContractPresent"));
```

In `ReadinessScriptProtectsV23RouteGateContract`, change:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 91"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 92"));
```

Then add this new test after `StaticReadinessScriptContainsV36SidecarObservabilityMarkers`:

```csharp
[Test]
public void StaticReadinessScriptContainsV38EndToEndChainMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);
    string declaration = FindNewCheckDeclaration(script, "DataAgentEndToEndChainContractPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentEndToEndChainContractTests.cs"));
        Assert.That(declaration, Does.Contain("ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface"));
        Assert.That(declaration, Does.Contain("XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools"));
        Assert.That(declaration, Does.Contain("AcceptedAnalysisPublishesSessionStateAndAllDiagnostics"));
        Assert.That(declaration, Does.Contain("RouteDeniedAnalysisDoesNotExecuteSql"));
        Assert.That(declaration, Does.Contain("TerminalAnalysisActionsDoNotExecuteSqlAndRemainSessionScoped"));
        Assert.That(declaration, Does.Contain("QChatDiagnosticsService"));
        Assert.That(declaration, Does.Contain("DataAgentEndToEndChainContractPresent"));
        Assert.That(declaration, Does.Contain("sidecar_authority=false"));
        Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
    });
}
```

- [ ] **Step 2: Run static readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptDefaultModeExitsZeroAndPrintsSummary|FullyQualifiedName~DataAgentReadinessTests.StaticReadinessScriptContainsV38EndToEndChainMarkers" -v:minimal
```

Expected: fail because the PowerShell readiness script still expects `91` required checks and lacks `DataAgentEndToEndChainContractPresent`.

- [ ] **Step 3: Add the static readiness marker**

In `tools/check-dataagent-readiness.ps1`, insert this check immediately after the `GraphHandshakeDevSidecarObservabilityContractPresent` check:

```powershell
    New-Check -Group "Governance" -Name "DataAgentEndToEndChainContractPresent" -Passed ((Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs" @("ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface", "XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools", "AcceptedAnalysisPublishesSessionStateAndAllDiagnostics", "RouteDeniedAnalysisDoesNotExecuteSql", "TerminalAnalysisActionsDoNotExecuteSqlAndRemainSessionScoped", "QChatDiagnosticsService", "DataAgentGraphHandshakeCoordinator", "sidecar_authority=false", "default_tests_live_runtime=false")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentEndToEndChainContractPresent", "route_boundary=true", "xml_policy=true", "session_state=true", "diagnostics_closure=true", "route_denied_no_execute=true", "terminal_no_execute=true", "sidecar_authority=false", "default_tests_live_runtime=false")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj" @("Alife.Function.QChat.csproj")) -and (Test-FileOmitsMarker "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" @("DataAgentEndToEndChainContractPresent", "DataAgentEndToEndChainContractTests"))) -Detail "V3.8 DataAgent end-to-end chain contract route_boundary=true xml_policy=true session_state=true diagnostics_closure=true route_denied_no_execute=true terminal_no_execute=true sidecar_authority=false default_tests_live_runtime=false"
```

Then change:

```powershell
$expectedRequired = 91
```

to:

```powershell
$expectedRequired = 92
```

- [ ] **Step 4: Run static readiness tests and script**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptDefaultModeExitsZeroAndPrintsSummary|FullyQualifiedName~DataAgentReadinessTests.StaticReadinessScriptContainsV38EndToEndChainMarkers" -v:minimal
```

Expected: pass.

Then run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
PASS     DataAgentEndToEndChainContractPresent
Summary: 92 required passed, 0 required missing
```

- [ ] **Step 5: Commit static readiness**

Run:

```powershell
git add tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs
git commit -m "Add DataAgent V3.8 static readiness marker"
```

---

### Task 5: Final Verification

**Files:**
- Verify only; no planned file edits.

- [ ] **Step 1: Run focused V3.8 tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEndToEndChainContractTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: pass.

- [ ] **Step 2: Run full DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: pass.

- [ ] **Step 3: Run static readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected: exit code `0`, `PASS     DataAgentEndToEndChainContractPresent`, and `Summary: 92 required passed, 0 required missing`.

- [ ] **Step 4: Verify V3.8 did not modify forbidden areas**

Run:

```powershell
git diff --name-only HEAD~4..HEAD
```

Expected changed files are limited to:

```text
Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj
Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs
Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs
tools/check-dataagent-readiness.ps1
```

- [ ] **Step 5: Commit any final verification-only correction**

If no correction was needed, do not create an empty commit. If a correction was needed, run:

```powershell
git add Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj Tests\Alife.Test.DataAgent\DataAgentEndToEndChainContractTests.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1
git commit -m "Verify DataAgent V3.8 chain contract"
```
