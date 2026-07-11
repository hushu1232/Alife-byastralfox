# Alife Local Dual-Account Production Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make two local NapCat/OneBot-backed Alife accounts independently usable on one Windows machine, with account-scoped supervision, recovery, durable heavy-work scheduling, and on-demand capability adapters.

**Architecture:** Windows Task Scheduler starts one local PowerShell supervisor. It owns two active-active slots and exposes no listener. Per-process environment roots isolate the Alife client; a .NET 9 runtime library supplies strict configuration validation, SQLite durable work state, resource leases, safe status, and adapter contracts. QChat owns reconnection; PowerShell owns health, draining, and account-only process restart.

**Tech Stack:** .NET 9 / C# / NUnit 4, Microsoft.Data.Sqlite, PowerShell, Windows Task Scheduler, existing NapCat + loopback OneBot WebSocket.

---

## Delivery boundaries

- Local Windows only: no public API, cloud service, Redis, BullMQ, Node.js worker, Vue, ChatBI Console, FOXD change, push, PR, or merge.
- Account A/B are active-active and fully isolated: QQ login, OneBot token/port, session/message data, runtime/storage/temp/logs, WebView2 profile, queue database, health/retry history, and tasks are never shared or migrated.
- Shared assets are read-only binaries, approved models, knowledge assets, and configuration templates.
- Heavy capabilities start only after an explicit approved request and bind to loopback. They never download or install dependencies/models/browsers; they cannot write SQL/checkpoints or send QQ text directly.
- All status/notice output is allowlisted safe state and reason code only; it never includes token, login state, chat/model text, SQL, stack trace, hidden context, or absolute path.
- Automated tasks use fakes and temporary directories. Only Task 11 may touch live processes, after explicit owner authorization.

## File structure

| Path | Responsibility |
|---|---|
| Sources/Alife/Alife.Platform/AlifePath.cs | Read per-process root overrides before creating runtime/storage/temp roots. |
| Sources/Alife.Function/Alife.Function.LocalRuntime/* | Validated configuration, safe models, SQLite queue, leases, recovery, adapters. |
| Tests/Alife.Test.LocalRuntime/* | NUnit tests for local runtime contracts and acceptance simulation. |
| Sources/Alife.Function/Alife.Function.QChat/OneBotReconnectPolicy.cs | Deterministic bounded reconnection decision. |
| Sources/Alife.Function/Alife.Function.QChat/OneBotConnectionSupervisor.cs | Serializes reconnect attempts without exposing credentials. |
| tools/local-production/*.ps1 | Configuration, supervisor, scheduler installation, safe report, mocked tests. |
| config/local-production/accounts.example.json | Committable no-secret two-slot template. |
| config/local-production/accounts.local.json | Ignored real local account references. |
| docs/runbooks/alife-local-dual-account-production.md | Bootstrap, maintenance, drills, observation and acceptance evidence. |
| Alife.slnx | New runtime/test project registration. |

### Task 1: Isolate Alife roots per account process

**Files:**
- Modify: Sources/Alife/Alife.Platform/AlifePath.cs
- Create: Tests/Alife.Test.Framework/AlifePathEnvironmentOverrideTests.cs

- [ ] **Step 1: Write failing resolver tests.**

~~~csharp
[Test]
public void ResolveLocalProductionPaths_uses_explicit_absolute_account_roots()
{
    var paths = AlifePath.ResolveLocalProductionPaths(
        @"D:\Alife\runtime\account-a",
        @"D:\Alife\storage\account-a",
        @"D:\Alife\.tmp\account-a");

    Assert.That(paths.RuntimeFolderPath, Is.EqualTo(@"D:\Alife\runtime\account-a"));
    Assert.That(paths.StorageFolderPath, Is.EqualTo(@"D:\Alife\storage\account-a"));
}

[TestCase("relative\\runtime")]
[TestCase("")]
public void ResolveLocalProductionPaths_rejects_non_absolute_runtime(string value) =>
    Assert.That(() => AlifePath.ResolveLocalProductionPaths(value, null, null),
        Throws.TypeOf<ArgumentException>());
~~~

- [ ] **Step 2: Run the focused test to prove the test is red.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.Framework/Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AlifePathEnvironmentOverrideTests" -v:minimal

Expected: compilation failure because ResolveLocalProductionPaths is absent.

- [ ] **Step 3: Implement the minimal resolver.** Create public sealed record AlifeLocalPaths(string RuntimeFolderPath, string StorageFolderPath, string TempFolderPath). In static initialization, resolve ALIFE_RUNTIME_PATH, ALIFE_STORAGE_PATH, and ALIFE_TEMP_PATH before legacy Runtime/storage_path.txt; explicit storage wins. Normalize every nonempty override with Path.GetFullPath, reject non-absolute input, and create only resolved directories. Keep legacy behavior when no override exists.

~~~csharp
public static AlifeLocalPaths ResolveLocalProductionPaths(string? runtime, string? storage, string? temp)
{
    static string? Full(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("Local production path overrides must be absolute.");
        return Path.GetFullPath(value);
    }

    return new(
        Full(runtime) ?? Path.Combine(RootFolderPath, "Runtime"),
        Full(storage) ?? Path.Combine(RootFolderPath, "Storage"),
        Full(temp) ?? Path.Combine(RootFolderPath, ".tmp", "Alife.Client"));
}
~~~

- [ ] **Step 4: Run framework regression.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.Framework/Alife.Test.Framework.csproj --no-restore -v:minimal

Expected: all pass and test paths do not use a real account root.

- [ ] **Step 5: Commit.**

~~~powershell
git add Sources/Alife/Alife.Platform/AlifePath.cs Tests/Alife.Test.Framework/AlifePathEnvironmentOverrideTests.cs
git commit -m "feat(runtime): isolate Alife paths per local account"
~~~

### Task 2: Add strict production contracts and configuration

**Files:**
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/Alife.Function.LocalRuntime.csproj
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/LocalProductionContracts.cs
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/LocalProductionConfiguration.cs
- Create: Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj
- Create: Tests/Alife.Test.LocalRuntime/LocalProductionConfigurationTests.cs
- Modify: Alife.slnx

- [ ] **Step 1: Write failing configuration tests.**

~~~csharp
[Test]
public void Parse_accepts_exactly_two_unique_loopback_slots()
{
    var plan = LocalProductionConfiguration.Parse("""{"accounts":[
      {"id":"account-a","oneBotUrl":"ws://127.0.0.1:3001","runtimeRoot":"D:\\Alife\\runtime\\account-a","storageRoot":"D:\\Alife\\storage\\account-a","tempRoot":"D:\\Alife\\.tmp\\account-a"},
      {"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","runtimeRoot":"D:\\Alife\\runtime\\account-b","storageRoot":"D:\\Alife\\storage\\account-b","tempRoot":"D:\\Alife\\.tmp\\account-b"}]}""");
    Assert.That(plan.Accounts.Select(x => x.Id), Is.EqualTo(new[] { "account-a", "account-b" }));
}

[TestCase("ws://0.0.0.0:3001")]
[TestCase("ws://example.invalid:3001")]
public void Parse_rejects_non_loopback_uri(string url) =>
    Assert.That(() => LocalProductionConfiguration.Parse(ConfigurationFor(url)),
        Throws.TypeOf<LocalProductionConfigurationException>());
~~~

- [ ] **Step 2: Run the missing project test.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore -v:minimal

Expected: project/type failure.

- [ ] **Step 3: Implement vocabulary and validation.** Target net9.0-windows; add only Microsoft.Data.Sqlite 9.0.0, no queue framework. Define exactly these public names:

~~~csharp
public enum LocalAccountHealth { Healthy, Degraded, Unavailable, Draining }
public enum CapabilityKind { Speech, Vision, Browser, LangGraph }
public enum DurableTaskState { Queued, Starting, Ready, Running, Completed, TimedOut, Failed, Cancelled, Degraded }
public enum SafeReasonCode { None, Busy, DeadlineExceeded, DependencyUnavailable, HealthProbeFailed, RestartRecoveryRequired, ConfigurationRejected }
public sealed record LocalAccountSlot(string Id, Uri OneBotUrl, string RuntimeRoot, string StorageRoot, string TempRoot);
public sealed record LocalProductionPlan(IReadOnlyList<LocalAccountSlot> Accounts, int MaxQueueDepth, TimeSpan DrainTimeout, TimeSpan IdleTimeout);
public sealed record SafeLocalStatus(string Overall, IReadOnlyDictionary<string, LocalAccountHealth> Accounts, IReadOnlyDictionary<CapabilityKind, string> Capabilities, SafeReasonCode Reason);
~~~

Parse permits only account-a/account-b, validates distinct loopback host/ports and normalized roots, 1–100 queue depth, positive timeouts, and rejects token, secret, password, connectionString, and ownerId JSON properties.

- [ ] **Step 4: Add both projects to Alife.slnx, then run tests.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore -v:minimal

Expected: valid two-slot plan passes; every invalid case fails closed.

- [ ] **Step 5: Commit.**

~~~powershell
git add Alife.slnx Sources/Alife.Function/Alife.Function.LocalRuntime Tests/Alife.Test.LocalRuntime
git commit -m "feat(runtime): add local production configuration contracts"
~~~

### Task 3: Implement account-local durable state and global capability leases

**Files:**
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/SqliteDurableTaskStore.cs
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/CapabilityLeaseCoordinator.cs
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/DurableTaskRecovery.cs
- Create: Tests/Alife.Test.LocalRuntime/SqliteDurableTaskStoreTests.cs
- Create: Tests/Alife.Test.LocalRuntime/CapabilityLeaseCoordinatorTests.cs

- [ ] **Step 1: Write failing recovery and lease tests.**

~~~csharp
[Test]
public async Task Restart_requeues_only_nonexpired_retry_safe_work()
{
    var unsafeTask = await store.EnqueueAsync(NewTask("account-a", CapabilityKind.Vision, retrySafe: false));
    await store.TransitionAsync(unsafeTask.Id, DurableTaskState.Starting, SafeReasonCode.None);
    await store.TransitionAsync(unsafeTask.Id, DurableTaskState.Running, SafeReasonCode.None);
    await recovery.RecoverAfterSupervisorRestartAsync();
    Assert.That((await store.GetAsync(unsafeTask.Id))!.State, Is.EqualTo(DurableTaskState.Degraded));
}

[Test]
public async Task One_browser_lease_cannot_be_preempted()
{
    await using var lease = await leases.AcquireAsync("account-a", CapabilityKind.Browser, deadline, CancellationToken.None);
    Assert.That(await leases.TryAcquireAsync("account-b", CapabilityKind.Browser, deadline), Is.Null);
}
~~~

- [ ] **Step 2: Run focused tests and confirm missing implementation.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore --filter "FullyQualifiedName~DurableTaskStore|FullyQualifiedName~CapabilityLeaseCoordinator" -v:minimal

Expected: red before store/lease implementation.

- [ ] **Step 3: Implement SQLite schema and legal transitions.** One database is under each slot runtimeRoot/local-production/queue.db; never use a shared database. Persist only task_id, account_id, capability, created_at_utc, deadline_utc, attempt_count, retry_safe, state, safe_reason_code. Store access rejects another account id. Enforce in one transaction:

~~~csharp
Queued   => [Starting, TimedOut, Failed, Cancelled, Degraded],
Starting => [Ready, TimedOut, Failed, Cancelled, Degraded],
Ready    => [Running, TimedOut, Failed, Cancelled, Degraded],
Running  => [Completed, TimedOut, Failed, Cancelled, Degraded]
~~~

RecoverAfterSupervisorRestartAsync increments/requeues only unexpired retry_safe nonterminal rows. All other nonterminal rows become Degraded with RestartRecoveryRequired. Use exactly one SemaphoreSlim(1, 1) per CapabilityKind; no later request can preempt a holder and lease timeout returns Busy.

- [ ] **Step 4: Run complete runtime tests.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore -v:minimal

Expected: state, isolation, recovery, bounded deadline and lease tests pass.

- [ ] **Step 5: Commit.**

~~~powershell
git add Sources/Alife.Function/Alife.Function.LocalRuntime Tests/Alife.Test.LocalRuntime
git commit -m "feat(runtime): add local durable queue and capability leases"
~~~

### Task 4: Add one safe on-demand adapter contract

**Files:**
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/IHeavyCapabilityAdapter.cs
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/HeavyCapabilityExecutor.cs
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/LoopbackProcessCapabilityAdapter.cs
- Create: Tests/Alife.Test.LocalRuntime/HeavyCapabilityExecutorTests.cs

- [ ] **Step 1: Write failing readiness/deadline/drain/idle tests.**

~~~csharp
[Test]
public async Task Unhealthy_adapter_does_not_execute_user_work()
{
    var adapter = new FakeAdapter(AdapterHealth.Unhealthy);
    var result = await executor.ExecuteAsync(NewTask("account-a", CapabilityKind.Speech), adapter, CancellationToken.None);
    Assert.Multiple(() => {
        Assert.That(result.Reason, Is.EqualTo(SafeReasonCode.HealthProbeFailed));
        Assert.That(adapter.ExecuteCalls, Is.Zero);
    });
}
~~~

- [ ] **Step 2: Run test to prove types are missing.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore --filter "FullyQualifiedName~HeavyCapabilityExecutor" -v:minimal

Expected: red before adapter code.

- [ ] **Step 3: Implement common contract and executor.**

~~~csharp
public interface IHeavyCapabilityAdapter
{
    CapabilityKind Kind { get; }
    Task<AdapterReadiness> EnsureReadyAsync(DateTimeOffset deadline, CancellationToken cancellationToken);
    Task<AdapterHealth> GetHealthAsync(CancellationToken cancellationToken);
    Task<AdapterExecutionResult> ExecuteAsync(HeavyCapabilityRequest request, CancellationToken cancellationToken);
    Task DrainAsync(DateTimeOffset deadline, CancellationToken cancellationToken);
    Task StopIfIdleAsync(CancellationToken cancellationToken);
    SafeCapabilityStatus GetSafeStatus();
}
~~~

Only HeavyCapabilityExecutor may call an adapter: persist state, acquire lease, EnsureReadyAsync, require Healthy, execute constrained request, release lease, schedule idle stop. LoopbackProcessCapabilityAdapter accepts only preconfigured executable and 127.0.0.1/::1 health URI. Reject all other hosts and never call an installer, downloader, package manager, browser/model acquisition.

- [ ] **Step 4: Re-run all local-runtime tests.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore -v:minimal

Expected: safe unavailable, timeout, drain, and idle cases pass.

- [ ] **Step 5: Commit.**

~~~powershell
git add Sources/Alife.Function/Alife.Function.LocalRuntime Tests/Alife.Test.LocalRuntime
git commit -m "feat(runtime): add safe on-demand adapter contract"
~~~

### Task 5: Make OneBot recovery bounded, deterministic, and private

**Files:**
- Create: Sources/Alife.Function/Alife.Function.QChat/OneBotReconnectPolicy.cs
- Create: Sources/Alife.Function/Alife.Function.QChat/OneBotConnectionSupervisor.cs
- Modify: Sources/Alife.Function/Alife.Function.QChat/OneBotClient.cs
- Create: Tests/Alife.Test.QChat/OneBotConnectionSupervisorTests.cs

- [ ] **Step 1: Write a failing fake-runtime test.**

~~~csharp
[Test]
public async Task Supervisor_reaches_restart_threshold_after_bounded_backoff()
{
    var runtime = new FakeOneBotRuntime(connectFailuresBeforeSuccess: 3);
    var policy = new OneBotReconnectPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8), restartThreshold: 3);
    var outcome = await new OneBotConnectionSupervisor(runtime, policy, clock).EnsureConnectedAsync(CancellationToken.None);
    Assert.That(outcome, Is.EqualTo(OneBotConnectionOutcome.RestartThresholdReached));
    Assert.That(runtime.ConnectCalls, Is.EqualTo(3));
}
~~~

- [ ] **Step 2: Run focused test.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~OneBotConnectionSupervisorTests" -v:minimal

Expected: red because reconnect types are absent.

- [ ] **Step 3: Implement policy and supervisor.** NextDelay(failures) is min(initial * 2^(failures-1), maximum); test clock makes it deterministic. Serialize IOneBotRuntime.ConnectAsync, stop at threshold, expose only Connected, RetryScheduled, RestartThresholdReached. In OneBotClient.ReceiveLoop accept cancellation during normal disposal, emit false once, and complete/cancel pending actions. URL/token never appear in outcome/status/log text.

- [ ] **Step 4: Run OneBot regression tests.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~OneBot" -v:minimal

Expected: all selected offline tests pass.

- [ ] **Step 5: Commit.**

~~~powershell
git add Sources/Alife.Function/Alife.Function.QChat Tests/Alife.Test.QChat
git commit -m "feat(qchat): add bounded OneBot reconnection supervision"
~~~

### Task 6: Implement PowerShell two-slot process supervisor

**Files:**
- Create: tools/local-production/LocalProduction.Configuration.psm1
- Create: tools/local-production/Start-AlifeLocalSupervisor.ps1
- Create: tools/local-production/Get-AlifeLocalProductionStatus.ps1
- Create: tools/local-production/Test-AlifeLocalSupervisor.ps1
- Create: config/local-production/accounts.example.json
- Modify: .gitignore

- [ ] **Step 1: Write mocked script tests before implementation.**

~~~powershell
Assert-Equal (Get-OverallStatus @{ 'account-a' = 'Healthy'; 'account-b' = 'Degraded' }) 'degraded'
Assert-Equal (Get-OverallStatus @{ 'account-a' = 'Unavailable'; 'account-b' = 'Unavailable' }) 'unavailable'
Assert-Throws { Read-LocalProductionPlan '{"accounts":[{"id":"account-a","oneBotUrl":"ws://0.0.0.0:3001"}]}' } 'loopback'
Assert-Equal (Invoke-AccountRecovery -Slot $slot -ActiveWorkCount 1 -Now $now).Action 'drain'
Assert-Equal (Invoke-AccountRecovery -Slot $slot -ActiveWorkCount 0 -Now $now).Action 'restart-worker'
~~~

Tests inject fake Start-Process, Get-Process, TCP probe, clock/sleep and child-stop functions. They never launch NapCat/Alife.

- [ ] **Step 2: Run the script test to prove it fails.**

Run: pwsh -NoProfile -File tools/local-production/Test-AlifeLocalSupervisor.ps1

Expected: undefined helper failure.

- [ ] **Step 3: Implement strict configuration and state machine.** Module requires exactly two slots, unique loopback ports/absolute roots, rejects literal secrets. Local file uses environment-variable names such as oneBotTokenEnvironmentVariable; actual user-scoped token is injected only into child environment:

~~~powershell
$env:ALIFE_RUNTIME_PATH = $slot.runtimeRoot
$env:ALIFE_STORAGE_PATH = $slot.storageRoot
$env:ALIFE_TEMP_PATH = $slot.tempRoot
$env:ALIFE_WEBVIEW2_USER_DATA_FOLDER = (Join-Path $slot.runtimeRoot 'webview2')
$env:ALIFE_ONEBOT_URL = $slot.oneBotUrl
$env:ALIFE_ONEBOT_TOKEN = [Environment]::GetEnvironmentVariable($slot.oneBotTokenEnvironmentVariable, 'User')
~~~

Supervisor accepts PlanPath, Once, StatusPath, StatePath, DryRun, PollSeconds. Each cycle probes process, loopback TCP, safe business readiness. Recovery: reconnect window → backoff → threshold → drain failed slot → stop new slot work → wait active count zero or deadline → restart only failed process/slot → require 3 healthy cycles. Force stop only after deadline with DeadlineExceeded; restarting A never stops B. Atomic state contains only pid, health, failures, backoff deadline, restart count, drain flag, active count, safe reason. Status script has no Start-Process and emits safe fields only.

- [ ] **Step 4: Add template and ignored local paths.**

~~~json
{"maxQueueDepth":16,"drainTimeoutSeconds":90,"idleTimeoutSeconds":300,"accounts":[
 {"id":"account-a","oneBotUrl":"ws://127.0.0.1:3001","runtimeRoot":"D:\\Alife\\runtime\\account-a","storageRoot":"D:\\Alife\\storage\\account-a","tempRoot":"D:\\Alife\\.tmp\\account-a","oneBotTokenEnvironmentVariable":"ALIFE_ACCOUNT_A_ONEBOT_TOKEN"},
 {"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","runtimeRoot":"D:\\Alife\\runtime\\account-b","storageRoot":"D:\\Alife\\storage\\account-b","tempRoot":"D:\\Alife\\.tmp\\account-b","oneBotTokenEnvironmentVariable":"ALIFE_ACCOUNT_B_ONEBOT_TOKEN"}]}
~~~

Append config/local-production/accounts.local.json, runtime/account-*/, storage/account-*/, logs/account-*/, and *.local-production-state.json to .gitignore.

- [ ] **Step 5: Verify without live process.**

Run: pwsh -NoProfile -File tools/local-production/Test-AlifeLocalSupervisor.ps1

Run: pwsh -NoProfile -File tools/local-production/Start-AlifeLocalSupervisor.ps1 -PlanPath config/local-production/accounts.example.json -Once -DryRun

Expected: tests pass; dry run validates two slots and does not print token data or start process.

- [ ] **Step 6: Commit.**

~~~powershell
git add .gitignore config/local-production tools/local-production
git commit -m "feat(ops): add local dual-account supervisor"
~~~

### Task 7: Install one local task and document operations

**Files:**
- Create: tools/local-production/Install-AlifeLocalSupervisorTask.ps1
- Create: tools/local-production/Test-InstallAlifeLocalSupervisorTask.ps1
- Create: docs/runbooks/alife-local-dual-account-production.md

- [ ] **Step 1: Write failing mocked scheduler assertions.**

~~~powershell
Assert-Equal $registered.TaskName 'Alife Local Dual Account Supervisor'
Assert-True ($registered.Action.Execute -match 'pwsh.exe|powershell.exe')
Assert-False ($registered.Action.Arguments -match 'TOKEN|Bearer|accounts\.local\.json')
Assert-True ($registered.Principal.RunLevel -eq 'Limited')
~~~

- [ ] **Step 2: Run missing installer test.**

Run: pwsh -NoProfile -File tools/local-production/Test-InstallAlifeLocalSupervisorTask.ps1

Expected: missing-file failure.

- [ ] **Step 3: Implement installer/runbook.** Register exactly Alife Local Dual Account Supervisor at user logon and machine startup under current user, Limited run level. Command contains only script path and PlanPath; never secret. Remove unregisters only exact name. Runbook gives bootstrap, health layers, safe status command, A-only draining/restart, reason codes, local-only files, and eight live drills.

- [ ] **Step 4: Re-run installation test.**

Run: pwsh -NoProfile -File tools/local-production/Test-InstallAlifeLocalSupervisorTask.ps1

Expected: pass without registering a task.

- [ ] **Step 5: Commit.**

~~~powershell
git add tools/local-production docs/runbooks/alife-local-dual-account-production.md
git commit -m "docs(ops): add local supervisor bootstrap runbook"
~~~

### Task 8: Implement four concrete capability adapters

**Files:**
- Create: Sources/Alife.Function/Alife.Function.Speech/LocalSpeechCapabilityAdapter.cs
- Create: Sources/Alife.Function/Alife.Function.Vision/LocalVisionCapabilityAdapter.cs
- Create: Sources/Alife.Function/Alife.Function.Browser/LocalBrowserCapabilityAdapter.cs
- Create: Sources/Alife.Function/Alife.Function.DataAgent/LocalLangGraphCapabilityAdapter.cs
- Modify: four corresponding csproj files
- Create: Tests/Alife.Test.Speech/LocalSpeechCapabilityAdapterTests.cs
- Create: Tests/Alife.Test.QChat/LocalVisionCapabilityAdapterTests.cs
- Create: Tests/Alife.Test.Browser/LocalBrowserCapabilityAdapterTests.cs
- Create: Tests/Alife.Test.DataAgent/LocalLangGraphCapabilityAdapterTests.cs

- [ ] **Step 1: Write failing public-endpoint rejection test in each project.**

~~~csharp
[Test]
public async Task Browser_rejects_public_debug_endpoint_without_starting_process()
{
    var process = new FakeProcessHost();
    var adapter = new LocalBrowserCapabilityAdapter(new Uri("http://192.0.2.8:9222"), process);
    var readiness = await adapter.EnsureReadyAsync(DateTimeOffset.UtcNow.AddSeconds(1), CancellationToken.None);
    Assert.That(readiness.Reason, Is.EqualTo(SafeReasonCode.ConfigurationRejected));
    Assert.That(process.StartCalls, Is.Zero);
}
~~~

- [ ] **Step 2: Run four test filters.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --filter "FullyQualifiedName~LocalSpeechCapabilityAdapter|FullyQualifiedName~LocalVisionCapabilityAdapter|FullyQualifiedName~LocalBrowserCapabilityAdapter|FullyQualifiedName~LocalLangGraphCapabilityAdapter" -v:minimal

Expected: red because adapters are absent.

- [ ] **Step 3: Implement readiness boundaries.** Speech requires preinstalled local model/runtime plus minimal synthesis probe; vision needs preconfigured local inference plus minimal image probe; browser starts only preinstalled executable with isolated account context then checks loopback DevTools; LangGraph uses existing manually authorized loopback sidecar health protocol. Every unavailable/start timeout/health failure maps to DependencyUnavailable, DeadlineExceeded, HealthProbeFailed. Do not wire adapters directly to QQ and do not alter default DataAgent result.

- [ ] **Step 4: Run capability tests.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.Speech/Alife.Test.Speech.csproj --no-restore -v:minimal

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.Browser/Alife.Test.Browser.csproj --no-restore -v:minimal

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore -v:minimal

Expected: all use fake hosts; no real capability is launched.

- [ ] **Step 5: Commit.**

~~~powershell
git add Sources/Alife.Function Tests
git commit -m "feat(capabilities): add local on-demand adapters"
~~~

### Task 9: Add safe status and owner-notice projection

**Files:**
- Create: Sources/Alife.Function/Alife.Function.LocalRuntime/SafeLocalStatusFormatter.cs
- Create: Sources/Alife.Function/Alife.Function.QChat/QChatLocalProductionOwnerNotice.cs
- Create: Tests/Alife.Test.LocalRuntime/SafeLocalStatusFormatterTests.cs
- Create: Tests/Alife.Test.QChat/QChatLocalProductionOwnerNoticeTests.cs
- Modify: docs/runbooks/alife-local-dual-account-production.md

- [ ] **Step 1: Write failing safety test.**

~~~csharp
[Test]
public void Format_never_emits_sensitive_or_path_text()
{
    var text = formatter.Format(status, @"Bearer secret D:\Alife\storage\account-a SELECT * FROM chat");
    Assert.That(text, Does.Not.Contain("secret").And.Not.Contain(@"D:\").And.Not.Contain("SELECT"));
}
~~~

- [ ] **Step 2: Run focused tests.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --filter "FullyQualifiedName~SafeLocalStatusFormatter|FullyQualifiedName~LocalProductionOwnerNotice" -v:minimal

Expected: red before formatter/notice code.

- [ ] **Step 3: Implement safe output.** Formatter may output only healthy/degraded/unavailable, slot id, process/connection/business/drain/queue/restart enum states, capability lifecycle state, SafeReasonCode. Notice supports only draining, recovery, restart threshold, capability degraded/recovered, both-account outage. It deduplicates unchanged notice; QQ send failure is swallowed after local persistence and never blocks recovery.

- [ ] **Step 4: Run runtime and QChat suites.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore -v:minimal

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore -v:minimal

Expected: all rendered unsafe strings are absent.

- [ ] **Step 5: Commit.**

~~~powershell
git add Sources/Alife.Function Tests docs/runbooks/alife-local-dual-account-production.md
git commit -m "feat(ops): add safe local production status notices"
~~~

### Task 10: Simulate every production acceptance drill offline

**Files:**
- Create: Tests/Alife.Test.LocalRuntime/LocalDualAccountProductionScenarioTests.cs
- Modify: tools/local-production/Test-AlifeLocalSupervisor.ps1
- Modify: docs/runbooks/alife-local-dual-account-production.md

- [ ] **Step 1: Write failing scenario test.**

~~~csharp
[Test]
public async Task Account_a_restart_does_not_stop_b_or_migrate_b_task()
{
    await fixture.EnqueueAsync("account-b", CapabilityKind.Vision);
    await fixture.FailBusinessProbeAsync("account-a", consecutiveFailures: 3);
    await fixture.RunSupervisorCycleAsync();

    Assert.Multiple(() => {
        Assert.That(fixture.Status("account-a").Health, Is.EqualTo(LocalAccountHealth.Draining));
        Assert.That(fixture.Status("account-b").Health, Is.EqualTo(LocalAccountHealth.Healthy));
        Assert.That(fixture.Store("account-b").TaskAccountIds(), Is.All.EqualTo("account-b"));
    });
}
~~~

- [ ] **Step 2: Run scenario test.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore --filter "FullyQualifiedName~LocalDualAccountProductionScenarioTests" -v:minimal

Expected: red until fake fixture exists.

- [ ] **Step 3: Implement deterministic fake fixture.** Cover independent A/B baseline; A OneBot reconnect; A-only threshold drain/restart; drain during active finite work/deadline; one lease/bounded queue; unavailable/start-timeout/health fail for all adapters; supervisor durable recovery; repeated-cycle restart-storm detector. Assert all notices safe and every cross-account queue query empty.

- [ ] **Step 4: Run simulated acceptance plus full regression.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.LocalRuntime/Alife.Test.LocalRuntime.csproj --no-restore -v:minimal

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal

Expected: simulations and existing solution tests pass.

- [ ] **Step 5: Commit.**

~~~powershell
git add Tests/Alife.Test.LocalRuntime tools/local-production/Test-AlifeLocalSupervisor.ps1 docs/runbooks/alife-local-dual-account-production.md
git commit -m "test(ops): cover local dual-account recovery drills"
~~~

### Task 11: Execute owner-authorized live drills and observation

**Files:**
- Create (ignored only): Runtime/local-production-evidence/
- Modify: docs/runbooks/alife-local-dual-account-production.md

- [ ] **Step 1: Verify code before any live process starts.**

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal

Run: & "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal

Expected: zero errors and failures. If either fails, stop live work and use superpowers:systematic-debugging.

- [ ] **Step 2: Prepare ignored local configuration after explicit owner approval.** Copy example to accounts.local.json, set user-scoped token variables, validate without launch.

Run: pwsh -NoProfile -File tools/local-production/Start-AlifeLocalSupervisor.ps1 -PlanPath config/local-production/accounts.local.json -Once -DryRun

Expected: exactly two distinct loopback slots; no secret printed.

- [ ] **Step 3: Run bounded drills one at a time and retain safe local evidence only.** Record before/after status and PASS/FAIL: A+B baseline; disconnect A OneBot; threshold restart A; restart during approved active work; concurrent same-capability work; unavailable/start-timeout/health-fail each adapter; supervisor restart recovery; observation window. B stays healthy in every A-only drill.

- [ ] **Step 4: Decide production readiness from evidence.** Call system local dual-account production ready only when every one of eight drills passes and observation shows no restart storm, cross-talk, unsafe notice. Any failed row means not production ready, creates focused repair task, and cannot be overridden by unit tests.

- [ ] **Step 5: Commit only redacted runbook outcome.**

~~~powershell
git add docs/runbooks/alife-local-dual-account-production.md
git commit -m "docs(ops): record local production acceptance result"
~~~

## Final verification gate

- [ ] git diff --check returns no output.
- [ ] & "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal has zero errors.
- [ ] & "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal has zero failures.
- [ ] pwsh -NoProfile -File tools/local-production/Test-AlifeLocalSupervisor.ps1 passes mocked recovery/redaction tests.
- [ ] git status --short shows no Runtime, Storage, logs, accounts.local.json, token, login state, or evidence artifact staged.
- [ ] No upload action is taken. Before future upload, follow docs/alife-upload-rules.md and repository AGENTS.md.
