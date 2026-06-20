# Agent Control Center Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a read-oriented AstralFox Agent control center inside the existing Alife module UI.

**Architecture:** Add one new Agent module with a custom Razor `EditorUI`. The module aggregates existing Agent services into a UI snapshot and exposes read-only methods for pending workspace proposals where needed. High-risk actions remain outside first-version UI execution.

**Tech Stack:** C#/.NET 9, Blazor Razor components, AntDesign Blazor, NUnit.

---

### Task 1: Workspace Proposal Read API

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWorkspaceService.cs`
- Test: `Tests/Alife.Test.Framework/AgentCapabilityServiceTests.cs`

- [ ] **Step 1: Write failing test**

Add a test named `WorkspaceServiceListsPendingReplaceProposals` that creates a workspace, calls `ProposeReplace`, then asserts `GetPendingProposals()` returns the proposal without mutating the file.

- [ ] **Step 2: Verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter WorkspaceServiceListsPendingReplaceProposals
```

Expected: compile failure or test failure because `GetPendingProposals` does not exist.

- [ ] **Step 3: Implement read API**

Add:

```csharp
public IReadOnlyList<AgentWorkspacePatchProposal> GetPendingProposals()
{
    lock (patchProposals)
    {
        return patchProposals.Values
            .OrderByDescending(proposal => proposal.CreatedAt)
            .ToArray();
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run the same focused test and expect 1 passing test.

### Task 2: Agent Control Snapshot Service

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentControlCenterService.cs`
- Test: `Tests/Alife.Test.Framework/AgentCapabilityServiceTests.cs`

- [ ] **Step 1: Write failing test**

Add `AgentControlCenterBuildsReadOnlySnapshot` that constructs audit, command, task, workspace, diagnostics, project status, and issue report dependencies, then asserts a snapshot includes latest task, recent audit, allowed commands, workspace proposals, and issue report data.

- [ ] **Step 2: Verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter AgentControlCenterBuildsReadOnlySnapshot
```

Expected: compile failure because `AgentControlCenterService` and snapshot types do not exist.

- [ ] **Step 3: Implement service**

Create a module with:

- `[Module("Agent Control Center", ..., editorUI: typeof(AgentControlCenterServiceUI), LaunchOrder = -61)]`
- Constructor dependencies defaulting to existing service instances.
- `BuildSnapshot(ChatRuntimeState? runtime = null)` returning:
  - runtime snapshot/report;
  - latest task;
  - recent audit entries;
  - allowed commands;
  - issue report;
  - pending workspace proposals.

- [ ] **Step 4: Verify GREEN**

Run focused test and expect 1 passing test.

### Task 3: Blazor UI Component

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentControlCenterServiceUI.razor`
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentControlCenterService.cs` if namespace/type reference requires adjustment.

- [ ] **Step 1: Implement component**

Use existing AntDesign patterns:

- `@namespace Alife.Function.Agent`
- `@inherits Alife.Framework.ModuleUIBase<AgentControlCenterService>`
- compact `.agent-control-shell`
- metric row cards
- current task panel
- workspace proposal panel
- allowed command panel
- recent audit panel
- error report panel

- [ ] **Step 2: Build**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter AgentControlCenterBuildsReadOnlySnapshot
```

Expected: build succeeds and focused test passes.

### Task 4: Full Verification and Staging

**Files:**
- Stage the new service, UI, tests, and plan/spec docs.

- [ ] **Step 1: Run full solution tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Alife.slnx' --no-restore
```

Expected: 0 failures.

- [ ] **Step 2: Stage files**

Run:

```powershell
git add -- sources/Alife.Function/Alife.Function.MessageFilter/AgentWorkspaceService.cs sources/Alife.Function/Alife.Function.MessageFilter/AgentControlCenterService.cs sources/Alife.Function/Alife.Function.MessageFilter/AgentControlCenterServiceUI.razor Tests/Alife.Test.Framework/AgentCapabilityServiceTests.cs docs/superpowers/specs/2026-06-14-agent-control-center-design.md docs/superpowers/plans/2026-06-14-agent-control-center.md
```

Expected: files staged for later upload.

---

## Self-Review

Spec coverage:

- Existing UI integration: Task 2 and Task 3.
- Read-oriented first version: Task 2 and Task 3.
- Workspace proposal visibility: Task 1 and Task 3.
- Audit, command, task, issue report visibility: Task 2 and Task 3.
- No high-risk UI bypass: Task 3 keeps actions read-oriented.

No placeholder tasks remain. Type names are consistent across tasks.
