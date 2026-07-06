# DataAgent V2.16 DataQueryGraph Owner Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the existing V2.15 DataQueryGraph dry-run result through owner-only QChat diagnostics without adding LangGraph runtime, Python, HTTP sidecars, SQL authority, checkpoint authority, or QChat imports of DataQueryGraph model types.

**Architecture:** Extend the existing string diagnostics bridge: DataAgent formats the dry-run result, FunctionCaller stores the latest graph diagnostics string, and QChat retrieves it through owner-only commands and the recent diagnostics cache. DataAgent remains the graph and SQL safety owner; QChat remains a sanitized string consumer.

**Tech Stack:** .NET 9, C#, NUnit, existing DataAgent/FunctionCaller/QChat projects, existing PowerShell readiness scripts, no new NuGet packages, no Python, no LangGraph runtime.

---

## Scope Boundaries

Allowed:
- Add one optional DataAgent graph diagnostics publisher to the existing analysis handler path.
- Store graph diagnostics in `XmlFunctionCaller` as normalized text.
- Add a QChat `DataAgentGraph` recent diagnostics kind and owner-only `/dataagent diag graph` commands.
- Add readiness and engineering-map gates proving the owner diagnostics bridge exists and QChat does not import DataQueryGraph internals.
- Add documentation explaining how the owner uses graph diagnostics and what remains non-authoritative.

Forbidden:
- No real LangGraph runtime.
- No Python sidecar code.
- No FastAPI, HTTP client, HTTP server, or process manager.
- No new SQL compiler or SQL executor.
- No model-controlled SQL execution.
- No Tool Broker route authority inside DataQueryGraph.
- No checkpoint/session mutation by DataQueryGraph.
- No evidence, progress, trace, diagnostics, visible QChat text, or QQ ingress authority by DataQueryGraph.
- No QChat direct references to `DataAgentDataQueryGraph*` model types.

## File Structure

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
  - Adds optional `dataQueryGraphDiagnosticsPublisher`.
  - Publishes `DataAgentDataQueryGraphTraceFormatter.Format(DataAgentDataQueryGraphPilot.DryRun(result))`.
  - Keeps graph diagnostics independent from evidence and trace publishers.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
  - Accepts and forwards the graph diagnostics publisher.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Wires `functionService.RecordRecentDataAgentGraphDiagnostics`.
- Modify `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`
  - Adds string storage and normalization for recent graph diagnostics.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`
  - Adds `DataAgentGraph` cache kind and long text bound.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`
  - Adds title and `dataagent_graph_recent` summary line.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
  - Adds runtime state string, graph command handling, unavailable text, and redacted fallback handling.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
  - Adds `/dataagent diag graph` detection and passes recent graph diagnostics into runtime state.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs`
  - Makes `/dataagent diag graph` owner-only, and keeps existing DataAgent diagnostics protected.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Adds graph diagnostics local cache, FunctionCaller fallback sync, recent cache recording, and owner command wiring.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds runtime readiness check `DataQueryGraphOwnerDiagnosticsPresent`.
- Modify `tools/check-dataagent-readiness.ps1`
  - Adds static check `DataQueryGraphOwnerDiagnosticsPresent`; raises required count from `84` to `85`.
- Modify `tools/check-qchat-engineering-map.ps1`
  - Adds required check `DataAgent DataQueryGraph owner diagnostics`; raises required count from `59` to `60`.
- Modify existing DataAgent and QChat tests listed in tasks below.
- Create `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
  - Locks V2.16 runtime and static readiness markers.
- Create `docs/dataagent/dataagent-v2.16-dataquerygraph-owner-diagnostics.md`
  - Documents owner command, default-off pilot state, and authority boundaries.

## Task 1: Publish Graph Diagnostics From DataAgent Analysis

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`

- [ ] **Step 1: Add failing handler tests for graph diagnostics publishing**

Append these tests to `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs` before `AnalysisMethodsAreRegisteredAsXmlFunctions`:

```csharp
[Test]
[NonParallelizable]
public void StartPublishesDataQueryGraphDiagnosticsWithoutEvidenceOrTracePublisher()
{
    string? previous = Environment.GetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable);
    Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, null);
    try
    {
        List<string> graphDiagnostics = [];
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
                "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]")
        });
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            dataQueryGraphDiagnosticsPublisher: graphDiagnostics.Add);

        handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            Assert.That(graphDiagnostics, Has.Count.EqualTo(1));
            Assert.That(graphDiagnostics.Single(), Does.Contain("DataQueryGraph dry-run"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("enabled=false"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("accepted=false"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("reason=dataquerygraph_disabled"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("fallback=pilot_disabled"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("runtime=no_langgraph_runtime"));
        });
    }
    finally
    {
        Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, previous);
    }
}

[Test]
[NonParallelizable]
public void ContinueSummarizeAndEndPublishDataQueryGraphDiagnostics()
{
    string? previous = Environment.GetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable);
    Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, "true");
    try
    {
        List<string> graphDiagnostics = [];
        RecordingOrchestrator orchestrator = CreateOrchestrator();
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            dataQueryGraphDiagnosticsPublisher: graphDiagnostics.Add);

        handler.Continue("session-1", "continue");
        handler.Summarize("session-1");
        handler.End("session-1");

        Assert.Multiple(() =>
        {
            Assert.That(graphDiagnostics, Has.Count.EqualTo(3));
            Assert.That(graphDiagnostics, Has.All.Contains("DataQueryGraph dry-run"));
            Assert.That(graphDiagnostics, Has.All.Contains("enabled=true"));
            Assert.That(graphDiagnostics[0], Does.Contain("reason=dataquerygraph_dry_run_completed")
                .Or.Contain("reason=dataquerygraph_fallback_to_deterministic_orchestrator"));
            Assert.That(graphDiagnostics[1], Does.Contain("terminal"));
            Assert.That(graphDiagnostics[2], Does.Contain("terminal"));
        });
    }
    finally
    {
        Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, previous);
    }
}
```

- [ ] **Step 2: Run the failing handler tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: compile fails because `DataAgentAnalysisToolHandler` does not have `dataQueryGraphDiagnosticsPublisher`.

- [ ] **Step 3: Add the optional publisher to `DataAgentAnalysisToolHandler`**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`, change the primary constructor tail from:

```csharp
IDataAgentTraceRecorder? traceRecorder = null,
Func<DateTimeOffset>? traceClock = null)
```

to:

```csharp
IDataAgentTraceRecorder? traceRecorder = null,
Func<DateTimeOffset>? traceClock = null,
Action<string>? dataQueryGraphDiagnosticsPublisher = null)
```

Replace `PublishResult` with:

```csharp
void PublishResult(DataAgentOrchestrationResult result, string context)
{
    resultPublisher?.Invoke(context);
    PublishDataQueryGraphDiagnostics(result);

    if (evidenceDiagnosticsPublisher is null && traceDiagnosticsPublisher is null)
        return;

    DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result);
    evidenceDiagnosticsPublisher?.Invoke(DataAgentEvidenceDiagnosticsFormatter.Format(pack));

    if (traceDiagnosticsPublisher is null || traceRecorder is null)
        return;

    DateTimeOffset now = traceClock();
    DataAgentTraceTimeline timeline = new DataAgentTraceTimelineBuilder().Build(result, pack, now);
    traceRecorder.Record(timeline);
    DataAgentTraceTimeline? latestTimeline = traceRecorder.GetLatest(result.SessionId, now);
    traceDiagnosticsPublisher(DataAgentTraceDiagnosticsFormatter.Format(latestTimeline));
}

void PublishDataQueryGraphDiagnostics(DataAgentOrchestrationResult result)
{
    if (dataQueryGraphDiagnosticsPublisher is null)
        return;

    try
    {
        DataAgentDataQueryGraphDryRunResult graphResult = DataAgentDataQueryGraphPilot.DryRun(result);
        dataQueryGraphDiagnosticsPublisher(DataAgentDataQueryGraphTraceFormatter.Format(graphResult));
    }
    catch (Exception)
    {
        dataQueryGraphDiagnosticsPublisher(DataAgentDataQueryGraphTraceFormatter.Format(null));
    }
}
```

- [ ] **Step 4: Forward the publisher through `DataAgentAnalysisCapabilityProvider`**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`, change the constructor tail from:

```csharp
Action<string>? traceDiagnosticsPublisher = null,
IDataAgentTraceRecorder? traceRecorder = null) : IDataAgentCapabilityProvider
```

to:

```csharp
Action<string>? traceDiagnosticsPublisher = null,
IDataAgentTraceRecorder? traceRecorder = null,
Action<string>? dataQueryGraphDiagnosticsPublisher = null) : IDataAgentCapabilityProvider
```

Change the `DataAgentAnalysisToolHandler` registration from:

```csharp
traceDiagnosticsPublisher,
traceRecorder)));
```

to:

```csharp
traceDiagnosticsPublisher,
traceRecorder,
dataQueryGraphDiagnosticsPublisher: dataQueryGraphDiagnosticsPublisher)));
```

- [ ] **Step 5: Wire the publisher in `DataAgentModuleService`**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`, change:

```csharp
functionService.RecordRecentDataAgentTraceDiagnostics,
traceRecorder));
```

to:

```csharp
functionService.RecordRecentDataAgentTraceDiagnostics,
traceRecorder,
functionService.RecordRecentDataAgentGraphDiagnostics));
```

- [ ] **Step 6: Run the focused handler tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: selected tests pass with `0 Failed`.

- [ ] **Step 7: Commit Task 1**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentAnalysisToolHandlerTests.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentAnalysisToolHandler.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentAnalysisCapabilityProvider.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs
git commit -m "Publish DataQueryGraph owner diagnostics"
```

Expected: commit succeeds with only the listed files.

## Task 2: Store Graph Diagnostics In FunctionCaller

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentProgressDiagnosticsPublisherTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`

- [ ] **Step 1: Add a failing FunctionCaller storage test**

Append this test to `Tests/Alife.Test.DataAgent/DataAgentProgressDiagnosticsPublisherTests.cs`:

```csharp
[Test]
public void XmlFunctionCallerStoresRecentGraphDiagnostics()
{
    XmlFunctionCaller caller = new(new NullLogger<XmlFunctionCaller>());

    caller.RecordRecentDataAgentGraphDiagnostics("DataQueryGraph dry-run\r\nenabled=false  ");

    Assert.That(
        caller.RecentDataAgentGraphDiagnostics,
        Is.EqualTo("DataQueryGraph dry-run\nenabled=false"));
}
```

If the file does not already contain these usings, add them at the top:

```csharp
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging.Abstractions;
```

- [ ] **Step 2: Run the failing storage test**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressDiagnosticsPublisherTests.XmlFunctionCallerStoresRecentGraphDiagnostics" -v:minimal
```

Expected: compile fails because `XmlFunctionCaller` does not expose graph diagnostics storage.

- [ ] **Step 3: Add graph diagnostics storage to `XmlFunctionCaller`**

In `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`, add this property after `RecentDataAgentProgressDiagnostics`:

```csharp
public string RecentDataAgentGraphDiagnostics
{
    get
    {
        lock (dataAgentGraphDiagnosticsGate)
        {
            return recentDataAgentGraphDiagnostics;
        }
    }
}
```

Add this method after `RecordRecentDataAgentProgressDiagnostics`:

```csharp
public void RecordRecentDataAgentGraphDiagnostics(string? diagnostics)
{
    string normalized = string.IsNullOrWhiteSpace(diagnostics)
        ? string.Empty
        : diagnostics.ReplaceLineEndings("\n").Trim();

    lock (dataAgentGraphDiagnosticsGate)
    {
        recentDataAgentGraphDiagnostics = normalized;
    }
}
```

Add these fields next to the existing diagnostics gates and strings:

```csharp
readonly object dataAgentGraphDiagnosticsGate = new();
string recentDataAgentGraphDiagnostics = string.Empty;
```

- [ ] **Step 4: Run focused DataAgent bridge tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressDiagnosticsPublisherTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: selected tests pass with `0 Failed`.

- [ ] **Step 5: Commit Task 2**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentProgressDiagnosticsPublisherTests.cs sources\Alife.Function\Alife.Function.FunctionCaller\XmlFunctionCaller.cs
git commit -m "Store recent DataQueryGraph diagnostics"
```

Expected: commit succeeds with only the listed files.

## Task 3: Add QChat Graph Diagnostics Command And Cache Kind

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`

- [ ] **Step 1: Add failing QChat diagnostics service tests**

In `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`, append these tests near the existing DataAgent progress diagnostics tests:

```csharp
[TestCase("/dataagent diag graph")]
[TestCase("/dataagent diagnostics graph")]
[TestCase("/qchat diag dataagent graph")]
[TestCase("/qchat diagnostics dataagent graph")]
public void TryHandleDataAgentGraphDiagnosticsShowsRecentGraphForOwner(string command)
{
    QChatDiagnosticsRuntimeState state = new(
        RecentDataAgentGraph: string.Join(Environment.NewLine,
            "DataQueryGraph dry-run",
            "enabled=false",
            "reason=dataquerygraph_disabled"));

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        command,
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataQueryGraph dry-run"));
        Assert.That(result.Text, Does.Contain("reason=dataquerygraph_disabled"));
    });
}

[Test]
public void TryHandleDataAgentGraphDiagnosticsReturnsUnavailableWhenNoGraphExists()
{
    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag graph",
        CreateRoute(),
        CreateProfile(),
        new QChatDiagnosticsRuntimeState());

    string[] expectedLines =
    [
        "DataAgent graph diagnostics",
        "state=unavailable",
        "reason=graph_diagnostics_unavailable"
    ];

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    });
}

[Test]
public void TryHandleDataAgentGraphDiagnosticsPrefersSessionCacheOverLegacyGraph()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-06T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new();
    cache.Record(
        QChatRecentDiagnosticKind.DataAgentGraph,
        "qq:xiayu:2905391496:private:3045846738",
        "dataagent_graph",
        string.Join(Environment.NewLine,
            "DataQueryGraph dry-run",
            "graph_marker=from_cache"),
        now);
    QChatDiagnosticsRuntimeState state = new(
        RecentDataAgentGraph: "legacy graph text",
        RecentDiagnosticsCache: cache,
        SessionKey: "qq:xiayu:2905391496:private:3045846738",
        DiagnosticsNow: now);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag graph",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Text, Does.Contain("graph_marker=from_cache"));
        Assert.That(result.Text, Does.Not.Contain("legacy graph text"));
    });
}

[Test]
public void TryHandleDataAgentGraphDiagnosticsRedactsUnsafeLegacyFallbackText()
{
    QChatDiagnosticsRuntimeState state = new(
        RecentDataAgentGraph: "DataQueryGraph dry-run\nSELECT * FROM users\nBearer token-abcdef123456");

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag graph",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent graph diagnostics"));
        Assert.That(result.Text, Does.Contain("state=redacted"));
        Assert.That(result.Text, Does.Contain("reason=hidden_context_redacted"));
        Assert.That(result.Text, Does.Not.Contain("SELECT"));
        Assert.That(result.Text, Does.Not.Contain("token-abcdef123456"));
    });
}
```

- [ ] **Step 2: Add failing recent diagnostics cache tests**

Append these tests to `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`:

```csharp
[Test]
public void GraphDiagnosticsUseLongTextLimit()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-06T00:00:00Z");
    QChatRecentDiagnosticsCache cache = new();
    string longText = "DataQueryGraph dry-run\n" + new string('g', 1600);

    cache.Record(QChatRecentDiagnosticKind.DataAgentGraph, "session-a", "dataagent_graph", longText, now);

    QChatRecentDiagnosticEntry latest = cache.GetLatest("session-a", QChatRecentDiagnosticKind.DataAgentGraph, now)!;
    Assert.Multiple(() =>
    {
        Assert.That(latest.Text, Does.StartWith("DataQueryGraph dry-run"));
        Assert.That(latest.Text.Length, Is.GreaterThan(900));
        Assert.That(latest.Text.Length, Is.LessThanOrEqualTo(1800));
    });
}

[Test]
public void SummaryIncludesDataAgentGraphRecentLine()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-06T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new();
    cache.Record(QChatRecentDiagnosticKind.DataAgentGraph, "session-a", "dataagent_graph", "DataQueryGraph dry-run", now.AddSeconds(-7));

    string text = QChatRecentDiagnosticsFormatter.FormatSummary(cache.GetRecent("session-a", now), "session-a", now);

    Assert.That(text, Does.Contain("dataagent_graph_recent=available age_seconds=7 source=dataagent_graph redacted=false"));
}

[Test]
public void GraphDiagnosticsUnsafeTextIsRedacted()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-06T00:00:00Z");
    QChatRecentDiagnosticsCache cache = new();

    cache.Record(QChatRecentDiagnosticKind.DataAgentGraph, "session-a", "dataagent_graph", "DataQueryGraph dry-run\nSELECT * FROM users", now);

    QChatRecentDiagnosticEntry latest = cache.GetLatest("session-a", QChatRecentDiagnosticKind.DataAgentGraph, now)!;
    Assert.Multiple(() =>
    {
        Assert.That(latest.Redacted, Is.True);
        Assert.That(latest.Text, Does.Contain("DataAgent graph diagnostics"));
        Assert.That(latest.Text, Does.Contain("state=redacted"));
        Assert.That(latest.Text, Does.Not.Contain("SELECT"));
    });
}
```

- [ ] **Step 3: Run failing QChat diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests.TryHandleDataAgentGraphDiagnostics|FullyQualifiedName~QChatRecentDiagnosticsCacheTests.GraphDiagnostics|FullyQualifiedName~QChatRecentDiagnosticsCacheTests.SummaryIncludesDataAgentGraphRecentLine" -v:minimal
```

Expected: compile fails because `DataAgentGraph` and `RecentDataAgentGraph` do not exist.

- [ ] **Step 4: Add the `DataAgentGraph` recent diagnostics kind**

In `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`, change the enum to:

```csharp
public enum QChatRecentDiagnosticKind
{
    SemanticState,
    DataAgentEvidence,
    DataAgentTrace,
    DataAgentProgress,
    DataAgentGraph,
    ToolRoute
}
```

Change `GetMaxTextChars` to:

```csharp
static int GetMaxTextChars(QChatRecentDiagnosticKind kind)
{
    return kind is QChatRecentDiagnosticKind.DataAgentTrace
        or QChatRecentDiagnosticKind.DataAgentProgress
        or QChatRecentDiagnosticKind.DataAgentGraph
            ? DataAgentTraceMaxTextChars
            : MaxTextChars;
}
```

- [ ] **Step 5: Add graph summary formatting**

In `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`, add this line in `FormatSummary` after `dataagent_progress_recent`:

```csharp
FormatKindLine("dataagent_graph_recent", entries, QChatRecentDiagnosticKind.DataAgentGraph, now),
```

In `Title`, add:

```csharp
QChatRecentDiagnosticKind.DataAgentGraph => "DataAgent graph diagnostics",
```

- [ ] **Step 6: Add graph command handling to `QChatDiagnosticsService`**

In `QChatDiagnosticsRuntimeState`, add:

```csharp
string? RecentDataAgentGraph = null,
```

Place it after `RecentDataAgentProgress`.

In the dataagent command switch, add:

```csharp
"diag graph" or "diagnostics graph" => Handled(BuildDataAgentGraphDiagnosticsText(runtimeState, route)),
```

In the qchat diagnostics switch, add:

```csharp
"diag dataagent graph" or "diagnostics dataagent graph" => Handled(BuildDataAgentGraphDiagnosticsText(runtimeState, route)),
```

Add this method after `BuildDataAgentProgressDiagnosticsText`:

```csharp
static string BuildDataAgentGraphDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState, QChatAgentRoute route)
{
    string? cached = GetRecentCachedText(runtimeState, route, QChatRecentDiagnosticKind.DataAgentGraph);
    if (string.IsNullOrWhiteSpace(cached) == false)
        return cached;

    string sanitized = SanitizeDiagnosticText(
        runtimeState.RecentDataAgentGraph,
        "DataAgent graph diagnostics",
        maxChars: 1800);
    return string.IsNullOrWhiteSpace(sanitized)
        ? string.Join(Environment.NewLine,
            "DataAgent graph diagnostics",
            "state=unavailable",
            "reason=graph_diagnostics_unavailable")
        : sanitized;
}
```

In `BuildDiagnosticsMenuText`, add this line after `/dataagent diag progress`:

```csharp
"/dataagent diag graph - DataAgent DataQueryGraph dry-run diagnostics",
```

- [ ] **Step 7: Run focused QChat diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests.TryHandleDataAgentGraphDiagnostics|FullyQualifiedName~QChatRecentDiagnosticsCacheTests.GraphDiagnostics|FullyQualifiedName~QChatRecentDiagnosticsCacheTests.SummaryIncludesDataAgentGraphRecentLine" -v:minimal
```

Expected: selected tests pass with `0 Failed`.

- [ ] **Step 8: Commit Task 3**

Run:

```powershell
git add Tests\Alife.Test.QChat\QChatDiagnosticsServiceTests.cs Tests\Alife.Test.QChat\QChatRecentDiagnosticsCacheTests.cs sources\Alife.Function\Alife.Function.QChat\QChatRecentDiagnosticsCache.cs sources\Alife.Function\Alife.Function.QChat\QChatRecentDiagnosticsFormatter.cs sources\Alife.Function\Alife.Function.QChat\QChatDiagnosticsService.cs
git commit -m "Add QChat graph diagnostics channel"
```

Expected: commit succeeds with only the listed files.

## Task 4: Wire Owner Commands And QChatService Recent Bridge

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`

- [ ] **Step 1: Add failing owner command service tests**

In `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`, append this test near the trace/progress diagnostics tests:

```csharp
[Test]
public async Task TryHandleDiagnosticsCommandAsyncPassesRecentGraphToOwnerDiagnostics()
{
    List<(OneBotMessageType Type, long TargetId, string Message)> sent = [];
    OneBotMessageEvent messageEvent = new()
    {
        SelfId = 2905391496,
        UserId = 3045846738,
        RawMessage = "/dataagent diag graph"
    };

    bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
        messageEvent,
        QChatSenderRole.Owner,
        new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738
        },
        (type, targetId, message) =>
        {
            sent.Add((type, targetId, message));
            return Task.CompletedTask;
        },
        (_, _, _, _) => { },
        recentDataAgentGraph: () => "DataQueryGraph dry-run\nenabled=false");

    Assert.Multiple(() =>
    {
        Assert.That(handled, Is.True);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].Message, Does.Contain("DataQueryGraph dry-run"));
        Assert.That(sent[0].Message, Does.Contain("enabled=false"));
    });
}
```

In `IsDiagnosticsCommandMatchesOnlyQChatAndDataAgentDiagnosticsCommands`, add:

```csharp
[TestCase("/dataagent diag graph", true)]
[TestCase("/dataagent diagnostics graph", true)]
```

- [ ] **Step 2: Add failing command access policy tests**

In `Tests/Alife.Test.QChat/QChatCommandAccessPolicyTests.cs`, add these cases to `OwnerDataAgentDiagnosticCommandIsAllowed`:

```csharp
[TestCase("/dataagent diag graph")]
[TestCase("/dataagent diagnostics graph")]
[TestCase("/dataagent diag graph - DataAgent DataQueryGraph dry-run diagnostics")]
```

Add this test after `NonOwnerDataAgentDiagnosticsEvidenceCommandIsDroppedSilently`:

```csharp
[TestCase(QChatSenderRole.PrivateGuest)]
[TestCase(QChatSenderRole.GroupMember)]
public void NonOwnerDataAgentDiagGraphCommandIsDroppedSilently(QChatSenderRole role)
{
    QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
        new QChatCommandAccessContext("/dataagent diag graph", role));

    Assert.Multiple(() =>
    {
        Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.DropSilently));
        Assert.That(decision.Reason, Is.EqualTo("non_owner_qchat_command"));
    });
}
```

- [ ] **Step 3: Add failing QChatService adapter tests**

Append these tests near existing DataAgent trace/progress adapter tests in `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`:

```csharp
[Test]
public async Task OwnerCanReadRecentDataAgentGraphDiagnosticsRecordedOnService()
{
    FakeOneBotRuntime runtime = new()
    {
        BotId = 2905391496
    };
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 2905391496,
        OwnerId = 3045846738,
        EnableBalancedTextStreaming = false
    });
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        dispatchCount++;
        return Task.CompletedTask;
    };
    service.RecordRecentDataAgentGraphDiagnostics(string.Join(Environment.NewLine,
        "DataQueryGraph dry-run",
        "enabled=false",
        "reason=dataquerygraph_disabled"));

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 2905391496,
        UserId = 3045846738,
        RawMessage = "/dataagent diag graph"
    });

    await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
    string reply = runtime.PrivateMessages.Single().Message;
    Assert.Multiple(() =>
    {
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(reply, Does.Contain("DataQueryGraph dry-run"));
        Assert.That(reply, Does.Contain("reason=dataquerygraph_disabled"));
        Assert.That(reply, Does.Not.Contain("state=unavailable"));
    });
}

[Test]
public async Task RecentDiagnosticsSummaryIncludesDataAgentGraphWhenFunctionCallerHasFallback()
{
    FakeOneBotRuntime runtime = new()
    {
        BotId = 2905391496
    };
    XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
    functionCaller.RecordRecentDataAgentGraphDiagnostics(string.Join(Environment.NewLine,
        "DataQueryGraph dry-run",
        "enabled=false"));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 2905391496,
        OwnerId = 3045846738,
        EnableBalancedTextStreaming = false
    }, functionCaller: functionCaller);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        dispatchCount++;
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 2905391496,
        UserId = 3045846738,
        RawMessage = "/qchat diag recent"
    });

    await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
    string reply = runtime.PrivateMessages.Single().Message;
    Assert.Multiple(() =>
    {
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(reply, Does.Contain("QChat recent diagnostics"));
        Assert.That(reply, Does.Contain("dataagent_graph_recent=available"));
        Assert.That(reply, Does.Contain("source=dataagent_graph"));
        Assert.That(reply, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
        Assert.That(reply, Does.Not.Contain("reason=recent_diagnostics_empty"));
    });
}
```

- [ ] **Step 4: Run failing owner/QChatService tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatOwnerCommandServiceTests.TryHandleDiagnosticsCommandAsyncPassesRecentGraphToOwnerDiagnostics|FullyQualifiedName~QChatOwnerCommandServiceTests.IsDiagnosticsCommandMatchesOnlyQChatAndDataAgentDiagnosticsCommands|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatServiceAdapterTests.OwnerCanReadRecentDataAgentGraphDiagnosticsRecordedOnService|FullyQualifiedName~QChatServiceAdapterTests.RecentDiagnosticsSummaryIncludesDataAgentGraphWhenFunctionCallerHasFallback" -v:minimal
```

Expected: compile fails because owner command and QChatService graph bridge methods do not exist.

- [ ] **Step 5: Extend `QChatOwnerCommandService`**

In `IsDiagnosticsCommand`, add graph command comparisons after progress:

```csharp
|| command.Equals("diag graph", StringComparison.OrdinalIgnoreCase)
|| command.Equals("diagnostics graph", StringComparison.OrdinalIgnoreCase)
```

In the overload that forwards to the full `TryHandleDiagnosticsCommandAsync`, append `recentDataAgentGraph: null`.

In the full `TryHandleDiagnosticsCommandAsync` signature, append this optional parameter after `diagnosticsNow`:

```csharp
Func<string>? recentDataAgentGraph = null
```

In the `QChatDiagnosticsRuntimeState` construction, add:

```csharp
RecentDataAgentGraph: recentDataAgentGraph?.Invoke(),
```

- [ ] **Step 6: Extend `QChatCommandAccessPolicy`**

Replace:

```csharp
return IsCommandWithPrefix(text, QChatPrefix) ||
       IsDataAgentEvidenceDiagnosticCommand(text);
```

with:

```csharp
return IsCommandWithPrefix(text, QChatPrefix) ||
       IsDataAgentDiagnosticCommand(text);
```

Rename `IsDataAgentEvidenceDiagnosticCommand` to `IsDataAgentDiagnosticCommand` and replace its return statement with:

```csharp
return command.Equals("diag evidence", StringComparison.OrdinalIgnoreCase) ||
       command.Equals("diagnostics evidence", StringComparison.OrdinalIgnoreCase) ||
       command.Equals("diag trace", StringComparison.OrdinalIgnoreCase) ||
       command.Equals("diagnostics trace", StringComparison.OrdinalIgnoreCase) ||
       command.Equals("diag progress", StringComparison.OrdinalIgnoreCase) ||
       command.Equals("diagnostics progress", StringComparison.OrdinalIgnoreCase) ||
       command.Equals("diag graph", StringComparison.OrdinalIgnoreCase) ||
       command.Equals("diagnostics graph", StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 7: Add QChatService graph diagnostics storage and command wiring**

In `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`, add a graph diagnostics lock and local string next to existing DataAgent diagnostics fields:

```csharp
readonly object dataAgentGraphDiagnosticsGate = new();
string recentDataAgentGraphDiagnostics = string.Empty;
```

In `TryHandleQChatDiagnosticsCommandAsync`, add a named argument:

```csharp
recentDataAgentGraph: GetRecentDataAgentGraphDiagnostics
```

Add this method after `RecordRecentDataAgentProgressDiagnostics`:

```csharp
public void RecordRecentDataAgentGraphDiagnostics(string? diagnostics)
{
    string normalized = NormalizeCachedDiagnosticText(diagnostics);
    lock (dataAgentGraphDiagnosticsGate)
    {
        recentDataAgentGraphDiagnostics = normalized;
    }

    functionService.RecordRecentDataAgentGraphDiagnostics(normalized);
    QChatReplySession? replySession = GetCurrentReplySessionForGuard();
    recentDiagnosticsCache.Record(
        QChatRecentDiagnosticKind.DataAgentGraph,
        replySession != null
            ? BuildRecentDiagnosticsSessionKey(replySession)
            : BuildOwnerPrivateRecentDiagnosticsSessionKey(),
        "dataagent_graph",
        normalized,
        DateTimeOffset.UtcNow);
}
```

Add this method after `GetRecentDataAgentProgressDiagnostics`:

```csharp
string GetRecentDataAgentGraphDiagnostics()
{
    string fallback = NormalizeCachedDiagnosticText(functionService.RecentDataAgentGraphDiagnostics);
    bool shouldRecordFallback = false;
    lock (dataAgentGraphDiagnosticsGate)
    {
        if (string.IsNullOrWhiteSpace(fallback))
            return recentDataAgentGraphDiagnostics;

        if (string.Equals(recentDataAgentGraphDiagnostics, fallback, StringComparison.Ordinal))
            return recentDataAgentGraphDiagnostics;

        recentDataAgentGraphDiagnostics = fallback;
        shouldRecordFallback = true;
    }

    if (shouldRecordFallback)
    {
        recentDiagnosticsCache.Record(
            QChatRecentDiagnosticKind.DataAgentGraph,
            BuildOwnerPrivateRecentDiagnosticsSessionKey(),
            "dataagent_graph",
            fallback,
            DateTimeOffset.UtcNow);
    }

    return fallback;
}
```

- [ ] **Step 8: Run focused owner/QChatService tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatOwnerCommandServiceTests.TryHandleDiagnosticsCommandAsyncPassesRecentGraphToOwnerDiagnostics|FullyQualifiedName~QChatOwnerCommandServiceTests.IsDiagnosticsCommandMatchesOnlyQChatAndDataAgentDiagnosticsCommands|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatServiceAdapterTests.OwnerCanReadRecentDataAgentGraphDiagnosticsRecordedOnService|FullyQualifiedName~QChatServiceAdapterTests.RecentDiagnosticsSummaryIncludesDataAgentGraphWhenFunctionCallerHasFallback" -v:minimal
```

Expected: selected tests pass with `0 Failed`.

- [ ] **Step 9: Commit Task 4**

Run:

```powershell
git add Tests\Alife.Test.QChat\QChatOwnerCommandServiceTests.cs Tests\Alife.Test.QChat\QChatCommandAccessPolicyTests.cs Tests\Alife.Test.QChat\QChatServiceAdapterTests.cs sources\Alife.Function\Alife.Function.QChat\QChatOwnerCommandService.cs sources\Alife.Function\Alife.Function.QChat\QChatCommandAccessPolicy.cs sources\Alife.Function\Alife.Function.QChat\QChatService.cs
git commit -m "Wire QChat DataQueryGraph owner diagnostics"
```

Expected: commit succeeds with only the listed files.

## Task 5: Add Readiness, Engineering Map, And Documentation

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
- Create: `docs/dataagent/dataagent-v2.16-dataquerygraph-owner-diagnostics.md`

- [ ] **Step 1: Add failing V2.16 readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV216ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesDataQueryGraphOwnerDiagnosticsBridge()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v216-readiness.sqlite");
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);
        Dictionary<string, DataAgentReadinessCheck> byName = checks.ToDictionary(check => check.Name);

        Assert.Multiple(() =>
        {
            Assert.That(byName, Does.ContainKey("DataQueryGraphOwnerDiagnosticsPresent"));
            DataAgentReadinessCheck check = byName["DataQueryGraphOwnerDiagnosticsPresent"];
            Assert.That(check.Passed, Is.True, check.Detail);
            Assert.That(check.Detail, Does.Contain("handler_publisher=true"));
            Assert.That(check.Detail, Does.Contain("capability_provider=true"));
            Assert.That(check.Detail, Does.Contain("function_caller=true"));
            Assert.That(check.Detail, Does.Contain("disabled_diagnostics=true"));
            Assert.That(check.Detail, Does.Contain("no_langgraph_runtime=true"));
        });
    }

    [Test]
    public void StaticReadinessScriptIncludesV216OwnerDiagnosticsBridge()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("DataQueryGraphOwnerDiagnosticsPresent"));
            Assert.That(script, Does.Contain("RecordRecentDataAgentGraphDiagnostics"));
            Assert.That(script, Does.Contain("RecentDataAgentGraphDiagnostics"));
            Assert.That(script, Does.Contain("dataQueryGraphDiagnosticsPublisher"));
            Assert.That(script, Does.Contain("DataAgentDataQueryGraphTraceFormatter.Format"));
            Assert.That(script, Does.Contain("$expectedRequired = 85"));
        });
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
```

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, add assertions to the top readiness test:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataQueryGraphOwnerDiagnosticsPresent"));
DataAgentReadinessCheck graphDiagnosticsCheck = checks.Single(check => check.Name == "DataQueryGraphOwnerDiagnosticsPresent");
Assert.That(graphDiagnosticsCheck.Passed, Is.True, graphDiagnosticsCheck.Detail);
Assert.That(graphDiagnosticsCheck.Detail, Does.Contain("handler_publisher=true"));
```

Update static count expectations in `DataAgentReadinessTests` from:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 84"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 85"));
```

In `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`, change:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 84"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 59"));
```

to:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 85"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 60"));
```

- [ ] **Step 2: Add failing QChat engineering-map tests**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add to `RequiredV2Checks` immediately after `"DataAgent DataQueryGraph pilot"`:

```csharp
"DataAgent DataQueryGraph owner diagnostics",
```

Append this test after `DataQueryGraphPilotCheckRequiresDataAgentRuntimeAndQChatBoundary`:

```csharp
[Test]
public void DataQueryGraphOwnerDiagnosticsCheckRequiresStringBridgeAndQChatBoundary()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindAddCheckDeclaration(script, "DataAgent DataQueryGraph owner diagnostics");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataQueryGraphOwnerDiagnosticsPresent"));
        Assert.That(declaration, Does.Contain("RecentDataAgentGraph"));
        Assert.That(declaration, Does.Contain("DataAgentGraph"));
        Assert.That(declaration, Does.Contain("diag graph"));
        Assert.That(declaration, Does.Contain("QChatDoesNotDirectlyImportDataAgentBoundaryTypes"));
        Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphOptions"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPilot"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphDryRunResult"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphTraceFormatter"));
    });
}
```

Update summary/count assertions from `59` to `60`:

```csharp
Assert.That(result.StandardOutput, Does.Contain("Summary: 60 required passed, 0 required missing, 0 optional present, 0 optional missing"));
Assert.That(script, Does.Contain("$expectedRequired = 60"));
```

- [ ] **Step 3: Run failing readiness and map tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: failures mention missing `DataQueryGraphOwnerDiagnosticsPresent`, missing static checks, and old required counts.

- [ ] **Step 4: Add runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after the `DataQueryGraphPilotPresent` check, insert:

```csharp
string dataQueryGraphDisabledDiagnostics = DataAgentDataQueryGraphTraceFormatter.Format(
    DataAgentDataQueryGraphPilot.DryRun(CreateReadinessDataQueryGraphAcceptedResult(), DataAgentDataQueryGraphOptions.Disabled));
bool dataQueryGraphHandlerPublisherReady =
    typeof(DataAgentAnalysisToolHandler)
        .GetConstructors()
        .SelectMany(constructor => constructor.GetParameters())
        .Any(parameter => string.Equals(parameter.Name, "dataQueryGraphDiagnosticsPublisher", StringComparison.Ordinal));
bool dataQueryGraphCapabilityProviderReady =
    typeof(DataAgentAnalysisCapabilityProvider)
        .GetConstructors()
        .SelectMany(constructor => constructor.GetParameters())
        .Any(parameter => string.Equals(parameter.Name, "dataQueryGraphDiagnosticsPublisher", StringComparison.Ordinal));
bool dataQueryGraphFunctionCallerReady =
    typeof(XmlFunctionCaller).GetProperty("RecentDataAgentGraphDiagnostics") is not null &&
    typeof(XmlFunctionCaller).GetMethod("RecordRecentDataAgentGraphDiagnostics") is not null;
bool dataQueryGraphDisabledDiagnosticsReady =
    dataQueryGraphDisabledDiagnostics.Contains("DataQueryGraph dry-run", StringComparison.Ordinal) &&
    dataQueryGraphDisabledDiagnostics.Contains("enabled=false", StringComparison.Ordinal) &&
    dataQueryGraphDisabledDiagnostics.Contains("reason=dataquerygraph_disabled", StringComparison.Ordinal) &&
    dataQueryGraphDisabledDiagnostics.Contains("runtime=no_langgraph_runtime", StringComparison.Ordinal);
bool dataQueryGraphOwnerDiagnosticsReady =
    dataQueryGraphHandlerPublisherReady &&
    dataQueryGraphCapabilityProviderReady &&
    dataQueryGraphFunctionCallerReady &&
    dataQueryGraphDisabledDiagnosticsReady &&
    string.Equals(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker, "no_langgraph_runtime", StringComparison.Ordinal);
string dataQueryGraphOwnerDiagnosticsDetail =
    $"handler_publisher={LowerBool(dataQueryGraphHandlerPublisherReady)};capability_provider={LowerBool(dataQueryGraphCapabilityProviderReady)};function_caller={LowerBool(dataQueryGraphFunctionCallerReady)};disabled_diagnostics={LowerBool(dataQueryGraphDisabledDiagnosticsReady)};no_langgraph_runtime={LowerBool(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker == "no_langgraph_runtime")}";
checks.Add(dataQueryGraphOwnerDiagnosticsReady
    ? Pass("DataQueryGraphOwnerDiagnosticsPresent", dataQueryGraphOwnerDiagnosticsDetail)
    : Fail("DataQueryGraphOwnerDiagnosticsPresent", dataQueryGraphOwnerDiagnosticsDetail));
```

- [ ] **Step 5: Add static DataAgent readiness gate**

In `tools/check-dataagent-readiness.ps1`, add this check immediately after `DataQueryGraphPilotPresent`:

```powershell
New-Check -Group "Store" -Name "DataQueryGraphOwnerDiagnosticsPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("dataQueryGraphDiagnosticsPublisher", "DataAgentDataQueryGraphPilot.DryRun", "DataAgentDataQueryGraphTraceFormatter.Format", "PublishDataQueryGraphDiagnostics")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs" @("dataQueryGraphDiagnosticsPublisher", "DataAgentAnalysisToolHandler")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("functionService.RecordRecentDataAgentGraphDiagnostics")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs" @("RecentDataAgentGraphDiagnostics", "RecordRecentDataAgentGraphDiagnostics", "dataAgentGraphDiagnosticsGate")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs" @("StartPublishesDataQueryGraphDiagnosticsWithoutEvidenceOrTracePublisher", "ContinueSummarizeAndEndPublishDataQueryGraphDiagnostics")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs" @("DataQueryGraphOwnerDiagnosticsPresent", "StaticReadinessScriptIncludesV216OwnerDiagnosticsBridge"))) -Detail "V2.16 DataQueryGraph owner diagnostics string bridge markers"
```

Change:

```powershell
$expectedRequired = 84
```

to:

```powershell
$expectedRequired = 85
```

- [ ] **Step 6: Add QChat engineering-map gate**

In `tools/check-qchat-engineering-map.ps1`, add this check immediately after `DataAgent DataQueryGraph pilot`:

```powershell
Add-Check -Group "Harness" -Name "DataAgent DataQueryGraph owner diagnostics" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataQueryGraphOwnerDiagnosticsPresent", "RecordRecentDataAgentGraphDiagnostics", "RecentDataAgentGraphDiagnostics", "dataQueryGraphDiagnosticsPublisher") -AlsoPath "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -AlsoPatterns @("RecentDataAgentGraph", "diag graph", "BuildDataAgentGraphDiagnosticsText", "DataAgent graph diagnostics") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentDataQueryGraphOptions", "DataAgentDataQueryGraphPilot", "DataAgentDataQueryGraphPlan", "DataAgentDataQueryGraphNode", "DataAgentDataQueryGraphTransition", "DataAgentDataQueryGraphDryRunResult", "DataAgentDataQueryGraphTraceFormatter")
```

Change:

```powershell
$expectedRequired = 59
```

to:

```powershell
$expectedRequired = 60
```

- [ ] **Step 7: Add V2.16 developer documentation**

Create `docs/dataagent/dataagent-v2.16-dataquerygraph-owner-diagnostics.md`:

```markdown
# DataAgent V2.16 DataQueryGraph Owner Diagnostics

V2.16 surfaces the V2.15 DataQueryGraph dry-run result through owner-only diagnostics. It does not add LangGraph runtime behavior, Python sidecar code, FastAPI, HTTP calls, a sidecar process, or a new SQL execution path.

## Owner Command

The owner can inspect the latest graph diagnostics with:

```text
/dataagent diag graph
/dataagent diagnostics graph
/qchat diag dataagent graph
/qchat diagnostics dataagent graph
```

When the pilot flag is disabled, the command reports the disabled dry-run state:

```text
DataQueryGraph dry-run
enabled=false
accepted=false
reason=dataquerygraph_disabled
fallback=pilot_disabled
runtime=no_langgraph_runtime
nodes=
```

## Pilot Flag

The feature flag remains:

```text
ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED=false
```

Missing, blank, `false`, `0`, and `no` values are disabled. Explicit `true`, `1`, and `yes` values enable only C# dry-run diagnostics.

## Authority Boundary

DataQueryGraph diagnostics are observable state only. They cannot authorize datasets, fields, operators, limits, SQL generation, SQL execution, Tool Broker route state, checkpoint mutation, query audit, Tool Broker audit, evidence packs, progress events, trace timelines, visible QChat text, or QQ ingress.

The existing C# DataAgent pipeline remains authoritative:

- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles read-only parameterized SQL.
- `DataAgentSqlSafetyValidator` rejects unsafe SQL shapes.
- `IDataAgentStore` executes read-only queries.
- `IDataAgentAnalysisSessionStore` persists checkpoints and turns.
- Tool Broker route state decides whether DataAgent tools can be used.

## QChat Boundary

QChat consumes graph diagnostics as sanitized strings through FunctionCaller and the recent diagnostics cache. QChat does not import DataQueryGraph pilot model types and does not build graph plans.

## Plugin Governance

V2.16 does not force QQchat, desktop, browser, RAG, or other deterministic plugin abilities into graph nodes. Non-agentized abilities remain normal services unless a future design explicitly assigns them to a graph node with scoped capability manifests.

## Future Path

This release makes the graph projection inspectable before a real LangGraph adapter exists. A future V3 adapter should only be added after the C# contract, node scopes, owner diagnostics, no-execute behavior, and fallback behavior remain stable.
```

- [ ] **Step 8: Run readiness scripts and focused readiness tests**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected:

```text
Summary: 85 required passed, 0 required missing
Summary: 60 required passed, 0 required missing, 0 optional present, 0 optional missing
```

Selected tests pass with `0 Failed`.

- [ ] **Step 9: Commit Task 5**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV216ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV210ReadinessTests.cs tools\check-dataagent-readiness.ps1 tools\check-qchat-engineering-map.ps1 Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs docs\dataagent\dataagent-v2.16-dataquerygraph-owner-diagnostics.md
git commit -m "Add DataQueryGraph owner diagnostics readiness"
```

Expected: commit succeeds with only the listed files.

## Task 6: Full Verification And Safety Scan

**Files:**
- Read-only verification across the solution.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentAnalysisToolHandlerTests|FullyQualifiedName~DataAgentProgressDiagnosticsPublisherTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentDataQueryGraphPilotTests" -v:minimal
```

Expected: selected DataAgent tests pass with `0 Failed`.

- [ ] **Step 2: Run focused QChat tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: selected QChat tests pass with `0 Failed`.

- [ ] **Step 3: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 85 required passed, 0 required missing
Summary: 60 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run forbidden boundary scans**

Run:

```powershell
rg -n "StateGraph|FastAPI|uvicorn|http://|https://|ProcessStartInfo|Start\\(|python|Python" sources\Alife.Function\Alife.Function.DataAgent sources\Alife.Function\Alife.Function.QChat Tests\Alife.Test.DataAgent Tests\Alife.Test.QChat docs\dataagent\dataagent-v2.16-dataquerygraph-owner-diagnostics.md
```

Expected: no runtime implementation matches. Documentation matches that explain the absence of Python, FastAPI, HTTP, and runtime sidecars are acceptable.

Run:

```powershell
rg -n "DataAgentDataQueryGraphOptions|DataAgentDataQueryGraphPilot|DataAgentDataQueryGraphPlan|DataAgentDataQueryGraphNode|DataAgentDataQueryGraphTransition|DataAgentDataQueryGraphDryRunResult|DataAgentDataQueryGraphTraceFormatter" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches.

- [ ] **Step 5: Run restore, build, and full tests sequentially**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal -m:1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected: restore succeeds, build succeeds with `0 Error(s)`, and full test run succeeds with `0 Failed`.

- [ ] **Step 6: Inspect final branch state**

Run:

```powershell
git status --short --branch
git log -8 --oneline --decorate
```

Expected: worktree is clean after task commits; branch remains ahead of `alife-byastralfox/master`.

## Self-Review

- Spec coverage: tasks cover DataAgent publisher, FunctionCaller bridge, QChat graph diagnostics command, owner-only access, recent diagnostics summary, QChat no-import boundary, readiness gates, engineering-map gates, docs, and full verification.
- Scope check: the plan does not add LangGraph runtime, Python, FastAPI, HTTP calls, sidecar process management, new SQL execution paths, QChat imports of DataQueryGraph types, or QQ ingress changes.
- Type consistency: the plan uses existing V2.15 DataQueryGraph names and adds only local diagnostics names: `DataAgentGraph`, `RecentDataAgentGraph`, `RecentDataAgentGraphDiagnostics`, `RecordRecentDataAgentGraphDiagnostics`, and `DataQueryGraphOwnerDiagnosticsPresent`.
- Verification coverage: focused DataAgent tests, focused QChat tests, readiness scripts, no-runtime scans, QChat no-import scans, restore, build, and full solution tests are included.
