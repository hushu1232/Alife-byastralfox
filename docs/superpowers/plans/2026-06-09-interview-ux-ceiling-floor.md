# Alife Interview And User Experience Ceiling Floor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise Alife's upper ceiling and lower floor from two separate viewpoints: interview value and real user experience.

**Architecture:** Keep the existing role container, module system, local model services, Live2D desk pet, PAD emotion engine, memory storage, and WebBridge direction. This plan improves reliability, observability, startup clarity, user-facing recovery, and demo evidence without adding unrelated feature width.

**Tech Stack:** .NET 9, WPF, WebView2, Blazor Hybrid, Semantic Kernel, Autofac, DuckDB, NUnit, Live2D via WebView2/pixi-live2d, Alife module system, WebBridge, PAD emotion engine.

---

## 0. Current Baseline

Alife already has a strong interview and product foundation:

- Runtime: `ChatActivity`, `InteractiveModule`, `ModuleSystem`, Autofac module container, Semantic Kernel chat agent.
- Intelligence: memory, function calling, Python, browser, MCP, Skill, local language/vision/speech models.
- Embodiment: desk pet service, WebView2/Live2D path, `PetServer`, PAD emotion state, Live2D parameter driver.
- Ecosystem: WebBridge config pull, local character upsert, asset sync foundation, polling lifecycle.
- Recent hardening: ChatBot lifecycle, update loop cancellation, PythonPipe health recovery, plugin compatibility boundary, DuckDB connection reuse.

Remaining floor issues:

- `WindowsPlatform.Command` blocks synchronously and is used from model setup paths.
- `MemoryStorage` is disposable, but `MemoryManager` and `MemoryService` do not yet close it through module destruction.
- Live2D visual behavior has protocol tests but no repeatable visual smoke runbook.
- PAD-to-Live2D parameter updates push every tick even when values do not materially change.
- Operators have no unified local diagnostic snapshot for counters and durations.
- Users do not yet get standardized startup status or classified recoverable errors.
- Demo evidence exists in pieces, but the interview and user acceptance flows are not scripted.

---

## 1. File Structure

### Runtime Reliability

- Modify: `sources/Alife/Alife.Platform/WindowsPlatform.cs`
  - Convert process command execution to `CommandAsync`.
- Modify: `sources/Alife/Alife.Platform/AlifePlatform.cs`
  - Expose `CommandAsync` at the platform abstraction boundary.
- Modify: `sources/Alife/Alife.Platform/AlifeModel.cs`
  - Remove blocking command execution from the static constructor.
  - Add async model dependency setup.
- Modify: model setup callers:
  - `sources/Alife.Function/Alife.Function.Memory/TextVectorizer.cs`
  - `sources/Alife.Function/Alife.Function.SpeechModel/EdgeSpeechModel.cs`
  - `sources/Alife.Function/Alife.Function.SpeechModel/GenieSpeechModel.cs`
  - `sources/Alife.Function/Alife.Function.SpeechModel/VitsSpeechModel.cs`
  - `sources/Alife.Function/Alife.Function.VisionModel/MiniCPMVisionModel.cs`
  - `sources/Alife.Function/Alife.Function.VisionModel/QwenVisionModel.cs`

### Memory Lifecycle

- Modify: `sources/Alife.Function/Alife.Function.Memory/MemoryManager.cs`
  - Implement `IAsyncDisposable`.
- Modify: `sources/Alife.Function/Alife.Function.Memory/MemoryService.cs`
  - Dispose `MemoryManager` in `DestroyAsync`.
  - Unsubscribe `ChatBot.ChatHistoryAdd` safely.

### Diagnostics

- Create: `sources/Alife/Alife.Framework/Diagnostics/DiagnosticSnapshot.cs`
- Create: `sources/Alife/Alife.Framework/Diagnostics/DiagnosticSnapshotService.cs`
- Modify: `sources/Alife.Function/Alife.Function.WebBridge/WebBridgeService.cs`
  - Record sync counters and durations.
- Create: `Tests/Alife.Test.Framework/DiagnosticSnapshotTests.cs`

### Startup Status

- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatus.cs`
- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusService.cs`
- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusProgress.cs`
- Create: `Tests/Alife.Test.Framework/StartupStatusTests.cs`

### Emotion And DeskPet UX

- Modify: `sources/Alife.Function/Alife.Function.Emotion/EmotionLive2DParameterDriver.cs`
- Modify: `Tests/Alife.Test.Framework/PadEmotionEngineTests.cs`
- Create: `docs/runbooks/deskpet-visual-smoke.md`

### User-Facing Errors

- Create: `sources/Alife/Alife.Framework/Diagnostics/UserFacingError.cs`
- Create: `sources/Alife/Alife.Framework/Diagnostics/UserFacingErrorClassifier.cs`
- Create: `Tests/Alife.Test.Framework/UserFacingErrorClassifierTests.cs`

### Evidence And Demo Docs

- Create: `docs/architecture/alife-runtime-story.md`
- Create: `docs/demo/alife-interview-demo-script.md`
- Create: `docs/demo/alife-user-acceptance-demo.md`
- Modify: `MASTER_EXECUTION_PLAN.md`
  - Add local tracking markers after each completed task.

---

## 2. Recommended Execution Order

1. A2: Convert platform command execution to async.
2. A3: Connect memory disposal to module lifecycle.
3. B2: Add DeskPet visual smoke runbook.
4. B3: Smooth PAD-to-Live2D parameter pushes.
5. A4: Add diagnostic snapshot service and WebBridge metrics.
6. B4: Add user-facing error classification.
7. B1: Add startup status service and progress adapter.
8. A1, A5, B5: Write architecture and demo evidence docs.
9. Update `MASTER_EXECUTION_PLAN.md` markers.

FOXD real-service E2E remains deferred until the upstream FOXD Web endpoint is complete. Do not fake completion by testing only against local mocks.

---

## 3. Execution Track A: Interview Value

### Task A1: Create A One-Page Architecture Story

**Files:**
- Create: `docs/architecture/alife-runtime-story.md`
- Reference: `README.md`
- Reference: `MASTER_EXECUTION_PLAN.md`

**Why this matters:** Interviewers should be able to understand Alife as a local AI agent runtime, not a loose set of AI demos.

- [ ] **Step 1: Create the directory**

Run:

```powershell
New-Item -ItemType Directory -Path 'D:\Alife\docs\architecture' -Force
```

Expected: `D:\Alife\docs\architecture` exists.

- [ ] **Step 2: Write `docs/architecture/alife-runtime-story.md`**

Use this content:

```markdown
# Alife Runtime Story

## Positioning

Alife is a local AI agent runtime. It hosts role-scoped activities, pluginized capabilities, long-term memory, local model services, tool execution, and a visible Live2D desk pet body.

## Runtime Layer

`ChatActivity` owns the role-scoped runtime. `InteractiveModule` defines module lifecycle. `ModuleSystem` discovers modules and filters them by compatibility. Autofac builds the per-character module container.

## Intelligence Layer

Memory, function calling, MCP, Skill, Python, browser, language models, vision models, and speech models provide reasoning and action capabilities. PAD emotion state adds continuous internal state that can influence prompts and visible behavior.

## Embodiment Layer

`Alife.DeskPet.Client` renders the Live2D body through WebView2. `PetServer` and IPC commands connect C# modules to WebView2 parameters, expressions, motions, bubbles, and user interaction callbacks.

## Ecosystem Layer

`WebBridgeService` connects local Alife roles and avatar assets to the FOXD/avatar-web-management direction. This turns the local runtime into a candidate member of a wider avatar and asset ecosystem.

## Current Evidence

- Lifecycle hardening: ChatBot and InteractiveModule update-loop tests.
- Plugin safety: compatibility filtering and plugin safety boundary.
- Memory performance: DuckDB connection reuse guarded by `SemaphoreSlim`.
- Live2D path: PAD emotion state can drive `IEmotionParameterSink.SetParams(...)`.
- WebBridge path: config pull, state push, character upsert, asset sync, and sync polling.
```

- [ ] **Step 3: Verify the headings**

Run:

```powershell
Select-String -LiteralPath 'D:\Alife\docs\architecture\alife-runtime-story.md' -Pattern 'Runtime Layer|Intelligence Layer|Embodiment Layer|Ecosystem Layer|Current Evidence'
```

Expected: all five headings are found.

- [ ] **Step 4: Commit**

Run:

```powershell
git add docs/architecture/alife-runtime-story.md
git commit -m "Document Alife runtime architecture story"
```

### Task A2: Convert Platform Command Execution To Async

**Files:**
- Modify: `sources/Alife/Alife.Platform/WindowsPlatform.cs`
- Modify: `sources/Alife/Alife.Platform/AlifePlatform.cs`
- Modify: `sources/Alife/Alife.Platform/AlifeModel.cs`
- Modify: `sources/Alife.Function/Alife.Function.Memory/TextVectorizer.cs`
- Modify: `sources/Alife.Function/Alife.Function.SpeechModel/EdgeSpeechModel.cs`
- Modify: `sources/Alife.Function/Alife.Function.SpeechModel/GenieSpeechModel.cs`
- Modify: `sources/Alife.Function/Alife.Function.SpeechModel/VitsSpeechModel.cs`
- Modify: `sources/Alife.Function/Alife.Function.VisionModel/MiniCPMVisionModel.cs`
- Modify: `sources/Alife.Function/Alife.Function.VisionModel/QwenVisionModel.cs`
- Test: `Tests/Alife.Test.Framework/CodingStandardTests.cs`

**Why this matters:** A synchronous command runner is an easy reliability and interview failure because package/model setup can freeze the caller.

- [ ] **Step 1: Add the failing static test**

Add this test to `Tests/Alife.Test.Framework/CodingStandardTests.cs`:

```csharp
[Test]
public void PlatformCommandExecutionShouldBeAsyncOnly()
{
    string repositoryRoot = FindRepositoryRoot();
    string windowsPlatformFile = Path.Combine(repositoryRoot, "sources", "Alife", "Alife.Platform", "WindowsPlatform.cs");
    string alifePlatformFile = Path.Combine(repositoryRoot, "sources", "Alife", "Alife.Platform", "AlifePlatform.cs");
    string windowsSource = File.ReadAllText(windowsPlatformFile);
    string platformSource = File.ReadAllText(alifePlatformFile);

    Assert.That(windowsSource, Does.Contain("Task CommandAsync"), "WindowsPlatform should expose async command execution.");
    Assert.That(platformSource, Does.Contain("Task CommandAsync"), "AlifePlatform should expose async command execution.");
    Assert.That(windowsSource, Does.Contain("WaitForExitAsync"), "WindowsPlatform should await process completion asynchronously.");
    Assert.That(windowsSource, Does.Not.Contain(".WaitForExit();"), "WindowsPlatform must not block synchronously.");
    Assert.That(windowsSource, Does.Not.Contain("public static void Command("), "The sync WindowsPlatform.Command API should be removed.");
    Assert.That(platformSource, Does.Not.Contain("public static void Command("), "The sync AlifePlatform.Command API should be removed.");
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter PlatformCommandExecutionShouldBeAsyncOnly
```

Expected: FAIL because `WindowsPlatform.Command` and `AlifePlatform.Command` are still synchronous.

- [ ] **Step 3: Replace `WindowsPlatform.Command` with `CommandAsync`**

In `sources/Alife/Alife.Platform/WindowsPlatform.cs`, replace the current `public static void Command(string fileName, string arguments)` method with:

```csharp
public static async Task CommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
{
    ProcessStartInfo psi = new() {
        FileName = "cmd.exe",
        Arguments = $"/c {fileName} {arguments}",
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    using Process process = Process.Start(psi) ??
        throw new InvalidOperationException($"Failed to start command: {fileName}");

    process.OutputDataReceived += (_, eventArgs) => {
        if (eventArgs.Data != null)
            AlifeTerminal.LogInfo(eventArgs.Data);
    };
    process.ErrorDataReceived += (_, eventArgs) => {
        if (eventArgs.Data != null)
            AlifeTerminal.LogWarning(eventArgs.Data);
    };

    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    try
    {
        await process.WaitForExitAsync(cancellationToken);
    }
    finally
    {
        if (process.HasExited == false)
            process.Kill(entireProcessTree: true);
    }
}
```

Also add `using System.Threading;` and `using System.Threading.Tasks;` if they are not already present.

- [ ] **Step 4: Replace `AlifePlatform.Command` with `CommandAsync`**

In `sources/Alife/Alife.Platform/AlifePlatform.cs`, replace `public static void Command(string fileName, string arguments)` with:

```csharp
public static async Task CommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
{
    if (CommandIgnore.Length != 0)
    {
        string fullCommand = $"{fileName} {arguments}";
        if (CommandIgnore.Any(ignore => Regex.IsMatch(fullCommand, ignore)))
            return;
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        await WindowsPlatform.CommandAsync(fileName, arguments, cancellationToken);
        return;
    }

    throw new PlatformNotSupportedException("Current platform does not support command execution.");
}
```

Also add:

```csharp
using System.Threading;
using System.Threading.Tasks;
```

- [ ] **Step 5: Remove blocking setup from `AlifeModel`**

In `sources/Alife/Alife.Platform/AlifeModel.cs`, replace the sync API with:

```csharp
public static async Task<string> EnsureModelExistingAsync(string modelId, string? targetFile = null, CancellationToken cancellationToken = default)
{
    await EnsureModelScopeReadyAsync(cancellationToken);

    string localPath = Path.Combine(ModelScopeModelPath, modelId.Replace(".", "___"));
    string checkFile = Path.Combine(localPath, targetFile ?? "README.md");

    if (!File.Exists(checkFile))
        await AlifePlatform.CommandAsync(
            "python",
            $"-c \"from modelscope import snapshot_download; snapshot_download('{modelId}')\"",
            cancellationToken);

    if (!File.Exists(checkFile))
        throw new DirectoryNotFoundException($"Model download failed, directory does not exist: {localPath}");

    return targetFile != null ? checkFile : localPath;
}

static Task EnsureModelScopeReadyAsync(CancellationToken cancellationToken = default)
{
    return AlifePlatform.CommandAsync("python", "-m pip install modelscope", cancellationToken);
}
```

Keep the static constructor, but make it only compute `ModelScopeModelPath`:

```csharp
static AlifeModel()
{
    string modelScopeCachePath = Environment.GetEnvironmentVariable("MODELSCOPE_CACHE") ??
                                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "modelscope", "hub");
    ModelScopeModelPath = Path.Combine(modelScopeCachePath, "models").Replace(Path.DirectorySeparatorChar, '/');
}
```

- [ ] **Step 6: Update `TextVectorizer`**

In `sources/Alife.Function/Alife.Function.Memory/TextVectorizer.cs`, change:

```csharp
string modelPath = AlifeModel.EnsureModelExisting("BAAI/bge-small-zh-v1.5");
```

to:

```csharp
string modelPath = await AlifeModel.EnsureModelExistingAsync("BAAI/bge-small-zh-v1.5");
```

Change:

```csharp
AlifePlatform.Command("python", "-m pip install transformers torch sentencepiece");
```

to:

```csharp
await AlifePlatform.CommandAsync("python", "-m pip install transformers torch sentencepiece");
```

- [ ] **Step 7: Update vision model setup**

In `MiniCPMVisionModel.AwakeAsync`, change:

```csharp
string modelPath = AlifeModel.EnsureModelExisting(ModelId);
AlifePlatform.Command("python", "-m pip install --upgrade \"transformers>=5.6.0\"");
AlifePlatform.Command("python", "-m pip install torch torchvision torchcodec bitsandbytes accelerate sentencepiece tiktoken");
```

to:

```csharp
string modelPath = await AlifeModel.EnsureModelExistingAsync(ModelId);
await AlifePlatform.CommandAsync("python", "-m pip install --upgrade \"transformers>=5.6.0\"");
await AlifePlatform.CommandAsync("python", "-m pip install torch torchvision torchcodec bitsandbytes accelerate sentencepiece tiktoken");
```

In `QwenVisionModel.AwakeAsync`, change:

```csharp
string modelPath = AlifeModel.EnsureModelExisting(ModelId);
AlifePlatform.Command("python", "-m pip install torch torchvision Pillow transformers qwen-vl-utils bitsandbytes accelerate sentencepiece tiktoken");
```

to:

```csharp
string modelPath = await AlifeModel.EnsureModelExistingAsync(ModelId);
await AlifePlatform.CommandAsync("python", "-m pip install torch torchvision Pillow transformers qwen-vl-utils bitsandbytes accelerate sentencepiece tiktoken");
```

- [ ] **Step 8: Update speech model setup**

In `GenieSpeechModel.AwakeAsync`, change:

```csharp
AlifePlatform.Command("python", "-m pip install genie-tts");
```

to:

```csharp
await AlifePlatform.CommandAsync("python", "-m pip install genie-tts");
```

In `VitsSpeechModel.AwakeAsync`, change:

```csharp
AlifePlatform.Command("python", $"-m pip install -r \"{Path.Combine(RuntimeFolder, "requirements.txt")}\"");
```

to:

```csharp
await AlifePlatform.CommandAsync("python", $"-m pip install -r \"{Path.Combine(RuntimeFolder, "requirements.txt")}\"");
```

In `EdgeSpeechModel`, remove the command call from the constructor:

```csharp
public EdgeSpeechModel()
{
    invalidChars = Path.GetInvalidFileNameChars();
}
```

Add a dependency guard:

```csharp
bool dependenciesReady;

async Task EnsureDependenciesAsync(CancellationToken cancellationToken)
{
    if (dependenciesReady)
        return;

    await AlifePlatform.CommandAsync("python", "-m pip install --upgrade edge-tts", cancellationToken);
    dependenciesReady = true;
}
```

At the start of `GenerateSpeechFileAsync`, after the empty text check, add:

```csharp
await EnsureDependenciesAsync(cancellationToken);
```

- [ ] **Step 9: Confirm no sync command callers remain**

Run:

```powershell
rg -n "AlifePlatform\.Command\(|WindowsPlatform\.Command\(|EnsureModelExisting\(" D:\Alife\sources D:\Alife\Tests
```

Expected:
- No `AlifePlatform.Command(` results.
- No `WindowsPlatform.Command(` results.
- No `EnsureModelExisting(` results.
- `CommandAsync` and `EnsureModelExistingAsync` usages are allowed.

- [ ] **Step 10: Run GREEN and build**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter PlatformCommandExecutionShouldBeAsyncOnly
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

Expected:
- Target test passes.
- Full framework tests pass.
- Build succeeds.

- [ ] **Step 11: Commit**

Run:

```powershell
git add sources/Alife/Alife.Platform sources/Alife.Function/Alife.Function.Memory/TextVectorizer.cs sources/Alife.Function/Alife.Function.SpeechModel sources/Alife.Function/Alife.Function.VisionModel Tests/Alife.Test.Framework/CodingStandardTests.cs
git commit -m "Make platform command execution async"
```

### Task A3: Connect MemoryStorage Disposal To Module Lifecycle

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.Memory/MemoryManager.cs`
- Modify: `sources/Alife.Function/Alife.Function.Memory/MemoryService.cs`
- Test: `Tests/Alife.Test.Framework/CodingStandardTests.cs`

**Why this matters:** PERF-1 reused a long-lived DuckDB connection. The next lifecycle question is who closes it.

- [ ] **Step 1: Add the failing lifecycle test**

Add this test to `CodingStandardTests.cs`:

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
    Assert.That(serviceSource, Does.Contain("ChatBot.ChatHistoryAdd -="), "MemoryService should unsubscribe its chat-history handler.");
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter MemoryStorageConnectionShouldBeDisposedByMemoryServiceLifecycle
```

Expected: FAIL because the disposal chain is incomplete.

- [ ] **Step 3: Implement `MemoryManager.DisposeAsync`**

In `MemoryManager.cs`, change the class declaration:

```csharp
public class MemoryManager : IAsyncDisposable
```

Add this public method before the private fields:

```csharp
public async ValueTask DisposeAsync()
{
    await memoryStorage.DisposeAsync();
}
```

- [ ] **Step 4: Make `MemoryService` destruction safe**

In `MemoryService.cs`, change the fields:

```csharp
MemoryManager? memoryManager;
bool chatHistoryHooked;
```

In methods that use `memoryManager`, add explicit guards. For example, `Recall` starts with:

```csharp
if (memoryManager == null)
    Throw("Memory manager is not initialized.");
```

Then continue with the existing logic:

```csharp
string? memory = await memoryManager.ReadMemory(index);
```

Apply the same guard to `Search`, `Forget`, `InsertMemory`, and `OnChatHistoryAdd`.

In `StartAsync`, after subscribing to the event, set the flag:

```csharp
ChatBot.ChatHistoryAdd += OnChatHistoryAdd;
chatHistoryHooked = true;
```

Add this override:

```csharp
public override async Task DestroyAsync()
{
    if (chatHistoryHooked)
    {
        ChatBot.ChatHistoryAdd -= OnChatHistoryAdd;
        chatHistoryHooked = false;
    }

    if (memoryManager != null)
    {
        await memoryManager.DisposeAsync();
        memoryManager = null;
    }

    await base.DestroyAsync();
}
```

- [ ] **Step 5: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter MemoryStorageConnectionShouldBeDisposedByMemoryServiceLifecycle
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

Expected: target test passes, full framework tests pass, build succeeds.

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
- Modify: `sources/Alife.Function/Alife.Function.WebBridge/WebBridgeService.cs`
- Test: `Tests/Alife.Test.Framework/DiagnosticSnapshotTests.cs`

**Why this matters:** Alife has many moving parts. Local diagnostics give interviewers and maintainers a concrete failure story: counters, durations, and timestamps.

- [ ] **Step 1: Write failing tests**

Create `Tests/Alife.Test.Framework/DiagnosticSnapshotTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Alife.Framework;
using Alife.Framework.Diagnostics;
using Alife.Function.WebBridge;

namespace Alife.Test.Framework;

public class DiagnosticSnapshotTests
{
    [Test]
    public void DiagnosticSnapshotServiceShouldRecordCountersAndDurations()
    {
        DiagnosticSnapshotService service = new();

        service.Increment("WebBridge.Sync.Failure");
        service.RecordDuration("Memory.Search", TimeSpan.FromMilliseconds(42));
        DiagnosticSnapshot snapshot = service.CreateSnapshot();

        Assert.That(snapshot.Counters["WebBridge.Sync.Failure"], Is.EqualTo(1));
        Assert.That(snapshot.Durations["Memory.Search"].LastMilliseconds, Is.EqualTo(42));
        Assert.That(snapshot.Durations["Memory.Search"].Count, Is.EqualTo(1));
        Assert.That(snapshot.CreatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public async Task WebBridgeSyncOnceShouldRecordDiagnosticDuration()
    {
        RecordingHandler handler = new();
        DiagnosticSnapshotService diagnostics = new();
        WebApiClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://foxd.example/")
        }, new WebBridgeServiceConfig());
        WebBridgeService service = new(client, new MemoryCharacterBridgeStore(), null, diagnostics);

        await service.SyncOnce(CancellationToken.None);

        DiagnosticSnapshot snapshot = diagnostics.CreateSnapshot();
        Assert.That(snapshot.Counters["WebBridge.Sync.Success"], Is.EqualTo(1));
        Assert.That(snapshot.Durations["WebBridge.SyncOnce"].Count, Is.EqualTo(1));
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            object response = request.RequestUri?.AbsolutePath switch
            {
                "/api/pet/assets" => new WebAssetManifest
                {
                    Files =
                    [
                        new WebAssetFile
                        {
                            RelativePath = "model/Mao/texture.png",
                            ContentBase64 = Convert.ToBase64String([1, 2, 3])
                        }
                    ]
                },
                _ => new WebAvatarConfig
                {
                    Id = "avatar-remote",
                    Name = "Remote character",
                    Description = "from web",
                    Prompt = "remote prompt",
                    Modules = ["module.remote"]
                }
            };

            string responseJson = JsonSerializer.Serialize(response);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }

    sealed class MemoryCharacterBridgeStore : ICharacterBridgeStore
    {
        public List<Character> SavedCharacters { get; } = new();

        public Character UpsertCharacter(WebAvatarConfig avatarConfig)
        {
            Character character = CharacterSync.ToCharacter(avatarConfig);
            SavedCharacters.Add(character);
            return character;
        }
    }
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter DiagnosticSnapshotTests
```

Expected: compile fails because diagnostic types and the new WebBridge constructor do not exist.

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
using System.Linq;

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

- [ ] **Step 5: Wire WebBridge metrics**

In `WebBridgeService.cs`, add:

```csharp
using System.Diagnostics;
using Alife.Framework.Diagnostics;
```

Change the test constructor to:

```csharp
public WebBridgeService(
    WebApiClient webApiClient,
    ICharacterBridgeStore characterStore,
    WebAssetSync? assetSync = null,
    DiagnosticSnapshotService? diagnostics = null)
{
    this.webApiClient = webApiClient;
    this.characterStore = characterStore;
    this.assetSync = assetSync;
    this.diagnostics = diagnostics;
}
```

Add the field:

```csharp
DiagnosticSnapshotService? diagnostics;
```

Update `SyncOnce`:

```csharp
public async Task<WebBridgeSyncResult> SyncOnce(CancellationToken cancellationToken = default)
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    try
    {
        Character character = await PullConfig(cancellationToken);
        bool assetsSynced = false;
        if (Configuration?.SyncAssetsEnabled != false)
        {
            await PullAssets(cancellationToken);
            assetsSynced = true;
        }

        await PushState(character, cancellationToken);
        diagnostics?.Increment("WebBridge.Sync.Success");
        return new WebBridgeSyncResult(character, assetsSynced);
    }
    catch
    {
        diagnostics?.Increment("WebBridge.Sync.Failure");
        throw;
    }
    finally
    {
        stopwatch.Stop();
        diagnostics?.RecordDuration("WebBridge.SyncOnce", stopwatch.Elapsed);
    }
}
```

- [ ] **Step 6: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter DiagnosticSnapshotTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

Expected: target tests pass, full framework tests pass, build succeeds.

- [ ] **Step 7: Commit**

Run:

```powershell
git add sources/Alife/Alife.Framework/Diagnostics sources/Alife.Function/Alife.Function.WebBridge/WebBridgeService.cs Tests/Alife.Test.Framework/DiagnosticSnapshotTests.cs
git commit -m "Add local diagnostic snapshot service"
```

### Task A5: Prepare A Two-Minute Technical Demo Script

**Files:**
- Create: `docs/demo/alife-interview-demo-script.md`

- [ ] **Step 1: Create the directory**

Run:

```powershell
New-Item -ItemType Directory -Path 'D:\Alife\docs\demo' -Force
```

- [ ] **Step 2: Create the script**

Create `docs/demo/alife-interview-demo-script.md`:

```markdown
# Alife Two-Minute Interview Demo Script

## Claim

Alife is a local AI agent runtime with pluginized capabilities, long-term memory, visible Live2D embodiment, PAD emotion state, and WebBridge ecosystem direction.

## Flow

1. Start Alife and open one role activity.
2. Show enabled modules: Memory, Emotion, DeskPet, one tool module, and WebBridge if configured.
3. Trigger a positive interaction and show PAD state changing.
4. Show the Live2D desk pet expression or posture parameter responding.
5. Trigger a memory write or search and show DuckDB-backed retrieval.
6. Show diagnostic snapshot counters or logs for the flow.
7. Explain FOXD/WebBridge as the ecosystem extension, noting that real-service E2E is paused until the FOXD Web endpoint is complete.

## Backup If Visual Smoke Is Unavailable

- PAD emotion tests.
- DeskPet IPC/protocol tests.
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

## 4. Execution Track B: User Experience

### Task B1: Standardize Startup Status Reporting

**Files:**
- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatus.cs`
- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusService.cs`
- Create: `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusProgress.cs`
- Test: `Tests/Alife.Test.Framework/StartupStatusTests.cs`

**Why this matters:** Users forgive waiting when the app explains what is happening. Silent startup looks broken.

- [ ] **Step 1: Write failing tests**

Create `Tests/Alife.Test.Framework/StartupStatusTests.cs`:

```csharp
using Alife.Framework.Models.Runtime;

namespace Alife.Test.Framework;

public class StartupStatusTests
{
    [Test]
    public void StartupStatusServiceShouldTrackLatestStage()
    {
        StartupStatusService service = new();

        service.Report("LoadingModules", "Loading modules", 0.25f);
        StartupStatus status = service.Current;

        Assert.That(status.Stage, Is.EqualTo("LoadingModules"));
        Assert.That(status.Message, Is.EqualTo("Loading modules"));
        Assert.That(status.Progress, Is.EqualTo(0.25f));
        Assert.That(status.UpdatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public void StartupStatusProgressShouldAdaptExistingProgressReports()
    {
        StartupStatusService service = new();
        StartupStatusProgress progress = new(service);

        progress.Report(("Starting module DeskPetService", 0.75f));

        Assert.That(service.Current.Stage, Is.EqualTo("Startup"));
        Assert.That(service.Current.Message, Is.EqualTo("Starting module DeskPetService"));
        Assert.That(service.Current.Progress, Is.EqualTo(0.75f));
    }
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter StartupStatusTests
```

Expected: compile fails because startup status types do not exist.

- [ ] **Step 3: Implement the status model**

Create `sources/Alife/Alife.Framework/Models/Runtime/StartupStatus.cs`:

```csharp
namespace Alife.Framework.Models.Runtime;

public record StartupStatus(string Stage, string Message, float Progress, DateTimeOffset UpdatedAt);
```

- [ ] **Step 4: Implement the status service**

Create `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusService.cs`:

```csharp
namespace Alife.Framework.Models.Runtime;

public class StartupStatusService
{
    public StartupStatus Current { get; private set; } = new("Idle", "Waiting to start", 0f, DateTimeOffset.Now);

    public void Report(string stage, string message, float progress)
    {
        Current = new StartupStatus(stage, message, Math.Clamp(progress, 0f, 1f), DateTimeOffset.Now);
    }
}
```

- [ ] **Step 5: Implement the progress adapter**

Create `sources/Alife/Alife.Framework/Models/Runtime/StartupStatusProgress.cs`:

```csharp
namespace Alife.Framework.Models.Runtime;

public class StartupStatusProgress(StartupStatusService service) : IProgress<(string, float)>
{
    public void Report((string, float) value)
    {
        service.Report("Startup", value.Item1, value.Item2);
    }
}
```

- [ ] **Step 6: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter StartupStatusTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

- [ ] **Step 7: Commit**

Run:

```powershell
git add sources/Alife/Alife.Framework/Models/Runtime Tests/Alife.Test.Framework/StartupStatusTests.cs
git commit -m "Add startup status tracking"
```

### Task B2: Add DeskPet Visual Smoke Checklist And Edge/WebView2 Runbook

**Files:**
- Create: `docs/runbooks/deskpet-visual-smoke.md`

- [ ] **Step 1: Create the directory**

Run:

```powershell
New-Item -ItemType Directory -Path 'D:\Alife\docs\runbooks' -Force
```

- [ ] **Step 2: Create the runbook**

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
7. Trigger a PAD positive event and verify Live2D parameters visibly soften or brighten the pose.
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
- Modify: `Tests/Alife.Test.Framework/PadEmotionEngineTests.cs`

**Why this matters:** Users read jitter as fake or broken emotion. Small-value changes should not flood IPC or produce visible twitching.

- [ ] **Step 1: Add a failing test**

Add this test to `PadEmotionEngineTests.cs`:

```csharp
[Test]
public void EmotionLive2DDriverSkipsTinyParameterChanges()
{
    PADEmotionEngine engine = new();
    CapturingEmotionParameterSink sink = new();
    EmotionLive2DParameterDriver driver = new(engine, sink, minimumDelta: 0.03f);

    engine.ModulatePAD(0.2f, 0.1f, 0.1f);
    driver.PushCurrentState();
    engine.ModulatePAD(0.001f, 0.001f, 0.001f);
    driver.PushCurrentState();

    Assert.That(sink.PushCount, Is.EqualTo(1));
}
```

Update the existing nested `CapturingEmotionParameterSink`:

```csharp
sealed class CapturingEmotionParameterSink : IEmotionParameterSink
{
    public Dictionary<string, float>? LastParameters { get; private set; }
    public int PushCount { get; private set; }

    public void SetParams(Dictionary<string, float> parameters)
    {
        LastParameters = parameters;
        PushCount++;
    }
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter EmotionLive2DDriverSkipsTinyParameterChanges
```

Expected: FAIL because the driver currently calls `SetParams` every time.

- [ ] **Step 3: Implement thresholding**

Replace `EmotionLive2DParameterDriver.cs` with this shape:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.Emotion;

public class EmotionLive2DParameterDriver
{
    public EmotionLive2DParameterDriver(
        PADEmotionEngine emotionEngine,
        IEmotionParameterSink parameterSink,
        EmotionParameterMapper? parameterMapper = null,
        float minimumDelta = 0f)
    {
        this.emotionEngine = emotionEngine;
        this.parameterSink = parameterSink;
        this.parameterMapper = parameterMapper ?? new EmotionParameterMapper();
        this.minimumDelta = Math.Max(0f, minimumDelta);
    }

    public Dictionary<string, float> PushCurrentState()
    {
        Dictionary<string, float> parameters = parameterMapper.MapEmotionToParams(
            emotionEngine.Pleasure,
            emotionEngine.Arousal,
            emotionEngine.Dominance);

        if (ShouldPush(parameters))
        {
            parameterSink.SetParams(parameters);
            lastParameters = parameters;
        }

        return parameters;
    }

    bool ShouldPush(Dictionary<string, float> parameters)
    {
        if (lastParameters == null)
            return true;

        return parameters.Any(pair =>
            lastParameters.TryGetValue(pair.Key, out float previous) == false ||
            Math.Abs(pair.Value - previous) >= minimumDelta);
    }

    readonly PADEmotionEngine emotionEngine;
    readonly IEmotionParameterSink parameterSink;
    readonly EmotionParameterMapper parameterMapper;
    readonly float minimumDelta;
    Dictionary<string, float>? lastParameters;
}
```

- [ ] **Step 4: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter EmotionLive2DDriverSkipsTinyParameterChanges
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter PadEmotionEngineTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

- [ ] **Step 5: Commit**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.Emotion/EmotionLive2DParameterDriver.cs Tests/Alife.Test.Framework/PadEmotionEngineTests.cs
git commit -m "Smooth PAD Live2D parameter updates"
```

### Task B4: Add User-Facing Error Classification

**Files:**
- Create: `sources/Alife/Alife.Framework/Diagnostics/UserFacingError.cs`
- Create: `sources/Alife/Alife.Framework/Diagnostics/UserFacingErrorClassifier.cs`
- Test: `Tests/Alife.Test.Framework/UserFacingErrorClassifierTests.cs`

**Why this matters:** Users should receive stable, recoverable explanations. Logs can keep full developer detail.

- [ ] **Step 1: Write failing tests**

Create `Tests/Alife.Test.Framework/UserFacingErrorClassifierTests.cs`:

```csharp
using Alife.Framework.Diagnostics;

namespace Alife.Test.Framework;

public class UserFacingErrorClassifierTests
{
    [Test]
    public void ClassifierShouldMapTimeoutToRecoverableMessage()
    {
        UserFacingError error = UserFacingErrorClassifier.Classify(new TimeoutException("request timed out"));

        Assert.That(error.IsRecoverable, Is.True);
        Assert.That(error.Category, Is.EqualTo("Timeout"));
        Assert.That(error.Message, Does.Contain("temporarily did not respond"));
    }

    [Test]
    public void ClassifierShouldHideUnknownExceptionDetails()
    {
        UserFacingError error = UserFacingErrorClassifier.Classify(new InvalidOperationException("secret stack detail"));

        Assert.That(error.IsRecoverable, Is.False);
        Assert.That(error.Category, Is.EqualTo("Unknown"));
        Assert.That(error.Message, Does.Not.Contain("secret stack detail"));
    }
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter UserFacingErrorClassifierTests
```

Expected: compile fails because the classifier does not exist.

- [ ] **Step 3: Implement the record**

Create `sources/Alife/Alife.Framework/Diagnostics/UserFacingError.cs`:

```csharp
namespace Alife.Framework.Diagnostics;

public record UserFacingError(string Message, bool IsRecoverable, string Category);
```

- [ ] **Step 4: Implement the classifier**

Create `sources/Alife/Alife.Framework/Diagnostics/UserFacingErrorClassifier.cs`:

```csharp
using System.Net.Http;

namespace Alife.Framework.Diagnostics;

public static class UserFacingErrorClassifier
{
    public static UserFacingError Classify(Exception exception)
    {
        return exception switch
        {
            TimeoutException => new UserFacingError("The service temporarily did not respond. The current state was kept, so you can retry later.", true, "Timeout"),
            OperationCanceledException => new UserFacingError("The operation was canceled.", true, "Canceled"),
            IOException => new UserFacingError("Local file access failed. Check whether the file is in use and whether the path is available.", true, "FileSystem"),
            HttpRequestException => new UserFacingError("Network access failed. Check the connection or retry later.", true, "Network"),
            _ => new UserFacingError("The feature failed to run. Details were written to the log.", false, "Unknown")
        };
    }
}
```

- [ ] **Step 5: Verify**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore --filter UserFacingErrorClassifierTests
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj' --no-restore
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

- [ ] **Step 6: Commit**

Run:

```powershell
git add sources/Alife/Alife.Framework/Diagnostics Tests/Alife.Test.Framework/UserFacingErrorClassifierTests.cs
git commit -m "Add user facing error classification"
```

### Task B5: Define The Real User Acceptance Demo

**Files:**
- Create: `docs/demo/alife-user-acceptance-demo.md`

- [ ] **Step 1: Create the acceptance demo**

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

## 5. Local Tracking Markers

After each task completes, add the matching marker to `MASTER_EXECUTION_PLAN.md`:

```markdown
- [ ] PERF-2: `WindowsPlatform.Command` and `AlifePlatform.Command` converted to async-only APIs.
- [ ] PERF-1b: `MemoryStorage` disposal connected through `MemoryManager` and `MemoryService.DestroyAsync`.
- [ ] UX-1: DeskPet Edge/WebView2 visual smoke runbook added.
- [ ] UX-2: PAD-to-Live2D parameter updates skip sub-threshold changes.
- [ ] OPS-1: Local diagnostic snapshot service added and WebBridge sync emits counters/durations.
- [ ] UX-3: User-facing error classification added.
- [ ] UX-4: Startup status tracking service and progress adapter added.
- [ ] DOC-1: Interview architecture story and two-minute demo script added.
- [ ] DOC-2: User acceptance demo script added.
```

---

## 6. Success Metrics

### Interview Metrics

- Architecture story exists and matches current code.
- Full framework tests pass.
- Full solution build passes.
- At least one end-to-end demo script exists.
- Diagnostic snapshot can report counters and durations.
- Critical lifecycle/resource questions have direct answers:
  - Who owns modules?
  - Who stops background loops?
  - Who closes DuckDB?
  - How are plugin risks bounded?
  - How are failures observed?

### User Experience Metrics

- Startup has visible status data that UI can bind to.
- Normal interaction has no known command-execution freeze from platform setup paths.
- DeskPet appears and remains nonblank in the smoke runbook.
- PAD-driven Live2D updates are visible but not jittery.
- Recoverable errors produce understandable messages.
- Closing a character activity does not leave orphan desk pet or background loops.

---

## 7. Self-Review

- Spec coverage: The plan separately covers interview upper/lower ceiling and user experience upper/lower floor.
- Placeholder scan: The code tasks include exact file paths, test snippets, implementation snippets, commands, and expected outcomes.
- Type consistency: Constructor and method names match the current repository shape observed on 2026-06-10: `PADEmotionEngine`, `IEmotionParameterSink`, `EmotionLive2DParameterDriver`, `MemoryManager`, `MemoryService`, `WebBridgeService`, `ChatActivity`, and `InteractiveModule`.
- Scope control: The plan does not add unrelated plugins. It deepens existing runtime, memory, diagnostics, desk pet, emotion, startup, and documentation chains.
- Risk control: Visual checks remain runbook-based because WebView2 and Live2D rendering require real windows. Protocol tests alone cannot prove the user-visible body is nonblank.
