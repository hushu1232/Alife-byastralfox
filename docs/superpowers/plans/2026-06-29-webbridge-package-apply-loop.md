# WebBridge Package Apply Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Alife .NET 9 local confirmation/apply loop for staged WebBridge packages.

**Architecture:** Keep package staging and apply state in `WebBridgePackageInstaller`. Add `WebBridgeService.ApplyPackage` as the local confirmation entry point that reports `packageApplied` or `packageFailed` to FOXD Web through the existing status endpoint.

**Tech Stack:** C# 13, .NET 9, NUnit, existing Alife WebBridge module and HTTP client.

---

### Task 1: Add Installer Apply Tests

**Files:**
- Modify: `Tests/Alife.Test.Framework/WebBridgeServiceTests.cs`

- [ ] **Step 1: Write a failing test for successful local apply**

Add a test that installs a package into a temp root, calls `ApplyPackage`, asserts `Status == WebBridgePackageStatus.Applied`, asserts `ActiveConfig/<packageId>.json` exists, and asserts `catalog.json` contains `"applied"`.

- [ ] **Step 2: Write a failing test for missing draft rejection**

Add a test that installs a package, deletes the staged draft file, calls `ApplyPackage`, expects `InvalidOperationException`, and asserts `catalog.json` still contains `"pendingActivation"`.

- [ ] **Step 3: Run focused tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test "D:\FOXD\.worktrees\alife-webbridge-apply-loop\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj" --filter "WebBridgePackageInstallerApply" -v:minimal
```

Expected: compile failure or test failure because `ApplyPackage` and `Applied` do not exist yet.

### Task 2: Implement Installer Apply

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.WebBridge/WebBridgeInstallModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.WebBridge/WebBridgePackageInstaller.cs`

- [ ] **Step 1: Add `WebBridgePackageStatus.Applied`**

Add:

```csharp
public const string Applied = "applied";
```

- [ ] **Step 2: Add `AppliedAtUtc` and `ActiveConfigPath` to catalog records**

Add nullable metadata to `WebBridgeInstalledPackageRecord`:

```csharp
public DateTimeOffset? AppliedAtUtc { get; set; }
public string ActiveConfigPath { get; set; } = string.Empty;
```

- [ ] **Step 3: Implement `WebBridgePackageInstaller.ApplyPackage`**

Read `catalog.json`, find the pending package, validate the draft path, copy the draft into `ActiveConfig/<packageId>.json`, set status to `applied`, set `AppliedAtUtc`, set `ActiveConfigPath`, write catalog, and return a `WebBridgeInstallResult` containing the known paths.

- [ ] **Step 4: Run installer tests and verify GREEN**

Run the same `WebBridgePackageInstallerApply` filter. Expected: successful apply tests pass.

### Task 3: Add Service Apply Reporting Tests

**Files:**
- Modify: `Tests/Alife.Test.Framework/WebBridgeServiceTests.cs`

- [ ] **Step 1: Write a failing test for `packageApplied` reporting**

Use `RecordingHandler` and a temp package root. Install a package, call `service.ApplyPackage("xiayu-character-bundle")`, then assert posted milestones end with `packageApplied`.

- [ ] **Step 2: Write a failing test for apply failure reporting**

Create a package root with no catalog, call `service.ApplyPackage("missing-package")`, expect `InvalidOperationException`, and assert the handler posted a `packageFailed` milestone.

- [ ] **Step 3: Run focused tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test "D:\FOXD\.worktrees\alife-webbridge-apply-loop\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj" --filter "WebBridgeServiceApply" -v:minimal
```

Expected: compile failure or test failure because `WebBridgeService.ApplyPackage` does not exist yet.

### Task 4: Implement Service Apply

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.WebBridge/WebBridgeSyncStatusModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.WebBridge/WebBridgeService.cs`
- Modify: `sources/Alife.Function/Alife.Function.WebBridge/WebBridgePackageInstaller.cs`

- [ ] **Step 1: Add milestone constant**

Add:

```csharp
public const string PackageApplied = "packageApplied";
```

- [ ] **Step 2: Add `WebBridgeService.ApplyPackage`**

Resolve `WebApiClient`, create or reuse the installer, apply the staged package, load its manifest for version reporting, report `packageApplied`, and return the apply result.

- [ ] **Step 3: Preserve failure reporting**

On local apply failure, best-effort report `packageFailed` with `ErrorFromException(exception)` and rethrow.

- [ ] **Step 4: Run service tests and verify GREEN**

Run the `WebBridgeServiceApply` filter. Expected: service apply tests pass.

### Task 5: Verify And Commit

**Files:**
- Verify all changed files.

- [ ] **Step 1: Run full focused WebBridge tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test "D:\FOXD\.worktrees\alife-webbridge-apply-loop\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj" --filter "WebBridge" -v:minimal
```

Expected: all WebBridge tests pass.

- [ ] **Step 2: Build the WebBridge project**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build "D:\FOXD\.worktrees\alife-webbridge-apply-loop\sources\Alife.Function\Alife.Function.WebBridge\Alife.Function.WebBridge.csproj" --no-restore -v:minimal
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Review git diff**

```powershell
git -C "D:\FOXD\.worktrees\alife-webbridge-apply-loop" diff --check
git -C "D:\FOXD\.worktrees\alife-webbridge-apply-loop" status --short --branch --untracked-files=all
```

Expected: only WebBridge source, tests, and the two planning docs changed.

- [ ] **Step 4: Commit**

```powershell
git -C "D:\FOXD\.worktrees\alife-webbridge-apply-loop" add docs/superpowers/specs/2026-06-29-webbridge-package-apply-loop-design.md docs/superpowers/plans/2026-06-29-webbridge-package-apply-loop.md sources/Alife.Function/Alife.Function.WebBridge Tests/Alife.Test.Framework/WebBridgeServiceTests.cs
git -C "D:\FOXD\.worktrees\alife-webbridge-apply-loop" commit -m "feat: apply staged WebBridge packages"
```
