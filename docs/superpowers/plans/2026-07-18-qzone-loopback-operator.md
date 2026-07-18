# QZone Per-Character Loopback Operator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `Test-QZoneRealRuntime.ps1 -Execute` forward a safe operation to the matching already-running character instance through a loopback-only QZone Operator endpoint.

**Architecture:** Each `Alife.Client` process starts a small loopback listener bound only to its configured operator URL and dispatches safe operation requests to its own `QZoneService`. The PowerShell tool remains inert without `-Execute`, validates the selected loopback endpoint, and never handles QZone credentials.

**Tech Stack:** .NET 9/C#, existing Alife.Client host and QZoneService, `HttpListener` or project-native loopback listener, PowerShell, NUnit.

---

### Task 1: Operator protocol and loopback validation

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneLoopbackOperator.cs`
- Create: `Tests/Alife.Test.QChat/QZoneLoopbackOperatorTests.cs`

- [ ] Write failing tests for a request model accepting only `Read`, `Post`, `Comment`, `Like`, `Image`, and `Delete`; reject unknown operations and non-loopback endpoint URLs with safe codes.
- [ ] Run the focused test and observe the missing operator types fail.
- [ ] Implement `QZoneLoopbackOperatorRequest`, `QZoneLoopbackOperatorResult`, and `QZoneLoopbackOperatorEndpoint.TryCreate` so only `127.0.0.1`, `localhost`, or `::1` absolute HTTP URLs are accepted, with no credentials in result serialization.
- [ ] Re-run focused tests and commit `feat(qzone): define loopback operator protocol`.

### Task 2: Character-local dispatch and lifecycle host

**Files:**

- Modify: the discovered `Alife.Client` composition/lifecycle host
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs` only to expose safe operator dispatch helpers if the host cannot invoke existing public methods directly
- Create: `Tests/Alife.Test.QChat/QZoneLoopbackOperatorHostTests.cs`

- [ ] Write failing tests proving an operator hosted for one instance dispatches only to its injected QZoneService and stops with the instance; fake service results must never contain Cookie/BKN/token/raw exception text.
- [ ] Run RED.
- [ ] Implement a per-instance loopback listener using the configured operator URL, a single JSON request body, and compact safe JSON results. It must not start a second process, bind an external interface, construct QZone HTTP, or affect DataAgent/LangGraph.
- [ ] Re-run GREEN and commit `feat(qzone): host per-character qzone operator`.

### Task 3: Script forwarding and two-account operator configuration

**Files:**

- Modify: `tools/local-production/Test-QZoneRealRuntime.ps1`
- Modify: `tools/local-production/Test-QZoneRealRuntimeScript.ps1`
- Modify: `tools/local-production/Start-AlifeLocalSupervisor.ps1` and its configuration model only if required to pass each role's loopback operator URL
- Modify: `docs/qzone-boundary.md`

- [ ] Write failing script tests with a local fake operator listener: `-Execute` forwards only operation/role-safe fields to the selected loopback URL, preserves inert default mode, rejects non-loopback URLs, and never prints environment values.
- [ ] Run RED.
- [ ] Implement selected-port-to-operator mapping and forwarding. `Delete` must send only full own-post metadata; image must send a local path only after local file existence validation. A missing operator returns `local_qzone_runtime_unavailable`.
- [ ] Re-run script tests and commit `feat(qzone): forward operator script to role runtime`.

### Task 4: Verification and deployment handoff

**Files:**

- Modify: `docs/qzone-boundary.md`
- Modify: `tools/local-production/Test-QZoneRealRuntime.ps1` only if verification finds a safe result-mapping defect

- [ ] Build QChat test project and run all QZone-focused tests.
- [ ] Run the script in inert mode for both 3001 and 3002 and assert no network/process/token access.
- [ ] With the user's real-account authorization, start the existing two role instances and run the documented once-per-account matrix: Read, unique Post, Comment-or-Like, Image, Delete own test post.
- [ ] Do not enable `EnableQZoneAutonomyLivePosting` until both matrices and cleanup succeed.
- [ ] Commit any documentation-only correction as `docs(qzone): finalize loopback operator validation`.

## Self-review

- The endpoint is per-character and loopback-only; no third service, external binding, browser automation, credentials, DataAgent/LangGraph route, whitelist, or automatic remote image retrieval is added.
- Script default mode remains inert; `-Execute` does not claim success without an explicit safe operator success result.
