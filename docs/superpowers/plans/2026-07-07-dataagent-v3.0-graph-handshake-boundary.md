# DataAgent V3.0 Graph Handshake Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a disabled-by-default C# graph handshake boundary that can accept or reject LangGraph-shaped orchestration suggestions while preserving the existing deterministic DataAgent execution path.

**Architecture:** Add graph handshake models, scoped node manifests, a sidecar-client interface, a validator, a coordinator, and owner diagnostics. Keep C# authoritative for QueryPlan safety, SQL validation, execution, Tool Broker policy, checkpoints, QChat ingress, and plugin services. The default implementation never starts Python, never calls HTTP, and always falls back safely when the sidecar is disabled or invalid.

**Tech Stack:** .NET 9, C#, NUnit, existing DataAgent analysis/orchestration services, existing FunctionCaller/QChat diagnostics bridge, PowerShell readiness scripts.

---

## Scope Check

This plan implements one subsystem: the V3.0 DataAgent graph handshake boundary. It does not add a production LangGraph runtime, Python process, FastAPI service, HTTP transport, streaming sidecar protocol, human-in-the-loop interrupts, checkpointer reconciliation, or direct sidecar tool execution.

## File Structure

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs`
  - Defines options, status enums, node manifests, request/response records, validation result, and outcome models.

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeManifestFactory.cs`
  - Builds scoped node manifests from the existing DataAgent node vocabulary without exposing SQL execution authority.

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeValidator.cs`
  - Treats sidecar responses as untrusted input and rejects authority overreach, unknown nodes/tools, unsafe trace text, checkpoint mutation, visible text, and schema mismatch.

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
  - Builds requests from deterministic orchestration results, calls an injected sidecar client only when enabled, validates responses, and returns accepted/rejected/fallback outcomes.

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatter.cs`
  - Formats owner-visible bounded diagnostics for disabled, accepted, rejected, unavailable, timeout, and invalid sidecar outcomes.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
  - Publishes graph handshake diagnostics through the existing DataAgent graph diagnostics channel without changing user-visible result context.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
  - Passes the graph handshake coordinator into the analysis handler without exposing QChat-specific dependencies.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Wires the default disabled handshake coordinator and graph diagnostics publisher.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds a V3.0 readiness check proving default-disabled, no-SQL-authority, scoped-manifest, fallback, and validator behavior.

- Modify: `tools/check-dataagent-readiness.ps1`
  - Adds static readiness markers for the V3.0 graph handshake boundary and increments `$expectedRequired` from `85` to `86`.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Updates summary counts and adds static readiness assertions.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
  - Updates the shared DataAgent readiness expected count to `86`.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
  - Updates the shared DataAgent readiness expected count to `86`.

- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Extends QChat boundary markers so QChat production source must not import `DataAgentGraphHandshake*` types.

- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeContractTests.cs`
  - Tests options, manifest scoping, request shape, and validator fail-closed behavior.

- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`
  - Tests disabled, accepted, rejected, unavailable, invalid response, and deterministic result preservation.

- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatterTests.cs`
  - Tests diagnostics formatting and SQL-like text redaction/rejection.

- Create: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
  - Tests the dynamic core readiness check and static script markers for V3.0.

- Create: `docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md`
  - Developer note explaining V3.0 scope, disabled default, no SQL authority, scoped node manifests, and V3.x handoff.

---

### Task 1: Add Graph Handshake Contract And Scoped Manifests

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeManifestFactory.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeContractTests.cs`

- [ ] **Step 1: Write the failing contract tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeContractTests.cs` with this content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeContractTests
{
    [Test]
    public void OptionsDefaultDisabledAndParseOnlyExplicitTrueLikeValues()
    {
        string?[] disabledValues = [null, "", "   ", "false", "FALSE", "0", "no", "unexpected"];
        string[] enabledValues = ["true", "TRUE", "1", "yes", " YES "];

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeOptions.Disabled.Enabled, Is.False);
            Assert.That(DataAgentGraphHandshakeOptions.EnabledEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED"));

            foreach (string? value in disabledValues)
                Assert.That(DataAgentGraphHandshakeOptions.FromValue(value).Enabled, Is.False, $"Expected disabled for '{value}'.");

            foreach (string value in enabledValues)
                Assert.That(DataAgentGraphHandshakeOptions.FromValue(value).Enabled, Is.True, $"Expected enabled for '{value}'.");
        });
    }

    [Test]
    public void DefaultManifestScopesNodeCapabilitiesAndNeverExposesSqlExecution()
    {
        IReadOnlyList<DataAgentGraphNodeManifest> manifests = DataAgentGraphHandshakeManifestFactory.CreateDefault();

        DataAgentGraphNodeManifest queryPlanner = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.QueryPlanner);
        DataAgentGraphNodeManifest sqlSafety = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.SqlSafety);
        DataAgentGraphNodeManifest readOnlyExecute = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.ReadOnlyExecute);
        DataAgentGraphNodeManifest diagnostics = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.DiagnosticsRouter);

        Assert.Multiple(() =>
        {
            Assert.That(manifests.Select(manifest => manifest.NodeName), Does.Contain(DataAgentWorkflowNodeNames.ScenarioKnowledge));
            Assert.That(queryPlanner.AllowedToolNames, Does.Contain(DataAgentGraphHandshakeToolNames.ProposeQueryPlan));
            Assert.That(queryPlanner.AllowedToolNames, Does.Not.Contain(DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery));
            Assert.That(sqlSafety.AllowedToolNames, Does.Contain(DataAgentGraphHandshakeToolNames.ReadSqlSafetyStatus));
            Assert.That(readOnlyExecute.AllowedToolNames, Is.Empty);
            Assert.That(readOnlyExecute.DeniedCapabilityMarkers, Does.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
            Assert.That(diagnostics.AllowedToolNames, Does.Contain(DataAgentGraphHandshakeToolNames.ReadProgressDiagnostics));
            Assert.That(manifests.SelectMany(manifest => manifest.AllowedToolNames), Does.Not.Contain(DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery));
        });
    }

    [Test]
    public void BuildRequestDefaultsToReadOnlyNoSqlAuthorityAndFallbackAvailable()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();

        Assert.Multiple(() =>
        {
            Assert.That(request.NoSqlAuthority, Is.True);
            Assert.That(request.ReadOnly, Is.True);
            Assert.That(request.FallbackAvailable, Is.True);
            Assert.That(request.NodeManifests, Is.Not.Empty);
            Assert.That(request.TraceBudgetChars, Is.EqualTo(DataAgentGraphHandshakeLimits.MaxTraceSummaryChars));
            Assert.That(request.ProgressBudget, Is.EqualTo(DataAgentGraphHandshakeLimits.MaxProgressEvents));
        });
    }

    [Test]
    public void ValidatorAcceptsSafeResponseAndRejectsAuthorityOverreach()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse safe = NewResponse(request);
        DataAgentGraphHandshakeResponse sqlAuthority = safe with { NoSqlAuthority = false };
        DataAgentGraphHandshakeResponse unknownNode = safe with { SelectedNodes = ["unknown_node"] };
        DataAgentGraphHandshakeResponse unknownTool = safe with { RequestedToolNames = ["browser.open"] };
        DataAgentGraphHandshakeResponse checkpointMutation = safe with { RequestsCheckpointMutation = true };
        DataAgentGraphHandshakeResponse visibleText = safe with { RequestsVisibleText = true };
        DataAgentGraphHandshakeResponse sqlTrace = safe with { TraceSummary = "SELECT * FROM document_index" };
        DataAgentGraphHandshakeResponse wrongRequest = safe with { RequestId = "wrong-request" };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, safe).Accepted, Is.True);
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, sqlAuthority).ReasonCode, Is.EqualTo("sql_authority_requested"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unknownNode).ReasonCode, Is.EqualTo("unknown_node"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unknownTool).ReasonCode, Is.EqualTo("unknown_tool"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, checkpointMutation).ReasonCode, Is.EqualTo("checkpoint_mutation_requested"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, visibleText).ReasonCode, Is.EqualTo("visible_text_requested"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, sqlTrace).ReasonCode, Is.EqualTo("unsafe_trace"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, wrongRequest).ReasonCode, Is.EqualTo("request_id_mismatch"));
        });
    }

    [Test]
    public void ValidatorRejectsInvalidProgressAndOverBudgetTrace()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse safe = NewResponse(request);
        DataAgentGraphHandshakeResponse unknownProgressNode = safe with
        {
            NodeProgress = [new DataAgentGraphHandshakeProgress("unknown_node", DataAgentGraphHandshakeProgressStatus.Completed, "done")]
        };
        DataAgentGraphHandshakeResponse tooManyProgressEvents = safe with
        {
            NodeProgress = Enumerable.Range(0, DataAgentGraphHandshakeLimits.MaxProgressEvents + 1)
                .Select(index => new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, $"p{index}"))
                .ToArray()
        };
        DataAgentGraphHandshakeResponse overBudgetTrace = safe with
        {
            TraceSummary = new string('x', DataAgentGraphHandshakeLimits.MaxTraceSummaryChars + 1)
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unknownProgressNode).ReasonCode, Is.EqualTo("progress_invalid"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, tooManyProgressEvents).ReasonCode, Is.EqualTo("progress_invalid"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, overBudgetTrace).ReasonCode, Is.EqualTo("unsafe_trace"));
        });
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "request-1",
            "session-1",
            "turn-1",
            "owner",
            "Which required gates failed?",
            "scenario_context=true",
            "route_allowed",
            "dataset=engineering_gate;limit<=50",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphHandshakeResponse NewResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            request.RequestId,
            Accepted: true,
            ReasonCode: "handshake_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress:
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentGraphHandshakeProgressStatus.Completed, "scenario_context_ready"),
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            TraceSummary: "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }
}
```

- [ ] **Step 2: Run the contract tests and verify they fail before implementation**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeContractTests" -v:minimal
```

Expected: FAIL at compile time because `DataAgentGraphHandshakeOptions`, `DataAgentGraphHandshakeRequest`, `DataAgentGraphHandshakeResponse`, `DataAgentGraphHandshakeValidator`, and manifest types do not exist.

- [ ] **Step 3: Add the handshake model file**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs` with this content:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeOptions(bool Enabled)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED";

    public static DataAgentGraphHandshakeOptions Disabled { get; } = new(false);

    public static DataAgentGraphHandshakeOptions FromEnvironment()
    {
        return FromValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
    }

    public static DataAgentGraphHandshakeOptions FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Disabled;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => new DataAgentGraphHandshakeOptions(true),
            "false" or "0" or "no" => Disabled,
            _ => Disabled
        };
    }
}

public static class DataAgentGraphHandshakeLimits
{
    public const int MaxRequestIdLength = 128;
    public const int MaxSessionIdLength = 128;
    public const int MaxTurnIdLength = 128;
    public const int MaxCallerIdLength = 128;
    public const int MaxQuestionLength = 2048;
    public const int MaxScenarioContextLength = 4096;
    public const int MaxRouteScopeLength = 512;
    public const int MaxQueryConstraintsLength = 1024;
    public const int MaxNodeManifests = 16;
    public const int MaxToolNamesPerNode = 8;
    public const int MaxDeniedMarkersPerNode = 16;
    public const int MaxTraceSummaryChars = 1800;
    public const int MaxContextContributionChars = 1200;
    public const int MaxProgressEvents = 16;
    public const int MaxReasonCodeLength = 128;
}

public enum DataAgentGraphHandshakeStatus
{
    Disabled,
    Accepted,
    Rejected,
    Unavailable,
    Timeout,
    Invalid
}

public enum DataAgentGraphHandshakeProgressStatus
{
    Started,
    Completed,
    Skipped,
    Rejected,
    Failed
}

public static class DataAgentGraphHandshakeToolNames
{
    public const string ReadScenarioContext = "dataagent.scenario_context.read";
    public const string ReadRouteScope = "dataagent.route_scope.read";
    public const string ProposeQueryPlan = "dataagent.query_plan.propose";
    public const string ReadQueryPlanValidationStatus = "dataagent.query_plan.validation_status.read";
    public const string ReadSqlSafetyStatus = "dataagent.sql_safety.status.read";
    public const string InterpretControlledResult = "dataagent.result.interpret_controlled";
    public const string ReadEvidenceDiagnostics = "dataagent.diagnostics.evidence.read";
    public const string ReadTraceDiagnostics = "dataagent.diagnostics.trace.read";
    public const string ReadProgressDiagnostics = "dataagent.diagnostics.progress.read";
    public const string ExecuteReadOnlyQuery = "dataagent.query.execute_readonly";
}

public sealed record DataAgentGraphNodeManifest(
    string NodeName,
    string Purpose,
    IReadOnlyList<string> AllowedToolNames,
    IReadOnlyList<string> DeniedCapabilityMarkers,
    string InputShape,
    string OutputShape,
    IReadOnlyList<string> BusinessTerms,
    string SafetyNotes);

public sealed record DataAgentGraphHandshakeRequest(
    string RequestId,
    string SessionId,
    string TurnId,
    string CallerId,
    string GoalOrQuestion,
    string ScenarioContextSummary,
    string RouteScope,
    string QueryConstraints,
    IReadOnlyList<DataAgentGraphNodeManifest> NodeManifests,
    bool NoSqlAuthority,
    bool ReadOnly,
    bool FallbackAvailable,
    int TraceBudgetChars,
    int ProgressBudget);

public sealed record DataAgentGraphHandshakeProgress(
    string NodeName,
    DataAgentGraphHandshakeProgressStatus Status,
    string ReasonCode);

public sealed record DataAgentGraphHandshakeResponse(
    string RequestId,
    bool Accepted,
    string ReasonCode,
    IReadOnlyList<string> SelectedNodes,
    IReadOnlyList<DataAgentGraphHandshakeProgress> NodeProgress,
    string TraceSummary,
    string ContextContribution,
    bool FallbackRequired,
    bool NoSqlAuthority,
    bool ReadOnly,
    IReadOnlyList<string> RequestedToolNames,
    bool RequestsCheckpointMutation,
    bool RequestsVisibleText);

public sealed record DataAgentGraphHandshakeValidationResult(
    bool Accepted,
    string ReasonCode);

public sealed record DataAgentGraphHandshakeOutcome(
    DataAgentGraphHandshakeStatus Status,
    string ReasonCode,
    bool FallbackRequired,
    DataAgentGraphHandshakeRequest? Request,
    DataAgentGraphHandshakeResponse? Response,
    DataAgentGraphHandshakeValidationResult Validation);
```

- [ ] **Step 4: Add the scoped manifest factory**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeManifestFactory.cs` with this content:

```csharp
namespace Alife.Function.DataAgent;

public static class DataAgentGraphHandshakeManifestFactory
{
    public static IReadOnlyList<DataAgentGraphNodeManifest> CreateDefault()
    {
        return
        [
            Manifest(
                DataAgentWorkflowNodeNames.ScenarioKnowledge,
                "Map deterministic scenario knowledge and business vocabulary.",
                [DataAgentGraphHandshakeToolNames.ReadScenarioContext],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.CompileSql],
                "goal_or_question + scenario pack summary",
                "business_terms + dataset vocabulary",
                ["dataset", "field", "operator", "limit"],
                "No SQL generation or execution."),
            Manifest(
                DataAgentWorkflowNodeNames.RouteGate,
                "Read current route scope and explain permission state.",
                [DataAgentGraphHandshakeToolNames.ReadRouteScope],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.WriteAudit],
                "Tool Broker route state",
                "allow_or_deny + reason_code",
                ["owner", "private", "route"],
                "Cannot decide Tool Broker route; C# policy remains authority."),
            Manifest(
                DataAgentWorkflowNodeNames.QueryPlanner,
                "Suggest a QueryPlan-shaped intent candidate.",
                [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.CompileSql],
                "goal_or_question + allowed dataset vocabulary",
                "query_plan_candidate",
                ["dataset", "field", "filter", "sort", "limit"],
                "Candidate only; C# validator decides."),
            Manifest(
                DataAgentWorkflowNodeNames.QueryPlanValidator,
                "Read validation status for C# QueryPlan checks.",
                [DataAgentGraphHandshakeToolNames.ReadQueryPlanValidationStatus],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "query_plan_candidate",
                "validation_status_summary",
                ["field", "operator", "limit"],
                "Cannot override validator."),
            Manifest(
                DataAgentWorkflowNodeNames.SqlSafety,
                "Read SQL safety status produced by C#.",
                [DataAgentGraphHandshakeToolNames.ReadSqlSafetyStatus],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.CompileSql],
                "compiled_sql_status",
                "safe_or_rejected_summary",
                ["read_only", "parameterized", "single_statement"],
                "Cannot see executable SQL text."),
            Manifest(
                DataAgentWorkflowNodeNames.ReadOnlyExecute,
                "Represent the C# read-only execution boundary.",
                [],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery],
                "validated_query_plan",
                "execution_boundary_status",
                ["read_only"],
                "Sidecar cannot execute or request execution."),
            Manifest(
                DataAgentWorkflowNodeNames.ResultExplainer,
                "Interpret already controlled result state.",
                [DataAgentGraphHandshakeToolNames.InterpretControlledResult],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "controlled_result_summary",
                "result_interpretation_summary",
                ["result_summary", "evidence"],
                "Cannot fetch new data."),
            Manifest(
                DataAgentWorkflowNodeNames.DiagnosticsRouter,
                "Summarize owner diagnostics availability.",
                [
                    DataAgentGraphHandshakeToolNames.ReadEvidenceDiagnostics,
                    DataAgentGraphHandshakeToolNames.ReadTraceDiagnostics,
                    DataAgentGraphHandshakeToolNames.ReadProgressDiagnostics
                ],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.WriteAudit],
                "diagnostics_state",
                "diagnostics_summary",
                ["evidence", "trace", "progress"],
                "Diagnostics only; no hidden context leakage."),
            Manifest(
                DataAgentWorkflowNodeNames.CheckpointProgress,
                "Represent C# checkpoint and progress ownership.",
                [],
                [DataAgentNodeCapabilities.PublishProgress, DataAgentGraphSidecarAuthority.MutateCheckpoint.ToString()],
                "checkpoint_status",
                "checkpoint_boundary_status",
                ["checkpoint", "progress"],
                "Sidecar cannot mutate checkpoints or publish progress."),
            Manifest(
                DataAgentWorkflowNodeNames.Terminal,
                "Represent summarize/end terminal states.",
                [],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "terminal_request",
                "terminal_status",
                ["summary", "end"],
                "No query execution."),
            Manifest(
                DataAgentWorkflowNodeNames.Reject,
                "Represent fail-closed rejection states.",
                [],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "rejection_reason",
                "fallback_required",
                ["reason_code"],
                "No query execution.")
        ];
    }

    static DataAgentGraphNodeManifest Manifest(
        string nodeName,
        string purpose,
        IReadOnlyList<string> allowedToolNames,
        IReadOnlyList<string> deniedCapabilityMarkers,
        string inputShape,
        string outputShape,
        IReadOnlyList<string> businessTerms,
        string safetyNotes)
    {
        return new DataAgentGraphNodeManifest(
            nodeName,
            purpose,
            Array.AsReadOnly(allowedToolNames.ToArray()),
            Array.AsReadOnly(deniedCapabilityMarkers.ToArray()),
            inputShape,
            outputShape,
            Array.AsReadOnly(businessTerms.ToArray()),
            safetyNotes);
    }
}
```

- [ ] **Step 5: Add a temporary validator stub so the contract test compiles**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeValidator.cs` with this temporary content:

```csharp
namespace Alife.Function.DataAgent;

public static class DataAgentGraphHandshakeValidator
{
    public static DataAgentGraphHandshakeValidationResult Validate(
        DataAgentGraphHandshakeRequest? request,
        DataAgentGraphHandshakeResponse? response)
    {
        return new DataAgentGraphHandshakeValidationResult(false, "validator_not_implemented");
    }
}
```

- [ ] **Step 6: Run the contract tests and verify the stub fails behaviorally**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeContractTests" -v:minimal
```

Expected: FAIL because `ValidatorAcceptsSafeResponseAndRejectsAuthorityOverreach` expects a safe response to be accepted, but the temporary validator returns `validator_not_implemented`.

- [ ] **Step 7: Keep the red contract baseline uncommitted**

Run:

```powershell
git status --short
```

Expected: the new contract, manifest factory, temporary validator stub, and contract tests are listed as working tree changes. Do not commit the temporary failing validator. Continue directly to Task 2 and make the first V3.0 implementation commit after the validator passes.

---

### Task 2: Implement Handshake Validator

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeValidator.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeContractTests.cs`

- [ ] **Step 1: Replace the temporary validator with the full validator**

Replace `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeValidator.cs` with this content:

```csharp
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentGraphHandshakeValidator
{
    static readonly Regex RawSqlPattern = new(
        @"```sql|\b(select|insert|update|delete|drop|alter|truncate)\b|\bcreate\b|\bwith\s+(?:recursive\s+)?[A-Za-z_][A-Za-z0-9_]*\s+as\s*\(|\bexecute\s+[A-Za-z_][A-Za-z0-9_.]*\b|\bcall\s+[A-Za-z_][A-Za-z0-9_.]*\s*\(|\bmerge\s+into\b|\bgrant\s+[A-Za-z]+\b|\brevoke\s+[A-Za-z]+\b|\bpragma\s+[A-Za-z_][A-Za-z0-9_]*\b|\bbegin(?:\s+(?:transaction|work))?\b|\bcommit\b|\brollback\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly string[] ForbiddenToolMarkers =
    [
        "qchat",
        "qq",
        "browser",
        "desktop",
        "voice",
        "tts",
        "file",
        "rag.manage",
        "checkpoint.write"
    ];

    public static DataAgentGraphHandshakeValidationResult Validate(
        DataAgentGraphHandshakeRequest? request,
        DataAgentGraphHandshakeResponse? response)
    {
        if (request is null || response is null)
            return Reject("invalid_response_schema");

        if (IsValidRequest(request) == false)
            return Reject("invalid_request_schema");

        if (string.Equals(request.RequestId, response.RequestId, StringComparison.Ordinal) == false)
            return Reject("request_id_mismatch");

        if (response.NoSqlAuthority == false || response.ReadOnly == false)
            return Reject("sql_authority_requested");

        if (response.RequestsCheckpointMutation)
            return Reject("checkpoint_mutation_requested");

        if (response.RequestsVisibleText)
            return Reject("visible_text_requested");

        if (HasBoundedText(response.ReasonCode, DataAgentGraphHandshakeLimits.MaxReasonCodeLength) == false)
            return Reject("invalid_response_schema");

        if (response.TraceSummary.Length > request.TraceBudgetChars ||
            response.TraceSummary.Length > DataAgentGraphHandshakeLimits.MaxTraceSummaryChars ||
            response.ContextContribution.Length > DataAgentGraphHandshakeLimits.MaxContextContributionChars ||
            ContainsRawSql(response.TraceSummary) ||
            ContainsRawSql(response.ContextContribution))
        {
            return Reject("unsafe_trace");
        }

        HashSet<string> manifestNodeNames = request.NodeManifests
            .Select(manifest => manifest.NodeName)
            .ToHashSet(StringComparer.Ordinal);
        if (response.SelectedNodes is null ||
            response.SelectedNodes.Count == 0 ||
            response.SelectedNodes.Any(node => manifestNodeNames.Contains(node) == false))
        {
            return Reject("unknown_node");
        }

        if (IsProgressValid(request, response.NodeProgress, manifestNodeNames) == false)
            return Reject("progress_invalid");

        HashSet<string> allowedToolNames = request.NodeManifests
            .SelectMany(manifest => manifest.AllowedToolNames)
            .ToHashSet(StringComparer.Ordinal);
        if (response.RequestedToolNames is null ||
            response.RequestedToolNames.Any(tool => IsAllowedTool(tool, allowedToolNames) == false))
        {
            return Reject("unknown_tool");
        }

        return new DataAgentGraphHandshakeValidationResult(response.Accepted, response.Accepted ? "handshake_accepted" : response.ReasonCode);
    }

    static bool IsValidRequest(DataAgentGraphHandshakeRequest request)
    {
        return HasBoundedText(request.RequestId, DataAgentGraphHandshakeLimits.MaxRequestIdLength) &&
               HasBoundedText(request.SessionId, DataAgentGraphHandshakeLimits.MaxSessionIdLength) &&
               HasBoundedText(request.TurnId, DataAgentGraphHandshakeLimits.MaxTurnIdLength) &&
               HasBoundedText(request.CallerId, DataAgentGraphHandshakeLimits.MaxCallerIdLength) &&
               HasBoundedText(request.GoalOrQuestion, DataAgentGraphHandshakeLimits.MaxQuestionLength) &&
               HasBoundedText(request.ScenarioContextSummary, DataAgentGraphHandshakeLimits.MaxScenarioContextLength) &&
               HasBoundedText(request.RouteScope, DataAgentGraphHandshakeLimits.MaxRouteScopeLength) &&
               HasBoundedText(request.QueryConstraints, DataAgentGraphHandshakeLimits.MaxQueryConstraintsLength) &&
               request.NoSqlAuthority &&
               request.ReadOnly &&
               request.FallbackAvailable &&
               request.TraceBudgetChars is > 0 and <= DataAgentGraphHandshakeLimits.MaxTraceSummaryChars &&
               request.ProgressBudget is > 0 and <= DataAgentGraphHandshakeLimits.MaxProgressEvents &&
               request.NodeManifests is { Count: > 0 } &&
               request.NodeManifests.Count <= DataAgentGraphHandshakeLimits.MaxNodeManifests &&
               request.NodeManifests.All(IsManifestSafe);
    }

    static bool IsManifestSafe(DataAgentGraphNodeManifest manifest)
    {
        return HasBoundedText(manifest.NodeName, 128) &&
               HasBoundedText(manifest.Purpose, 512) &&
               HasBoundedText(manifest.InputShape, 256) &&
               HasBoundedText(manifest.OutputShape, 256) &&
               HasBoundedText(manifest.SafetyNotes, 512) &&
               manifest.AllowedToolNames.Count <= DataAgentGraphHandshakeLimits.MaxToolNamesPerNode &&
               manifest.DeniedCapabilityMarkers.Count <= DataAgentGraphHandshakeLimits.MaxDeniedMarkersPerNode &&
               manifest.AllowedToolNames.All(tool => IsForbiddenToolName(tool) == false);
    }

    static bool IsProgressValid(
        DataAgentGraphHandshakeRequest request,
        IReadOnlyList<DataAgentGraphHandshakeProgress>? progress,
        HashSet<string> manifestNodeNames)
    {
        if (progress is null ||
            progress.Count > request.ProgressBudget ||
            progress.Count > DataAgentGraphHandshakeLimits.MaxProgressEvents)
        {
            return false;
        }

        return progress.All(item =>
            manifestNodeNames.Contains(item.NodeName) &&
            HasBoundedText(item.ReasonCode, DataAgentGraphHandshakeLimits.MaxReasonCodeLength));
    }

    static bool IsAllowedTool(string? toolName, HashSet<string> allowedToolNames)
    {
        return HasBoundedText(toolName, 128) &&
               IsForbiddenToolName(toolName) == false &&
               allowedToolNames.Contains(toolName!);
    }

    static bool IsForbiddenToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return true;

        return ForbiddenToolMarkers.Any(marker =>
            toolName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static bool HasBoundedText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               value.Length <= maxLength;
    }

    static bool ContainsRawSql(string? value)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               RawSqlPattern.IsMatch(value);
    }

    static DataAgentGraphHandshakeValidationResult Reject(string reasonCode)
    {
        return new DataAgentGraphHandshakeValidationResult(false, reasonCode);
    }
}
```

- [ ] **Step 2: Run the contract tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeContractTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 3: Commit Task 1 and Task 2 together**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeModels.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeManifestFactory.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeValidator.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeContractTests.cs
git commit -m "Add DataAgent graph handshake contract and validator"
```

---

### Task 3: Add Disabled Sidecar Client And Handshake Coordinator

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`

- [ ] **Step 1: Write the failing coordinator tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs` with this content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeCoordinatorTests
{
    [Test]
    public void DisabledCoordinatorReturnsFallbackWithoutCallingSidecar()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(
            DataAgentGraphHandshakeOptions.Disabled,
            sidecar);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Disabled));
            Assert.That(outcome.ReasonCode, Is.EqualTo("sidecar_disabled"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(sidecar.Requests, Is.Empty);
        });
    }

    [Test]
    public void EnabledCoordinatorAcceptsSafeSidecarResponseWithoutChangingDeterministicResult()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);
        DataAgentOrchestrationResult deterministicResult = AcceptedResult();

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            deterministicResult);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(outcome.FallbackRequired, Is.False);
            Assert.That(outcome.Request?.NoSqlAuthority, Is.True);
            Assert.That(outcome.Response?.ContextContribution, Does.Contain("graph_handshake=accepted"));
            Assert.That(sidecar.Requests, Has.Count.EqualTo(1));
            Assert.That(deterministicResult.Response.Accepted, Is.True);
            Assert.That(deterministicResult.Steps.Any(step => step.ExecutedSql), Is.True);
        });
    }

    [Test]
    public void EnabledCoordinatorRejectsUnsafeResponseAndRequiresFallback()
    {
        RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
        {
            NoSqlAuthority = false,
            TraceSummary = "SELECT * FROM document_index"
        });
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("sql_authority_requested"));
            Assert.That(outcome.FallbackRequired, Is.True);
        });
    }

    [Test]
    public void EnabledCoordinatorHandlesUnavailableAndTimeoutSidecarWithoutThrowing()
    {
        DataAgentGraphHandshakeCoordinator unavailable = new(
            new DataAgentGraphHandshakeOptions(true),
            new ThrowingSidecarClient(new InvalidOperationException("sidecar offline")));
        DataAgentGraphHandshakeCoordinator timeout = new(
            new DataAgentGraphHandshakeOptions(true),
            new ThrowingSidecarClient(new TimeoutException("sidecar timeout")));

        DataAgentGraphHandshakeOutcome unavailableOutcome = unavailable.TryHandshake("owner", "question", AcceptedResult());
        DataAgentGraphHandshakeOutcome timeoutOutcome = timeout.TryHandshake("owner", "question", AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(unavailableOutcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Unavailable));
            Assert.That(unavailableOutcome.ReasonCode, Is.EqualTo("sidecar_unavailable"));
            Assert.That(unavailableOutcome.FallbackRequired, Is.True);
            Assert.That(timeoutOutcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Timeout));
            Assert.That(timeoutOutcome.ReasonCode, Is.EqualTo("sidecar_timeout"));
            Assert.That(timeoutOutcome.FallbackRequired, Is.True);
        });
    }

    static DataAgentGraphHandshakeResponse NewAcceptedResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            request.RequestId,
            true,
            "handshake_accepted",
            [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            [new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")],
            "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            "graph_handshake=accepted",
            false,
            true,
            true,
            [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            false,
            false);
    }

    static DataAgentOrchestrationResult AcceptedResult()
    {
        DataAgentAnalysisResponse response = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            null,
            "summary",
            "context",
            true,
            string.Empty);

        return new DataAgentOrchestrationResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "engineering_gate", 1, true, false, false),
            response,
            new DataAgentToolRouteContext(true, "dataagent_analysis_start", true, true, "route-1", "analysis_start", "route_allowed", string.Empty));
    }

    sealed class RecordingSidecarClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeResponse> responseFactory)
        : IDataAgentGraphSidecarClient
    {
        public List<DataAgentGraphHandshakeRequest> Requests { get; } = [];

        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
        {
            Requests.Add(request);
            return responseFactory(request);
        }
    }

    sealed class ThrowingSidecarClient(Exception exception) : IDataAgentGraphSidecarClient
    {
        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
        {
            throw exception;
        }
    }
}
```

- [ ] **Step 2: Run coordinator tests and verify they fail before implementation**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests" -v:minimal
```

Expected: FAIL at compile time because `IDataAgentGraphSidecarClient` and `DataAgentGraphHandshakeCoordinator` do not exist.

- [ ] **Step 3: Add the coordinator**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs` with this content:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentGraphSidecarClient
{
    DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request);
}

public sealed class DisabledDataAgentGraphSidecarClient : IDataAgentGraphSidecarClient
{
    public static DisabledDataAgentGraphSidecarClient Instance { get; } = new();

    DisabledDataAgentGraphSidecarClient()
    {
    }

    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
    {
        throw new InvalidOperationException("sidecar_disabled");
    }
}

public sealed class DataAgentGraphHandshakeCoordinator(
    DataAgentGraphHandshakeOptions options,
    IDataAgentGraphSidecarClient? sidecarClient = null)
{
    readonly IDataAgentGraphSidecarClient sidecarClient = sidecarClient ?? DisabledDataAgentGraphSidecarClient.Instance;

    public DataAgentGraphHandshakeOutcome TryHandshake(
        string callerId,
        string goalOrQuestion,
        DataAgentOrchestrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string normalizedCaller = string.IsNullOrWhiteSpace(callerId) ? "local" : callerId.Trim();
        string normalizedQuestion = string.IsNullOrWhiteSpace(goalOrQuestion) ? "dataagent_analysis" : goalOrQuestion.Trim();
        DataAgentGraphHandshakeRequest request = BuildRequest(normalizedCaller, normalizedQuestion, result);

        if (options.Enabled == false)
            return Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", true, request, null);

        try
        {
            DataAgentGraphHandshakeResponse response = sidecarClient.TryHandshake(request);
            DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, response);
            if (validation.Accepted == false)
                return Outcome(DataAgentGraphHandshakeStatus.Rejected, validation.ReasonCode, true, request, response, validation);

            return Outcome(DataAgentGraphHandshakeStatus.Accepted, validation.ReasonCode, response.FallbackRequired, request, response, validation);
        }
        catch (TimeoutException)
        {
            return Outcome(DataAgentGraphHandshakeStatus.Timeout, "sidecar_timeout", true, request, null);
        }
        catch (Exception)
        {
            return Outcome(DataAgentGraphHandshakeStatus.Unavailable, "sidecar_unavailable", true, request, null);
        }
    }

    static DataAgentGraphHandshakeRequest BuildRequest(
        string callerId,
        string goalOrQuestion,
        DataAgentOrchestrationResult result)
    {
        string turnId = result.Checkpoint.TurnCount <= 0
            ? "turn-0"
            : $"turn-{result.Checkpoint.TurnCount}";
        string routeScope = result.RouteContext is null
            ? "route_present=false"
            : $"route_present=true;route_allows_query={LowerBool(result.RouteContext.AllowsQuery)};route_reason_code={result.RouteContext.ReasonCode}";
        string constraints = $"status={result.SessionStatus};executed_sql={LowerBool(result.Steps.Any(step => step.ExecutedSql))};terminal={LowerBool(result.Checkpoint.Terminal)}";

        return new DataAgentGraphHandshakeRequest(
            $"graph-handshake-{result.SessionId}-{turnId}",
            string.IsNullOrWhiteSpace(result.SessionId) ? "pending" : result.SessionId,
            turnId,
            callerId,
            Bound(goalOrQuestion, DataAgentGraphHandshakeLimits.MaxQuestionLength),
            "scenario_context=deterministic_csharp",
            Bound(routeScope, DataAgentGraphHandshakeLimits.MaxRouteScopeLength),
            Bound(constraints, DataAgentGraphHandshakeLimits.MaxQueryConstraintsLength),
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphHandshakeOutcome Outcome(
        DataAgentGraphHandshakeStatus status,
        string reasonCode,
        bool fallbackRequired,
        DataAgentGraphHandshakeRequest request,
        DataAgentGraphHandshakeResponse? response,
        DataAgentGraphHandshakeValidationResult? validation = null)
    {
        return new DataAgentGraphHandshakeOutcome(
            status,
            reasonCode,
            fallbackRequired,
            request,
            response,
            validation ?? new DataAgentGraphHandshakeValidationResult(false, reasonCode));
    }

    static string Bound(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    static string LowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}
```

- [ ] **Step 4: Run coordinator and contract tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentGraphHandshakeContractTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 5: Commit Task 3**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeCoordinator.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs
git commit -m "Add disabled DataAgent graph handshake coordinator"
```

---

### Task 4: Publish Owner Diagnostics Without Changing DataAgent Results

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatter.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatterTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`

- [ ] **Step 1: Write diagnostics formatter tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatterTests.cs` with this content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeDiagnosticsFormatterTests
{
    [Test]
    public void FormatDisabledOutcomeEmitsFallbackAndNoSqlAuthority()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Disabled,
            "sidecar_disabled",
            true,
            request,
            null,
            new DataAgentGraphHandshakeValidationResult(false, "sidecar_disabled"));

        string text = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("DataAgent graph handshake"));
            Assert.That(text, Does.Contain("status=disabled"));
            Assert.That(text, Does.Contain("reason=sidecar_disabled"));
            Assert.That(text, Does.Contain("fallback_required=true"));
            Assert.That(text, Does.Contain("no_sql_authority=true"));
            Assert.That(text, Does.Contain("scoped_node_manifest=true"));
            Assert.That(text, Does.Contain("runtime_required=false"));
        });
    }

    [Test]
    public void FormatAcceptedOutcomeEmitsSelectedNodesAndBoundsTrace()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse response = NewResponse(request);
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Accepted,
            "handshake_accepted",
            false,
            request,
            response,
            new DataAgentGraphHandshakeValidationResult(true, "handshake_accepted"));

        string text = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome, maxChars: 600);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("status=accepted"));
            Assert.That(text, Does.Contain("selected_nodes=scenario_knowledge,query_planner"));
            Assert.That(text, Does.Contain("progress=query_planner:Completed:planner_suggested"));
            Assert.That(text, Does.Contain("trace=ScenarioKnowledge:Completed>QueryPlanner:Completed"));
            Assert.That(text.Length, Is.LessThanOrEqualTo(600));
        });
    }

    [Test]
    public void FormatRejectedOutcomeRedactsSqlLikeTrace()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse response = NewResponse(request) with
        {
            TraceSummary = "SELECT * FROM document_index",
            ContextContribution = "DROP TABLE document_index"
        };
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Rejected,
            "unsafe_trace",
            true,
            request,
            response,
            new DataAgentGraphHandshakeValidationResult(false, "unsafe_trace"));

        string text = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("status=rejected"));
            Assert.That(text, Does.Contain("reason=unsafe_trace"));
            Assert.That(text, Does.Contain("trace=redacted"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("DROP TABLE"));
        });
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "request-1",
            "session-1",
            "turn-1",
            "owner",
            "question",
            "scenario_context=true",
            "route_allowed",
            "dataset=engineering_gate",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            true,
            true,
            true,
            DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphHandshakeResponse NewResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            request.RequestId,
            true,
            "handshake_accepted",
            [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            [new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")],
            "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            "graph_handshake=accepted",
            false,
            true,
            true,
            [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            false,
            false);
    }
}
```

- [ ] **Step 2: Run formatter tests and verify they fail before implementation**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDiagnosticsFormatterTests" -v:minimal
```

Expected: FAIL at compile time because `DataAgentGraphHandshakeDiagnosticsFormatter` does not exist.

- [ ] **Step 3: Add the diagnostics formatter**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatter.cs` with this content:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentGraphHandshakeDiagnosticsFormatter
{
    static readonly Regex RawSqlPattern = new(
        @"```sql|\b(select|insert|update|delete|drop|alter|truncate)\b|\bcreate\b|\bwith\s+(?:recursive\s+)?[A-Za-z_][A-Za-z0-9_]*\s+as\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Format(DataAgentGraphHandshakeOutcome? outcome, int maxChars = 1800)
    {
        if (outcome is null)
            return "DataAgent graph handshake\nstatus=unavailable\nreason=handshake_unavailable\nfallback_required=true\nruntime_required=false";

        StringBuilder builder = new();
        builder.AppendLine("DataAgent graph handshake");
        builder.AppendLine($"status={FormatStatus(outcome.Status)}");
        builder.AppendLine($"reason={SafeToken(outcome.ReasonCode)}");
        builder.AppendLine($"fallback_required={LowerBool(outcome.FallbackRequired)}");
        builder.AppendLine("no_sql_authority=true");
        builder.AppendLine("read_only=true");
        builder.AppendLine("scoped_node_manifest=true");
        builder.AppendLine("runtime_required=false");

        if (outcome.Response is not null)
        {
            builder.AppendLine($"selected_nodes={string.Join(",", outcome.Response.SelectedNodes.Select(SafeToken))}");
            builder.AppendLine($"progress={FormatProgress(outcome.Response.NodeProgress)}");
            builder.AppendLine($"trace={SafeDiagnosticText(outcome.Response.TraceSummary)}");
            builder.AppendLine($"context={SafeDiagnosticText(outcome.Response.ContextContribution)}");
        }

        return Bound(builder.ToString().TrimEnd(), maxChars);
    }

    static string FormatProgress(IReadOnlyList<DataAgentGraphHandshakeProgress> progress)
    {
        if (progress.Count == 0)
            return "none";

        return string.Join(",", progress.Select(item =>
            $"{SafeToken(item.NodeName)}:{item.Status}:{SafeToken(item.ReasonCode)}"));
    }

    static string SafeDiagnosticText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty";

        if (RawSqlPattern.IsMatch(value))
            return "redacted";

        return value.ReplaceLineEndings(" ").Trim();
    }

    static string SafeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty";

        return value.ReplaceLineEndings("_").Replace(' ', '_').Trim();
    }

    static string FormatStatus(DataAgentGraphHandshakeStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    static string LowerBool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Bound(string value, int maxChars)
    {
        if (maxChars <= 0 || value.Length <= maxChars)
            return value;

        return value[..maxChars];
    }
}
```

- [ ] **Step 4: Run formatter tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDiagnosticsFormatterTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 5: Add handler publishing test**

In `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`, add this test near `StartPublishesDataQueryGraphDiagnosticsForAcceptedQuery` or near other graph diagnostics tests:

```csharp
    [Test]
    public void StartPublishesGraphHandshakeDiagnosticsWithoutChangingContext()
    {
        List<string> graphDiagnostics = [];
        RecordingOrchestrator orchestrator = CreateOrchestrator();
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-1",
            "analysis_start",
            "route_allowed",
            string.Empty));
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            routeContextAccessor: routeAccessor,
            dataQueryGraphDiagnosticsPublisher: graphDiagnostics.Add,
            graphHandshakeCoordinator: new DataAgentGraphHandshakeCoordinator(
                DataAgentGraphHandshakeOptions.Disabled,
                DisabledDataAgentGraphSidecarClient.Instance));

        string context = handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(graphDiagnostics, Has.Count.EqualTo(1));
            Assert.That(graphDiagnostics.Single(), Does.Contain("DataQueryGraph dry-run"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("DataAgent graph handshake"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("reason=sidecar_disabled"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("no_sql_authority=true"));
            Assert.That(graphDiagnostics.Single(), Does.Not.Contain("SELECT"));
        });
    }
```

- [ ] **Step 6: Run the handler test and verify it fails before wiring**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~StartPublishesGraphHandshakeDiagnosticsWithoutChangingContext" -v:minimal
```

Expected: FAIL at compile time because `DataAgentAnalysisToolHandler` does not accept `graphHandshakeCoordinator`.

- [ ] **Step 7: Wire handler diagnostics**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`, extend the primary constructor by adding this parameter after `Action<string>? dataQueryGraphDiagnosticsPublisher = null`:

```csharp
    DataAgentGraphHandshakeCoordinator? graphHandshakeCoordinator = null)
```

Add this field near the existing readonly fields:

```csharp
    readonly DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator =
        graphHandshakeCoordinator ?? new DataAgentGraphHandshakeCoordinator(DataAgentGraphHandshakeOptions.Disabled);
```

Change the four `PublishResult` calls to pass caller and question:

```csharp
        PublishResult(result, context, callerId, goalOrQuestion);
```

```csharp
        PublishResult(result, context, "local", question);
```

```csharp
        PublishResult(result, context, "local", "summarize");
```

```csharp
        PublishResult(result, context, "local", "end");
```

Change the method signature:

```csharp
    void PublishResult(DataAgentOrchestrationResult result, string context, string callerId, string goalOrQuestion)
```

Change the call to graph diagnostics:

```csharp
        PublishDataQueryGraphDiagnostics(result, callerId, goalOrQuestion);
```

Replace `PublishDataQueryGraphDiagnostics` with:

```csharp
    void PublishDataQueryGraphDiagnostics(
        DataAgentOrchestrationResult result,
        string callerId,
        string goalOrQuestion)
    {
        if (dataQueryGraphDiagnosticsPublisher is null)
            return;

        string dataQueryGraphDiagnostics;
        try
        {
            DataAgentDataQueryGraphDryRunResult graphResult = DataAgentDataQueryGraphPilot.DryRun(result);
            dataQueryGraphDiagnostics = DataAgentDataQueryGraphTraceFormatter.Format(graphResult);
        }
        catch (Exception)
        {
            dataQueryGraphDiagnostics = DataAgentDataQueryGraphTraceFormatter.Format(null);
        }

        DataAgentGraphHandshakeOutcome handshakeOutcome = graphHandshakeCoordinator.TryHandshake(
            callerId,
            goalOrQuestion,
            result);
        string handshakeDiagnostics = DataAgentGraphHandshakeDiagnosticsFormatter.Format(handshakeOutcome);
        dataQueryGraphDiagnosticsPublisher($"{dataQueryGraphDiagnostics}{Environment.NewLine}{handshakeDiagnostics}");
    }
```

- [ ] **Step 8: Wire module default coordinator**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`, before creating `DataAgentCapabilityRegistry`, add:

```csharp
        DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
            DataAgentGraphHandshakeOptions.FromEnvironment(),
            DisabledDataAgentGraphSidecarClient.Instance);
```

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`, extend the primary constructor by adding this parameter after `Action<string>? dataQueryGraphDiagnosticsPublisher = null`:

```csharp
    DataAgentGraphHandshakeCoordinator? graphHandshakeCoordinator = null) : IDataAgentCapabilityProvider
```

The `DataAgentAnalysisCapabilityProvider` handler construction should end with:

```csharp
            traceRecorder,
            dataQueryGraphDiagnosticsPublisher: dataQueryGraphDiagnosticsPublisher,
            graphHandshakeCoordinator: graphHandshakeCoordinator)));
```

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`, update the `DataAgentAnalysisCapabilityProvider` registration to end with:

```csharp
            traceRecorder,
            functionService.RecordRecentDataAgentGraphDiagnostics,
            graphHandshakeCoordinator));
```

- [ ] **Step 9: Run focused diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~StartPublishesGraphHandshakeDiagnosticsWithoutChangingContext|FullyQualifiedName~DataAgentGraphHandshakeDiagnosticsFormatterTests|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentGraphHandshakeContractTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 10: Commit Task 4**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeDiagnosticsFormatter.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentAnalysisToolHandler.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentAnalysisCapabilityProvider.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDiagnosticsFormatterTests.cs Tests\Alife.Test.DataAgent\DataAgentAnalysisToolHandlerTests.cs
git commit -m "Publish DataAgent graph handshake diagnostics"
```

---

### Task 5: Add V3.0 Readiness And QChat Boundary Gates

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
- Modify: `tools/check-qchat-engineering-map.ps1`

- [ ] **Step 1: Write V3.0 readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs` with this content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV30ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesGraphHandshakeBoundary()
    {
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(NewDatabasePath());
        DataAgentReadinessCheck check = checks.Single(item => item.Name == "GraphHandshakeBoundaryPresent");

        Assert.Multiple(() =>
        {
            Assert.That(check.Passed, Is.True, check.Detail);
            Assert.That(check.Detail, Does.Contain("default_enabled=false"));
            Assert.That(check.Detail, Does.Contain("validator=true"));
            Assert.That(check.Detail, Does.Contain("no_sql_authority=true"));
            Assert.That(check.Detail, Does.Contain("scoped_node_manifest=true"));
            Assert.That(check.Detail, Does.Contain("fallback=true"));
            Assert.That(check.Detail, Does.Contain("runtime_required=false"));
        });
    }

    [Test]
    public void StaticReadinessScriptContainsV30Markers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("GraphHandshakeBoundaryPresent"));
            Assert.That(script, Does.Contain("DataAgentGraphHandshakeCoordinator"));
            Assert.That(script, Does.Contain("DataAgentGraphHandshakeValidator"));
            Assert.That(script, Does.Contain("DataAgentGraphHandshakeManifestFactory"));
            Assert.That(script, Does.Contain("default_enabled=false"));
            Assert.That(script, Does.Contain("no_sql_authority=true"));
            Assert.That(script, Does.Contain("scoped_node_manifest=true"));
            Assert.That(script, Does.Contain("fallback=true"));
            Assert.That(script, Does.Contain("$expectedRequired = 86"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v30-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run readiness tests and verify they fail before readiness wiring**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
```

Expected: FAIL because `GraphHandshakeBoundaryPresent` is not added to `DataAgentReadiness` or static readiness script.

- [ ] **Step 3: Add dynamic readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after the existing `DataQueryGraphPilotPresent` check, add:

```csharp
            DataAgentGraphHandshakeOptions graphHandshakeDefaultOptions = DataAgentGraphHandshakeOptions.FromValue(null);
            IReadOnlyList<DataAgentGraphNodeManifest> graphHandshakeManifests = DataAgentGraphHandshakeManifestFactory.CreateDefault();
            DataAgentGraphHandshakeRequest graphHandshakeRequest = new(
                "readiness-request",
                "readiness-session",
                "turn-1",
                "owner",
                "Which required gates failed?",
                "scenario_context=true",
                "route_allowed",
                "dataset=engineering_gate;limit<=50",
                graphHandshakeManifests,
                true,
                true,
                true,
                DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
                DataAgentGraphHandshakeLimits.MaxProgressEvents);
            DataAgentGraphHandshakeResponse graphHandshakeSafeResponse = new(
                graphHandshakeRequest.RequestId,
                true,
                "handshake_accepted",
                [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
                [new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")],
                "ScenarioKnowledge:Completed>QueryPlanner:Completed",
                "graph_handshake=accepted",
                false,
                true,
                true,
                [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
                false,
                false);
            DataAgentGraphHandshakeResponse graphHandshakeUnsafeResponse = graphHandshakeSafeResponse with
            {
                NoSqlAuthority = false,
                TraceSummary = "SELECT * FROM document_index"
            };
            DataAgentGraphHandshakeValidationResult graphHandshakeSafeValidation =
                DataAgentGraphHandshakeValidator.Validate(graphHandshakeRequest, graphHandshakeSafeResponse);
            DataAgentGraphHandshakeValidationResult graphHandshakeUnsafeValidation =
                DataAgentGraphHandshakeValidator.Validate(graphHandshakeRequest, graphHandshakeUnsafeResponse);
            DataAgentGraphHandshakeCoordinator graphHandshakeDisabledCoordinator = new(
                DataAgentGraphHandshakeOptions.Disabled,
                DisabledDataAgentGraphSidecarClient.Instance);
            DataAgentGraphHandshakeOutcome graphHandshakeDisabledOutcome =
                graphHandshakeDisabledCoordinator.TryHandshake("owner", "Which required gates failed?", CreateReadinessDataQueryGraphAcceptedResult());
            bool graphHandshakeNoSqlAuthority =
                graphHandshakeRequest.NoSqlAuthority &&
                graphHandshakeSafeResponse.NoSqlAuthority &&
                graphHandshakeUnsafeValidation.Accepted == false &&
                string.Equals(graphHandshakeUnsafeValidation.ReasonCode, "sql_authority_requested", StringComparison.Ordinal);
            bool graphHandshakeScopedManifest =
                graphHandshakeManifests.Count > 0 &&
                graphHandshakeManifests.SelectMany(manifest => manifest.AllowedToolNames)
                    .Contains(DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false;
            bool graphHandshakeFallback =
                graphHandshakeDisabledOutcome.FallbackRequired &&
                string.Equals(graphHandshakeDisabledOutcome.ReasonCode, "sidecar_disabled", StringComparison.Ordinal);
            bool graphHandshakeReady =
                graphHandshakeDefaultOptions.Enabled == false &&
                graphHandshakeSafeValidation.Accepted &&
                graphHandshakeNoSqlAuthority &&
                graphHandshakeScopedManifest &&
                graphHandshakeFallback;
            checks.Add(graphHandshakeReady
                ? Pass("GraphHandshakeBoundaryPresent", "default_enabled=false;validator=true;no_sql_authority=true;scoped_node_manifest=true;fallback=true;runtime_required=false")
                : Fail("GraphHandshakeBoundaryPresent", $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};validator={LowerBool(graphHandshakeSafeValidation.Accepted)};no_sql_authority={LowerBool(graphHandshakeNoSqlAuthority)};scoped_node_manifest={LowerBool(graphHandshakeScopedManifest)};fallback={LowerBool(graphHandshakeFallback)};runtime_required=false"));
```

- [ ] **Step 4: Add static readiness script check**

In `tools/check-dataagent-readiness.ps1`, add this check after `DataQueryGraphPilotPresent`:

```powershell
    New-Check -Group "Store" -Name "GraphHandshakeBoundaryPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs" @("DataAgentGraphHandshakeOptions", "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED", "DataAgentGraphHandshakeRequest", "DataAgentGraphHandshakeResponse", "NoSqlAuthority", "FallbackAvailable")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeManifestFactory.cs" @("DataAgentGraphHandshakeManifestFactory", "scoped", "ExecuteReadOnlyQuery")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeValidator.cs" @("DataAgentGraphHandshakeValidator", "sql_authority_requested", "unknown_node", "unknown_tool", "unsafe_trace", "checkpoint_mutation_requested", "visible_text_requested")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs" @("DataAgentGraphHandshakeCoordinator", "sidecar_disabled", "sidecar_unavailable", "sidecar_timeout", "DisabledDataAgentGraphSidecarClient")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("GraphHandshakeBoundaryPresent", "default_enabled=false", "validator=true", "no_sql_authority=true", "scoped_node_manifest=true", "fallback=true", "runtime_required=false"))) -Detail "V3.0 graph handshake boundary markers default_enabled=false validator=true no_sql_authority=true scoped_node_manifest=true fallback=true runtime_required=false"
```

Change:

```powershell
$expectedRequired = 85
```

to:

```powershell
$expectedRequired = 86
```

- [ ] **Step 5: Update DataAgent readiness count tests**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, change the static readiness script summary from:

```csharp
                "  Summary: 85 required passed, 0 required missing"
```

to:

```csharp
                "  Summary: 86 required passed, 0 required missing"
```

Change:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 85"));
```

to:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 86"));
```

In `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs` and `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`, change every:

```csharp
            Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 85"));
```

to:

```csharp
            Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 86"));
```

- [ ] **Step 6: Tighten QChat no-import boundary**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add this marker to the `forbiddenMarkers` array in `QChatDoesNotDirectlyImportDataAgentBoundaryTypes`:

```csharp
            "DataAgentGraphHandshake"
```

In `tools/check-qchat-engineering-map.ps1`, keep `$expectedRequired = 63` and update the existing required `DataAgent diagnostics command contract` check. Replace its trailing omit list:

```powershell
-OmitPatterns @("DataAgentDataQueryGraph")
```

with:

```powershell
-OmitPatterns @("DataAgentDataQueryGraph", "DataAgentGraphHandshake")
```

This keeps the QChat engineering map count unchanged while scanning all production QChat `.cs` files for both graph-family forbidden markers.

- [ ] **Step 7: Run readiness and QChat boundary tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV30ReadinessTests|FullyQualifiedName~QChatEngineeringMapDefaultModeExitsZeroAndPrintsSummary|FullyQualifiedName~QChatEngineeringMapScriptProtectsRequiredCheckCount|FullyQualifiedName~StaticReadinessScriptContainsV210Markers" -v:minimal
```

Expected: PASS with `Failed: 0`.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDoesNotDirectlyImportDataAgentBoundaryTypes|FullyQualifiedName~DataAgentDiagnosticsCommandContractCheckRequiresSharedParserAndQChatBoundary" -v:minimal
```

Expected: PASS with `Failed: 0`.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
GraphHandshakeBoundaryPresent
  Summary: 86 required passed, 0 required missing
```

- [ ] **Step 8: Commit Task 5**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1 tools\check-qchat-engineering-map.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV210ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV216ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV30ReadinessTests.cs Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Add DataAgent V3 graph handshake readiness"
```

---

### Task 6: Add V3.0 Developer Note

**Files:**
- Create: `docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md`

- [ ] **Step 1: Create the developer note**

Create `docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md` with this content:

```markdown
# DataAgent V3.0 Graph Handshake Boundary

V3.0 starts the LangGraph integration path by adding a C# graph handshake boundary.

It does not add a production Python sidecar, FastAPI service, HTTP transport, process manager, LangGraph runtime dependency, SQL execution path, or QChat graph ownership.

## What It Adds

- A disabled-by-default graph handshake option.
- Scoped DataAgent graph node manifests.
- A sidecar client interface for future runtime integration.
- A validator that treats sidecar responses as untrusted input.
- A coordinator that falls back to deterministic C# orchestration when the sidecar is disabled, unavailable, timed out, invalid, or overreaching.
- Owner diagnostics showing handshake status and fallback reason.

## Authority Boundary

C# remains the authority for:

- QueryPlan validation.
- SQL compilation.
- SQL Safety Validator decisions.
- Read-only query execution.
- Tool Broker route state.
- checkpoint persistence.
- evidence, trace, progress, and query audit.
- QChat and QQ ingress.

The sidecar can suggest orchestration shape. It cannot authorize datasets, fields, operators, limits, tools, checkpoint mutation, visible QChat text, QQ messages, executable SQL, or SQL execution.

## Attention Dilution Control

The handshake uses scoped node manifests. Each node receives only the small capability vocabulary needed for its role.

This is the structural answer to random tool choice caused by overlapping tool names and descriptions. The model should not see every Alife plugin tool when it is only planning DataAgent query steps.

## Plugin Policy

These remain deterministic services in V3.0:

- QChat message send and receive.
- owner command access policy.
- visible reply policy.
- voice and TTS.
- desktop pet actions.
- browser control.
- file transfer.
- external RAG source management.
- PostgreSQL checkpoint store internals.
- SQL execution.
- Tool Broker execution policy.

Future V3.x work may expose selected capabilities as graph-scoped manifests, but not as sidecar-callable tools without a separate design.

## V3.x Handoff

V3.1 can add an optional local Python/FastAPI sidecar behind this validated C# contract.
V3.2 can map sidecar progress into existing DataAgent progress diagnostics.
V3.3 can map human-in-the-loop interrupts into QChat owner events.
V3.4 can design checkpointer reconciliation while preserving C# checkpoint authority.
```

- [ ] **Step 2: Run doc marker checks**

Run:

```powershell
rg -n "Graph Handshake Boundary|NoSqlAuthority|sidecar|Attention Dilution|Plugin Policy" docs\dataagent\dataagent-v3.0-graph-handshake-boundary.md
```

Expected: output contains all searched markers.

Run:

```powershell
rg -n "FastAPI service, HTTP transport, process manager|cannot authorize datasets|deterministic services" docs\dataagent\dataagent-v3.0-graph-handshake-boundary.md
```

Expected: output contains the non-overreach and plugin-boundary statements.

- [ ] **Step 3: Commit Task 6**

Run:

```powershell
git add docs\dataagent\dataagent-v3.0-graph-handshake-boundary.md
git commit -m "Document DataAgent V3 graph handshake boundary"
```

---

### Task 7: Final Verification

**Files:**
- Verify all files changed in Tasks 1-6.

- [ ] **Step 1: Check status and recent commits**

Run:

```powershell
git status --short --branch
git log --oneline -12
```

Expected:

- Working tree is clean after all task commits.
- Recent commits include the V3.0 design and implementation commits.

- [ ] **Step 2: Run focused DataAgent graph handshake tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeContractTests|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentGraphHandshakeDiagnosticsFormatterTests|FullyQualifiedName~DataAgentV30ReadinessTests|FullyQualifiedName~StartPublishesGraphHandshakeDiagnosticsWithoutChangingContext" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 3: Run DataAgent readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
GraphHandshakeBoundaryPresent
  Summary: 86 required passed, 0 required missing
```

- [ ] **Step 4: Run QChat graph model boundary scan**

Run:

```powershell
rg -n "DataAgentDataQueryGraph|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches. `rg` exit code `1` is acceptable for no matches.

- [ ] **Step 5: Run QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 6: Run restore**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
```

Expected: restore completes with exit code `0`.

- [ ] **Step 7: Run build**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal -m:1
```

Expected: build completes with exit code `0` and `0` errors. Existing `CS0067` warnings in QChat test fakes may remain.

- [ ] **Step 8: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 9: Record final status**

Run:

```powershell
git status --short --branch
git log --oneline -10
```

Expected:

- Working tree is clean.
- Recent commits include all V3.0 graph handshake boundary commits.

---

## Self-Review

Spec coverage:

- C# graph handshake request and response contract: Task 1.
- Scoped capability manifests: Task 1.
- Sidecar client interface and disabled default: Task 3.
- Validator rejects authority overreach: Task 2.
- Disabled, unavailable, timeout, rejected, and accepted coordinator paths: Task 3.
- Owner diagnostics: Task 4.
- No change to deterministic DataAgent result behavior: Task 3 and Task 4 tests.
- DataAgent readiness gate: Task 5.
- QChat no-import boundary: Task 5 and Task 7.
- Developer note: Task 6.
- Full verification: Task 7.

Red-flag scan:

- The plan contains no unresolved marker strings.
- The plan contains no empty future-work markers.
- Every created file has exact path and concrete contents or exact insertion snippets.
- Every test command includes expected outcome.

Type consistency:

- Options type: `DataAgentGraphHandshakeOptions`.
- Request type: `DataAgentGraphHandshakeRequest`.
- Response type: `DataAgentGraphHandshakeResponse`.
- Manifest type: `DataAgentGraphNodeManifest`.
- Validator type: `DataAgentGraphHandshakeValidator`.
- Coordinator type: `DataAgentGraphHandshakeCoordinator`.
- Sidecar interface: `IDataAgentGraphSidecarClient`.
- Formatter type: `DataAgentGraphHandshakeDiagnosticsFormatter`.
- Readiness check name: `GraphHandshakeBoundaryPresent`.
