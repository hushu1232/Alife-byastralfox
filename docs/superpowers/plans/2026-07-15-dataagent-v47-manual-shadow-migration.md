# DataAgent V4.7 Manual Shadow Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the existing manual shadow script's internal handshake from V4.0 to the real V4.7 LangGraph contract while retaining its C#-only SQLite artifact persistence and existing terminal interface.

**Architecture:** The PowerShell script will validate the loopback V4.7 health attestation, emit one exact V4.7 advisory request, and validate one exact V4.7 advisory response. Its established C# bridge call, bounded scalar arguments, safe fallback mapping, and PASS/FALLBACK isolation remain unchanged; tests make the V4.7 contract and real script-to-SQLite path explicit.

**Tech Stack:** Windows PowerShell 5, Python LangGraph 0.3.34 sidecar, .NET 9 DataAgent SQLite bridge, NUnit.

---

## File structure

- Modify: `tools/run-dataagent-v4-manual-shadow.ps1` — replace V4.0 health/request/response protocol handling with V4.7 strict validation; retain public parameters, artifact name, bridge, and terminal markers.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs` — replace V4.0 contract expectations with exact V4.7 loopback request/response fixtures and actual script persistence coverage.
- Modify: `docs/superpowers/specs/2026-07-15-dataagent-v47-manual-shadow-migration-design.md` only if review reveals a concrete ambiguity; otherwise leave committed design unchanged.

### Task 1: Specify the V4.7 script contract in failing tests

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Write a failing exact-V4.7 request test**

  Add a function-harness test that evaluates the renamed script request constructor, serializes it, and asserts the top-level property set is exactly:

  ```csharp
  string[] expected =
  [
      "RequestId", "SessionId", "TurnId", "CallerId", "GoalOrQuestion",
      "ScenarioContextSummary", "RouteScope", "QueryConstraints",
      "NodeManifests", "NoSqlAuthority", "ReadOnly", "FallbackAvailable",
      "TraceBudgetChars", "ProgressBudget"
  ];

  Assert.That(propertyNames, Is.EquivalentTo(expected));
  Assert.That(propertyNames, Does.Not.Contain("ContextBudget"));
  Assert.That(propertyNames, Does.Not.Contain("ContextLayers"));
  ```

  Assert one manifest has exactly the V4.7 manifest fields and a known V4.7 node name, `NoSqlAuthority=true`, `ReadOnly=true`, `FallbackAvailable=true`, a bounded positive `TraceBudgetChars`, and a bounded positive `ProgressBudget`. Preserve the existing test assertion that the request does not contain SQL execution, checkpoint mutation, visible-text request, secret, or hidden-context content.

- [ ] **Step 2: Write failing V4.7 health and response gate tests**

  Add harness tests for `Assert-ManualShadowV47HealthResponse` and the V4.7 handshake-response assertion. A valid health object must have exactly the V4.7 health fields and the seven pinned safe values from the design. A valid response must have exactly:

  ```csharp
  string[] responseFields =
  [
      "RequestId", "Accepted", "ReasonCode", "SelectedNodes", "NodeProgress",
      "TraceSummary", "ContextContribution", "FallbackRequired", "NoSqlAuthority",
      "ReadOnly", "RequestedToolNames", "RequestsCheckpointMutation", "RequestsVisibleText"
  ];
  ```

  Add independent rejecting cases for wrong `contractVersion`, non-ready health, a health body with an extra field, a response with an extra V4.0 marker, non-empty `RequestedToolNames`, `RequestsCheckpointMutation=true`, and `RequestsVisibleText=true`. Each script failure must normalize to `manual_shadow_response_rejected` and must not expose the supplied unsafe/body text.

- [ ] **Step 3: Update the actual `-File` loopback server fixture to V4.7 shape**

  Extend the in-test loopback server so `/health` returns the strict V4.7 health attestation and `/handshake` returns a strict V4.7 accepted response. Capture the handshake request body only in the test server, never script output, and assert its property names equal the request list in Step 1. Add a real-script test using the existing C# bridge path and child-only SQLite environment which expects:

  ```csharp
  Assert.That(result.ExitCode, Is.EqualTo(0));
  Assert.That(result.StandardOutput, Does.Contain("artifact_persisted=true"));
  Assert.That(result.StandardOutput, Does.Contain("PASS manual_shadow"));
  Assert.That(aggregate.Accepted, Is.EqualTo(1));
  Assert.That(aggregate.Fallback, Is.EqualTo(0));
  ```

- [ ] **Step 4: Run the focused V4.0 manual-shadow fixture and verify RED**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DataAgentV40RealLangGraphManualShadowIntegrationTests"
  ```

  Expected: FAIL because the script still has `New-V40HandshakeRequest`, sends `ContextBudget`/`ContextLayers`, and has no V4.7 health validator/response validator.

- [ ] **Step 5: Commit the red contract tests only**

  ```powershell
  git add Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
  git commit -m "test: specify V4.7 manual shadow contract"
  ```

### Task 2: Implement the smallest V4.7 protocol migration

**Files:**
- Modify: `tools/run-dataagent-v4-manual-shadow.ps1`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Add strict health-attestation validation**

  Replace the health-only status assumption with `Assert-ManualShadowV47HealthResponse`. It must require a single string `Content` property, parse only one JSON object, reject any non-exact field set, and require:

  ```powershell
  $json.ready -eq $true
  $json.runtimeMode -eq 'langgraph'
  $json.langGraphLoaded -eq $true
  $json.langGraphVersion -eq '0.3.34'
  $json.graphCompiled -eq $true
  $json.contractVersion -eq 'v4.7'
  $json.graphVersion -eq 'dataagent-advisory-v1'
  ```

  On any failure throw only `manual_shadow_response_rejected`; never print the health body.

- [ ] **Step 2: Replace the request builder with the exact V4.7 envelope**

  Replace `New-V40HandshakeRequest` with `New-V47HandshakeRequest` returning only the fourteen fields from Task 1. Use the fixed advisory-only values below; they are bounded values already accepted by `tools/run-dataagent-langgraph-manual-smoke.ps1` and `tools/dataagent-langgraph-sidecar/contracts.py`:

  ```powershell
  RequestId = 'v4-manual-shadow-operator-run'
  SessionId = 'v4-manual-shadow'
  TurnId = 'manual-shadow-1'
  CallerId = 'operator'
  GoalOrQuestion = 'Summarize replay evidence for operator review.'
  ScenarioContextSummary = 'scenario_context=manual_shadow;source_baseline=v3.28'
  RouteScope = 'route_present=true;route_allows_query=true'
  QueryConstraints = 'default_result_changed=false;execute_sql=false'
  NoSqlAuthority = $true
  ReadOnly = $true
  FallbackAvailable = $true
  TraceBudgetChars = 1200
  ProgressBudget = 8
  ```

  Use one `diagnostics_router` manifest with exactly the V4.7 manifest fields, advisory-only purpose/safety notes, non-executable allowed/denied capability markers, and bounded strings/lists. Delete `ContextBudget` and `ContextLayers`; do not substitute any new context payload.

- [ ] **Step 3: Replace response validation with exact V4.7 validation**

  Rename `Assert-ManualShadowHandshakeResponse` to `Assert-ManualShadowV47HandshakeResponse`. Validate exact response property names, types, bounded string/list values, and the required authority values:

  ```powershell
  Accepted = $true
  FallbackRequired = $false
  NoSqlAuthority = $true
  ReadOnly = $true
  RequestedToolNames = @()
  RequestsCheckpointMutation = $false
  RequestsVisibleText = $false
  ```

  The script may inspect response values for validation only. It must neither pass them to `Invoke-ManualShadowArtifactBridge` nor write them into the optional JSON artifact.

- [ ] **Step 4: Wire the new V4.7 validators into the existing terminal flow**

  In the outer `try`, construct `New-V47HandshakeRequest`, save health and handshake HTTP statuses, validate health before POSTing `/handshake`, and validate the V4.7 response before optional legacy JSON artifact output and accepted bridge persistence. Leave these operations unchanged: loopback URI guard, timeout calculation, bridge scalar arguments, bridge timeout/cleanup handling, safe fallback mapping, JSON artifact filename, and terminal output/exit codes.

- [ ] **Step 5: Run the focused fixture and verify GREEN**

  Re-run the Task 1 Step 4 command.

  Expected: all V4.7 request, V4.7 health/response rejection, real `-File` SQLite accepted/fallback, bridge-failure/hang/cleanup, and legacy JSON-artifact tests pass.

- [ ] **Step 6: Commit the protocol migration**

  ```powershell
  git add tools/run-dataagent-v4-manual-shadow.ps1 Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
  git commit -m "feat: migrate manual shadow handshake to V4.7"
  ```

### Task 3: Verify real V4.7 staging behavior and full regressions

**Files:**
- Modify only if verification finds a concrete defect: files from Tasks 1–2.
- Test: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
- Test: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj`

- [ ] **Step 1: Build bridge and run the full affected fixtures**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" build tools\dataagent-shadow-artifact\Alife.Tools.DataAgentShadowArtifact.csproj --no-restore -v:minimal
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DataAgentV40RealLangGraphManualShadowIntegrationTests|FullyQualifiedName~DataAgentLangGraphShadowArtifactStoreTests"
  ```

  Expected: the bridge build succeeds, V4.7 accepted/fallback real-script paths write only sanitized C# SQLite metadata, and all prior terminal-contract cases remain green.

- [ ] **Step 2: Run the real operator-owned V4.7 staging acceptance**

  From a clean merged checkout or this isolated worktree only, use a C#-initialized ignored staging SQLite database and set the two DataAgent store environment variables for the child script process. Manually start only:

  ```powershell
  python tools/dataagent-langgraph-sidecar/server.py --host 127.0.0.1 --port 8765 --runtime-mode langgraph
  ```

  Verify its V4.7 health with:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/run-dataagent-langgraph-manual-smoke.ps1 -Endpoint http://127.0.0.1:8765 -ExpectedContractVersion v4.7
  ```

  Then run the migrated script. It must produce `artifact_persisted=true` and `PASS manual_shadow`. Run one strict V4.7 invalid-response/rejection drill; it must produce `artifact_persisted=true` and `FALLBACK manual_shadow ...`. Read only the C# aggregate/owner diagnostic values; do not log or display raw request/response bodies. Stop only the sidecar process started for this acceptance run.

- [ ] **Step 3: Run complete DataAgent and QChat suites**

  Run:

  ```powershell
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
  & "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
  ```

  Expected: zero failures. Existing QChat unused fake-event `CS0067` warnings remain warnings only.

- [ ] **Step 4: Review authority boundaries and diff quality**

  Inspect the final source and tests to confirm the script has no SQL/SQLite writes, no sidecar start/supervision, no tool/QQ/checkpoint authority, and no raw body forwarding. Run:

  ```powershell
  git diff alife-byastralfox/master...HEAD --check
  git status --short --branch
  ```

  Expected: no whitespace errors, no generated outputs/runtime databases/logs, no credentials, and no unrelated changes.

- [ ] **Step 5: Commit only a concrete verification correction, then request final delivery review**

  If verification exposed a concrete defect, fix it with a red/green test and commit it separately. Then request final review that inspects both the exact V4.7 request/response fields and a real script-to-SQLite accepted/fallback route before claiming completion.
