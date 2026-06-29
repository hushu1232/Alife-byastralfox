# LangGraph Multi-Agent Orchestration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prepare Alife for LangGraph-powered multi-agent linked runs without weakening the existing QChat state machine, DataAgent safety boundary, or Tool Broker execution gates.

**Architecture:** Keep the .NET runtime as the system of record and introduce LangGraph only as a V2.5 sidecar pilot for DataAgent analysis workflows. V1.6 and V1.7 build observability and capability boundaries first; V2 adds provider-neutral persistence and PostgreSQL; V3 can promote supervisor-controlled multi-agent governance after pilot evidence is stable.

**Tech Stack:** .NET 9, NUnit, PowerShell readiness scripts, Alife Tool Broker, DataAgent, QChat, SQLite in V1.x, PostgreSQL in V2, Python LangGraph sidecar in V2.5.

---

## Execution Order

The high-quality fast path is:

```text
V1.6 Tool Broker observability
  -> V1.7 DataAgent capability providers
  -> V2 PostgreSQL/store contracts
  -> V2.5 LangGraph sidecar pilot
  -> V3 supervisor-controlled multi-agent governance
```

Do not add LangGraph as a runtime dependency before V2.5.

## File Structure

- Create `docs/superpowers/specs/2026-06-29-langgraph-multi-agent-orchestration-design.md` for the approved design.
- Create `docs/superpowers/plans/2026-06-29-langgraph-multi-agent-orchestration.md` for this executable plan.
- Modify `docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md` only when the LangGraph rollout milestones need to be cross-linked.
- V1.6 code will modify `sources/Alife.Function/Alife.Function.FunctionCaller/*`, `sources/Alife.Function/Alife.Function.DataAgent/*`, and `sources/Alife.Function/Alife.Function.QChat/*`.
- V2.5 sidecar code should live behind a dedicated adapter boundary and should not be called directly from QChat visible reply logic.

---

### Task 1: Preserve The Approved LangGraph Design

**Files:**

- Create: `docs/superpowers/specs/2026-06-29-langgraph-multi-agent-orchestration-design.md`
- Create: `docs/superpowers/plans/2026-06-29-langgraph-multi-agent-orchestration.md`
- Modify: `docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md`

- [ ] **Step 1: Confirm the design file exists**

Run:

```powershell
Test-Path -LiteralPath docs\superpowers\specs\2026-06-29-langgraph-multi-agent-orchestration-design.md
```

Expected:

```text
True
```

- [ ] **Step 2: Confirm the plan file exists**

Run:

```powershell
Test-Path -LiteralPath docs\superpowers\plans\2026-06-29-langgraph-multi-agent-orchestration.md
```

Expected:

```text
True
```

- [ ] **Step 3: Cross-link the LangGraph plan from the V1.6 to V2 roadmap**

Append this section to `docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md`:

```markdown
## LangGraph Multi-Agent Orchestration Link

LangGraph is intentionally deferred until V2.5. The approved design is tracked in `docs/superpowers/specs/2026-06-29-langgraph-multi-agent-orchestration-design.md`, and the execution plan is tracked in `docs/superpowers/plans/2026-06-29-langgraph-multi-agent-orchestration.md`.

V1.6 and V1.7 should focus on Tool Broker observability and DataAgent capability boundaries. V2 should establish provider-neutral persistence and PostgreSQL. Only after those gates pass should V2.5 introduce a LangGraph sidecar pilot for DataAgent analysis workflows.
```

- [ ] **Step 4: Stage ignored docs explicitly**

Run:

```powershell
git add -f docs/superpowers/specs/2026-06-29-langgraph-multi-agent-orchestration-design.md docs/superpowers/plans/2026-06-29-langgraph-multi-agent-orchestration.md docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md
```

Expected: files are staged even though `docs/superpowers/specs/*` and `docs/superpowers/plans/*` are ignored by default.

- [ ] **Step 5: Commit the planning flow**

Run:

```powershell
git commit -m "Document LangGraph multi-agent orchestration plan"
```

Expected: commit succeeds with only docs changes.

---

### Task 2: V1.6 Tool Broker Observability Must Land Before LangGraph

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`
- Test: `Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs`
- Test: `Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs`

- [ ] **Step 1: Write failing reason-code tests**

Add a test to `Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs`:

```csharp
[Test]
public void RouteReturnsStableReasonCodeWhenDataAgentAnalysisSessionIsMissing()
{
    ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();

    ToolRouteDecision decision = router.Route("continue the data analysis", ToolRouteState.Empty);

    Assert.Multiple(() =>
    {
        Assert.That(decision.AllowedTools, Is.Empty);
        Assert.That(decision.DeniedTools, Does.Contain("dataagent_analysis_continue"));
        Assert.That(decision.ReasonCode, Is.EqualTo("dataagent_analysis_session_missing"));
    });
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "Name=RouteReturnsStableReasonCodeWhenDataAgentAnalysisSessionIsMissing" -v:minimal
```

Expected: fail because `ToolRouteDecision.ReasonCode` is not implemented yet.

- [ ] **Step 3: Add reason codes to route decisions**

Update `ToolRouteDecision` to include `ReasonCode` and update router construction sites:

```csharp
public sealed record ToolRouteDecision(
    ToolRouteState State,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DeniedTools,
    string Reason,
    string ReasonCode);
```

Use stable reason codes:

```text
route_allowed
intent_not_matched
owner_private_required
trusted_runtime_required
dataagent_analysis_session_missing
dataagent_analysis_session_inactive
```

- [ ] **Step 4: Add execution audit tests**

Add a test to `Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs`:

```csharp
[Test]
public void GovernedToolDenialPublishesExecutionAudit()
{
    XmlHandlerTable table = new();
    table.RegisterHandler(new DataAgentAnalysisToolHandlerStub());
    table.ExecutionPolicy.SetGovernedToolNames(["dataagent_analysis_continue"]);

    List<XmlFunctionExecutionAuditRecord> records = [];
    table.ExecutionPolicy.ExecutionAudited += (_, record) => records.Add(record);

    _ = table.Handle("<dataagent_analysis_continue><sessionId>missing</sessionId><question>next</question></dataagent_analysis_continue>");

    Assert.Multiple(() =>
    {
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].ToolName, Is.EqualTo("dataagent_analysis_continue"));
        Assert.That(records[0].Allowed, Is.False);
        Assert.That(records[0].ReasonCode, Is.EqualTo("tool_route_required"));
    });
}
```

- [ ] **Step 5: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "Name=GovernedToolDenialPublishesExecutionAudit" -v:minimal
```

Expected: fail because `XmlFunctionExecutionAuditRecord` and `ExecutionAudited` do not exist yet.

- [ ] **Step 6: Implement execution audit**

Add this record near `XmlFunctionExecutionPolicy`:

```csharp
public sealed record XmlFunctionExecutionAuditRecord(
    string ToolName,
    bool Allowed,
    string ReasonCode,
    string Reason,
    string? RouteSessionId,
    DateTimeOffset CreatedAt);
```

Add:

```csharp
public event EventHandler<XmlFunctionExecutionAuditRecord>? ExecutionAudited;
```

Publish an audit record when governed tools are allowed or denied. Do not include raw prompts, XML manuals, or user message bodies in the audit record.

- [ ] **Step 7: Verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore -v:minimal
```

Expected: interpreter tests pass.

- [ ] **Step 8: Commit V1.6 Tool Broker observability slice**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs
git commit -m "Add Tool Broker observability primitives"
```

---

### Task 3: V1.7 DataAgent Capability Boundary

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Write failing provider boundary test**

Add this test:

```csharp
[Test]
public void DataAgentModuleExposesCapabilityProviderNamesWithoutStaticToolManuals()
{
    DataAgentModuleService service = new();

    IReadOnlyList<string> providerNames = service.RegisteredCapabilityProviderNames;
    string prompt = service.CreateSystemPromptForTest();

    Assert.Multiple(() =>
    {
        Assert.That(providerNames, Does.Contain("DataAgentQueryCapabilityProvider"));
        Assert.That(providerNames, Does.Contain("DataAgentAnalysisCapabilityProvider"));
        Assert.That(prompt, Does.Contain("Tool Broker contract"));
        Assert.That(prompt, Does.Not.Contain("<dataagent_query>"));
        Assert.That(prompt, Does.Not.Contain("<dataagent_analysis_continue>"));
    });
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name=DataAgentModuleExposesCapabilityProviderNamesWithoutStaticToolManuals" -v:minimal
```

Expected: fail because provider names are not exposed yet.

- [ ] **Step 3: Add capability provider interface**

Create:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentCapabilityProvider
{
    string Name { get; }
    IReadOnlyList<string> ToolNames { get; }
    void Register(DataAgentModuleService moduleService);
}
```

- [ ] **Step 4: Implement provider registration inside DataAgentModuleService**

Add provider name exposure and register the existing query and analysis handlers through provider objects. Keep existing runtime behavior unchanged.

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: DataAgent tests pass.

- [ ] **Step 6: Commit provider boundary**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs
git commit -m "Define DataAgent capability provider boundary"
```

---

### Task 4: V2 Store Contract For Future LangGraph Runs

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs`

- [ ] **Step 1: Write failing provider-neutral store contract test**

Create:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentStoreContractTests
{
    [Test]
    public void SqliteStoreCanInitializeAndRunReadOnlyPlan()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.db");
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        store.Initialize();

        DataAgentQueryResult result = store.Query(new DataAgentQueryPlan(
            "document_index",
            "find_documents",
            "SELECT path, title FROM document_index LIMIT 20",
            [],
            [],
            20,
            new DataAgentPlannerExplanation("contract", "find_documents", "document_index", "high", ["document"], "contract test")));

        Assert.That(result.Rows.Count, Is.GreaterThanOrEqualTo(0));
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentStoreContractTests" -v:minimal
```

Expected: fail because store contract types do not exist.

- [ ] **Step 3: Add IDataAgentStore**

Create:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentStore
{
    string ProviderName { get; }
    void Initialize();
    DataAgentQueryResult Query(DataAgentQueryPlan plan);
    void RecordAccepted(DataAgentQueryRequest request, DataAgentQueryPlan plan, DataAgentQueryResult result, string summary);
    void RecordRejected(DataAgentQueryRequest request, string reason);
}
```

- [ ] **Step 4: Add SqliteDataAgentStore**

Implement the interface by delegating to existing SQLite schema initializer, query executor, and audit log.

- [ ] **Step 5: Add PostgresDataAgentStore behind environment-gated tests**

Create the type and keep live PostgreSQL tests skipped unless `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is set.

- [ ] **Step 6: Verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: DataAgent tests pass with PostgreSQL live tests skipped unless configured.

- [ ] **Step 7: Commit store contract**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs
git commit -m "Add DataAgent store contract for orchestration"
```

---

### Task 5: V2.5 LangGraph Sidecar Pilot Contract

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/AgentWorkflowModels.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IAgentWorkflowOrchestrator.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DisabledAgentWorkflowOrchestrator.cs`
- Test: `Tests/Alife.Test.DataAgent/AgentWorkflowOrchestratorTests.cs`

- [ ] **Step 1: Write failing disabled-orchestrator test**

Create:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class AgentWorkflowOrchestratorTests
{
    [Test]
    public async Task DisabledOrchestratorReturnsFeatureDisabledWithoutCallingSidecar()
    {
        IAgentWorkflowOrchestrator orchestrator = new DisabledAgentWorkflowOrchestrator();

        AgentWorkflowResult result = await orchestrator.RunAsync(new AgentWorkflowRequest(
            "workflow-1",
            "caller-1",
            "session-1",
            "Analyze DataAgent readiness",
            ["dataagent_query"]),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AgentWorkflowStatus.FeatureDisabled));
            Assert.That(result.Steps, Is.Empty);
            Assert.That(result.FinalText, Is.EqualTo("LangGraph orchestration is disabled."));
        });
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~AgentWorkflowOrchestratorTests" -v:minimal
```

Expected: fail because workflow types do not exist.

- [ ] **Step 3: Add workflow model records**

Create `AgentWorkflowModels.cs`:

```csharp
namespace Alife.Function.DataAgent;

public enum AgentWorkflowStatus
{
    Completed,
    FeatureDisabled,
    PolicyDenied,
    TimedOut,
    InvalidOutput,
    SidecarUnavailable
}

public sealed record AgentWorkflowRequest(
    string WorkflowId,
    string CallerId,
    string SessionId,
    string Goal,
    IReadOnlyList<string> AllowedCapabilities);

public sealed record AgentWorkflowStep(
    string AgentName,
    string Action,
    string Status,
    string Summary);

public sealed record AgentWorkflowResult(
    AgentWorkflowStatus Status,
    IReadOnlyList<AgentWorkflowStep> Steps,
    string FinalText);
```

- [ ] **Step 4: Add disabled orchestrator**

Create `IAgentWorkflowOrchestrator.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IAgentWorkflowOrchestrator
{
    Task<AgentWorkflowResult> RunAsync(AgentWorkflowRequest request, CancellationToken cancellationToken);
}
```

Create `DisabledAgentWorkflowOrchestrator.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DisabledAgentWorkflowOrchestrator : IAgentWorkflowOrchestrator
{
    public Task<AgentWorkflowResult> RunAsync(AgentWorkflowRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentWorkflowResult(
            AgentWorkflowStatus.FeatureDisabled,
            [],
            "LangGraph orchestration is disabled."));
    }
}
```

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~AgentWorkflowOrchestratorTests" -v:minimal
```

Expected: orchestrator tests pass.

- [ ] **Step 6: Commit V2.5 contract shell**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/AgentWorkflowModels.cs sources/Alife.Function/Alife.Function.DataAgent/IAgentWorkflowOrchestrator.cs sources/Alife.Function/Alife.Function.DataAgent/DisabledAgentWorkflowOrchestrator.cs Tests/Alife.Test.DataAgent/AgentWorkflowOrchestratorTests.cs
git commit -m "Add LangGraph orchestration contract shell"
```

---

### Task 6: Final Verification Gate

**Files:**

- All files changed by previous tasks.

- [ ] **Step 1: Build**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal
```

Expected: exit code 0.

- [ ] **Step 2: Test**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal
```

Expected: exit code 0.

- [ ] **Step 3: Required readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected: both scripts exit with code 0 and report `0 required missing`.

- [ ] **Step 4: Push**

Run:

```powershell
git push alife-byastralfox HEAD
```

Expected: branch is uploaded to `git@github.com:hushu1232/Alife-byastralfox.git`.

## Immediate Next Action

Start with Task 1 in the current V1.5 branch, then create the V1.6 implementation branch:

```text
dataagent-v1.6-tool-broker-observability
```

Base it on `dataagent-v1.5-tool-broker`, not on `master`, until V1.5 is merged.
