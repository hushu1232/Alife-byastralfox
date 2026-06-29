# DataAgent V1.6 to V2 Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the V1.5 Tool Broker runtime gate into an observable, policy-driven DataAgent foundation that can open a DataAgent plugin boundary in V1.x and migrate persistence to PostgreSQL in V2.

**Architecture:** V1.6 keeps the existing .NET 9 module layout and strengthens the Tool Broker with diagnostics, audit records, and route-decision contracts without introducing the V2 supervisor runtime. V1.7 defines a plugin-like DataAgent boundary while still using the current SQLite-backed V1.x store. V2 introduces PostgreSQL behind provider-neutral interfaces, then V3 can evolve to a stronger supervisor/tool-market governance model.

**Tech Stack:** .NET 9, NUnit, PowerShell readiness scripts, `Alife.Function.FunctionCaller`, `Alife.Function.DataAgent`, `Alife.Function.QChat`, SQLite for V1.x, PostgreSQL planned for V2.

---

## Current Baseline

V1.5 uses scheme 3: centralized Tool Broker runtime gates. DataAgent XML tools are no longer statically exposed in the DataAgent prompt; the model sees per-turn `[tool_route_context]` only when `ToolCapabilityRouter` allows a route. `XmlFunctionExecutionPolicy` fail-closes governed tools without an allowed route, and session-scoped DataAgent analysis tools require the XML `sessionId` to match the route state.

Important files:

- `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityManifest.cs`: capability metadata and preconditions.
- `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs`: per-turn tool route decisions.
- `sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs`: route state and route decision records.
- `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`: `[tool_route_context]` injection and scoped route state.
- `sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs`: `XmlFunctionExecutionPolicy` runtime execution gate.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`: DataAgent module registration and dynamic tool route contract.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs`: analysis session state machine.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`: session XML tools.
- `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`: QChat sender/private route-state wiring.
- `tools/check-dataagent-readiness.ps1`: DataAgent required readiness gate.
- `tools/check-qchat-engineering-map.ps1`: QChat Harness/Loop/Prompt engineering map.

## File Structure For Next Work

- Modify `sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs` to add serializable route diagnostics records.
- Modify `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs` to emit stable decision reasons.
- Modify `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs` to publish route diagnostics through an internal event or callback.
- Modify `sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs` to expose execution-denial reasons without exposing hidden tool manuals to visible chat.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolBrokerAuditLog.cs` for DataAgent route audit persistence in V1.x.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs` to add the V1.x audit table.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs` and `tools/check-dataagent-readiness.ps1` to make route audit/readiness evidence required.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs` and `sources/Alife.Function/Alife.Function.QChat/QChatService.cs` to surface owner-only diagnostics.
- Create tests in `Tests/Alife.Test.Interpreter`, `Tests/Alife.Test.DataAgent`, and `Tests/Alife.Test.QChat` before production code changes.

---

### Task 1: V1.6 Tool Broker Decision Diagnostics

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs`
- Test: `Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs`

- [ ] **Step 1: Write the failing router diagnostics test**

Add this test to `Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs`:

```csharp
[Test]
public void RouteReturnsStableDecisionReasonCodes()
{
    ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
    ToolRouteState state = ToolRouteState.Empty;

    ToolRouteDecision decision = router.Route("继续分析这个会话", state);

    Assert.Multiple(() =>
    {
        Assert.That(decision.AllowedTools, Is.Empty);
        Assert.That(decision.DeniedTools, Does.Contain("dataagent_analysis_continue"));
        Assert.That(decision.ReasonCode, Is.EqualTo("dataagent_analysis_session_missing"));
        Assert.That(decision.Reason, Does.Contain("session"));
    });
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "Name=RouteReturnsStableDecisionReasonCodes" -v:minimal
```

Expected: fail because `ToolRouteDecision` does not yet expose `ReasonCode`.

- [ ] **Step 3: Add stable fields to the route decision model**

Extend `ToolRouteDecision` in `sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs` with explicit reason fields:

```csharp
public sealed record ToolRouteDecision(
    ToolRouteState State,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DeniedTools,
    string Reason,
    string ReasonCode)
{
    public bool HasAllowedTools => AllowedTools.Count > 0;
}
```

Update every construction site in `ToolCapabilityRouter.Route` so each decision has a stable `ReasonCode`. Use codes such as `route_allowed`, `dataagent_analysis_session_missing`, `owner_private_required`, `trusted_runtime_required`, and `intent_not_matched`.

- [ ] **Step 4: Run focused router tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "FullyQualifiedName~ToolCapabilityRouterTests" -v:minimal
```

Expected: all `ToolCapabilityRouterTests` pass.

- [ ] **Step 5: Commit the diagnostics model**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs
git commit -m "Add Tool Broker route decision reason codes"
```

---

### Task 2: V1.6 Execution Gate Audit Contract

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`
- Test: `Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs`

- [ ] **Step 1: Write the failing execution-denial audit test**

Add this test to `Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs`:

```csharp
[Test]
public void HandlePublishesDenialAuditWhenGovernedToolIsBlocked()
{
    XmlHandlerTable table = new();
    table.RegisterHandler(new DataAgentAnalysisToolHandlerStub());
    table.ExecutionPolicy.SetGovernedToolNames(["dataagent_analysis_continue"]);

    List<XmlFunctionExecutionAuditRecord> records = [];
    table.ExecutionPolicy.ExecutionAudited += (_, record) => records.Add(record);

    string output = table.Handle("<dataagent_analysis_continue><sessionId>missing</sessionId><question>next</question></dataagent_analysis_continue>");

    Assert.Multiple(() =>
    {
        Assert.That(output, Does.Contain("tool_route_required"));
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].ToolName, Is.EqualTo("dataagent_analysis_continue"));
        Assert.That(records[0].Allowed, Is.False);
        Assert.That(records[0].ReasonCode, Is.EqualTo("tool_route_required"));
    });
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "Name=HandlePublishesDenialAuditWhenGovernedToolIsBlocked" -v:minimal
```

Expected: fail because `XmlFunctionExecutionAuditRecord` and `ExecutionAudited` do not exist yet.

- [ ] **Step 3: Add an internal execution audit record**

Add this record near `XmlFunctionExecutionPolicy` in `XmlStruct.cs`:

```csharp
public sealed record XmlFunctionExecutionAuditRecord(
    string ToolName,
    bool Allowed,
    string ReasonCode,
    string Reason,
    string? RouteSessionId,
    DateTimeOffset CreatedAt);
```

Add an event on `XmlFunctionExecutionPolicy`:

```csharp
public event EventHandler<XmlFunctionExecutionAuditRecord>? ExecutionAudited;
```

Call the event both when a governed tool is allowed and when it is denied. Use UTC timestamps and stable reason codes. Do not include raw user messages or hidden prompt/tool documentation in this audit record.

- [ ] **Step 4: Run focused policy tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "FullyQualifiedName~XmlFunctionPolicyTests" -v:minimal
```

Expected: all `XmlFunctionPolicyTests` pass.

- [ ] **Step 5: Commit the execution audit contract**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs
git commit -m "Add Tool Broker execution audit records"
```

---

### Task 3: V1.6 DataAgent Route Audit Persistence

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolBrokerAuditLog.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentToolBrokerAuditLogTests.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Write the failing audit-log persistence test**

Create `Tests/Alife.Test.DataAgent/DataAgentToolBrokerAuditLogTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentToolBrokerAuditLogTests
{
    [Test]
    public void RecordAndReadAllPreservesRouteDecision()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.db");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentToolBrokerAuditLog log = new(databasePath);

        log.Record(new DataAgentToolBrokerAuditRecord(
            "session-1",
            "dataagent_analysis_continue",
            false,
            "tool_route_required",
            "route is required",
            DateTimeOffset.Parse("2026-06-29T00:00:00Z")));

        IReadOnlyList<DataAgentToolBrokerAuditRecord> records = log.ReadAll();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].SessionId, Is.EqualTo("session-1"));
            Assert.That(records[0].ToolName, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(records[0].Allowed, Is.False);
            Assert.That(records[0].ReasonCode, Is.EqualTo("tool_route_required"));
        });
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name=RecordAndReadAllPreservesRouteDecision" -v:minimal
```

Expected: fail because the audit log type and table do not exist.

- [ ] **Step 3: Add the V1.x SQLite audit table**

In `DataAgentSchemaInitializer.Initialize`, add this table creation:

```sql
CREATE TABLE IF NOT EXISTS tool_broker_audit (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id TEXT NOT NULL,
    tool_name TEXT NOT NULL,
    allowed INTEGER NOT NULL,
    reason_code TEXT NOT NULL,
    reason TEXT NOT NULL,
    created_at TEXT NOT NULL
);
```

- [ ] **Step 4: Add the audit log class**

Create `DataAgentToolBrokerAuditLog.cs` with a record and SQLite-backed read/write methods:

```csharp
using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed record DataAgentToolBrokerAuditRecord(
    string SessionId,
    string ToolName,
    bool Allowed,
    string ReasonCode,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed class DataAgentToolBrokerAuditLog(string databasePath)
{
    public void Record(DataAgentToolBrokerAuditRecord record)
    {
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tool_broker_audit (session_id, tool_name, allowed, reason_code, reason, created_at)
            VALUES ($session_id, $tool_name, $allowed, $reason_code, $reason, $created_at)
            """;
        command.Parameters.AddWithValue("$session_id", record.SessionId);
        command.Parameters.AddWithValue("$tool_name", record.ToolName);
        command.Parameters.AddWithValue("$allowed", record.Allowed ? 1 : 0);
        command.Parameters.AddWithValue("$reason_code", record.ReasonCode);
        command.Parameters.AddWithValue("$reason", record.Reason);
        command.Parameters.AddWithValue("$created_at", record.CreatedAt.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadAll()
    {
        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, tool_name, allowed, reason_code, reason, created_at
            FROM tool_broker_audit
            ORDER BY id
            """;

        List<DataAgentToolBrokerAuditRecord> records = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new DataAgentToolBrokerAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2) == 1,
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return records;
    }
}
```

- [ ] **Step 5: Add readiness evidence**

In `DataAgentReadiness.CheckCore`, add a required check named `ToolBrokerAuditLogPresent` that initializes a temp database, records one denied route, reads it back, and passes only when `ReasonCode == "tool_route_required"`.

Update `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs` expected count and containment assertions to include `ToolBrokerAuditLogPresent`.

- [ ] **Step 6: Run DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass.

- [ ] **Step 7: Commit DataAgent audit persistence**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolBrokerAuditLog.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentToolBrokerAuditLogTests.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "Persist DataAgent Tool Broker audit records"
```

---

### Task 4: V1.6 Owner-Only Route Diagnostics In QChat

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatToolRouteStateWiringTests.cs`

- [ ] **Step 1: Write the failing diagnostics command test**

Add this test to `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`:

```csharp
[Test]
public void TryHandleToolBrokerDiagnosticsShowsRecentRouteStateForOwner()
{
    QChatDiagnosticsRuntimeState state = new(
        RecentDecisionTrace: "reply=direct",
        RecentToolRouteTrace: "allowed=dataagent_analysis_continue; denied=dataagent_query; reason=route_allowed");

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag toolbroker",
        QChatAgentRoute.Xiayu,
        QChatAgentProfile.Xiayu,
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("Tool Broker"));
        Assert.That(result.Text, Does.Contain("dataagent_analysis_continue"));
        Assert.That(result.Text, Does.Not.Contain("[tool_route_context]"));
    });
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name=TryHandleToolBrokerDiagnosticsShowsRecentRouteStateForOwner" -v:minimal
```

Expected: fail because `RecentToolRouteTrace` is not yet exposed.

- [ ] **Step 3: Extend diagnostics runtime state**

Add a nullable `RecentToolRouteTrace` property to `QChatDiagnosticsRuntimeState`. Keep visible output bounded to a short line and never return raw prompt text or raw XML manuals.

- [ ] **Step 4: Wire recent Tool Broker state in QChat**

In `QChatService.DispatchToModelAsync`, after creating the route state and before calling `ChatBot.ChatAsync`, record a bounded trace such as:

```text
allowed=dataagent_analysis_continue; denied=dataagent_query; reason=route_allowed
```

Store it in the same diagnostics path used by owner-only status commands. Do not send this trace to normal chat replies.

- [ ] **Step 5: Run QChat focused tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatToolRouteStateWiringTests" -v:minimal
```

Expected: relevant QChat diagnostics and Tool Route state tests pass.

- [ ] **Step 6: Commit QChat route diagnostics**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs Tests/Alife.Test.QChat/QChatToolRouteStateWiringTests.cs
git commit -m "Expose owner-only Tool Broker diagnostics"
```

---

### Task 5: V1.6 Readiness Gates

**Files:**

- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV15ReadinessTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Add failing readiness assertions**

Add checks that require all new V1.6 evidence:

```csharp
Assert.That(output, Does.Contain("ToolBrokerRouteDecisionReasonCodesPresent"));
Assert.That(output, Does.Contain("ToolBrokerExecutionAuditPresent"));
Assert.That(output, Does.Contain("ToolBrokerAuditLogPresent"));
Assert.That(output, Does.Contain("QChatOwnerToolBrokerDiagnosticsPresent"));
```

- [ ] **Step 2: Run readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV15ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: fail until the scripts include the new required markers.

- [ ] **Step 3: Update DataAgent readiness script**

Add required Tool Broker checks in `tools/check-dataagent-readiness.ps1`:

```powershell
New-Check -Group "ToolBroker" -Name "ToolBrokerRouteDecisionReasonCodesPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs" @("ReasonCode", "ToolRouteDecision")) -Detail "route decision reason code markers"
New-Check -Group "ToolBroker" -Name "ToolBrokerExecutionAuditPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs" @("XmlFunctionExecutionAuditRecord", "ExecutionAudited")) -Detail "execution audit markers"
New-Check -Group "ToolBroker" -Name "ToolBrokerAuditLogPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolBrokerAuditLog.cs" @("DataAgentToolBrokerAuditRecord", "tool_broker_audit")) -Detail "DataAgent Tool Broker audit log markers"
```

- [ ] **Step 4: Update QChat engineering map**

Add a required Harness check in `tools/check-qchat-engineering-map.ps1`:

```powershell
Add-Check -Group "Harness" -Name "QChat owner Tool Broker diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("RecentToolRouteTrace", "Tool Broker")
```

- [ ] **Step 5: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected: both scripts exit with code 0. DataAgent required count increases by the number of new checks. QChat engineering map still reports `0 required missing`.

- [ ] **Step 6: Commit readiness gate updates**

Run:

```powershell
git add tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.DataAgent/DataAgentV15ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Require V1.6 Tool Broker observability readiness"
```

---

### Task 6: V1.7 DataAgent Plugin Boundary

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Write the failing plugin-boundary test**

Add this test to `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`:

```csharp
[Test]
public void ModuleServiceRegistersCapabilityProvidersWithoutPromptToolLeakage()
{
    DataAgentModuleService service = new();

    IReadOnlyList<string> providerNames = service.RegisteredCapabilityProviderNames;
    string prompt = service.CreateSystemPromptForTest();

    Assert.Multiple(() =>
    {
        Assert.That(providerNames, Does.Contain("DataAgentQueryCapabilityProvider"));
        Assert.That(prompt, Does.Contain("Tool Broker contract"));
        Assert.That(prompt, Does.Not.Contain("<dataagent_query>"));
        Assert.That(prompt, Does.Not.Contain("<dataagent_analysis_continue>"));
    });
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name=ModuleServiceRegistersCapabilityProvidersWithoutPromptToolLeakage" -v:minimal
```

Expected: fail until provider registration is explicit and test-visible.

- [ ] **Step 3: Add a provider interface**

Create `IDataAgentCapabilityProvider.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentCapabilityProvider
{
    string Name { get; }
    IReadOnlyList<string> ToolNames { get; }
    void Register(DataAgentModuleService moduleService);
}
```

Implement query and analysis providers in the same DataAgent module, keeping the runtime behavior identical. This is not a Codex plugin and not a separate package yet; it is the internal boundary that makes a future plugin module possible.

- [ ] **Step 4: Run DataAgent module tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: all module service tests pass.

- [ ] **Step 5: Commit V1.7 plugin boundary**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentCatalog.cs Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs
git commit -m "Define DataAgent capability provider boundary"
```

---

### Task 7: V2 PostgreSQL Persistence Boundary

**Files:**

- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs`

- [ ] **Step 1: Write provider-neutral store contract tests**

Create `Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentStoreContractTests
{
    [Test]
    public void SqliteStoreImplementsDocumentIndexQueryContract()
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

- [ ] **Step 2: Run the contract test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentStoreContractTests" -v:minimal
```

Expected: fail because `IDataAgentStore` and provider implementations do not exist yet.

- [ ] **Step 3: Introduce the provider-neutral interface**

Create `IDataAgentStore.cs`:

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

- [ ] **Step 4: Move current SQLite behavior behind the interface**

Create `SqliteDataAgentStore.cs` that delegates to the existing SQLite schema initializer, executor, and audit log. Update `DataAgentService` to depend on `IDataAgentStore`, while preserving a constructor overload that accepts the current database path for V1.x compatibility.

- [ ] **Step 5: Add PostgreSQL as V2 implementation behind the same contract**

Create `PostgresDataAgentStore.cs` with constructor:

```csharp
public PostgresDataAgentStore(string connectionString)
```

The first V2 implementation should support schema initialization, read-only plan execution, and audit writes. Keep live PostgreSQL integration tests skipped unless `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is set.

- [ ] **Step 6: Run provider-neutral and existing DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass. PostgreSQL live tests are skipped when the connection environment variable is absent.

- [ ] **Step 7: Commit V2 store boundary**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs Tests/Alife.Test.DataAgent/DataAgentStoreContractTests.cs
git commit -m "Add DataAgent store provider boundary"
```

---

### Task 8: Full Verification Before V2 Start

**Files:**

- Modify only files touched by Tasks 1-7.

- [ ] **Step 1: Build with .NET 9**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal
```

Expected: exit code 0, `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 2: Run the full test suite**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal
```

Expected: exit code 0. Existing live tests remain skipped unless live credentials and services are intentionally enabled.

- [ ] **Step 3: Run required readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected: both exit with code 0 and report `0 required missing`.

- [ ] **Step 4: Confirm V2 start criteria**

V2 can start only when these are true:

- Tool Broker decisions have stable reason codes.
- Execution-denial audit records exist and are test-covered.
- DataAgent can persist Tool Broker audit evidence.
- QChat owner diagnostics can inspect recent Tool Broker state without leaking hidden prompts or XML manuals.
- DataAgent provider boundary exists.
- SQLite remains compatible for V1.x.
- PostgreSQL is isolated behind `IDataAgentStore` and live tests are environment-gated.

- [ ] **Step 5: Commit final readiness updates**

Run:

```powershell
git status --short
git add docs/superpowers/plans/2026-06-29-dataagent-v1.6-v2-roadmap.md
git commit -m "Document DataAgent V1.6 to V2 roadmap"
```

---

## LangGraph Multi-Agent Orchestration Link

LangGraph is intentionally deferred until V2.5. The approved design is tracked in `docs/superpowers/specs/2026-06-29-langgraph-multi-agent-orchestration-design.md`, and the execution plan is tracked in `docs/superpowers/plans/2026-06-29-langgraph-multi-agent-orchestration.md`.

V1.6 and V1.7 should focus on Tool Broker observability and DataAgent capability boundaries. V2 should establish provider-neutral persistence and PostgreSQL. Only after those gates pass should V2.5 introduce a LangGraph sidecar pilot for DataAgent analysis workflows.

V1.7 capability metadata should also preserve the later multi-agent coordination norm: split permission validation, SQL generation, and report interpretation into dedicated nodes; normalize business terms through a pre-scheduling Scenario Knowledge Package; persist intermediate state through checkpoints; provide degradation paths for failed nodes; and stream linked-run progress to owner diagnostics or a frontend. These are V2.5/V3 orchestration requirements, not V1.7 runtime dependencies.

---

## V3 Direction

V3 should evolve from scheme 3 to scheme 4: a supervisor-controlled tool governance layer. The supervisor should own tool budget, tool conflict resolution, per-agent capability leases, cross-module audit queries, and policy escalation. V3 should not be started until V2 has PostgreSQL-backed persistence, provider-neutral store contracts, and reliable Tool Broker observability.

## Execution Recommendation

Use V1.6 as the next implementation branch. Do not start V2 PostgreSQL migration before V1.6 observability is merged and V1.7 provider boundaries are stable. The safest next branch name is:

```text
dataagent-v1.6-tool-broker-observability
```
