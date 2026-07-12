# DataAgent V4.6 Runtime Truth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Keep execution sequential because Tasks 2-6 depend on contracts introduced earlier. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the manual LangGraph sidecar skeleton into a truthful, reproducible, resource-bounded production-canary candidate without granting it SQL, tool, mutation, checkpoint, or visible-text authority.

**Architecture:** Python owns a strict loopback HTTP boundary and one startup-compiled typed LangGraph advisory graph. The default `langgraph` runtime fails closed when its dependency or graph is unavailable; the explicit developer stub is visible and never production-ready. C# remains the final authority, bounds the response before deserialization, and preserves invalid-response classification through the V4.4 decorator, coordinator, and V4.5 observer.

**Tech Stack:** Python 3.11-3.13, `langgraph==0.3.34`, Python `unittest`, .NET 9, C#/NUnit, PowerShell readiness and manual smoke scripts.

**Scope boundary:** V4.6 hardens the runtime and contract only. It does not enable production shadow by default and does not claim a completed production canary. V4.7 owns the real 20-request observation window, seven live drills, runtime/config identity, safe aggregate persistence, and closure artifact.

---

### Task 1: Pin the Python runtime and make runtime mode truthful

**Files:**
- Create: `tools/dataagent-langgraph-sidecar/pyproject.toml`
- Create: `tools/dataagent-langgraph-sidecar/requirements.lock`
- Create: `tools/dataagent-langgraph-sidecar/runtime.py`
- Create: `tools/dataagent-langgraph-sidecar/tests/__init__.py`
- Create: `tools/dataagent-langgraph-sidecar/tests/test_runtime.py`

- [ ] Write failing tests proving the default mode is exactly `langgraph`, a missing or incompatible LangGraph dependency fails startup, graph compilation failure fails startup, and `deterministic-stub` works only when explicitly selected.
- [ ] Write failing attestation tests requiring runtime mode, LangGraph-loaded state/version, graph-compiled state, contract version `v4.6`, and graph version `dataagent-advisory-v1`. The explicit stub must report `ready=false` and must never attest `runtimeMode=langgraph`.
- [ ] Run `python -m unittest tools.dataagent-langgraph-sidecar.tests.test_runtime -v` from the repository root and verify RED because the runtime module does not exist. If dotted discovery is unsuitable because of the directory name, use `python -m unittest discover tools/dataagent-langgraph-sidecar/tests -p 'test_runtime.py' -v`; retain one stable command in the README.
- [ ] Implement an explicit runtime loader with no automatic fallback. Import and version checks must be injectable in tests, must not install packages, bind a port, or access a network, and must expose only fixed safe failure reason codes.
- [ ] Declare Python `>=3.11,<3.14`, pin direct dependency `langgraph==0.3.34`, and check in the fully resolved transitive lock used by operators. Do not modify the user's active Python environment while implementing or testing.
- [ ] Run the runtime tests and verify GREEN.
- [ ] Commit with `feat(dataagent): require truthful langgraph runtime`.

### Task 2: Add strict contracts and compile the typed graph once

**Files:**
- Create: `tools/dataagent-langgraph-sidecar/contracts.py`
- Create: `tools/dataagent-langgraph-sidecar/graph.py`
- Create: `tools/dataagent-langgraph-sidecar/tests/test_contracts.py`
- Create: `tools/dataagent-langgraph-sidecar/tests/test_graph.py`
- Modify: `tools/dataagent-langgraph-sidecar/runtime.py`
- Modify: `tools/dataagent-langgraph-sidecar/server.py`

- [ ] Write failing contract tests for a JSON-object request, required bounded string/list fields, manifest inventory validation, rejection of unknown/unsafe authority, and a fixed health/response schema. Tests must reject SQL authority, mutation, checkpoint mutation, visible text, non-empty requested tool names, extra authority-bearing fields, and free-form unsafe output.
- [ ] Write failing graph tests proving a typed state is used, construction/compilation occurs exactly once per runtime startup, multiple requests reuse the compiled graph, and graph invocation never imports or invokes a model, SQL client, checkpoint store, or tool executor.
- [ ] Require an accepted LangGraph result to have exactly `Accepted=true`, `FallbackRequired=false`, `NoSqlAuthority=true`, `ReadOnly=true`, `RequestsCheckpointMutation=false`, `RequestsVisibleText=false`, and `RequestedToolNames=[]`.
- [ ] Require runtime-unavailable, invalid manifest, graph exception, or invalid graph output to fail closed; no error path may fabricate an accepted handshake.
- [ ] Run the contract/graph tests and verify RED against the current per-request `StateGraph(dict)` implementation.
- [ ] Implement frozen/typed request, graph-state, response, and health structures plus deterministic validation. Build and compile the graph once in runtime initialization and inject the compiled invoker into the server.
- [ ] Run all Python tests and verify GREEN.
- [ ] Commit with `feat(dataagent): compile typed advisory graph once`.

### Task 3: Enforce the Python HTTP resource and error boundary

**Files:**
- Create: `tools/dataagent-langgraph-sidecar/tests/test_server.py`
- Modify: `tools/dataagent-langgraph-sidecar/server.py`
- Modify: `tools/dataagent-langgraph-sidecar/contracts.py`
- Modify: `tools/dataagent-langgraph-sidecar/runtime.py`

- [ ] Write failing handler tests without binding a real port. Require `POST /handshake`, `Content-Type: application/json`, declared body length 1 through 65536 bytes, a JSON-object root, and strict request validation before graph invocation.
- [ ] Prove exact statuses: `400` malformed JSON/schema, `413` oversized body, `415` unsupported content type, `503` runtime not ready, and `500` unexpected graph failure. Require a fixed JSON error envelope with stable codes only.
- [ ] Write failing health tests for the fixed V4.6 attestation. Production-ready health requires `runtimeMode=langgraph`, the pinned LangGraph version, compiled graph, and exact contract/graph versions; explicit stub health remains honest and non-ready.
- [ ] Add tests proving rejected requests never invoke the graph and response/log output contains no exception text, stack trace, request body, endpoint, SQL, token, hidden context, caller/session identifier, or absolute path.
- [ ] Run `python -m unittest discover tools/dataagent-langgraph-sidecar/tests -v` and verify RED.
- [ ] Implement bounded reading before JSON parsing, exact content-type/status handling, safe fixed error serialization, and health attestation. Keep server startup explicit; automated tests must not bind a port.
- [ ] Run all Python sidecar tests and verify GREEN.
- [ ] Commit with `feat(dataagent): bound langgraph sidecar http contract`.

### Task 4: Bound C# response parsing before deserialization

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpClient.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeHttpClientTests.cs`

- [ ] Write failing tests proving a valid response of at most 65536 bytes is accepted and a declared or streamed response above 65536 bytes throws `DataAgentGraphSidecarInvalidResponseException("response_body_too_large")` before JSON deserialization.
- [ ] Add failing tests requiring malformed JSON, non-object JSON, missing/wrong-type fields, unsafe authority fields, and trailing invalid content to throw `DataAgentGraphSidecarInvalidResponseException("invalid_response_schema")`.
- [ ] Preserve existing timeout classification as `sidecar_timeout`; prove cancellation and transport handling remain bounded and deterministic.
- [ ] Prove exception messages and outward results contain no raw response, inner exception text, endpoint, token, SQL, hidden context, caller/session identifier, or local path.
- [ ] Run the filtered HTTP client tests with the user-local .NET 9 SDK and verify RED:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter 'DataAgentGraphHandshakeHttpClientTests' -v:minimal
```

- [ ] Implement an exact 65536-byte cap while streaming the response, then deserialize and validate only the bounded buffer. Do not rely solely on `Content-Length`.
- [ ] Run the focused tests and verify GREEN.
- [ ] Commit with `feat(dataagent): bound graph sidecar responses`.

### Task 5: Preserve invalid-response classification through the real C# path

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV44ProductionShadowClient.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV44ProductionShadowTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV45ProductionClosureTests.cs`

- [ ] Write a failing integration-style unit test using a fake HTTP handler and the real HTTP client, V4.4 decorator, coordinator, and V4.5 recorder. Feed an invalid/oversized response and prove the complete path is exercised.
- [ ] Require the V4.4 decorator to convert `DataAgentGraphSidecarInvalidResponseException` into safe reason `production_shadow_invalid_response`, with `NetworkAttempted=true`, no raw payload, and one circuit-failure increment.
- [ ] Require the coordinator outcome to be `DataAgentGraphHandshakeStatus.Invalid`, preserve the stable reason code, and produce exactly one final observation.
- [ ] Require V4.5 to increment rejected count once, not unavailable/fallback/timeout count, while preserving existing circuit-open/recovery, timeout, busy, kill-switch, and unsafe-authority behavior.
- [ ] Run the focused V4.4/V4.5/coordinator tests and verify RED:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter 'DataAgentV44ProductionShadow|DataAgentGraphHandshakeCoordinator|DataAgentV45ProductionClosure' -v:minimal
```

- [ ] Add the narrow invalid-response catch before broader unavailable/transport catches, map it explicitly in the coordinator, and keep circuit accounting unchanged for all other outcomes.
- [ ] Run the focused tests and verify GREEN.
- [ ] Commit with `fix(dataagent): preserve invalid graph responses`.

### Task 6: Make smoke evidence honest and close V4.6 readiness

**Files:**
- Modify: `tools/run-dataagent-langgraph-manual-smoke.ps1`
- Modify: `tools/dataagent-langgraph-sidecar/README.md`
- Create: `docs/dataagent/dataagent-v4.6-runtime-truth.md`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3ClosureManifest.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs`
- Modify: relevant readiness total tests under `Tests/Alife.Test.DataAgent/`

- [ ] Refactor the manual smoke so it tests an already-running operator-provided loopback sidecar only. It must execute and report exactly five checks: health attestation, valid advisory with `FallbackRequired=false`, malformed JSON `400`, oversized request `413`, and unsupported content type `415`.
- [ ] Remove messages that label unit-test-only timeout, unsafe authority, saturation, circuit, or kill-switch coverage as live `PASS`. A skipped or unexecuted check must never be printed as passed.
- [ ] Add script-level/static tests or readiness assertions proving all five live requests are present, health rejects stub/non-ready/wrong-version attestation, failures return nonzero, and the script does not start/restart Python, install dependencies, touch QQ/NapCat, or contact a non-loopback endpoint.
- [ ] Document explicit environment preparation, pinned dependency install outside automated tests, startup modes, health schema, smoke invocation, shutdown ownership, safe failure codes, byte limits, and the V4.7 transition gate.
- [ ] Add dynamic/static `GraphHandshakeV46RuntimeTruthPresent`; advance expected totals from 102/118 to 103/119 and include V4.6 only in the post-V3 set. Preserve frozen V3 totals at 111/95 exactly.
- [ ] Run V4.6/readiness tests and `tools/check-dataagent-readiness.ps1`; verify exactly 103 dynamic and 119 static checks pass.
- [ ] Commit with `docs(dataagent): close v4.6 runtime truth readiness`.

### Completion audit and verification

- [ ] Confirm every design requirement maps to a test and implementation step: strict runtime mode, health truth, one-time typed graph, fallback semantics, dependency pinning, Python request cap, C# response cap, safe errors, invalid-response preservation, truthful smoke, readiness totals, and V3 freeze.
- [ ] Confirm default configuration still disables production shadow, automated tests start no runtime, install no dependency, bind no port, access no network, and modify no QQ/NapCat/account state.
- [ ] Run the complete verification sequence:

```powershell
python -m unittest discover tools/dataagent-langgraph-sidecar/tests -v
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter 'DataAgentGraphHandshakeHttpClient|DataAgentV44ProductionShadow|DataAgentGraphHandshakeCoordinator|DataAgentV45ProductionClosure|DataAgentV46' -v:minimal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/check-dataagent-readiness.ps1
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore -v:minimal
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
git diff --check
git status --short --branch
```

- [ ] Record exact Python, focused .NET, DataAgent, solution, dynamic readiness, and static readiness counts. Do not infer success from an earlier run.
- [ ] Commit only audit/document corrections required by evidence. Do not start the sidecar, perform V4.7 live drills, enable production shadow, push, merge, or create a PR.

V4.6 is complete only when the hardened sidecar can truthfully attest a real startup-compiled LangGraph runtime and every automated verification above passes. Production closure remains incomplete until V4.7 collects the real canary and drill artifact.
