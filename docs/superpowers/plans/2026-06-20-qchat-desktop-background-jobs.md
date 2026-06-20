# QChat Desktop Background Jobs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make approved QChat desktop execution non-blocking by queueing whitelisted business jobs and exposing compact job status commands.

**Architecture:** Move the desktop business whitelist into a registry, wrap approved draft execution in a local background task queue, and expose job summaries through existing QChat desktop owner commands. QChat returns immediately after queueing while the background worker updates job status and marks the draft executed only after success.

**Tech Stack:** C#/.NET 9, NUnit, existing `Alife.Function.DesktopControl` and `Alife.Function.QChat` modules, local JSONL persistence.

---

### Task 1: Desktop Business Action Registry

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DesktopControl/DesktopBusinessActionRegistry.cs`
- Modify: `sources/Alife.Function/Alife.Function.DesktopControl/DesktopBusinessExecutionService.cs`
- Test: `Tests/Alife.Test.DesktopControl/DesktopBusinessActionRegistryTests.cs`

- [ ] **Step 1: Write failing registry tests**

Add tests proving `open notepad` resolves and `open calculator` is unsupported.

- [ ] **Step 2: Run registry tests and verify RED**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.DesktopControl\Alife.Test.DesktopControl.csproj --no-restore --filter DesktopBusinessActionRegistry
```

Expected: build or tests fail because registry types do not exist.

- [ ] **Step 3: Implement the registry and wire the Windows executor through it**

Create a registry with exact normalized action matching and keep the process-start behavior for `open notepad`.

- [ ] **Step 4: Run registry tests and verify GREEN**

Run the same filtered command. Expected: all filtered tests pass.

### Task 2: Desktop Business Job Queue

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DesktopControl/DesktopBusinessTaskQueue.cs`
- Modify: `sources/Alife.Function/Alife.Function.DesktopControl/DesktopBusinessExecutionService.cs`
- Test: `Tests/Alife.Test.DesktopControl/DesktopBusinessTaskQueueTests.cs`

- [ ] **Step 1: Write failing queue tests**

Add tests for queueing a supported draft, denying an unsupported draft before queueing, not queueing the same draft twice while running, marking a successful draft executed, and leaving a failed draft approved.

- [ ] **Step 2: Run queue tests and verify RED**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.DesktopControl\Alife.Test.DesktopControl.csproj --no-restore --filter DesktopBusinessTaskQueue
```

Expected: build or tests fail because queue types do not exist.

- [ ] **Step 3: Implement the queue**

Implement compact JSONL-backed job state with in-memory recent status, background execution via fire-and-forget task guarded by a single `SemaphoreSlim`, and draft status update on success only.

- [ ] **Step 4: Run queue tests and verify GREEN**

Run the same filtered command. Expected: all filtered tests pass.

### Task 3: QChat Command Integration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DesktopControl/DesktopReadOnlyActions.cs`
- Modify: `sources/Alife.Function/Alife.Function.DesktopControl/DesktopCapabilityRegistry.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing QChat tests**

Update desktop draft execute tests to expect `desktop_execution=queued`. Add tests for `/qchat desktop jobs recent`, `/qchat desktop job <job_id>`, and a slow queued job that does not block a later owner command.

- [ ] **Step 2: Run QChat filtered tests and verify RED**

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter OwnerXiayuQChatDesktop
```

Expected: tests fail because job commands and queueing behavior are not wired.

- [ ] **Step 3: Wire queue and job commands**

Construct a `DesktopBusinessTaskQueue` in the default desktop gateway path when a draft controller is available. Add `qchat.desktop.jobs.recent` and `qchat.desktop.job.detail` actions and map `/qchat desktop jobs recent` plus `/qchat desktop job <job_id>` in QChat.

- [ ] **Step 4: Run QChat filtered tests and verify GREEN**

Run the same filtered command. Expected: filtered tests pass.

### Task 4: Full Verification and Commit

**Files:**
- Verify all modified production and test files.

- [ ] **Step 1: Run desktop control tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.DesktopControl\Alife.Test.DesktopControl.csproj --no-restore
```

Expected: 0 failures.

- [ ] **Step 2: Run QChat tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Expected: 0 failures, existing live tests may remain skipped.

- [ ] **Step 3: Run solution tests**

```powershell
dotnet test D:\Alife\Alife.slnx --no-restore
```

Expected: 0 failures.

- [ ] **Step 4: Run diff checks**

```powershell
git diff --check
git diff --cached --check
```

Expected: exit code 0. Existing local Git ignore permission warning is acceptable.

- [ ] **Step 5: Commit implementation**

```powershell
git add sources/Alife.Function/Alife.Function.DesktopControl sources/Alife.Function/Alife.Function.QChat Tests/Alife.Test.DesktopControl Tests/Alife.Test.QChat
git commit -m "Queue QChat desktop draft execution"
```

Expected: one implementation commit.
