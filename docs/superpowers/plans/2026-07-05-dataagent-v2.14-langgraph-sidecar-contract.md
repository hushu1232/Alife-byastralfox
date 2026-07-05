# DataAgent V2.14 LangGraph Sidecar Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a disabled-by-default DataAgent graph sidecar contract that prepares for a future LangGraph pilot without adding graph runtime behavior, SQL authority, QChat coupling, Python, HTTP, or a new execution path.

**Architecture:** V2.14 adds only DataAgent-owned C# contract types, deterministic policy checks, readiness gates, QChat boundary gates, and developer documentation. The sidecar contract may describe orchestration intent, but all dataset, field, operator, SQL, route, checkpoint, audit, evidence, and visible text authority stays in the existing C# DataAgent and Tool Broker pipeline.

**Tech Stack:** .NET 9, C# records/enums, NUnit, PowerShell static readiness checks, existing Alife DataAgent/QChat test projects.

---

## Scope Boundaries

This plan implements only the contract milestone that the V2.14 design approved.

Allowed:
- DataAgent-owned DTO, option, policy, and validation types.
- Default-off environment option parsing for `ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED`.
- Readiness gates proving the contract exists and has no runtime or SQL authority.
- QChat engineering-map gates proving QChat does not directly import sidecar contract types.
- Documentation that explains the future V2.15 path.

Forbidden in V2.14:
- No LangGraph runtime package.
- No `StateGraph`.
- No Python sidecar.
- No FastAPI or HTTP server/client.
- No real sidecar process.
- No DataQueryGraph pilot.
- No new SQL execution path.
- No QChat main-loop refactor.
- No natural-language QChat command auto-execution.

The contract rule to preserve in code, readiness, and docs:

```text
A graph sidecar may propose orchestration intent, but it cannot authorize datasets, fields, operators, SQL text, tool execution, route state, checkpoint mutation, or evidence.
```

## File Structure

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs`
  - Owns `DataAgentGraphSidecarOptions`, `DataAgentGraphSidecarPolicy`, `DataAgentGraphSidecarContract`, `DataAgentGraphSidecarRequest`, `DataAgentGraphSidecarResponse`, `DataAgentGraphSidecarNodeKind`, and `DataAgentGraphSidecarAuthority`.
  - Keeps the contract small and runtime-free.
- Create `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarContractTests.cs`
  - Proves option parsing, policy authority, request validation, response validation, and no-runtime behavior.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds `GraphSidecarContractPresent` to runtime readiness.
- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Increases core readiness count from `68` to `69`.
  - Adds detail assertions for `GraphSidecarContractPresent`.
  - Updates static script summary expectations after the static script task.
- Modify `tools/check-dataagent-readiness.ps1`
  - Adds a required static check for `GraphSidecarContractPresent`.
  - Increases required count from `82` to `83`.
- Modify `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
  - Updates static required count assertions to `83` and `58`.
- Modify `tools/check-qchat-engineering-map.ps1`
  - Adds `DataAgent graph sidecar contract` as a required QChat engineering-map check.
  - Adds QChat omit guards for sidecar contract types.
  - Increases required count from `57` to `58`.
- Modify `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Adds the new required V2 check.
  - Extends QChat direct-import forbidden markers.
  - Updates engineering-map count expectations.
- Create `docs/dataagent/dataagent-v2.14-langgraph-sidecar-contract.md`
  - Documents the contract-only nature, default-off flag, absent runtime, safety boundary, and V2.15 handoff.

## Task 1: Graph Sidecar Contract Types

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarContractTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs`

- [ ] **Step 1: Write the failing contract tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarContractTests.cs` with this complete content:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphSidecarContractTests
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
            Assert.That(DataAgentGraphSidecarOptions.Disabled.Enabled, Is.False);
            Assert.That(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED"));

            foreach (string? value in disabledValues)
            {
                Assert.That(DataAgentGraphSidecarOptions.FromValue(value).Enabled, Is.False, $"Expected disabled for '{value}'.");
            }

            foreach (string value in enabledValues)
            {
                Assert.That(DataAgentGraphSidecarOptions.FromValue(value).Enabled, Is.True, $"Expected enabled for '{value}'.");
            }
        });
    }

    [Test]
    public void ExplicitEnableDoesNotCreateRuntime()
    {
        string? previous = Environment.GetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, null);
            DataAgentGraphSidecarOptions defaultOptions = DataAgentGraphSidecarOptions.FromEnvironment();

            Environment.SetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, "true");
            DataAgentGraphSidecarOptions enabledOptions = DataAgentGraphSidecarOptions.FromEnvironment();

            Assert.Multiple(() =>
            {
                Assert.That(defaultOptions.Enabled, Is.False);
                Assert.That(enabledOptions.Enabled, Is.True);
                Assert.That(DataAgentGraphSidecarContract.IsRuntimeAvailable, Is.False);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, previous);
        }
    }

    [Test]
    public void DefaultPolicyAllowsIntentAndForbidsAuthoritySurfaces()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent), Is.True);
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.RequestCSharpSafetyService), Is.True);
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.ReturnBoundedTrace), Is.True);
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.ReportDeterministicFallback), Is.True);

            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeDataset), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeField), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeOperator), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeLimit), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.ProvideExecutableSql), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.ExecuteSql), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.DecideToolRoute), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.MutateCheckpoint), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteEvidence), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteAudit), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteProgress), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteDiagnostics), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.SendVisibleQChatText), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.OwnQqIngress), Is.True);

            Assert.That(policy.NoSqlAuthority, Is.True);
            Assert.That(policy.NoToolRouteAuthority, Is.True);
            Assert.That(policy.NoCheckpointAuthority, Is.True);
            Assert.That(policy.NoEvidenceAuthority, Is.True);
            Assert.That(policy.NoVisibleTextAuthority, Is.True);
        });
    }

    [Test]
    public void RequestValidationRequiresBoundedIdentity()
    {
        DataAgentGraphSidecarRequest valid = NewRequest(
            workflowId: "wf-1",
            sessionId: "session-1",
            allowedCapabilityNames: ["DataAgentQueryPlanner", "DataAgentQueryPlanValidator"]);
        DataAgentGraphSidecarRequest blankWorkflow = valid with { WorkflowId = " " };
        DataAgentGraphSidecarRequest blankSession = valid with { SessionId = "" };
        DataAgentGraphSidecarRequest blankTrace = valid with { TraceId = " " };
        DataAgentGraphSidecarRequest blankCapability = valid with { AllowedCapabilityNames = ["DataAgentQueryPlanner", " "] };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(valid), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankWorkflow), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankSession), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankTrace), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankCapability), Is.False);
        });
    }

    [Test]
    public void ResponseValidationRejectsSqlAndForbiddenAuthorityClaims()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanValidator",
            trace: ["QueryPlanner:Proposed", "QueryPlanValidation:DelegatedToCSharp"],
            claimedAuthorities: [DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent]);
        DataAgentGraphSidecarResponse sqlTrace = safe with
        {
            Trace = ["SELECT * FROM document_index"]
        };
        DataAgentGraphSidecarResponse sqlCapability = safe with
        {
            RequestedCapabilityName = "ExecuteSql"
        };
        DataAgentGraphSidecarResponse authorityClaim = safe with
        {
            ClaimedAuthorities = [DataAgentGraphSidecarAuthority.ExecuteSql]
        };
        DataAgentGraphSidecarResponse visibleTextClaim = safe with
        {
            ClaimedAuthorities = [DataAgentGraphSidecarAuthority.SendVisibleQChatText]
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(safe, policy), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(sqlTrace, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(sqlCapability, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(authorityClaim, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(visibleTextClaim, policy), Is.False);
        });
    }

    static DataAgentGraphSidecarRequest NewRequest(
        string workflowId,
        string sessionId,
        IReadOnlyList<string>? allowedCapabilityNames = null)
    {
        return new DataAgentGraphSidecarRequest(
            workflowId,
            sessionId,
            "owner",
            "Which required engineering gates failed?",
            "scenario_context=true",
            DataAgentGraphSidecarContract.DefaultAllowedNodeKinds,
            allowedCapabilityNames ?? ["DataAgentQueryPlanner"],
            "checkpoint-1",
            "Active",
            "trace-1");
    }

    static DataAgentGraphSidecarResponse NewResponse(
        string requestedCapabilityName,
        IReadOnlyList<string> trace,
        IReadOnlyList<DataAgentGraphSidecarAuthority> claimedAuthorities)
    {
        return new DataAgentGraphSidecarResponse(
            "wf-1",
            true,
            "intent_proposed",
            "Delegating to C# DataAgent safety service.",
            DataAgentGraphSidecarNodeKind.QueryPlanValidation,
            requestedCapabilityName,
            true,
            trace,
            claimedAuthorities);
    }
}
```

- [ ] **Step 2: Run the failing contract tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarContractTests" -v:minimal -m:1
```

Expected: FAIL because `DataAgentGraphSidecarOptions`, `DataAgentGraphSidecarContract`, `DataAgentGraphSidecarPolicy`, `DataAgentGraphSidecarRequest`, `DataAgentGraphSidecarResponse`, `DataAgentGraphSidecarNodeKind`, and `DataAgentGraphSidecarAuthority` do not exist yet.

- [ ] **Step 3: Add the minimal contract implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs` with this complete content:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphSidecarOptions(bool Enabled)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED";

    public static DataAgentGraphSidecarOptions Disabled { get; } = new(false);

    public static DataAgentGraphSidecarOptions FromEnvironment()
    {
        return FromValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
    }

    public static DataAgentGraphSidecarOptions FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Disabled;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => new DataAgentGraphSidecarOptions(true),
            "false" or "0" or "no" => Disabled,
            _ => Disabled
        };
    }
}

public enum DataAgentGraphSidecarNodeKind
{
    ScenarioContext,
    QueryPlanner,
    QueryPlanValidation,
    SqlSafetyValidation,
    ReadOnlyExecution,
    Evidence,
    Checkpoint,
    Diagnostics,
    Terminal
}

public enum DataAgentGraphSidecarAuthority
{
    ProposeOrchestrationIntent,
    RequestCSharpSafetyService,
    ReturnBoundedTrace,
    ReportDeterministicFallback,
    AuthorizeDataset,
    AuthorizeField,
    AuthorizeOperator,
    AuthorizeLimit,
    ProvideExecutableSql,
    ExecuteSql,
    DecideToolRoute,
    MutateCheckpoint,
    WriteEvidence,
    WriteAudit,
    WriteProgress,
    WriteDiagnostics,
    SendVisibleQChatText,
    OwnQqIngress
}

public sealed record DataAgentGraphSidecarRequest(
    string WorkflowId,
    string SessionId,
    string CallerId,
    string Question,
    string ScenarioContext,
    IReadOnlyList<DataAgentGraphSidecarNodeKind> AllowedNodeKinds,
    IReadOnlyList<string> AllowedCapabilityNames,
    string? CheckpointSessionId,
    string? CheckpointStatus,
    string TraceId);

public sealed record DataAgentGraphSidecarResponse(
    string WorkflowId,
    bool Accepted,
    string ReasonCode,
    string Message,
    DataAgentGraphSidecarNodeKind? ProposedNodeKind,
    string? RequestedCapabilityName,
    bool RequiresCSharpSafetyService,
    IReadOnlyList<string> Trace,
    IReadOnlyList<DataAgentGraphSidecarAuthority> ClaimedAuthorities);

public sealed class DataAgentGraphSidecarPolicy
{
    static readonly DataAgentGraphSidecarAuthority[] AllowedAuthority =
    [
        DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
        DataAgentGraphSidecarAuthority.RequestCSharpSafetyService,
        DataAgentGraphSidecarAuthority.ReturnBoundedTrace,
        DataAgentGraphSidecarAuthority.ReportDeterministicFallback
    ];

    static readonly DataAgentGraphSidecarAuthority[] ForbiddenAuthority =
    [
        DataAgentGraphSidecarAuthority.AuthorizeDataset,
        DataAgentGraphSidecarAuthority.AuthorizeField,
        DataAgentGraphSidecarAuthority.AuthorizeOperator,
        DataAgentGraphSidecarAuthority.AuthorizeLimit,
        DataAgentGraphSidecarAuthority.ProvideExecutableSql,
        DataAgentGraphSidecarAuthority.ExecuteSql,
        DataAgentGraphSidecarAuthority.DecideToolRoute,
        DataAgentGraphSidecarAuthority.MutateCheckpoint,
        DataAgentGraphSidecarAuthority.WriteEvidence,
        DataAgentGraphSidecarAuthority.WriteAudit,
        DataAgentGraphSidecarAuthority.WriteProgress,
        DataAgentGraphSidecarAuthority.WriteDiagnostics,
        DataAgentGraphSidecarAuthority.SendVisibleQChatText,
        DataAgentGraphSidecarAuthority.OwnQqIngress
    ];

    readonly HashSet<DataAgentGraphSidecarAuthority> allowed;
    readonly HashSet<DataAgentGraphSidecarAuthority> forbidden;

    DataAgentGraphSidecarPolicy(
        IEnumerable<DataAgentGraphSidecarAuthority> allowed,
        IEnumerable<DataAgentGraphSidecarAuthority> forbidden)
    {
        this.allowed = new HashSet<DataAgentGraphSidecarAuthority>(allowed);
        this.forbidden = new HashSet<DataAgentGraphSidecarAuthority>(forbidden);
    }

    public static DataAgentGraphSidecarPolicy CreateDefault()
    {
        return new DataAgentGraphSidecarPolicy(AllowedAuthority, ForbiddenAuthority);
    }

    public bool Allows(DataAgentGraphSidecarAuthority authority)
    {
        return allowed.Contains(authority);
    }

    public bool Forbids(DataAgentGraphSidecarAuthority authority)
    {
        return forbidden.Contains(authority);
    }

    public bool NoSqlAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.ProvideExecutableSql) &&
        Forbids(DataAgentGraphSidecarAuthority.ExecuteSql) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeDataset) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeField) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeOperator) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeLimit);

    public bool NoToolRouteAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.DecideToolRoute);

    public bool NoCheckpointAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.MutateCheckpoint);

    public bool NoEvidenceAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.WriteEvidence) &&
        Forbids(DataAgentGraphSidecarAuthority.WriteAudit) &&
        Forbids(DataAgentGraphSidecarAuthority.WriteProgress) &&
        Forbids(DataAgentGraphSidecarAuthority.WriteDiagnostics);

    public bool NoVisibleTextAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.SendVisibleQChatText) &&
        Forbids(DataAgentGraphSidecarAuthority.OwnQqIngress);
}

public static class DataAgentGraphSidecarContract
{
    static readonly string[] RawSqlMarkers =
    [
        "select ",
        "insert ",
        "update ",
        "delete ",
        "drop ",
        "alter ",
        "truncate ",
        "```sql",
        ";"
    ];

    static readonly string[] ForbiddenCapabilityNames =
    [
        "ExecuteSql",
        "ProvideExecutableSql",
        "DataAgentQueryExecutor",
        "IDataAgentStore.Query"
    ];

    public static bool IsRuntimeAvailable => false;

    public static IReadOnlyList<DataAgentGraphSidecarNodeKind> DefaultAllowedNodeKinds { get; } =
    [
        DataAgentGraphSidecarNodeKind.ScenarioContext,
        DataAgentGraphSidecarNodeKind.QueryPlanner,
        DataAgentGraphSidecarNodeKind.QueryPlanValidation,
        DataAgentGraphSidecarNodeKind.SqlSafetyValidation,
        DataAgentGraphSidecarNodeKind.ReadOnlyExecution,
        DataAgentGraphSidecarNodeKind.Evidence,
        DataAgentGraphSidecarNodeKind.Checkpoint,
        DataAgentGraphSidecarNodeKind.Diagnostics,
        DataAgentGraphSidecarNodeKind.Terminal
    ];

    public static bool IsRequestValid(DataAgentGraphSidecarRequest request)
    {
        return HasText(request.WorkflowId) &&
               HasText(request.SessionId) &&
               HasText(request.CallerId) &&
               HasText(request.TraceId) &&
               request.AllowedNodeKinds.Count > 0 &&
               request.AllowedNodeKinds.All(DefaultAllowedNodeKinds.Contains) &&
               request.AllowedCapabilityNames.All(HasText);
    }

    public static bool IsResponseSafe(
        DataAgentGraphSidecarResponse response,
        DataAgentGraphSidecarPolicy policy)
    {
        if (HasText(response.WorkflowId) == false)
            return false;

        if (response.ProposedNodeKind.HasValue &&
            DefaultAllowedNodeKinds.Contains(response.ProposedNodeKind.Value) == false)
            return false;

        if (response.RequiresCSharpSafetyService && HasText(response.RequestedCapabilityName) == false)
            return false;

        if (ContainsForbiddenCapability(response.RequestedCapabilityName))
            return false;

        if (ContainsRawSql(response.ReasonCode) ||
            ContainsRawSql(response.Message) ||
            ContainsRawSql(response.RequestedCapabilityName) ||
            response.Trace.Any(ContainsRawSql))
        {
            return false;
        }

        return response.ClaimedAuthorities.All(policy.Allows);
    }

    static bool HasText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) == false;
    }

    static bool ContainsForbiddenCapability(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ForbiddenCapabilityNames.Any(marker =>
            string.Equals(value.Trim(), marker, StringComparison.OrdinalIgnoreCase));
    }

    static bool ContainsRawSql(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim().ToLowerInvariant();
        return RawSqlMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }
}
```

- [ ] **Step 4: Run the contract tests until they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarContractTests" -v:minimal -m:1
```

Expected: PASS for all tests in `DataAgentGraphSidecarContractTests`.

- [ ] **Step 5: Commit the contract types**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs Tests/Alife.Test.DataAgent/DataAgentGraphSidecarContractTests.cs
git commit -m "Add DataAgent graph sidecar contract types"
```

## Task 2: DataAgent Runtime Readiness Gate

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Update the core readiness test first**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, inside `CoreReadinessChecksAllPass`, change:

```csharp
Assert.That(checks, Has.Count.EqualTo(68));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(69));
```

Then add these assertions after the `PostgresCheckpointPersistencePresent` detail assertions and before `DataAgentServiceUsesStoreBoundary`:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("GraphSidecarContractPresent"));
DataAgentReadinessCheck graphSidecarCheck = checks.Single(check => check.Name == "GraphSidecarContractPresent");
Assert.That(graphSidecarCheck.Detail, Does.Contain("default_enabled=false"));
Assert.That(graphSidecarCheck.Detail, Does.Contain("contract=true"));
Assert.That(graphSidecarCheck.Detail, Does.Contain("policy=true"));
Assert.That(graphSidecarCheck.Detail, Does.Contain("no_sql_authority=true"));
Assert.That(graphSidecarCheck.Detail, Does.Contain("no_runtime=true"));
```

- [ ] **Step 2: Run the focused readiness test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.CoreReadinessChecksAllPass" -v:minimal -m:1
```

Expected: FAIL because `GraphSidecarContractPresent` is not emitted by `DataAgentReadiness.CheckCore`.

- [ ] **Step 3: Add the runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, add this block immediately after the existing `PostgresCheckpointPersistencePresent` check and before `DataAgentServiceUsesStoreBoundary`:

```csharp
DataAgentGraphSidecarOptions graphSidecarDefaultOptions = DataAgentGraphSidecarOptions.FromValue(null);
DataAgentGraphSidecarPolicy graphSidecarPolicy = DataAgentGraphSidecarPolicy.CreateDefault();
DataAgentGraphSidecarResponse graphSidecarForbiddenResponse = new(
    "readiness-workflow",
    true,
    "unsafe_sql_authority",
    "SELECT * FROM document_index",
    DataAgentGraphSidecarNodeKind.QueryPlanValidation,
    "ExecuteSql",
    true,
    ["SELECT * FROM document_index"],
    [DataAgentGraphSidecarAuthority.ExecuteSql]);
bool graphSidecarContractReady =
    typeof(DataAgentGraphSidecarContract).IsClass &&
    typeof(DataAgentGraphSidecarRequest).IsClass &&
    typeof(DataAgentGraphSidecarResponse).IsClass &&
    DataAgentGraphSidecarContract.DefaultAllowedNodeKinds.Contains(DataAgentGraphSidecarNodeKind.QueryPlanner);
bool graphSidecarPolicyReady =
    graphSidecarPolicy.Allows(DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent) &&
    graphSidecarPolicy.Allows(DataAgentGraphSidecarAuthority.RequestCSharpSafetyService) &&
    graphSidecarPolicy.Forbids(DataAgentGraphSidecarAuthority.ExecuteSql) &&
    graphSidecarPolicy.Forbids(DataAgentGraphSidecarAuthority.DecideToolRoute) &&
    graphSidecarPolicy.NoToolRouteAuthority &&
    graphSidecarPolicy.NoCheckpointAuthority &&
    graphSidecarPolicy.NoEvidenceAuthority;
bool graphSidecarNoSqlAuthority =
    graphSidecarPolicy.NoSqlAuthority &&
    DataAgentGraphSidecarContract.IsResponseSafe(graphSidecarForbiddenResponse, graphSidecarPolicy) == false;
bool graphSidecarNoRuntime = DataAgentGraphSidecarContract.IsRuntimeAvailable == false;
bool graphSidecarReady =
    graphSidecarDefaultOptions.Enabled == false &&
    graphSidecarContractReady &&
    graphSidecarPolicyReady &&
    graphSidecarNoSqlAuthority &&
    graphSidecarNoRuntime;
checks.Add(graphSidecarReady
    ? Pass("GraphSidecarContractPresent", "default_enabled=false;contract=true;policy=true;no_sql_authority=true;no_runtime=true")
    : Fail("GraphSidecarContractPresent", $"default_enabled={LowerBool(graphSidecarDefaultOptions.Enabled)};contract={LowerBool(graphSidecarContractReady)};policy={LowerBool(graphSidecarPolicyReady)};no_sql_authority={LowerBool(graphSidecarNoSqlAuthority)};no_runtime={LowerBool(graphSidecarNoRuntime)}"));
```

Keep this check close to the V2.13 PostgreSQL checkpoint gate because V2.14 builds on checkpoint/session persistence but does not modify it.

- [ ] **Step 4: Run the focused readiness test until it passes**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.CoreReadinessChecksAllPass" -v:minimal -m:1
```

Expected: PASS, with `GraphSidecarContractPresent` included and all `69` checks passing.

- [ ] **Step 5: Commit the runtime readiness gate**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "Add DataAgent graph sidecar readiness gate"
```

## Task 3: Static Readiness and QChat Engineering Map Gates

**Files:**
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Update DataAgent static readiness test expectations first**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, inside `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, change:

```csharp
"  Summary: 82 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 83 required passed, 0 required missing"
```

In the same test, add this assertion after the existing `PostgresCheckpointPersistencePresent` or near the other V2.10 and later readiness assertions:

```csharp
Assert.That(result.StandardOutput, Does.Contain("GraphSidecarContractPresent"));
```

In `ReadinessScriptProtectsV23RouteGateContract`, change:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 82"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 83"));
```

Then add this new test method before `FunctionCallerStoresRecentDataAgentTraceDiagnostics`:

```csharp
[Test]
public void ReadinessScriptProtectsV214GraphSidecarContract()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "GraphSidecarContractPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarContract.cs"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarOptions"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarPolicy"));
        Assert.That(declaration, Does.Contain("IsRuntimeAvailable"));
        Assert.That(declaration, Does.Contain("NoSqlAuthority"));
        Assert.That(declaration, Does.Contain("ExecuteSql"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarContractTests"));
        Assert.That(declaration, Does.Contain("default_enabled=false"));
        Assert.That(declaration, Does.Contain("policy=true"));
        Assert.That(declaration, Does.Contain("no_sql_authority=true"));
        Assert.That(declaration, Does.Contain("no_runtime=true"));
    });
}
```

- [ ] **Step 2: Update V2.10 static count guard tests first**

In `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`, inside `StaticReadinessScriptContainsV210Markers`, change:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 82"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 57"));
```

to:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 83"));
Assert.That(qchatScript, Does.Contain("$expectedRequired = 58"));
```

- [ ] **Step 3: Update QChat engineering-map tests first**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add this required check after `"DataAgent PostgreSQL checkpoint persistence"`:

```csharp
"DataAgent graph sidecar contract",
```

In `QChatDoesNotDirectlyImportDataAgentBoundaryTypes`, append these forbidden markers:

```csharp
"DataAgentGraphSidecarContract",
"DataAgentGraphSidecarOptions",
"DataAgentGraphSidecarPolicy",
"DataAgentGraphSidecarRequest",
"DataAgentGraphSidecarResponse"
```

In `QChatEngineeringMapDefaultModeExitsZeroAndPrintsSummary`, change:

```csharp
"Summary: 57 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

to:

```csharp
"Summary: 58 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

In `QChatEngineeringMapScriptProtectsRequiredCheckCount`, change:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 57"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 58"));
```

Add this new test after `PostgresCheckpointPersistenceCheckRequiresDataAgentRuntimeAndQChatBoundary`:

```csharp
[Test]
public void GraphSidecarContractCheckRequiresDataAgentRuntimeAndQChatBoundary()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindAddCheckDeclaration(script, "DataAgent graph sidecar contract");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("GraphSidecarContractPresent"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarContract"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED"));
        Assert.That(declaration, Does.Contain("no_sql_authority=true"));
        Assert.That(declaration, Does.Contain("no_runtime=true"));
        Assert.That(declaration, Does.Contain("QChatDoesNotDirectlyImportDataAgentBoundaryTypes"));
        Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarOptions"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarPolicy"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarRequest"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarResponse"));
    });
}
```

- [ ] **Step 4: Run the static tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptDefaultModeExitsZeroAndPrintsSummary|FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptProtectsV23RouteGateContract|FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptProtectsV214GraphSidecarContract|FullyQualifiedName~DataAgentV210ReadinessTests.StaticReadinessScriptContainsV210Markers" -v:minimal -m:1
```

Expected: FAIL because `tools/check-dataagent-readiness.ps1` still has `82` required checks and no `GraphSidecarContractPresent` static check.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal -m:1
```

Expected: FAIL because `tools/check-qchat-engineering-map.ps1` still has `57` required checks and no `DataAgent graph sidecar contract` check.

- [ ] **Step 5: Add the DataAgent static readiness check**

In `tools/check-dataagent-readiness.ps1`, insert this check immediately after `PostgresCheckpointPersistencePresent` and before `DataAgentServiceUsesStoreBoundary`:

```powershell
    New-Check -Group "Store" -Name "GraphSidecarContractPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs" @("DataAgentGraphSidecarOptions", "ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED", "DataAgentGraphSidecarPolicy", "DataAgentGraphSidecarContract", "IsRuntimeAvailable", "NoSqlAuthority", "ProposeOrchestrationIntent", "ExecuteSql")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentGraphSidecarContractTests.cs" @("DataAgentGraphSidecarContractTests", "OptionsDefaultToDisabledAndParseOnlyExplicitTrueValues", "ExplicitEnableDoesNotCreateRuntime", "DefaultPolicyAllowsIntentAndForbidsAuthoritySurfaces", "ResponseValidationRejectsSqlAndForbiddenAuthorityClaims")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("GraphSidecarContractPresent", "default_enabled=false", "policy=true", "no_sql_authority=true", "no_runtime=true"))) -Detail "V2.14 disabled graph sidecar contract markers"
```

Then change:

```powershell
$expectedRequired = 82
```

to:

```powershell
$expectedRequired = 83
```

- [ ] **Step 6: Add the QChat engineering-map check**

In `tools/check-qchat-engineering-map.ps1`, insert this check after `DataAgent PostgreSQL checkpoint persistence` and before the `# V2.10 governance readiness gates` comment:

```powershell
Add-Check -Group "Harness" -Name "DataAgent graph sidecar contract" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("GraphSidecarContractPresent", "DataAgentGraphSidecarContract", "ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED", "no_sql_authority=true", "no_runtime=true") -AlsoPath "Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs" -AlsoPatterns @("QChatDoesNotDirectlyImportDataAgentBoundaryTypes", "DataAgentGraphSidecarContract", "DataAgentGraphSidecarOptions", "DataAgentGraphSidecarPolicy", "DataAgentGraphSidecarRequest", "DataAgentGraphSidecarResponse") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentGraphSidecarContract", "DataAgentGraphSidecarOptions", "DataAgentGraphSidecarPolicy", "DataAgentGraphSidecarRequest", "DataAgentGraphSidecarResponse")
```

Then change:

```powershell
$expectedRequired = 57
```

to:

```powershell
$expectedRequired = 58
```

- [ ] **Step 7: Run the static scripts directly**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected summary:

```text
  Summary: 83 required passed, 0 required missing
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected summary:

```text
Summary: 58 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 8: Run the static guard tests until they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptDefaultModeExitsZeroAndPrintsSummary|FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptProtectsV23RouteGateContract|FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptProtectsV214GraphSidecarContract|FullyQualifiedName~DataAgentV210ReadinessTests.StaticReadinessScriptContainsV210Markers" -v:minimal -m:1
```

Expected: PASS.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal -m:1
```

Expected: PASS.

- [ ] **Step 9: Commit the static readiness and QChat map gates**

Run:

```powershell
git add tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Add sidecar contract readiness map gates"
```

## Task 4: Developer Documentation

**Files:**
- Create: `docs/dataagent/dataagent-v2.14-langgraph-sidecar-contract.md`

- [ ] **Step 1: Write the documentation**

Create `docs/dataagent/dataagent-v2.14-langgraph-sidecar-contract.md` with this complete content:

```markdown
# DataAgent V2.14 LangGraph Sidecar Contract

V2.14 is a contract milestone, not a graph runtime milestone.

The repository now defines a disabled-by-default C# contract for a future LangGraph sidecar, but it does not add LangGraph runtime behavior, a Python sidecar, FastAPI, HTTP calls, a StateGraph, or a DataQueryGraph pilot.

## Default State

The feature flag is:

```text
ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED=false
```

Missing, blank, `false`, `0`, and `no` values are disabled. Explicit `true`, `1`, and `yes` values may parse as enabled for future readiness, but V2.14 still does not start a runtime because `DataAgentGraphSidecarContract.IsRuntimeAvailable` is `false`.

## Authority Boundary

A future graph sidecar may propose orchestration intent, request that C# DataAgent run an existing safe operation, return a bounded trace, or report that deterministic fallback is needed.

It cannot authorize datasets, fields, operators, limits, SQL text, SQL execution, Tool Broker route state, checkpoint mutation, audit, evidence, progress, diagnostics, QChat visible text, or QQ ingress.

The C# DataAgent pipeline remains the authority:

- `DataAgentScenarioContextBuilder` provides hint-only scenario context.
- `DataAgentQueryPlanValidator` validates datasets, fields, operators, and limits.
- `DataAgentSqlCompiler` compiles read-only parameterized SQL.
- `DataAgentSqlSafetyValidator` rejects dangerous SQL shapes.
- `IDataAgentStore` executes read-only queries and records query/audit state.
- `IDataAgentAnalysisSessionStore` persists analysis session/checkpoint state.
- Tool Broker route state decides whether DataAgent tools may be used in the current QChat turn.

## Why This Exists

The goal is to prepare a small, testable boundary before any graph runtime exists. This prevents a future sidecar from becoming a second authority for SQL, tools, route state, checkpoints, or evidence.

V2.14 also keeps QChat as the interaction surface. QChat may consume DataAgent outputs through existing tool and diagnostics paths, but it does not import the graph sidecar contract types.

## V2.15 Handoff

V2.15 may pilot a disabled-by-default DataQueryGraph only after the V2.14 readiness and QChat boundary gates pass.

The pilot should map scenario context, planning, validation, SQL safety, execution, evidence, and checkpoint steps into graph-shaped nodes. Any node that touches safety or SQL must call the existing C# DataAgent services rather than reimplementing or bypassing them.
```

- [ ] **Step 2: Verify the documentation markers**

Run:

```powershell
rg -n "contract milestone|ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED|IsRuntimeAvailable|cannot authorize|V2.15" docs\dataagent\dataagent-v2.14-langgraph-sidecar-contract.md
```

Expected: all five markers are found.

- [ ] **Step 3: Commit the documentation**

Run:

```powershell
git add docs/dataagent/dataagent-v2.14-langgraph-sidecar-contract.md
git commit -m "Document DataAgent V2.14 sidecar contract"
```

## Task 5: Final Verification and Scope Audit

**Files:**
- No new source files unless verification exposes a defect.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarContractTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests" -v:minimal -m:1
```

Expected: PASS.

- [ ] **Step 2: Run focused QChat engineering-map tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal -m:1
```

Expected: PASS.

- [ ] **Step 3: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
  Summary: 83 required passed, 0 required missing
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 58 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run forbidden-shape scans**

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.DataAgent\*.cs -Pattern "StateGraph|FastAPI|http://|https://|Python sidecar"
```

Expected: no matches from V2.14 source files.

Run:

```powershell
Select-String -Path sources\Alife.Function\Alife.Function.QChat\*.cs -Pattern "DataAgentGraphSidecarContract|DataAgentGraphSidecarOptions|DataAgentGraphSidecarPolicy|DataAgentGraphSidecarRequest|DataAgentGraphSidecarResponse|LangGraph|StateGraph"
```

Expected: no matches.

- [ ] **Step 5: Run restore, build, and full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
```

Expected: restore succeeds.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal -m:1
```

Expected: build succeeds.

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected: all tests pass or only known environment-gated live tests are skipped.

- [ ] **Step 6: Inspect git diff**

Run:

```powershell
git status --short --branch
git diff --stat
git diff -- sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentGraphSidecarContractTests.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs docs/dataagent/dataagent-v2.14-langgraph-sidecar-contract.md
```

Expected:
- Only V2.14 contract, tests, readiness gates, QChat map gates, and docs changed.
- No `D:\FOXD` changes.
- No Python, FastAPI, LangGraph runtime, HTTP client/server, or QChat main-loop edits.

- [ ] **Step 7: Commit any final verification fixes**

If the previous steps required small fixes, commit them:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentGraphSidecarContractTests.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs docs/dataagent/dataagent-v2.14-langgraph-sidecar-contract.md
git commit -m "Harden DataAgent sidecar contract verification"
```

Expected: skip this commit if no fixes were needed after earlier task commits.

## Final Handoff

At completion, the branch should contain these logical commits:

1. `Add DataAgent graph sidecar contract types`
2. `Add DataAgent graph sidecar readiness gate`
3. `Add sidecar contract readiness map gates`
4. `Document DataAgent V2.14 sidecar contract`
5. Optional verification fix commit only if needed

Final branch status should be clean and ahead of `alife-byastralfox/master`.

## Self-Review

- Spec coverage: the plan covers contract types, default-off option parsing, deterministic policy, runtime readiness, static readiness, QChat omission guards, docs, forbidden runtime shapes, and final verification.
- Placeholder scan: all tasks include concrete code, commands, expected results, and file paths.
- Type consistency: the tests and implementation use the same names and signatures for `DataAgentGraphSidecarOptions`, `DataAgentGraphSidecarPolicy`, `DataAgentGraphSidecarContract`, `DataAgentGraphSidecarRequest`, `DataAgentGraphSidecarResponse`, `DataAgentGraphSidecarNodeKind`, and `DataAgentGraphSidecarAuthority`.
- Scope check: the plan does not add LangGraph runtime, `StateGraph`, Python sidecar code, FastAPI, HTTP client/server, SQL execution, QChat command execution, or a DataQueryGraph pilot.
