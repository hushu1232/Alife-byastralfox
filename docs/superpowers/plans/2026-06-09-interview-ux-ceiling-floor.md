# Alife Interview And User Experience Ceiling Floor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise Alife's upper ceiling and lower floor from two separate viewpoints: interview value and real user experience.

**Architecture:** Keep Alife's existing "role container + plugin modules + local multi-process desk pet + WebBridge" architecture. Improve the system by completing missing reliability links, adding measurable observability, standardizing end-to-end demos, and deepening the Live2D/PAD user-facing loop instead of adding unrelated new features.

**Tech Stack:** .NET 9, WPF, WebView2, Blazor Hybrid, Semantic Kernel, DuckDB, NUnit, Live2D via WebView2/pixi-live2d, Alife module system, WebBridge, PAD emotion engine.

---

## 0. Summary Of Previous Analysis

Alife already has a rare foundation for both interviews and product use: it is not a simple chat UI, but a local AI agent runtime with pluginized capabilities, long-term memory, model services, browser/Python/MCP tools, a Live2D desk pet, and FOXD/WebBridge ecosystem direction.

The current strong points are:
- A plugin-oriented runtime centered around `ChatActivity`, `InteractiveModule`, and `ModuleSystem`.
- Multi-modal functions: memory, browser, Python, vision, auditory, speech, desk pet, QQ, MCP, and Skill modules.
- Live2D body loop already partly closed: JS parameter control, C# IPC, `PetServer.SetParams(...)`, and PAD emotion state driving Live2D parameters.
- WebBridge has reached config sync, local character upsert, asset sync foundation, and polling lifecycle.
- Several low-floor technical debts have already been fixed: ChatBot lifecycle, event/message caps, PythonPipe health recovery, plugin safety boundary, `InteractiveModule` update task lifecycle, and `MemoryStorage` DuckDB connection reuse.

The current weak points are:
- Some reliability links remain incomplete: `WindowsPlatform.Command` still blocks synchronously; `MemoryStorage` now supports disposal but the upper lifecycle has not yet been connected; non-UI `async void` remains in some modules.
- User-visible validation is still thin: Live2D/WebView2 visual smoke tests and real FOXD end-to-end checks are not yet closed.
- Resource governance is not yet platform-grade: Vision/Speech models still need pooling, lazy loading, and idle release.
- Observability is still missing: when latency, IPC, model loading, WebBridge, or memory search fails, there is no unified local diagnostic snapshot.
- The project has high feature width. Its next improvement should deepen critical chains rather than add more unrelated plugins.

## 1. Planning Principles

1. Stabilize before expanding.
   Why: Alife's feature surface is already broad. Adding more features before fixing blocking calls, resource lifetimes, visual smoke checks, and diagnostics will make failures harder to isolate.

2. Prefer demonstrable chains over isolated features.
   Why: Interviewers and users both value complete workflows. A chain like "chat event -> PAD change -> Live2D expression -> memory write -> diagnostic snapshot" is more valuable than five disconnected modules.

3. Separate interview value from user value.
   Why: Interview value rewards architecture, evidence, metrics, and failure handling. User value rewards speed, stability, emotional continuity, and clear recovery. Some work helps both, but the acceptance criteria are different.

4. Keep every task testable and small.
   Why: The project already follows RED/GREEN records in `MASTER_EXECUTION_PLAN.md`. Continuing that habit protects the lower floor and makes future claims credible.

---

## 2. Execution Track A: Interview Value

### Objective

Make Alife easy to explain as a serious local AI agent runtime rather than a collection of AI features.

### Interview Upper Ceiling

The upper ceiling should become:
- "I built a local AI agent runtime with plugin governance, long-term memory, multimodal tools, a visible Live2D body, PAD emotion state, and WebBridge ecosystem integration."
- "I can prove its architecture with lifecycle tests, diagnostics, resource governance, metrics, and end-to-end demos."

### Interview Lower Floor

The lower floor should become:
- No obvious UI blocking bug.
- No unresolved resource-lifetime story in recently modified code.
- No unbounded model/memory/process behavior in critical flows.
- Every important claim has a command, test, metric, or demo artifact behind it.

### Task A1: Create A One-Page Architecture Story

**Files:**
- Create: `docs/architecture/alife-runtime-story.md`
- Reference: `README.md`
- Reference: `MASTER_EXECUTION_PLAN.md`

**Why this matters:** Interviewers do not read the whole repository. A concise story prevents the project from looking like a loose feature pile and gives every later technical detail a place in the system.

- [ ] **Step 1: Create the architecture docs directory**

Run:

```powershell
New-Item -ItemType Directory -Path 'D:\Alife\docs\architecture' -Force
```

Expected:
- Directory exists at `D:\Alife\docs\architecture`.

- [ ] **Step 2: Write the architecture story**

Create `docs/architecture/alife-runtime-story.md` with these sections:

```markdown
# Alife Runtime Story

## Positioning

Alife is a local AI agent runtime. It hosts role-scoped activities, pluginized capabilities, long-term memory, local model services, tool execution, and a visible Live2D desk pet body.

## Runtime Layer

`ChatActivity` owns the role-scoped runtime. `InteractiveModule` defines module lifecycle. `ModuleSystem` discovers and filters modules. This layer exists so capabilities can be added, disabled, tested, and eventually versioned without rewriting the host.

## Intelligence Layer

Memory, function calling, MCP, Skill, Python, browser, and model plugins provide the agent's reasoning and action capabilities. PAD emotion state adds an internal continuous state that can influence prompts and visible behavior.

## Embodiment Layer

`Alife.DeskPet.Client` renders the Live2D body through WebView2. `PetServer` and IPC commands connect C# modules to WebView2 parameters, expressions, motions, bubbles, and user interaction callbacks.

## Ecosystem Layer

`WebBridgeService` connects local Alife roles and avatar assets to the FOXD/avatar-web-management direction. This turns the local runtime into a candidate member of a wider avatar and asset ecosystem.

## Current Evidence

- Lifecycle hardening: ChatBot and InteractiveModule update loop tests.
- Plugin safety: compatibility mode and safety boundary.
- Memory performance: DuckDB connection reuse with async lock protection.
- Live2D path: PAD emotion state can drive `PetServer.SetParams(...)`.
- WebBridge path: config pull, state push, character upsert, asset sync foundation, and sync polling.
```

- [ ] **Step 3: Verify documentation references compile mentally**

Run:

```powershell
Select-String -LiteralPath 'D:\Alife\docs\architecture\alife-runtime-story.md' -Pattern 'Runtime Layer|Intelligence Layer|Embodiment Layer|Ecosystem Layer|Current Evidence'
```

Expected:
- All five headings are found.

- [ ] **Step 4: Commit**

Run:

```powershell
git add docs/architecture/alife-runtime-story.md
git commit -m "Document Alife runtime architecture story"
```

Expected:
- Commit created.

### Task A2: Finish PERF-2 WindowsPlatform.Command Async Conversion

**Files:**
- Modify: `sources/Alife/Alife.Platform/WindowsPlatform.cs`
- Test: `Tests/Alife.Test.Framework/CodingStandardTests.cs`

**Why this matters:** A synchronous external command runner is an easy interview failure. It can freeze callers and contradict the claim that Alife is a responsive runtime.

- [ ] **Step 1: Add a failing static test**

In `Tests/Alife.Test.Framework/CodingStandardTests.cs`, add:

```csharp
[Test]
public void WindowsPlatformCommandShouldNotBlockSynchronously()
{
    string repositoryRoot = FindRepositoryRoot();
    string windowsPlatformFile = Path.Combine(
        repositoryRoot,
        "sources",
        "Alife",
        "Alife.Platform",
        "WindowsPlatform.cs");
    string source = File.ReadAllText(windowsPlatformFile);

    Assert.That(source, Does.Contain("WaitForExitAsync"), "WindowsPlatform.Command should await external process completion asynchronously.");
    Assert.That(source, Does.Not.Contain(".WaitForExit();"), "WindowsPlatform.Command should not block synchronously.");
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter WindowsPlatformCommandShouldNotBlockSynchronously
```

Expected:
- Test fails because `WaitForExitAsync` is not used or `.WaitForExit();` still exists.

- [ ] **Step 3: Change Command to async**

Update `WindowsPlatform.Command` from:

```csharp
public static void Command(string fileName, string arguments)
```

to:

```csharp
public static async Task CommandAsync(string fileName, string arguments)
```

Use this shape:

```csharp
using Process process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start command: {fileName}");
string output = await process.StandardOutput.ReadToEndAsync();
string error = await process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();
```

Preserve existing logging behavior through the platform terminal/logging abstraction already used by this project.

- [ ] **Step 4: Update callers**

Run:

```powershell
rg -n "WindowsPlatform\.Command|AlifePlatform\.Command|\.Command\(" D:\Alife\sources D:\Alife\Tests
```

For each real caller of the changed method:
- Change call sites to `await WindowsPlatform.CommandAsync(...)`.
- If a caller cannot be async, create a narrow async wrapper and avoid `.Wait()` or `.Result`.

- [ ] **Step 5: Run GREEN and full verification**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter WindowsPlatformCommandShouldNotBlockSynchronously
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

Expected:
- Target test passes.
- Framework tests pass.
- Build succeeds with 0 warnings and 0 errors.

- [ ] **Step 6: Commit**

Run:

```powershell
git add sources/Alife/Alife.Platform/WindowsPlatform.cs Tests/Alife.Test.Framework/CodingStandardTests.cs
git commit -m "Make WindowsPlatform command execution async"
```

### Task A3: Connect MemoryStorage Disposal To Module Lifecycle

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.Memory/MemoryManager.cs`
- Modify: `sources/Alife.Function/Alife.Function.Memory/MemoryService.cs`
- Test: `Tests/Alife.Test.Framework/CodingStandardTests.cs`

**Why this matters:** PERF-1 introduced a long-lived DuckDB connection. That improved performance, but an interviewer can now reasonably ask who closes it. This task closes the lifecycle story.

- [ ] **Step 1: Add a failing lifecycle structure test**

Add to `CodingStandardTests.cs`:

```csharp
[Test]
public void MemoryStorageConnectionShouldBeDisposedByMemoryServiceLifecycle()
{
    string repositoryRoot = FindRepositoryRoot();
    string memoryManagerFile = Path.Combine(repositoryRoot, "sources", "Alife.Function", "Alife.Function.Memory", "MemoryManager.cs");
    string memoryServiceFile = Path.Combine(repositoryRoot, "sources", "Alife.Function", "Alife.Function.Memory", "MemoryService.cs");
    string managerSource = File.ReadAllText(memoryManagerFile);
    string serviceSource = File.ReadAllText(memoryServiceFile);

    Assert.That(managerSource, Does.Contain("IAsyncDisposable"), "MemoryManager should expose async disposal for MemoryStorage.");
    Assert.That(managerSource, Does.Contain("memoryStorage.DisposeAsync"), "MemoryManager should dispose MemoryStorage.");
    Assert.That(serviceSource, Does.Contain("DestroyAsync"), "MemoryService should participate in module destruction.");
    Assert.That(serviceSource, Does.Contain("memoryManager.DisposeAsync"), "MemoryService should dispose MemoryManager during destruction.");
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter MemoryStorageConnectionShouldBeDisposedByMemoryServiceLifecycle
```

Expected:
- Test fails because disposal chain is incomplete.

- [ ] **Step 3: Implement MemoryManager disposal**

Change class declaration:

```csharp
public class MemoryManager : IAsyncDisposable
```

Add:

```csharp
public async ValueTask DisposeAsync()
{
    await memoryStorage.DisposeAsync();
}
```

- [ ] **Step 4: Implement MemoryService destruction**

In `MemoryService`, add:

```csharp
public override async Task DestroyAsync()
{
    ChatBot.ChatHistoryAdd -= OnChatHistoryAdd;
    if (memoryManager != null)
        await memoryManager.DisposeAsync();

    await base.DestroyAsync();
}
```

If `memoryManager` is currently non-nullable, keep the field nullable or guard initialization carefully so destruction before `StartAsync` does not crash.

- [ ] **Step 5: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter MemoryStorageConnectionShouldBeDisposedByMemoryServiceLifecycle
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

Expected:
- Target test passes.
- Full test suite passes.
- Build succeeds.

- [ ] **Step 6: Commit**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.Memory/MemoryManager.cs sources/Alife.Function/Alife.Function.Memory/MemoryService.cs Tests/Alife.Test.Framework/CodingStandardTests.cs
git commit -m "Dispose memory storage from service lifecycle"
```

### Task A4: Add Local Diagnostic Snapshot

**Files:**
- Create: `sources/Alife/Alife.Framework/Diagnostics/DiagnosticSnapshot.cs`
- Create: `sources/Alife/Alife.Framework/Diagnostics/DiagnosticSnapshotService.cs`
- Test: `Tests/Alife.Test.Framework/DiagnosticSnapshotTests.cs`

**Why this matters:** A project with many moving parts must explain failures. Diagnostics raise interview value because they show operational thinking: latency, failures, process health, IPC health, and module status are first-class.

- [ ] **Step 1: Write failing tests**

Create `Tests/Alife.Test.Framework/DiagnosticSnapshotTests.cs`:

```csharp
namespace Alife.Test.Framework;

public class DiagnosticSnapshotTests
{
    [Test]
    public void DiagnosticSnapshotServiceShouldRecordCountersAndDurations()
    {
        Alife.Framework.Diagnostics.DiagnosticSnapshotService service = new();

        service.Increment("WebBridge.Sync.Failure");
        service.RecordDuration("Memory.Search", TimeSpan.FromMilliseconds(42));
        Alife.Framework.Diagnostics.DiagnosticSnapshot snapshot = service.CreateSnapshot();

        Assert.That(snapshot.Counters["WebBridge.Sync.Failure"], Is.EqualTo(1));
        Assert.That(snapshot.Durations["Memory.Search"].LastMilliseconds, Is.EqualTo(42));
    }
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter DiagnosticSnapshotTests
```

Expected:
- Compile fails because diagnostic types do not exist.

- [ ] **Step 3: Implement snapshot records**

Create `sources/Alife/Alife.Framework/Diagnostics/DiagnosticSnapshot.cs`:

```csharp
namespace Alife.Framework.Diagnostics;

public record DiagnosticDuration(long LastMilliseconds, long Count);

public record DiagnosticSnapshot(
    IReadOnlyDictionary<string, long> Counters,
    IReadOnlyDictionary<string, DiagnosticDuration> Durations,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 4: Implement snapshot service**

Create `sources/Alife/Alife.Framework/Diagnostics/DiagnosticSnapshotService.cs`:

```csharp
using System.Collections.Concurrent;

namespace Alife.Framework.Diagnostics;

public class DiagnosticSnapshotService
{
    readonly ConcurrentDictionary<string, long> counters = new();
    readonly ConcurrentDictionary<string, DiagnosticDuration> durations = new();

    public void Increment(string name)
    {
        counters.AddOrUpdate(name, 1, (_, value) => value + 1);
    }

    public void RecordDuration(string name, TimeSpan duration)
    {
        durations.AddOrUpdate(
            name,
            new DiagnosticDuration((long)duration.TotalMilliseconds, 1),
            (_, current) => new DiagnosticDuration((long)duration.TotalMilliseconds, current.Count + 1));
    }

    public DiagnosticSnapshot CreateSnapshot()
    {
        return new DiagnosticSnapshot(
            counters.ToDictionary(item => item.Key, item => item.Value),
            durations.ToDictionary(item => item.Key, item => item.Value),
            DateTimeOffset.Now);
    }
}
```

- [ ] **Step 5: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter DiagnosticSnapshotTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

Expected:
- Tests pass and build succeeds.

- [ ] **Step 6: Commit**

Run:

```powershell
git add sources/Alife/Alife.Framework/Diagnostics Tests/Alife.Test.Framework/DiagnosticSnapshotTests.cs
git commit -m "Add local diagnostic snapshot service"
```

### Task A5: Prepare A Two-Minute Technical Demo Script

**Files:**
- Create: `docs/demo/alife-interview-demo-script.md`

**Why this matters:** Interview upper ceiling depends on a repeatable story. A polished demo script prevents the project from being judged by random exploration or one fragile feature.

- [ ] **Step 1: Create demo docs directory**

Run:

```powershell
New-Item -ItemType Directory -Path 'D:\Alife\docs\demo' -Force
```

- [ ] **Step 2: Create the demo script**

Create `docs/demo/alife-interview-demo-script.md`:

```markdown
# Alife Two-Minute Interview Demo Script

## Claim

Alife is a local AI agent runtime with pluginized capabilities, long-term memory, visible Live2D embodiment, PAD emotion state, and WebBridge ecosystem direction.

## Demo Flow

1. Start Alife and show one role activity.
2. Show enabled modules: Memory, DeskPet, Emotion, Browser or Python, WebBridge if configured.
3. Trigger a positive interaction and show PAD state changing.
4. Show the Live2D desk pet expression or posture parameter responding.
5. Trigger a memory write or search and show DuckDB-backed memory retrieval.
6. Show diagnostic snapshot counters or logs for the flow.
7. Explain FOXD/WebBridge as the ecosystem extension, noting that real service E2E is paused until the FOXD Web endpoint is complete.

## Backup If Visual Smoke Is Unavailable

Use automated tests and protocol logs:
- PAD emotion tests.
- DeskPet IPC protocol tests.
- WebBridge sync tests.
- Framework full test suite.

## What This Proves

- Runtime lifecycle is testable.
- Plugins are discoverable and governable.
- The agent has memory and tool use.
- Internal emotional state reaches visible output.
- Failures can be diagnosed.
```

- [ ] **Step 3: Commit**

Run:

```powershell
git add docs/demo/alife-interview-demo-script.md
git commit -m "Add Alife interview demo script"
```

---

## 3. Execution Track B: User Experience

### Objective

Make Alife feel stable, responsive, understandable, and emotionally continuous to a normal user.

### User Experience Upper Ceiling

The upper ceiling should become:
- A visible AI companion that remembers, reacts, expresses emotion through Live2D, can use tools, and can eventually sync avatars/assets through FOXD.

### User Experience Lower Floor

The lower floor should become:
- Starts without silent hangs.
- Does not freeze during external commands or model loading.
- Desk pet remains visible or recovers if a process fails.
- Emotion changes look smooth rather than jittery.
- Errors are understandable and recoverable.

### Task B1: Standardize Startup Status Reporting

**Files:**
- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatus.cs`
- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusService.cs`
- Test: `Tests/Alife.Test.Framework/StartupStatusTests.cs`

**Why this matters:** Users forgive waiting when they understand what is happening. Silent loading looks broken, especially when models, plugins, desk pet, and WebBridge start at different speeds.

- [ ] **Step 1: Write failing test**

Create `Tests/Alife.Test.Framework/StartupStatusTests.cs`:

```csharp
namespace Alife.Test.Framework;

public class StartupStatusTests
{
    [Test]
    public void StartupStatusServiceShouldTrackLatestStage()
    {
        Alife.Framework.Models.Runtime.StartupStatusService service = new();

        service.Report("LoadingModules", "正在加载插件");
        Alife.Framework.Models.Runtime.StartupStatus status = service.Current;

        Assert.That(status.Stage, Is.EqualTo("LoadingModules"));
        Assert.That(status.Message, Is.EqualTo("正在加载插件"));
        Assert.That(status.UpdatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter StartupStatusTests
```

Expected:
- Compile fails because startup status types do not exist.

- [ ] **Step 3: Implement status model**

Create `sources/Alife/Alife.Framework/Models/Runtime/StartupStatus.cs`:

```csharp
namespace Alife.Framework.Models.Runtime;

public record StartupStatus(string Stage, string Message, DateTimeOffset UpdatedAt);
```

- [ ] **Step 4: Implement status service**

Create `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusService.cs`:

```csharp
namespace Alife.Framework.Models.Runtime;

public class StartupStatusService
{
    public StartupStatus Current { get; private set; } = new("Idle", "等待启动", DateTimeOffset.Now);

    public void Report(string stage, string message)
    {
        Current = new StartupStatus(stage, message, DateTimeOffset.Now);
    }
}
```

- [ ] **Step 5: Wire only the service first**

Do not add broad UI in this task. Register or instantiate the service in the same pattern used by existing framework services, then report at least these stages from existing startup code:
- `LoadingConfiguration`
- `LoadingModules`
- `StartingCharacter`
- `StartingDeskPet`

Why only service first: it gives a stable backend contract before UI is attached, and it is easy to test.

- [ ] **Step 6: Verify and commit**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter StartupStatusTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
git add sources/Alife/Alife.Framework/Models/Runtime Tests/Alife.Test.Framework/StartupStatusTests.cs
git commit -m "Add startup status tracking"
```

### Task B2: Add DeskPet Visual Smoke Checklist And Edge/WebView2 Runbook

**Files:**
- Create: `docs/runbooks/deskpet-visual-smoke.md`

**Why this matters:** The Live2D body is the most visible user feature. Automated protocol tests are not enough; users judge whether the model appears, moves, responds, and does not overlap or black-screen.

- [ ] **Step 1: Create runbook directory**

Run:

```powershell
New-Item -ItemType Directory -Path 'D:\Alife\docs\runbooks' -Force
```

- [ ] **Step 2: Create runbook**

Create `docs/runbooks/deskpet-visual-smoke.md`:

```markdown
# DeskPet Visual Smoke Runbook

## Browser Requirement

Use Microsoft Edge for browser-based checks:
`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`

## Preconditions

- Alife builds successfully.
- DeskPet module is enabled for the test character.
- Live2D model assets are present.
- WebView2 runtime is installed.

## Checks

1. Start Alife.
2. Start a character with DeskPet enabled.
3. Verify the desk pet window appears within 10 seconds.
4. Verify the model is nonblank and not black-screened.
5. Trigger a bubble message and verify text does not overlap the model or window edge.
6. Trigger expression/motion command and verify visible response.
7. Trigger PAD positive event and verify Live2D parameters visibly soften or brighten the pose.
8. Drag or interact with the pet and verify input returns to chat as an interaction event.
9. Close the character activity and verify the pet process exits.

## Pass Criteria

- Window appears.
- Model is visible.
- At least one expression or parameter change is visible.
- No stuck black window.
- No orphan desk pet process remains after activity shutdown.

## Failure Notes To Capture

- Screenshot.
- Latest Alife log.
- Whether WebView2 loaded assets.
- Whether `PetServer.SetParams(...)` was called.
```

- [ ] **Step 3: Commit**

Run:

```powershell
git add docs/runbooks/deskpet-visual-smoke.md
git commit -m "Add desk pet visual smoke runbook"
```

### Task B3: Smooth PAD To Live2D Parameter Changes

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.Emotion/EmotionLive2DParameterDriver.cs`
- Test: existing or new emotion driver tests under `Tests/Alife.Test.Framework`

**Why this matters:** Users read jitter as fake or broken emotion. Smooth state transitions raise the product ceiling more than adding more emotion labels.

- [ ] **Step 1: Add failing test for minimum update threshold**

Add a test that:
- Creates a fake `IEmotionParameterSink`.
- Pushes two nearly identical PAD states.
- Asserts only one parameter update is sent when all changes are below a configured threshold.

Test shape:

```csharp
[Test]
public void EmotionDriverShouldSkipTinyParameterChanges()
{
    FakeEmotionParameterSink sink = new();
    PADEmotionEngine emotion = new();
    EmotionLive2DParameterDriver driver = new(emotion, sink, minimumDelta: 0.03f);

    emotion.ModulatePAD(0.2f, 0.1f, 0.1f);
    driver.PushCurrentState();
    emotion.ModulatePAD(0.001f, 0.001f, 0.001f);
    driver.PushCurrentState();

    Assert.That(sink.PushCount, Is.EqualTo(1));
}
```

If constructor parameters differ, introduce the smallest overload needed rather than rewriting the whole driver.

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter EmotionDriverShouldSkipTinyParameterChanges
```

Expected:
- Test fails because the driver currently pushes every interval.

- [ ] **Step 3: Implement threshold and last-parameter cache**

In `EmotionLive2DParameterDriver`:
- Store the last pushed parameter dictionary.
- Compare new values to old values.
- If every value changes less than `minimumDelta`, skip sending.
- Always send the first state.

Why this implementation: it reduces visible jitter and IPC traffic without changing PAD engine semantics.

- [ ] **Step 4: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter EmotionDriverShouldSkipTinyParameterChanges
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

- [ ] **Step 5: Commit**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.Emotion Tests/Alife.Test.Framework
git commit -m "Smooth PAD Live2D parameter updates"
```

### Task B4: Add User-Facing Error Classification

**Files:**
- Create: `sources/Alife/Alife.Framework/Diagnostics/UserFacingError.cs`
- Create: `sources/Alife/Alife.Framework/Diagnostics/UserFacingErrorClassifier.cs`
- Test: `Tests/Alife.Test.Framework/UserFacingErrorClassifierTests.cs`

**Why this matters:** Users should see recoverable explanations, not raw stack traces. Developers still need logs, but the UI needs a stable translation layer.

- [ ] **Step 1: Write failing tests**

Create `Tests/Alife.Test.Framework/UserFacingErrorClassifierTests.cs`:

```csharp
namespace Alife.Test.Framework;

public class UserFacingErrorClassifierTests
{
    [Test]
    public void ClassifierShouldMapTimeoutToRecoverableMessage()
    {
        Alife.Framework.Diagnostics.UserFacingError error =
            Alife.Framework.Diagnostics.UserFacingErrorClassifier.Classify(new TimeoutException("request timed out"));

        Assert.That(error.IsRecoverable, Is.True);
        Assert.That(error.Message, Does.Contain("暂时没有响应"));
    }
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter UserFacingErrorClassifierTests
```

Expected:
- Compile fails because classifier does not exist.

- [ ] **Step 3: Implement error record**

Create `sources/Alife/Alife.Framework/Diagnostics/UserFacingError.cs`:

```csharp
namespace Alife.Framework.Diagnostics;

public record UserFacingError(string Message, bool IsRecoverable, string Category);
```

- [ ] **Step 4: Implement classifier**

Create `sources/Alife/Alife.Framework/Diagnostics/UserFacingErrorClassifier.cs`:

```csharp
namespace Alife.Framework.Diagnostics;

public static class UserFacingErrorClassifier
{
    public static UserFacingError Classify(Exception exception)
    {
        return exception switch
        {
            TimeoutException => new UserFacingError("服务暂时没有响应，已保留当前状态，可以稍后重试。", true, "Timeout"),
            OperationCanceledException => new UserFacingError("操作已取消。", true, "Canceled"),
            IOException => new UserFacingError("本地文件访问失败，请检查文件是否被占用或路径是否可用。", true, "FileSystem"),
            _ => new UserFacingError("功能执行失败，详细原因已写入日志。", false, "Unknown")
        };
    }
}
```

- [ ] **Step 5: Verify and commit**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter UserFacingErrorClassifierTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
git add sources/Alife/Alife.Framework/Diagnostics Tests/Alife.Test.Framework/UserFacingErrorClassifierTests.cs
git commit -m "Add user facing error classification"
```

### Task B5: Define The Real User Acceptance Demo

**Files:**
- Create: `docs/demo/alife-user-acceptance-demo.md`

**Why this matters:** Product quality is not proven by unit tests alone. A user acceptance script catches the practical failures users actually notice: startup delay, black windows, no response, weird emotion jumps, and unclear errors.

- [ ] **Step 1: Create acceptance demo document**

Create `docs/demo/alife-user-acceptance-demo.md`:

```markdown
# Alife User Acceptance Demo

## Goal

Verify that Alife feels like a stable, responsive, emotionally continuous desktop companion.

## Scenario

1. Start Alife with a character that enables DeskPet, Emotion, Memory, and one tool module.
2. Confirm startup status reaches ready state.
3. Send a friendly message.
4. Confirm the desk pet displays a bubble.
5. Confirm PAD state changes toward positive emotion.
6. Confirm Live2D expression or posture changes smoothly.
7. Ask the character to remember a small fact.
8. Ask the character to recall or search that fact.
9. Trigger a harmless recoverable error, such as a timeout test endpoint if available.
10. Confirm the user-facing error message is understandable and does not expose a raw stack trace.

## Pass Criteria

- Ready state is visible.
- No UI freeze longer than 1 second during normal interaction.
- Desk pet window is visible and nonblank.
- Emotion-driven change is visible but not jittery.
- Memory action succeeds or gives a clear recoverable message.
- Logs contain developer detail without exposing it directly to the user.
```

- [ ] **Step 2: Commit**

Run:

```powershell
git add docs/demo/alife-user-acceptance-demo.md
git commit -m "Add Alife user acceptance demo"
```

---

## 4. Recommended Combined Execution Order

1. Task A2: `WindowsPlatform.Command` async conversion.
   Why first: it improves interview lower floor and user lower floor immediately by removing a blocking primitive.

2. Task A3: `MemoryStorage` disposal lifecycle.
   Why second: PERF-1 is already done, so this closes the resource-lifetime story with low scope.

3. Task B2: DeskPet visual smoke runbook.
   Why third: Live2D is the most visible user feature, and the current plan already notes visual smoke remains unverified.

4. Task B3: Smooth PAD to Live2D updates.
   Why fourth: the emotion chain already works technically; smoothing turns it into a better product experience.

5. Task A4: Local diagnostic snapshot.
   Why fifth: after key chains exist, diagnostics make failures explainable and raise interview maturity.

6. Task B4: User-facing error classification.
   Why sixth: diagnostics serve developers; classified messages serve users.

7. Task B1: Startup status reporting.
   Why seventh: startup reporting benefits users, but it is easier to wire cleanly after diagnostics and error categories exist.

8. Task A1, A5, and B5: Documentation and demo scripts.
   Why last in execution, but not in importance: these should reflect the completed state rather than promise unfinished behavior. If an interview is imminent, do A1 and A5 earlier as documentation-only tasks.

9. Defer FOXD-1d until the real FOXD Web service endpoint is complete.
   Why: forcing a real end-to-end check against an incomplete service creates fake completion and wastes time. Keep WebBridge tests and runbooks ready, then complete FOXD-1d when the upstream service is ready.

---

## 5. Success Metrics

### Interview Metrics

- Architecture story exists and matches current code.
- Full Framework tests pass.
- Full solution build passes with 0 warnings and 0 errors.
- At least one end-to-end demo script exists.
- Diagnostic snapshot can report counters and durations.
- Critical lifecycle/resource questions have direct answers:
  - Who owns modules?
  - Who stops background loops?
  - Who closes DuckDB?
  - How are plugin risks bounded?
  - How are failures observed?

### User Experience Metrics

- Startup has visible status.
- Normal interaction has no noticeable freeze longer than 1 second.
- DeskPet appears and remains nonblank.
- PAD-driven Live2D updates are visible but not jittery.
- Recoverable errors produce understandable user messages.
- Closing a character activity does not leave orphan desk pet or background loops.

---

## 6. Local Tracking Markers

Add these markers to `MASTER_EXECUTION_PLAN.md` after each completed task:

```markdown
- [ ] PERF-2：`WindowsPlatform.Command` 异步化。
- [ ] PERF-1b：`MemoryStorage` 释放链路接入 `MemoryService` 生命周期。
- [ ] UX-1：DeskPet Edge/WebView2 视觉冒烟 Runbook。
- [ ] UX-2：PAD → Live2D 参数平滑与最小变化阈值。
- [ ] OPS-1：本地诊断快照服务。
- [ ] UX-3：用户可理解错误分类。
- [ ] UX-4：启动状态报告。
- [ ] DOC-1：面试架构故事与两分钟 Demo 脚本。
- [ ] DOC-2：用户验收 Demo 脚本。
```

Why use local markers: `MASTER_EXECUTION_PLAN.md` is already the local execution ledger. Keeping these markers there preserves continuity with the existing P0, Live2D, WebBridge, ARCH, and PERF batches.

---

## 7. Self-Review

- Spec coverage: The plan separately covers interview upper/lower ceiling and user experience upper/lower floor.
- No unresolved dependency hidden as completed work: FOXD-1d remains explicitly deferred because the real FOXD Web service is incomplete.
- TDD coverage: Code tasks include RED, GREEN, full tests, build, and commit steps.
- Scope control: The plan does not add unrelated new plugins. It deepens existing runtime, memory, diagnostics, desk pet, emotion, and documentation chains.
- Risk control: Visual checks are documented as runbooks because they depend on real WebView2/Edge windows and cannot be fully proven by protocol tests alone.
