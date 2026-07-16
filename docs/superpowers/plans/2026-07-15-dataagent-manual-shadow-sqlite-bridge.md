# DataAgent Manual Shadow SQLite Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make each real operator-run `run-dataagent-v4-manual-shadow.ps1` success or fallback record a C#-validated, metadata-only LangGraph shadow artifact in the configured DataAgent SQLite store without changing the script's PASS/FALLBACK exit contract.

**Architecture:** Add a small .NET 9 command-line bridge under `tools/` which accepts only a closed set of scalar options, converts them into a typed C# request, and calls the existing `DataAgentLangGraphShadowArtifactRuntimeProvider`. Extend that provider with a narrow manual-outcome entry point that constructs only an existing sanitized artifact; PowerShell launches the bridge after the normal success or failure result is known, emits only `artifact_persisted=true|false`, and deliberately ignores bridge failure for its own exit code.

**Tech Stack:** .NET 9/C#, `Alife.Function.DataAgent`, existing Microsoft.Data.Sqlite artifact store, Windows PowerShell, NUnit.

---

## File structure

- Create: `tools/dataagent-shadow-artifact/Alife.Tools.DataAgentShadowArtifact.csproj` — .NET 9 console project that references `Alife.Function.DataAgent`.
- Create: `tools/dataagent-shadow-artifact/Program.cs` — closed-option parser, safe result writer, and bridge process entry point.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentLangGraphShadowArtifactRuntimeProvider.cs` — typed C# request validation and C#-only metadata artifact recording.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentLangGraphShadowArtifactStoreTests.cs` — unit coverage for the new provider request mapping and rejected unsafe/bad metadata.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs` — actual PowerShell loopback runs for accepted/fallback persistence and bridge-failure exit preservation.
- Modify: `tools/run-dataagent-v4-manual-shadow.ps1` — bounded bridge invocation on both terminal paths, with no raw content forwarded.
- Modify: `Alife.slnx` only if the repository convention requires tool projects to be solution members; otherwise retain the tool as an explicitly built `tools/` project.

### Task 1: Define the typed C# persistence boundary

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentLangGraphShadowArtifactRuntimeProvider.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentLangGraphShadowArtifactStoreTests.cs`

- [ ] **Step 1: Write failing provider tests for accepted and fallback scalar requests**

  Add a test that initializes a temporary SQLite schema, sets `ALIFE_DATAAGENT_STORE_PROVIDER=sqlite` and `ALIFE_DATAAGENT_SQLITE_PATH` to it, records the two typed requests below, then reads the aggregate. Restore both environment variables in `finally`.

  ```csharp
  DataAgentManualShadowArtifactRequest accepted = new(
      Outcome: "accepted",
      ReasonCode: "manual_shadow_handshake_accepted",
      HealthStatusCode: 200,
      HandshakeStatusCode: 200,
      ContextLayerCount: 3);
  DataAgentManualShadowArtifactRequest fallback = new(
      Outcome: "fallback",
      ReasonCode: "manual_shadow_response_rejected",
      HealthStatusCode: 200,
      HandshakeStatusCode: 0,
      ContextLayerCount: 3);

  Assert.That(DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowArtifact(accepted, now).Written, Is.True);
  Assert.That(DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowArtifact(fallback, now).Written, Is.True);
  Assert.That(store.ReadLangGraphShadowArtifactAggregate(now).Aggregate!.Accepted, Is.EqualTo(1));
  Assert.That(store.ReadLangGraphShadowArtifactAggregate(now).Aggregate!.Fallback, Is.EqualTo(1));
  ```

- [ ] **Step 2: Write failing rejection tests for non-closed, unsafe, and invalid scalar values**

  Add test cases that assert `Written == false` and the exact bounded reason `langgraph_artifact_bridge_input_rejected` for each of: `Outcome: "protocol_rejected"` (the script is allowed only `accepted`/`fallback`), a SQL reason (`"SELECT_hidden_context"`), a secret reason (`"access_token"`), a path reason (`"C:\\private"`), health status `99`, handshake status `600`, context count `4`, and a null/empty reason. After every rejected call assert no artifact is present. Do not add an API which receives summary text, JSON, SQL, paths, or arbitrary metadata.

- [ ] **Step 3: Run the new provider tests to verify they fail**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DataAgentLangGraphShadowArtifactStoreTests"
  ```

  Expected: compilation failure because `DataAgentManualShadowArtifactRequest` and `RecordManualShadowArtifact` do not yet exist.

- [ ] **Step 4: Add the smallest typed request and C# implementation**

  In `DataAgentLangGraphShadowArtifactRuntimeProvider.cs`, add this public input type next to the provider:

  ```csharp
  public sealed record DataAgentManualShadowArtifactRequest(
      string Outcome,
      string ReasonCode,
      int HealthStatusCode,
      int HandshakeStatusCode,
      int ContextLayerCount);
  ```

  Add `RecordManualShadowArtifact(DataAgentManualShadowArtifactRequest request, DateTimeOffset now)`. It must reject invalid input before opening/writing the store and return only `new DataAgentLangGraphShadowArtifactWriteResult(false, "langgraph_artifact_bridge_input_rejected")`. Accept only outcome tokens `accepted` and `fallback`; accept reason tokens of 1–128 ASCII letters/digits/`_`/`-`/`.` and reject the existing unsafe categories (SQL, credentials, secrets, hidden context, paths, controls). Accept HTTP status `0` only for unavailable/not-reached endpoints and otherwise `100..599`; accept exactly context count `3`.

  For valid input call the existing configured SQLite store creation logic, create an existing `DataAgentLangGraphShadowArtifact` with fixed `session_id` and `replay_id` equal to `manual-shadow`, map the two outcomes to `Accepted`/`Fallback`, and set `DiffGatePassed` to `true` only for accepted. Derive `Summary` in C# only as the fixed-safe string `manual_shadow_outcome;health_status=<n>;handshake_status=<n>;context_layers=3`; do not include a caller-supplied body or text. Keep existing `RecordManualShadowResult` behavior unchanged by sharing a private store-write helper where useful.

- [ ] **Step 5: Run the focused provider tests to verify they pass**

  Run the Step 3 command again.

  Expected: PASS and all existing artifact-store tests still pass.

- [ ] **Step 6: Commit the C# boundary change**

  ```powershell
  git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentLangGraphShadowArtifactRuntimeProvider.cs Tests/Alife.Test.DataAgent/DataAgentLangGraphShadowArtifactStoreTests.cs
  git commit -m "feat: add manual shadow artifact bridge boundary"
  ```

### Task 2: Build the strict standalone CLI bridge

**Files:**
- Create: `tools/dataagent-shadow-artifact/Alife.Tools.DataAgentShadowArtifact.csproj`
- Create: `tools/dataagent-shadow-artifact/Program.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Write failing process-level CLI tests**

  Add test helpers using `ProcessStartInfo` and `ArgumentList` to execute the built tool DLL with the user-local `dotnet`. Test a valid argument vector and assert exit code `0`, exact output `artifact_persisted=true`, and one SQLite accepted aggregate. Separately pass `--raw-json {}` and `--outcome protocol_rejected`; assert nonzero exit, exact output `artifact_persisted=false`, and zero stored artifacts. Set the two DataAgent store environment variables only on the child `ProcessStartInfo.Environment` so tests do not mutate shared process state.

  ```csharp
  startInfo.ArgumentList.Add(bridgeDll);
  startInfo.ArgumentList.Add("--outcome");
  startInfo.ArgumentList.Add("accepted");
  startInfo.ArgumentList.Add("--reason-code");
  startInfo.ArgumentList.Add("manual_shadow_handshake_accepted");
  startInfo.ArgumentList.Add("--health-status");
  startInfo.ArgumentList.Add("200");
  startInfo.ArgumentList.Add("--handshake-status");
  startInfo.ArgumentList.Add("200");
  startInfo.ArgumentList.Add("--context-layers");
  startInfo.ArgumentList.Add("3");
  ```

- [ ] **Step 2: Run the CLI tests to verify they fail**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DataAgentV40RealLangGraphManualShadowIntegrationTests"
  ```

  Expected: FAIL because the tool project/DLL does not exist.

- [ ] **Step 3: Create the console project and closed parser**

  Create `Alife.Tools.DataAgentShadowArtifact.csproj` targeting `net9.0` and referencing `../../sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>net9.0</TargetFramework>
      <ImplicitUsings>enable</ImplicitUsings>
      <Nullable>enable</Nullable>
    </PropertyGroup>
    <ItemGroup>
      <ProjectReference Include="../../sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj" />
    </ItemGroup>
  </Project>
  ```

  In `Program.cs`, require precisely five name/value options in any order: `--outcome`, `--reason-code`, `--health-status`, `--handshake-status`, `--context-layers`. Reject duplicate, missing, unknown, bare, and `--name=value` options; do not deserialize JSON and do not read stdin. Parse integers with invariant culture and `NumberStyles.None`, create `DataAgentManualShadowArtifactRequest`, and call `RecordManualShadowArtifact(request, DateTimeOffset.UtcNow)`. Emit exactly one safe marker: `artifact_persisted=true` on a successful write, otherwise `artifact_persisted=false`; return `0` or `1` respectively. Catch unexpected exceptions at the top level and return the same false marker/nonzero result without printing exception text.

- [ ] **Step 4: Build the tool and run the process-level tests**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" build tools\dataagent-shadow-artifact\Alife.Tools.DataAgentShadowArtifact.csproj --no-restore -v:minimal
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DataAgentV40RealLangGraphManualShadowIntegrationTests"
  ```

  Expected: build succeeds and both the valid write and invalid-input rejection tests pass.

- [ ] **Step 5: Commit the CLI bridge**

  ```powershell
  git add tools/dataagent-shadow-artifact Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
  git commit -m "feat: add DataAgent shadow artifact CLI"
  ```

### Task 3: Wire the real PowerShell manual-shadow terminal paths

**Files:**
- Modify: `tools/run-dataagent-v4-manual-shadow.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Write failing actual-script success/fallback tests**

  Extend the existing loopback server tests to execute `-File tools/run-dataagent-v4-manual-shadow.ps1` rather than a function-only helper. Pass a temporary SQLite path through the child process environment and point the script at the test-built bridge DLL through a new explicit `-ArtifactBridgePath` parameter. On a valid handshake, assert script exit `0`, `PASS manual_shadow`, `artifact_persisted=true`, and aggregate accepted `1`. On an invalid handshake body, assert exit `1`, the existing sanitized `FALLBACK manual_shadow manual_shadow_response_rejected`, `artifact_persisted=true`, and aggregate fallback `1` with no accepted row.

  Add a second bridge fixture that exits `1` and prints no unbounded content. Assert a valid handshake still exits `0`/prints PASS and an invalid handshake still exits `1`/prints the same fallback reason, both with `artifact_persisted=false`.

- [ ] **Step 2: Run the real-script tests to verify they fail**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DataAgentV40RealLangGraphManualShadowIntegrationTests"
  ```

  Expected: FAIL because the script has neither bridge parameter nor persistence marker/invocation.

- [ ] **Step 3: Add a bounded non-authoritative bridge invoker to the script**

  Add optional parameter `[string]$ArtifactBridgePath = ""`; when blank, resolve the checked-in bridge DLL relative to `$PSScriptRoot` at `dataagent-shadow-artifact/bin/Release/net9.0/Alife.Tools.DataAgentShadowArtifact.dll`. Add `Invoke-ManualShadowArtifactBridge` that takes only outcome, already-normalized reason, integer status values, and fixed `3` context layers. It must invoke the local .NET DLL with `& "C:\Users\hu shu\.dotnet\dotnet.exe" $bridgePath` and the five closed name/value arguments; it must never forward `$handshakeResponse.Content`, `$request`, `$OutputDirectory`, URI text, error text, JSON, context text, SQL, paths, or secrets.

  The function must catch all bridge exceptions, inspect only `$LASTEXITCODE`, emit exactly `artifact_persisted=true` on exit `0` and `artifact_persisted=false` otherwise, then return a Boolean. It must not throw. Keep the existing optional JSON output artifact independent from SQLite persistence.

  In the normal terminal path, call it after `Assert-ManualShadowHandshakeResponse` with:

  ```powershell
  Invoke-ManualShadowArtifactBridge -Outcome "accepted" -ReasonCode "manual_shadow_handshake_accepted" `
      -HealthStatusCode ([int]$healthResponse.StatusCode) -HandshakeStatusCode ([int]$handshakeResponse.StatusCode)
  ```

  In `catch`, call it before the existing fallback line with:

  ```powershell
  Invoke-ManualShadowArtifactBridge -Outcome "fallback" -ReasonCode $reason `
      -HealthStatusCode 0 -HandshakeStatusCode 0
  ```

  Do not change `exit 0`, `exit 1`, loopback checks, request/response validation, or the existing safe failure normalizer.

- [ ] **Step 4: Run the real-script tests to verify they pass**

  Re-run the Step 2 command. Also run the existing function-level harness tests in that fixture to prove the bridge helper does not weaken manual protocol validation.

- [ ] **Step 5: Commit script wiring**

  ```powershell
  git add tools/run-dataagent-v4-manual-shadow.ps1 Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
  git commit -m "feat: persist manual LangGraph shadow outcomes"
  ```

### Task 4: Verify boundary, aggregates, and delivery quality

**Files:**
- Modify only if verification exposes a concrete defect: the files from Tasks 1–3.
- Test: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
- Test: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj`

- [ ] **Step 1: Run focused bridge and actual-script checks**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" build tools\dataagent-shadow-artifact\Alife.Tools.DataAgentShadowArtifact.csproj --no-restore -v:minimal
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DataAgentLangGraphShadowArtifactStoreTests|FullyQualifiedName~DataAgentV40RealLangGraphManualShadowIntegrationTests"
  ```

  Expected: all selected tests pass, including a real PowerShell success write, a real PowerShell fallback write, unsafe CLI rejection, and bridge-failure exit preservation.

- [ ] **Step 2: Run full DataAgent and QChat regression suites**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
  ```

  Expected: no failed tests. Existing QChat `CS0067` unused fake-event warnings may remain warnings only.

- [ ] **Step 3: Perform the required authority-boundary review**

  Inspect the final diff and assert all of the following from source and tests: PowerShell forwards exactly five bounded scalar values; only C# writes SQLite; the CLI has no HTTP/client, runtime start, dependency install, SQL execution, audit/checkpoint, or QChat dependency; it emits no artifact body; owner diagnostics remain aggregate-only; the original manual script's exit results do not depend on the bridge result.

- [ ] **Step 4: Run whitespace and full-diff validation**

  Run:

  ```powershell
  git diff alife-byastralfox/master...HEAD --check
  git status --short --branch
  ```

  Expected: no whitespace errors and no generated `bin`, `obj`, SQLite databases, logs, artifacts, credentials, or unrelated edits staged.

- [ ] **Step 5: Commit any verification-only correction and request final review**

  If a concrete correction was required, commit it with a focused message. Then obtain implementation/spec and final delivery review before claiming completion; the delivery reviewer must inspect the actual `run-dataagent-v4-manual-shadow.ps1` execution route, not only a helper/provider test.
