# DataAgent V2.15 DataQueryGraph Pilot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a disabled-by-default C# DataQueryGraph dry-run pilot that proves graph-shaped DataAgent orchestration without adding LangGraph runtime, Python sidecars, HTTP calls, SQL authority, checkpoint authority, QChat coupling, or new execution paths.

**Architecture:** Add focused DataAgent-owned pilot types that map existing orchestration results into scoped graph nodes using `DataAgentToolScopePolicy`. The pilot is an observation and diagnostics boundary only: existing QueryPlan validation, SQL compilation, SQL safety, stores, checkpoints, progress, trace, evidence, Tool Broker route state, and QChat visibility remain authoritative.

**Tech Stack:** .NET 9, C# records/classes, NUnit, existing DataAgent/QChat projects, PowerShell readiness scripts, no new NuGet packages, no Python, no LangGraph runtime.

---

## Scope Boundaries

Implement only the V2.15 C# dry-run pilot.

Allowed:
- DataAgent-owned DataQueryGraph option, node, transition, plan, dry-run result, pilot, and trace formatter types.
- Small extension to `DataAgentToolScopePolicy` for `terminal` and `reject` node scopes.
- Deterministic tests for default disabled behavior, enabled dry-run behavior, node scope, route-denied and terminal graph shapes, SQL-like trace rejection, readiness, QChat boundary guards, and documentation.
- Readiness and engineering-map checks proving the pilot is dry-run only and QChat does not import graph internals.

Forbidden:
- No LangGraph package or runtime.
- No `StateGraph`.
- No Python sidecar directory.
- No FastAPI.
- No HTTP client or server.
- No process manager.
- No new SQL compiler or SQL executor.
- No model-controlled SQL execution.
- No Tool Broker route authority inside DataQueryGraph.
- No checkpoint/session mutation by DataQueryGraph.
- No evidence, audit, progress, trace, diagnostics, visible QChat text, or QQ ingress authority by DataQueryGraph.
- No QChat main-loop changes.

## File Structure

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentDataQueryGraphPilot.cs`
  - Owns all V2.15 pilot models and dry-run logic.
  - Reuses `DataAgentToolScopePolicy.ForNode`.
  - Contains `NoLangGraphRuntimeMarker`, option parsing, graph plan building, fallback results, and SQL-like trace rejection.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolScopePolicy.cs`
  - Adds `Terminal` and `Reject` workflow node names with deterministic no-tool scopes.
- Create `Tests/Alife.Test.DataAgent/DataAgentDataQueryGraphPilotTests.cs`
  - Proves option parsing, disabled result, accepted graph, route-denied graph, terminal graph, scoped capabilities, fallback, and trace formatting.
- Modify `Tests/Alife.Test.DataAgent/DataAgentToolScopePolicyTests.cs`
  - Proves `Terminal` and `Reject` scopes are deterministic and tool-less.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds runtime readiness check `DataQueryGraphPilotPresent`.
- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Raises core readiness count from `69` to `70`.
  - Asserts `DataQueryGraphPilotPresent` detail markers.
  - Raises static script summary from `83` to `84`.
  - Adds a script-contract test for V2.15 markers.
- Create `Tests/Alife.Test.DataAgent/DataAgentV215ReadinessTests.cs`
  - Locks V2.15 runtime and static readiness markers independently.
- Modify `tools/check-dataagent-readiness.ps1`
  - Adds static check `DataQueryGraphPilotPresent`.
  - Raises `$expectedRequired` from `83` to `84`.
- Modify `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
  - Updates static count assertions to DataAgent `84` and QChat `59`.
- Modify `tools/check-qchat-engineering-map.ps1`
  - Adds required check `DataAgent DataQueryGraph pilot`.
  - Adds QChat omit guards for DataQueryGraph pilot types.
  - Raises `$expectedRequired` from `58` to `59`.
- Modify `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Adds the new required V2 check.
  - Adds DataQueryGraph pilot types to QChat direct-import forbidden markers.
  - Raises summary and protected count assertions to `59`.
- Create `docs/dataagent/dataagent-v2.15-dataquerygraph-pilot.md`
  - Documents dry-run-only behavior, default-off flag, authority boundaries, node-scope value, and V2.16 handoff.

## Task 1: Add DataQueryGraph Pilot Core

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentDataQueryGraphPilotTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolScopePolicy.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentToolScopePolicyTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentDataQueryGraphPilot.cs`

- [ ] **Step 1: Write the failing pilot tests**

Create `Tests/Alife.Test.DataAgent/DataAgentDataQueryGraphPilotTests.cs` with this complete content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentDataQueryGraphPilotTests
{
    [Test]
    public void OptionsDefaultToDisabledAndParseOnlyExplicitTrueValues()
    {
        string?[] disabledValues =
        [
            null,
            string.Empty,
            "   ",
            "false",
            "FALSE",
            "0",
            "no",
            "unexpected"
        ];

        string[] enabledValues =
        [
            "true",
            "TRUE",
            "1",
            "yes",
            " YES "
        ];

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentDataQueryGraphOptions.Disabled.Enabled, Is.False);
            Assert.That(
                DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable,
                Is.EqualTo("ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED"));

            foreach (string? value in disabledValues)
                Assert.That(DataAgentDataQueryGraphOptions.FromValue(value).Enabled, Is.False, value ?? "<null>");

            foreach (string value in enabledValues)
                Assert.That(DataAgentDataQueryGraphOptions.FromValue(value).Enabled, Is.True, value);
        });
    }

    [Test]
    [NonParallelizable]
    public void ExplicitEnableOnlyEnablesDryRunAndNoLangGraphRuntime()
    {
        string? previous = Environment.GetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, null);
            DataAgentDataQueryGraphOptions disabled = DataAgentDataQueryGraphOptions.FromEnvironment();

            Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, "true");
            DataAgentDataQueryGraphOptions enabled = DataAgentDataQueryGraphOptions.FromEnvironment();
            DataAgentDataQueryGraphDryRunResult result = new DataAgentDataQueryGraphPilot().DryRun(
                AcceptedResult(),
                enabled);

            Assert.Multiple(() =>
            {
                Assert.That(disabled.Enabled, Is.False);
                Assert.That(enabled.Enabled, Is.True);
                Assert.That(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker, Is.EqualTo("no_langgraph_runtime"));
                Assert.That(result.Enabled, Is.True);
                Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_dry_run_completed"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, previous);
        }
    }

    [Test]
    public void DisabledPilotReturnsDisabledResultWithoutNodes()
    {
        DataAgentDataQueryGraphDryRunResult result = new DataAgentDataQueryGraphPilot().DryRun(
            AcceptedResult(),
            DataAgentDataQueryGraphOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.False);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_disabled"));
            Assert.That(result.Plan.Nodes, Is.Empty);
            Assert.That(result.Plan.Transitions, Is.Empty);
            Assert.That(result.FallbackReason, Is.EqualTo("pilot_disabled"));
        });
    }

    [Test]
    public void EnabledPilotBuildsAcceptedQueryGraph()
    {
        DataAgentDataQueryGraphDryRunResult result = new DataAgentDataQueryGraphPilot().DryRun(
            AcceptedResult(),
            new DataAgentDataQueryGraphOptions(true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_dry_run_completed"));
            Assert.That(result.Plan.Nodes.Select(node => node.NodeName), Is.EqualTo(new[]
            {
                DataAgentWorkflowNodeNames.RouteGate,
                DataAgentWorkflowNodeNames.ScenarioKnowledge,
                DataAgentWorkflowNodeNames.QueryPlanner,
                DataAgentWorkflowNodeNames.QueryPlanValidator,
                DataAgentWorkflowNodeNames.SqlCompiler,
                DataAgentWorkflowNodeNames.SqlSafety,
                DataAgentWorkflowNodeNames.ReadOnlyExecute,
                DataAgentWorkflowNodeNames.ResultExplainer,
                DataAgentWorkflowNodeNames.EvidenceAudit,
                DataAgentWorkflowNodeNames.CheckpointProgress
            }));
            Assert.That(result.Plan.Transitions, Has.Count.EqualTo(result.Plan.Nodes.Count - 1));
            Assert.That(result.Plan.Nodes.Single(node => node.NodeName == DataAgentWorkflowNodeNames.QueryPlanner).AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
            Assert.That(result.Plan.Nodes.Single(node => node.NodeName == DataAgentWorkflowNodeNames.ReadOnlyExecute).AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
            Assert.That(DataAgentDataQueryGraphTraceFormatter.Format(result), Does.Not.Contain("SELECT"));
        });
    }

    [Test]
    public void RouteDeniedGraphDoesNotReachReadOnlyExecute()
    {
        DataAgentDataQueryGraphDryRunResult result = new DataAgentDataQueryGraphPilot().DryRun(
            RouteDeniedResult(),
            new DataAgentDataQueryGraphOptions(true));

        Assert.Multiple(() =>
        {
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_route_rejected"));
            Assert.That(result.Plan.Nodes.Select(node => node.NodeName), Is.EqualTo(new[]
            {
                DataAgentWorkflowNodeNames.RouteGate,
                DataAgentWorkflowNodeNames.Reject,
                DataAgentWorkflowNodeNames.CheckpointProgress
            }));
            Assert.That(result.Plan.Nodes.Select(node => node.NodeName), Does.Not.Contain(DataAgentWorkflowNodeNames.ReadOnlyExecute));
            Assert.That(result.Plan.Nodes.All(node => node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery) == false), Is.True);
        });
    }

    [Test]
    public void TerminalGraphDoesNotIncludeQueryExecution()
    {
        DataAgentDataQueryGraphDryRunResult result = new DataAgentDataQueryGraphPilot().DryRun(
            TerminalResult(),
            new DataAgentDataQueryGraphOptions(true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Plan.Nodes.Select(node => node.NodeName), Is.EqualTo(new[]
            {
                DataAgentWorkflowNodeNames.Terminal,
                DataAgentWorkflowNodeNames.CheckpointProgress
            }));
            Assert.That(result.Plan.Nodes.Select(node => node.NodeName), Does.Not.Contain(DataAgentWorkflowNodeNames.ReadOnlyExecute));
            Assert.That(result.Plan.Nodes.All(node => node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery) == false), Is.True);
        });
    }

    [Test]
    public void UnknownNodeFailsClosedWithNoCapabilitiesAndNoModelCall()
    {
        DataAgentDataQueryGraphNode node = new DataAgentDataQueryGraphPilot().BuildNode("future_unreviewed_node");

        Assert.Multiple(() =>
        {
            Assert.That(node.NodeName, Is.EqualTo("future_unreviewed_node"));
            Assert.That(node.AllowsModelCall, Is.False);
            Assert.That(node.AllowedCapabilities, Is.Empty);
            Assert.That(node.Reason, Is.EqualTo("unknown_node_fail_closed"));
        });
    }

    [Test]
    public void PlannerAndDiagnosticsScopesCannotExecuteReadOnlyQuery()
    {
        DataAgentDataQueryGraphPilot pilot = new();
        DataAgentDataQueryGraphNode planner = pilot.BuildNode(DataAgentWorkflowNodeNames.QueryPlanner);
        DataAgentDataQueryGraphNode diagnostics = pilot.BuildNode(DataAgentWorkflowNodeNames.DiagnosticsRouter);

        Assert.Multiple(() =>
        {
            Assert.That(planner.AllowsModelCall, Is.True);
            Assert.That(planner.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.GenerateQueryPlan));
            Assert.That(planner.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
            Assert.That(diagnostics.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadProgressDiagnostics));
            Assert.That(diagnostics.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadTraceDiagnostics));
            Assert.That(diagnostics.AllowedCapabilities, Does.Contain(DataAgentNodeCapabilities.ReadEvidenceDiagnostics));
            Assert.That(diagnostics.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
        });
    }

    [Test]
    public void NullOrFailedDryRunFallsBackToDeterministicOrchestrator()
    {
        DataAgentDataQueryGraphDryRunResult result = new DataAgentDataQueryGraphPilot().DryRun(
            null,
            new DataAgentDataQueryGraphOptions(true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_fallback_to_deterministic_orchestrator"));
            Assert.That(result.FallbackReason, Is.EqualTo("orchestration_result_missing"));
            Assert.That(result.Plan.Nodes, Is.Empty);
        });
    }

    [Test]
    public void TraceFormatterRejectsSqlLikeTraceFields()
    {
        DataAgentDataQueryGraphDryRunResult safe = new DataAgentDataQueryGraphPilot().DryRun(
            AcceptedResult(),
            new DataAgentDataQueryGraphOptions(true));
        DataAgentDataQueryGraphDryRunResult unsafeTrace = safe with
        {
            ComparedOrchestrationTrace = "SELECT * FROM document_index"
        };

        string formatted = DataAgentDataQueryGraphTraceFormatter.Format(unsafeTrace);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("dataquerygraph_sql_text_rejected"));
            Assert.That(formatted, Does.Not.Contain("SELECT"));
            Assert.That(formatted, Does.Not.Contain("document_index"));
        });
    }

    static DataAgentOrchestrationResult AcceptedResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "planner_response_received", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "result_explained", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            accepted: true,
            terminal: false);
    }

    static DataAgentOrchestrationResult RouteDeniedResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Rejected,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            accepted: false,
            terminal: true);
    }

    static DataAgentOrchestrationResult TerminalResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Summarized,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Summarize, DataAgentOrchestrationStepStatus.Succeeded, "terminal_summary", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            accepted: true,
            terminal: true,
            answer: null,
            intent: DataAgentAnalysisTurnIntent.Summarize,
            includeAnswer: false);
    }

    static DataAgentOrchestrationResult Result(
        DataAgentAnalysisSessionStatus status,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        bool accepted,
        bool terminal,
        DataAgentAnswer? answer = null,
        DataAgentAnalysisTurnIntent intent = DataAgentAnalysisTurnIntent.NewQuestion,
        bool includeAnswer = true)
    {
        DataAgentOrchestrationCheckpoint checkpoint = new(
            "session-1",
            status,
            accepted ? "document_index" : string.Empty,
            accepted ? 1 : 0,
            CanContinue: terminal == false,
            CanSummarize: accepted && terminal == false,
            Terminal: terminal);
        DataAgentAnalysisResponse response = new(
            "session-1",
            status,
            intent,
            includeAnswer ? answer ?? (accepted ? AcceptedAnswer() : null) : null,
            accepted ? "summary" : string.Empty,
            accepted ? "[data_agent_context]\n[/data_agent_context]" : string.Empty,
            accepted,
            accepted ? string.Empty : "tool_route_required");

        return new DataAgentOrchestrationResult(
            "session-1",
            status,
            steps,
            checkpoint,
            response,
            new DataAgentToolRouteContext(true, "dataagent_analysis_start", accepted, accepted, "route-1", "analysis", accepted ? "route_allowed" : "tool_route_required", "session-1"));
    }

    static DataAgentAnswer AcceptedAnswer()
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
            "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
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

- [ ] **Step 2: Run the focused pilot tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentDataQueryGraphPilotTests" -v:minimal
```

Expected: fail to compile because `DataAgentDataQueryGraphOptions`, `DataAgentDataQueryGraphPilot`, `DataAgentDataQueryGraphDryRunResult`, `DataAgentDataQueryGraphTraceFormatter`, `DataAgentWorkflowNodeNames.Terminal`, and `DataAgentWorkflowNodeNames.Reject` do not exist.

- [ ] **Step 3: Extend node scope policy for terminal and reject graph nodes**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolScopePolicy.cs`.

Add these constants inside `DataAgentWorkflowNodeNames`:

```csharp
public const string Terminal = "terminal";
public const string Reject = "reject";
```

Add these scopes to `CreateDefaultScopes()` immediately after the `CheckpointProgress` scope and before `DiagnosticsRouter`:

```csharp
Scope(
    DataAgentWorkflowNodeNames.Terminal,
    false,
    "terminal_node_has_no_query_capabilities"),
Scope(
    DataAgentWorkflowNodeNames.Reject,
    false,
    "reject_node_has_no_query_capabilities"),
```

- [ ] **Step 4: Add policy tests for terminal and reject scopes**

Append this test method to `Tests/Alife.Test.DataAgent/DataAgentToolScopePolicyTests.cs` before `RepeatedPlannerScopesExposeSamePropertyValues`:

```csharp
[Test]
public void TerminalAndRejectScopesAreDeterministicAndToolless()
{
    DataAgentNodeToolScope terminal = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.Terminal);
    DataAgentNodeToolScope reject = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.Reject);

    Assert.Multiple(() =>
    {
        Assert.That(terminal.AllowsModelCall, Is.False);
        Assert.That(terminal.AllowedCapabilities, Is.Empty);
        Assert.That(terminal.Reason, Is.EqualTo("terminal_node_has_no_query_capabilities"));
        Assert.That(reject.AllowsModelCall, Is.False);
        Assert.That(reject.AllowedCapabilities, Is.Empty);
        Assert.That(reject.Reason, Is.EqualTo("reject_node_has_no_query_capabilities"));
    });
}
```

- [ ] **Step 5: Implement the DataQueryGraph pilot**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentDataQueryGraphPilot.cs` with this complete content:

```csharp
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public sealed record DataAgentDataQueryGraphOptions(bool Enabled)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED";

    public static DataAgentDataQueryGraphOptions Disabled { get; } = new(false);

    public static DataAgentDataQueryGraphOptions FromEnvironment()
    {
        return FromValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
    }

    public static DataAgentDataQueryGraphOptions FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Disabled;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => new DataAgentDataQueryGraphOptions(true),
            "false" or "0" or "no" => Disabled,
            _ => Disabled
        };
    }
}

public sealed record DataAgentDataQueryGraphNode(
    string NodeName,
    bool AllowsModelCall,
    IReadOnlyList<string> AllowedCapabilities,
    string Reason);

public sealed record DataAgentDataQueryGraphTransition(
    string FromNode,
    string ToNode,
    string ReasonCode);

public sealed record DataAgentDataQueryGraphPlan(
    IReadOnlyList<DataAgentDataQueryGraphNode> Nodes,
    IReadOnlyList<DataAgentDataQueryGraphTransition> Transitions);

public sealed record DataAgentDataQueryGraphDryRunResult(
    bool Enabled,
    bool Accepted,
    string ReasonCode,
    string WorkflowId,
    string SessionId,
    DataAgentDataQueryGraphPlan Plan,
    string FallbackReason,
    string ComparedOrchestrationTrace);

public sealed class DataAgentDataQueryGraphPilot
{
    public const string NoLangGraphRuntimeMarker = "no_langgraph_runtime";
    public const string DryRunModeMarker = "dry_run";

    static readonly Regex RawSqlMarkerPattern = new(
        @"```sql|\b(select|insert|update|delete|drop|alter|truncate)\b|\bcreate\b|\bwith\s+(?:recursive\s+)?[A-Za-z_][A-Za-z0-9_]*\s+as\s*\(|\bexecute\s+[A-Za-z_][A-Za-z0-9_.]*\b|\bcall\s+[A-Za-z_][A-Za-z0-9_.]*\s*\(|\bmerge\s+into\b|\bgrant\s+[A-Za-z]+\b|\brevoke\s+[A-Za-z]+\b|\bpragma\s+[A-Za-z_][A-Za-z0-9_]*\b|\bbegin(?:\s+(?:transaction|work))?\b|\bcommit\b|\brollback\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly IReadOnlyList<string> AcceptedQueryNodeNames =
    [
        DataAgentWorkflowNodeNames.RouteGate,
        DataAgentWorkflowNodeNames.ScenarioKnowledge,
        DataAgentWorkflowNodeNames.QueryPlanner,
        DataAgentWorkflowNodeNames.QueryPlanValidator,
        DataAgentWorkflowNodeNames.SqlCompiler,
        DataAgentWorkflowNodeNames.SqlSafety,
        DataAgentWorkflowNodeNames.ReadOnlyExecute,
        DataAgentWorkflowNodeNames.ResultExplainer,
        DataAgentWorkflowNodeNames.EvidenceAudit,
        DataAgentWorkflowNodeNames.CheckpointProgress
    ];

    static readonly DataAgentDataQueryGraphPlan EmptyPlan = new([], []);

    public DataAgentDataQueryGraphDryRunResult DryRun(
        DataAgentOrchestrationResult? result,
        DataAgentDataQueryGraphOptions? options = null)
    {
        DataAgentDataQueryGraphOptions resolvedOptions = options ?? DataAgentDataQueryGraphOptions.FromEnvironment();
        if (resolvedOptions.Enabled == false)
            return BuildResult(false, false, "dataquerygraph_disabled", result, EmptyPlan, "pilot_disabled", string.Empty);

        if (result is null)
            return BuildResult(true, false, "dataquerygraph_fallback_to_deterministic_orchestrator", null, EmptyPlan, "orchestration_result_missing", string.Empty);

        try
        {
            IReadOnlyList<string> nodeNames = ResolveNodeNames(result);
            DataAgentDataQueryGraphPlan plan = BuildPlan(nodeNames);
            string comparedTrace = BuildComparedTrace(result);

            if (ContainsRawSqlMarker(comparedTrace))
                return BuildResult(true, false, "dataquerygraph_sql_text_rejected", result, EmptyPlan, "unsafe_compared_trace", string.Empty);

            if (HasScopeMismatch(plan))
                return BuildResult(true, false, "dataquerygraph_scope_mismatch", result, plan, "node_scope_mismatch", comparedTrace);

            string reason = IsRouteRejected(result)
                ? "dataquerygraph_route_rejected"
                : "dataquerygraph_dry_run_completed";

            return BuildResult(true, true, reason, result, plan, string.Empty, comparedTrace);
        }
        catch (Exception ex)
        {
            string reason = string.IsNullOrWhiteSpace(ex.Message)
                ? "dry_run_exception"
                : DataAgentContextFieldSanitizer.Sanitize(ex.Message, 96);

            return BuildResult(true, false, "dataquerygraph_fallback_to_deterministic_orchestrator", result, EmptyPlan, reason, string.Empty);
        }
    }

    public DataAgentDataQueryGraphNode BuildNode(string nodeName)
    {
        DataAgentNodeToolScope scope = DataAgentToolScopePolicy.ForNode(nodeName);
        return new DataAgentDataQueryGraphNode(
            scope.NodeName,
            scope.AllowsModelCall,
            scope.AllowedCapabilities.ToArray(),
            scope.Reason);
    }

    public static bool ContainsRawSqlMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return RawSqlMarkerPattern.IsMatch(value);
    }

    DataAgentDataQueryGraphPlan BuildPlan(IReadOnlyList<string> nodeNames)
    {
        DataAgentDataQueryGraphNode[] nodes = nodeNames.Select(BuildNode).ToArray();
        DataAgentDataQueryGraphTransition[] transitions = nodes
            .Zip(nodes.Skip(1), (from, to) => new DataAgentDataQueryGraphTransition(
                from.NodeName,
                to.NodeName,
                "dataquerygraph_transition"))
            .ToArray();

        return new DataAgentDataQueryGraphPlan(nodes, transitions);
    }

    static IReadOnlyList<string> ResolveNodeNames(DataAgentOrchestrationResult result)
    {
        if (IsRouteRejected(result))
        {
            return
            [
                DataAgentWorkflowNodeNames.RouteGate,
                DataAgentWorkflowNodeNames.Reject,
                DataAgentWorkflowNodeNames.CheckpointProgress
            ];
        }

        if (result.Steps.Any(step => step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End))
        {
            return
            [
                DataAgentWorkflowNodeNames.Terminal,
                DataAgentWorkflowNodeNames.CheckpointProgress
            ];
        }

        if (result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute && step.ExecutedSql))
            return AcceptedQueryNodeNames;

        if (result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Clarification))
        {
            return
            [
                DataAgentWorkflowNodeNames.RouteGate,
                DataAgentWorkflowNodeNames.ScenarioKnowledge,
                DataAgentWorkflowNodeNames.QueryPlanner,
                DataAgentWorkflowNodeNames.QueryPlanValidator,
                DataAgentWorkflowNodeNames.Terminal,
                DataAgentWorkflowNodeNames.CheckpointProgress
            ];
        }

        if (result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Reject))
        {
            return
            [
                DataAgentWorkflowNodeNames.RouteGate,
                DataAgentWorkflowNodeNames.ScenarioKnowledge,
                DataAgentWorkflowNodeNames.QueryPlanner,
                DataAgentWorkflowNodeNames.QueryPlanValidator,
                DataAgentWorkflowNodeNames.Reject,
                DataAgentWorkflowNodeNames.EvidenceAudit,
                DataAgentWorkflowNodeNames.CheckpointProgress
            ];
        }

        return result.Steps
            .Select(MapStep)
            .Where(name => string.IsNullOrWhiteSpace(name) == false)
            .ToArray();
    }

    static string MapStep(DataAgentOrchestrationStep step)
    {
        return step.Node switch
        {
            DataAgentOrchestrationNodeKind.RouteGate => DataAgentWorkflowNodeNames.RouteGate,
            DataAgentOrchestrationNodeKind.SchemaContext => DataAgentWorkflowNodeNames.ScenarioKnowledge,
            DataAgentOrchestrationNodeKind.Plan => DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentOrchestrationNodeKind.Validate => DataAgentWorkflowNodeNames.QueryPlanValidator,
            DataAgentOrchestrationNodeKind.Execute => DataAgentWorkflowNodeNames.ReadOnlyExecute,
            DataAgentOrchestrationNodeKind.Explain => DataAgentWorkflowNodeNames.ResultExplainer,
            DataAgentOrchestrationNodeKind.Clarification => DataAgentWorkflowNodeNames.Terminal,
            DataAgentOrchestrationNodeKind.Summarize => DataAgentWorkflowNodeNames.Terminal,
            DataAgentOrchestrationNodeKind.End => DataAgentWorkflowNodeNames.Terminal,
            DataAgentOrchestrationNodeKind.Reject => DataAgentWorkflowNodeNames.Reject,
            DataAgentOrchestrationNodeKind.Checkpoint => DataAgentWorkflowNodeNames.CheckpointProgress,
            _ => DataAgentWorkflowNodeNames.DiagnosticsRouter
        };
    }

    static bool IsRouteRejected(DataAgentOrchestrationResult result)
    {
        return result.Steps.Any(step =>
            step.Node == DataAgentOrchestrationNodeKind.RouteGate &&
            step.Status == DataAgentOrchestrationStepStatus.Rejected);
    }

    static bool HasScopeMismatch(DataAgentDataQueryGraphPlan plan)
    {
        foreach (DataAgentDataQueryGraphNode node in plan.Nodes)
        {
            bool canExecute = node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal);
            if (canExecute && string.Equals(node.NodeName, DataAgentWorkflowNodeNames.ReadOnlyExecute, StringComparison.Ordinal) == false)
                return true;

            if (node.NodeName is DataAgentWorkflowNodeNames.QueryPlanValidator
                or DataAgentWorkflowNodeNames.SqlCompiler
                or DataAgentWorkflowNodeNames.SqlSafety
                or DataAgentWorkflowNodeNames.ReadOnlyExecute)
            {
                if (node.AllowsModelCall)
                    return true;
            }
        }

        return false;
    }

    static string BuildComparedTrace(DataAgentOrchestrationResult result)
    {
        return string.Join(">", result.Steps.Select(step => $"{step.Node}:{step.Status}"));
    }

    static DataAgentDataQueryGraphDryRunResult BuildResult(
        bool enabled,
        bool accepted,
        string reasonCode,
        DataAgentOrchestrationResult? result,
        DataAgentDataQueryGraphPlan plan,
        string fallbackReason,
        string comparedTrace)
    {
        string sessionId = result?.SessionId ?? string.Empty;
        string workflowId = string.IsNullOrWhiteSpace(sessionId)
            ? "dataquerygraph-pending"
            : $"dataquerygraph-{sessionId}";

        return new DataAgentDataQueryGraphDryRunResult(
            enabled,
            accepted,
            reasonCode,
            workflowId,
            sessionId,
            plan,
            fallbackReason,
            comparedTrace);
    }
}

public static class DataAgentDataQueryGraphTraceFormatter
{
    public static string Format(DataAgentDataQueryGraphDryRunResult? result)
    {
        if (result is null)
            return "DataQueryGraph pilot: enabled=false; accepted=false; reason=dataquerygraph_unavailable";

        string nodeTrace = string.Join(">", result.Plan.Nodes.Select(node => node.NodeName));
        if (DataAgentDataQueryGraphPilot.ContainsRawSqlMarker(result.ReasonCode) ||
            DataAgentDataQueryGraphPilot.ContainsRawSqlMarker(result.FallbackReason) ||
            DataAgentDataQueryGraphPilot.ContainsRawSqlMarker(result.ComparedOrchestrationTrace) ||
            DataAgentDataQueryGraphPilot.ContainsRawSqlMarker(nodeTrace))
        {
            return "DataQueryGraph pilot: enabled=false; accepted=false; reason=dataquerygraph_sql_text_rejected";
        }

        return string.Join(
            "; ",
            "DataQueryGraph pilot",
            $"enabled={LowerBool(result.Enabled)}",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason={SanitizeToken(result.ReasonCode, 128)}",
            $"workflow={SanitizeToken(result.WorkflowId, 128)}",
            $"session={SanitizeToken(result.SessionId, 128)}",
            $"nodes={SanitizeToken(nodeTrace, 1024)}",
            $"fallback={SanitizeToken(result.FallbackReason, 128)}",
            $"compared_trace={SanitizeToken(result.ComparedOrchestrationTrace, 1024)}");
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SanitizeToken(string? value, int maxLength)
    {
        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value ?? string.Empty, maxLength);
        return string.IsNullOrWhiteSpace(sanitized) ? "none" : sanitized;
    }
}
```

- [ ] **Step 6: Run the focused pilot and scope tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentDataQueryGraphPilotTests|FullyQualifiedName~DataAgentToolScopePolicyTests" -v:minimal
```

Expected: all `DataAgentDataQueryGraphPilotTests` and `DataAgentToolScopePolicyTests` pass.

- [ ] **Step 7: Commit Task 1**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentDataQueryGraphPilot.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentToolScopePolicy.cs Tests\Alife.Test.DataAgent\DataAgentDataQueryGraphPilotTests.cs Tests\Alife.Test.DataAgent\DataAgentToolScopePolicyTests.cs
git commit -m "Add DataQueryGraph dry-run pilot"
```

Expected: commit succeeds with only the four listed files.

## Task 2: Add DataAgent Runtime Readiness Gate

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV215ReadinessTests.cs`

- [ ] **Step 1: Write the V2.15 readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV215ReadinessTests.cs` with this complete content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV215ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesV215DataQueryGraphPilotCheck()
    {
        string databasePath = NewDatabasePath();

        IReadOnlyDictionary<string, DataAgentReadinessCheck> checks = DataAgentReadiness
            .CheckCore(databasePath)
            .ToDictionary(check => check.Name, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(checks, Does.ContainKey("DataQueryGraphPilotPresent"));
            Assert.That(checks["DataQueryGraphPilotPresent"].Passed, Is.True, checks["DataQueryGraphPilotPresent"].Detail);
            Assert.That(checks["DataQueryGraphPilotPresent"].Detail, Does.Contain("default_enabled=false"));
            Assert.That(checks["DataQueryGraphPilotPresent"].Detail, Does.Contain("dry_run=true"));
            Assert.That(checks["DataQueryGraphPilotPresent"].Detail, Does.Contain("no_langgraph_runtime=true"));
            Assert.That(checks["DataQueryGraphPilotPresent"].Detail, Does.Contain("node_scope=true"));
            Assert.That(checks["DataQueryGraphPilotPresent"].Detail, Does.Contain("no_sql_authority=true"));
            Assert.That(checks["DataQueryGraphPilotPresent"].Detail, Does.Contain("fallback=true"));
        });
    }

    [Test]
    public void StaticReadinessScriptContainsV215Markers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string dataAgentScript = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string qchatScript = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(dataAgentScript, Does.Contain("DataQueryGraphPilotPresent"));
            Assert.That(dataAgentScript, Does.Contain("DataAgentDataQueryGraphPilot"));
            Assert.That(dataAgentScript, Does.Contain("ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED"));
            Assert.That(dataAgentScript, Does.Contain("no_langgraph_runtime=true"));
            Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 84"));
            Assert.That(qchatScript, Does.Contain("DataAgent DataQueryGraph pilot"));
            Assert.That(qchatScript, Does.Contain("$expectedRequired = 59"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v215-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }
}
```

- [ ] **Step 2: Update `DataAgentReadinessTests` expected runtime count and assertions**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, change:

```csharp
Assert.That(checks, Has.Count.EqualTo(69));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(70));
```

Immediately after the existing `GraphSidecarContractPresent` detail assertions, insert:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataQueryGraphPilotPresent"));
DataAgentReadinessCheck dataQueryGraphCheck = checks.Single(check => check.Name == "DataQueryGraphPilotPresent");
Assert.That(dataQueryGraphCheck.Detail, Does.Contain("default_enabled=false"));
Assert.That(dataQueryGraphCheck.Detail, Does.Contain("dry_run=true"));
Assert.That(dataQueryGraphCheck.Detail, Does.Contain("no_langgraph_runtime=true"));
Assert.That(dataQueryGraphCheck.Detail, Does.Contain("node_scope=true"));
Assert.That(dataQueryGraphCheck.Detail, Does.Contain("no_sql_authority=true"));
Assert.That(dataQueryGraphCheck.Detail, Does.Contain("fallback=true"));
```

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, change:

```csharp
"  Summary: 83 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 84 required passed, 0 required missing"
```

In the same test, add this output assertion after the existing `GraphSidecarContractPresent` assertion:

```csharp
Assert.That(result.StandardOutput, Does.Contain("DataQueryGraphPilotPresent"));
```

Append this test method near `ReadinessScriptProtectsV214GraphSidecarContract`:

```csharp
[Test]
public void ReadinessScriptProtectsV215DataQueryGraphPilot()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "DataQueryGraphPilotPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPilot.cs"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphOptions"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED"));
        Assert.That(declaration, Does.Contain("DataAgentToolScopePolicy.ForNode"));
        Assert.That(declaration, Does.Contain("NoLangGraphRuntimeMarker"));
        Assert.That(declaration, Does.Contain("dataquerygraph_disabled"));
        Assert.That(declaration, Does.Contain("dataquerygraph_fallback_to_deterministic_orchestrator"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPilotTests"));
        Assert.That(declaration, Does.Contain("default_enabled=false"));
        Assert.That(declaration, Does.Contain("dry_run=true"));
        Assert.That(declaration, Does.Contain("no_langgraph_runtime=true"));
        Assert.That(declaration, Does.Contain("node_scope=true"));
        Assert.That(declaration, Does.Contain("no_sql_authority=true"));
        Assert.That(declaration, Does.Contain("fallback=true"));
    });
}
```

- [ ] **Step 3: Run readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV215ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: fail because `DataQueryGraphPilotPresent` has not been added to `DataAgentReadiness.cs` or the static scripts.

- [ ] **Step 4: Add the runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, insert this block immediately after the existing `GraphSidecarContractPresent` check:

```csharp
DataAgentDataQueryGraphOptions dataQueryGraphDefaultOptions = DataAgentDataQueryGraphOptions.FromValue(null);
DataAgentDataQueryGraphPilot dataQueryGraphPilot = new();
DataAgentOrchestrationResult dataQueryGraphReadinessResult = new(
    "dataquerygraph-readiness-session",
    DataAgentAnalysisSessionStatus.Active,
    [
        new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
        new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false),
        new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "planner_response_received", false),
        new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false),
        new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
        new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "result_explained", false),
        new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
    ],
    new DataAgentOrchestrationCheckpoint(
        "dataquerygraph-readiness-session",
        DataAgentAnalysisSessionStatus.Active,
        "document_index",
        1,
        CanContinue: true,
        CanSummarize: true,
        Terminal: false),
    new DataAgentAnalysisResponse(
        "dataquerygraph-readiness-session",
        DataAgentAnalysisSessionStatus.Active,
        DataAgentAnalysisTurnIntent.NewQuestion,
        null,
        "readiness summary",
        string.Empty,
        true,
        string.Empty),
    new DataAgentToolRouteContext(
        true,
        "dataagent_analysis_start",
        true,
        true,
        "dataquerygraph-route",
        "analysis_start",
        "route_allowed",
        "dataquerygraph-readiness-session"));
DataAgentDataQueryGraphDryRunResult dataQueryGraphDisabledResult = dataQueryGraphPilot.DryRun(
    dataQueryGraphReadinessResult,
    DataAgentDataQueryGraphOptions.Disabled);
DataAgentDataQueryGraphDryRunResult dataQueryGraphEnabledResult = dataQueryGraphPilot.DryRun(
    dataQueryGraphReadinessResult,
    new DataAgentDataQueryGraphOptions(true));
DataAgentDataQueryGraphDryRunResult dataQueryGraphFallbackResult = dataQueryGraphPilot.DryRun(
    null,
    new DataAgentDataQueryGraphOptions(true));
DataAgentDataQueryGraphNode dataQueryGraphDiagnosticsNode = dataQueryGraphPilot.BuildNode(
    DataAgentWorkflowNodeNames.DiagnosticsRouter);
DataAgentDataQueryGraphNode dataQueryGraphUnknownNode = dataQueryGraphPilot.BuildNode(
    "future_unreviewed_node");
string dataQueryGraphUnsafeTrace = DataAgentDataQueryGraphTraceFormatter.Format(
    dataQueryGraphEnabledResult with { ComparedOrchestrationTrace = "SELECT * FROM document_index" });
bool dataQueryGraphDryRunReady =
    dataQueryGraphEnabledResult.Enabled &&
    dataQueryGraphEnabledResult.Accepted &&
    dataQueryGraphEnabledResult.ReasonCode == "dataquerygraph_dry_run_completed";
bool dataQueryGraphNodeScopeReady =
    dataQueryGraphEnabledResult.Plan.Nodes.Any(node =>
        node.NodeName == DataAgentWorkflowNodeNames.QueryPlanner &&
        node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.GenerateQueryPlan, StringComparer.Ordinal) &&
        node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false) &&
    dataQueryGraphDiagnosticsNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ReadProgressDiagnostics, StringComparer.Ordinal) &&
    dataQueryGraphDiagnosticsNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ReadTraceDiagnostics, StringComparer.Ordinal) &&
    dataQueryGraphDiagnosticsNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ReadEvidenceDiagnostics, StringComparer.Ordinal) &&
    dataQueryGraphDiagnosticsNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false &&
    dataQueryGraphUnknownNode.AllowsModelCall == false &&
    dataQueryGraphUnknownNode.AllowedCapabilities.Count == 0;
bool dataQueryGraphNoSqlAuthority =
    dataQueryGraphUnsafeTrace.Contains("dataquerygraph_sql_text_rejected", StringComparison.Ordinal) &&
    dataQueryGraphUnsafeTrace.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
    dataQueryGraphUnsafeTrace.Contains("document_index", StringComparison.OrdinalIgnoreCase) == false;
bool dataQueryGraphFallbackReady =
    dataQueryGraphFallbackResult.ReasonCode == "dataquerygraph_fallback_to_deterministic_orchestrator";
bool dataQueryGraphPilotReady =
    dataQueryGraphDefaultOptions.Enabled == false &&
    dataQueryGraphDisabledResult.Enabled == false &&
    dataQueryGraphDisabledResult.ReasonCode == "dataquerygraph_disabled" &&
    dataQueryGraphDryRunReady &&
    DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker == "no_langgraph_runtime" &&
    dataQueryGraphNodeScopeReady &&
    dataQueryGraphNoSqlAuthority &&
    dataQueryGraphFallbackReady;
checks.Add(dataQueryGraphPilotReady
    ? Pass("DataQueryGraphPilotPresent", "default_enabled=false;dry_run=true;no_langgraph_runtime=true;node_scope=true;no_sql_authority=true;fallback=true")
    : Fail("DataQueryGraphPilotPresent", $"default_enabled={LowerBool(dataQueryGraphDefaultOptions.Enabled)};dry_run={LowerBool(dataQueryGraphDryRunReady)};no_langgraph_runtime={LowerBool(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker == "no_langgraph_runtime")};node_scope={LowerBool(dataQueryGraphNodeScopeReady)};no_sql_authority={LowerBool(dataQueryGraphNoSqlAuthority)};fallback={LowerBool(dataQueryGraphFallbackReady)}"));
```

- [ ] **Step 5: Run focused readiness tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV215ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: runtime readiness checks pass, static script assertions still fail until Task 3 updates the scripts.

- [ ] **Step 6: Commit Task 2 runtime readiness changes**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV215ReadinessTests.cs
git commit -m "Add DataQueryGraph runtime readiness gate"
```

Expected: commit succeeds with only the three listed files.

## Task 3: Add Static Readiness And QChat Boundary Gates

**Files:**
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Update DataAgent static readiness script**

In `tools/check-dataagent-readiness.ps1`, add this check immediately after the `GraphSidecarContractPresent` check:

```powershell
New-Check -Group "Store" -Name "DataQueryGraphPilotPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentDataQueryGraphPilot.cs" @("DataAgentDataQueryGraphOptions", "ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED", "DataAgentDataQueryGraphPilot", "NoLangGraphRuntimeMarker", "DataAgentToolScopePolicy.ForNode", "dataquerygraph_disabled", "dataquerygraph_fallback_to_deterministic_orchestrator")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentDataQueryGraphPilotTests.cs" @("DataAgentDataQueryGraphPilotTests", "OptionsDefaultToDisabledAndParseOnlyExplicitTrueValues", "EnabledPilotBuildsAcceptedQueryGraph", "RouteDeniedGraphDoesNotReachReadOnlyExecute", "TraceFormatterRejectsSqlLikeTraceFields")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataQueryGraphPilotPresent", "default_enabled=false", "dry_run=true", "no_langgraph_runtime=true", "node_scope=true", "no_sql_authority=true", "fallback=true"))) -Detail "V2.15 disabled DataQueryGraph dry-run pilot markers"
```

Change:

```powershell
$expectedRequired = 83
```

to:

```powershell
$expectedRequired = 84
```

- [ ] **Step 2: Update QChat engineering map script**

In `tools/check-qchat-engineering-map.ps1`, add this check immediately after `DataAgent graph sidecar contract`:

```powershell
Add-Check -Group "Harness" -Name "DataAgent DataQueryGraph pilot" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataQueryGraphPilotPresent", "DataAgentDataQueryGraphPilot", "ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED", "no_langgraph_runtime=true", "node_scope=true", "no_sql_authority=true") -AlsoPath "Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs" -AlsoPatterns @("QChatDoesNotDirectlyImportDataAgentBoundaryTypes", "DataAgentDataQueryGraphOptions", "DataAgentDataQueryGraphPilot", "DataAgentDataQueryGraphPlan", "DataAgentDataQueryGraphNode", "DataAgentDataQueryGraphTransition", "DataAgentDataQueryGraphDryRunResult", "DataAgentDataQueryGraphTraceFormatter") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentDataQueryGraphOptions", "DataAgentDataQueryGraphPilot", "DataAgentDataQueryGraphPlan", "DataAgentDataQueryGraphNode", "DataAgentDataQueryGraphTransition", "DataAgentDataQueryGraphDryRunResult", "DataAgentDataQueryGraphTraceFormatter")
```

Change:

```powershell
$expectedRequired = 58
```

to:

```powershell
$expectedRequired = 59
```

- [ ] **Step 3: Update cross-version static count assertions**

In `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`, change:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 83"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 58"));
```

to:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 84"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 59"));
```

- [ ] **Step 4: Update QChat engineering-map tests**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add this string to `RequiredV2Checks` immediately after `"DataAgent graph sidecar contract"`:

```csharp
"DataAgent DataQueryGraph pilot",
```

Append this test method immediately after `GraphSidecarContractCheckRequiresDataAgentRuntimeAndQChatBoundary`:

```csharp
[Test]
public void DataQueryGraphPilotCheckRequiresDataAgentRuntimeAndQChatBoundary()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindAddCheckDeclaration(script, "DataAgent DataQueryGraph pilot");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataQueryGraphPilotPresent"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPilot"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED"));
        Assert.That(declaration, Does.Contain("no_langgraph_runtime=true"));
        Assert.That(declaration, Does.Contain("node_scope=true"));
        Assert.That(declaration, Does.Contain("no_sql_authority=true"));
        Assert.That(declaration, Does.Contain("QChatDoesNotDirectlyImportDataAgentBoundaryTypes"));
        Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphOptions"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPilot"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPlan"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphNode"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphTransition"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphDryRunResult"));
        Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphTraceFormatter"));
    });
}
```

In `QChatDoesNotDirectlyImportDataAgentBoundaryTypes`, add these forbidden markers after the existing graph sidecar markers:

```csharp
"DataAgentDataQueryGraphOptions",
"DataAgentDataQueryGraphPilot",
"DataAgentDataQueryGraphPlan",
"DataAgentDataQueryGraphNode",
"DataAgentDataQueryGraphTransition",
"DataAgentDataQueryGraphDryRunResult",
"DataAgentDataQueryGraphTraceFormatter"
```

In `QChatEngineeringMapDefaultModeExitsZeroAndPrintsSummary`, change:

```csharp
"Summary: 58 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

to:

```csharp
"Summary: 59 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

In `QChatEngineeringMapScriptProtectsRequiredCheckCount`, change:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 58"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 59"));
```

- [ ] **Step 5: Run static readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 84 required passed, 0 required missing
Summary: 59 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 6: Run focused readiness and QChat engineering-map tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV215ReadinessTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit Task 3**

Run:

```powershell
git add tools\check-dataagent-readiness.ps1 tools\check-qchat-engineering-map.ps1 Tests\Alife.Test.DataAgent\DataAgentV210ReadinessTests.cs Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Add DataQueryGraph readiness and QChat gates"
```

Expected: commit succeeds with only the four listed files.

## Task 4: Add V2.15 Developer Documentation

**Files:**
- Create: `docs/dataagent/dataagent-v2.15-dataquerygraph-pilot.md`

- [ ] **Step 1: Create the developer note**

Create `docs/dataagent/dataagent-v2.15-dataquerygraph-pilot.md` with this complete content:

```markdown
# DataAgent V2.15 DataQueryGraph Pilot

V2.15 adds a disabled-by-default C# DataQueryGraph dry-run pilot. It does not add LangGraph runtime behavior, Python sidecar code, FastAPI, HTTP calls, a sidecar process, or a new SQL execution path.

## Default State

The feature flag is:

```text
ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED=false
```

Missing, blank, `false`, `0`, and `no` values are disabled. Explicit `true`, `1`, and `yes` values enable only the C# dry-run pilot.

## What The Pilot Does

The pilot maps existing DataAgent workflow boundaries into graph-shaped nodes:

- `route_gate`
- `scenario_knowledge`
- `query_planner`
- `query_plan_validator`
- `sql_compiler`
- `sql_safety`
- `read_only_execute`
- `result_explainer`
- `evidence_audit`
- `checkpoint_progress`
- `diagnostics_router`
- `terminal`
- `reject`

Each node uses `DataAgentToolScopePolicy` to expose a small scoped capability list. This reduces future tool-selection ambiguity by keeping planner, deterministic safety, read-only execution, diagnostics, terminal, and reject responsibilities separate.

## Authority Boundary

DataQueryGraph may describe expected node order, scoped capabilities, transitions, bounded traces, and deterministic fallback reasons.

DataQueryGraph cannot authorize datasets, fields, operators, limits, executable SQL, SQL execution, Tool Broker route state, checkpoint mutation, query audit, Tool Broker audit, evidence packs, progress events, trace timelines, diagnostics, QChat visible text, or QQ ingress.

The C# DataAgent pipeline remains authoritative:

- `DataAgentScenarioContextBuilder` provides hint-only scenario context.
- `IDataAgentQueryPlanner` proposes a QueryPlan or clarification.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles read-only parameterized SQL.
- `DataAgentSqlSafetyValidator` rejects unsafe SQL shapes.
- `IDataAgentStore` executes read-only queries and records query/audit state.
- `IDataAgentAnalysisSessionStore` persists analysis session/checkpoint state.
- Tool Broker route state decides whether DataAgent tools can be used in the current QChat turn.

## QChat Boundary

QChat remains the interaction surface. It may consume existing DataAgent analysis context, progress diagnostics, trace diagnostics, evidence diagnostics, and Tool Broker route diagnostics.

QChat does not import DataQueryGraph pilot types and does not own DataAgent graph internals.

## V2.16 Handoff

V2.16 should use the V2.15 pilot results before deciding whether a real LangGraph/Python sidecar is worth the added runtime dependency. A future sidecar must pass the V2.14 sidecar contract and the V2.15 node-scope boundary.
```

- [ ] **Step 2: Run a documentation marker check**

Run:

```powershell
rg -n "V2.15|ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED|no LangGraph|Authority Boundary|QChat Boundary" docs\dataagent\dataagent-v2.15-dataquerygraph-pilot.md
```

Expected: matches for the V2.15 title, feature flag, authority section, and QChat boundary section.

- [ ] **Step 3: Commit Task 4**

Run:

```powershell
git add docs\dataagent\dataagent-v2.15-dataquerygraph-pilot.md
git commit -m "Document DataQueryGraph dry-run pilot"
```

Expected: commit succeeds with only the documentation file.

## Task 5: Full Verification And Final Safety Scan

**Files:**
- Read-only verification across the solution.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentDataQueryGraphPilotTests|FullyQualifiedName~DataAgentToolScopePolicyTests|FullyQualifiedName~DataAgentV215ReadinessTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests" -v:minimal
```

Expected: selected DataAgent tests pass with `0 Failed`.

- [ ] **Step 2: Run focused QChat engineering-map tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
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
Summary: 84 required passed, 0 required missing
Summary: 59 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run forbidden-runtime scans**

Run:

```powershell
rg -n "LangGraph|StateGraph|FastAPI|uvicorn|http://|https://|ProcessStartInfo|Start\\(|Python|python" sources\Alife.Function\Alife.Function.DataAgent Tests\Alife.Test.DataAgent docs\dataagent\dataagent-v2.15-dataquerygraph-pilot.md
```

Expected: no runtime implementation matches. Documentation matches that explain the absence of LangGraph, Python, FastAPI, HTTP, and sidecar runtime are acceptable.

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

Expected: restore succeeds, build succeeds with `0 Error(s)`, full test run succeeds with `0 Failed`.

- [ ] **Step 6: Inspect final diff and status**

Run:

```powershell
git status --short --branch
git log -8 --oneline --decorate
```

Expected: branch is ahead of `alife-byastralfox/master`; worktree is clean after all task commits.

## Self-Review

- Spec coverage: the plan covers disabled-by-default options, dry-run pilot, node scope, ToolScopePolicy reuse, fallback behavior, SQL-like trace rejection, readiness gates, QChat no-import gates, developer docs, and full verification.
- Scope check: the plan does not add LangGraph runtime, Python, FastAPI, HTTP, sidecar process management, new SQL execution paths, QChat main-loop changes, QQ ingress changes, or visible-text authority.
- Type consistency: all DataQueryGraph type names match the approved spec and are used consistently across tests, implementation, readiness, QChat boundary checks, and docs.
- Verification coverage: focused tests, readiness scripts, forbidden-runtime scans, restore, build, and full solution tests are included.
