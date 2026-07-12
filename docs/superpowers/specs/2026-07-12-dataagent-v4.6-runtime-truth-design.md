# DataAgent V4.6 Runtime Truth and Contract Coherence Design

## Objective

V4.6 turns the current manual LangGraph skeleton into a truthful, reproducible, resource-bounded production-canary candidate. It removes every condition that can report a successful LangGraph advisory when LangGraph did not run, aligns `FallbackRequired` with V4.5 acceptance semantics, and preserves invalid-response classification through the complete HTTP/decorator/coordinator path.

V4.6 does not run the production canary. V4.7 will use the hardened runtime to execute the 20-request observation window, seven live drills, and persistent closure artifact.

## Current blockers addressed

1. The sidecar returns `FallbackRequired=true` for every accepted response, making the V4.5 fallback ratio 100% and closure impossible.
2. Missing `langgraph` silently falls back to the same accepted response, while `/health` still reports `ok=true`.
3. The graph is a dynamically compiled `StateGraph(dict)` built once per request.
4. Python dependencies and supported Python versions are not declared or pinned.
5. Python request bodies and C# response bodies are not bounded before JSON parsing.
6. `DataAgentGraphSidecarInvalidResponseException` is collapsed into `production_shadow_unavailable` by the V4.4 decorator.
7. The manual smoke script executes only the valid request but labels unit-test coverage as live passes.

## Selected architecture

### Runtime modes

The sidecar has two explicit modes:

- `langgraph`: production-canary mode. Startup fails if LangGraph cannot be imported or the graph cannot compile.
- `deterministic-stub`: developer-only mode. It is never accepted by V4.6 production readiness and is visibly reported by `/health`.

There is no automatic mode fallback. `langgraph` remains the default mode.

### Runtime attestation

`GET /health` returns a versioned, fixed schema:

```json
{
  "ok": true,
  "ready": true,
  "runtimeMode": "langgraph",
  "langGraphLoaded": true,
  "langGraphVersion": "0.3.34",
  "graphCompiled": true,
  "contractVersion": "v4.6",
  "graphVersion": "dataagent-advisory-v1"
}
```

Production smoke rejects `ready=false`, `runtimeMode!=langgraph`, a missing version, or an unexpected contract/graph version.

### Graph construction

The graph uses a typed state containing only the bounded handshake request, selected advisory node, and response. It is constructed and compiled once during server startup. Request handlers invoke the compiled graph; they never compile per request.

The first V4.6 graph remains advisory-only and deterministic. It validates the manifest inventory, selects one allowed advisory node, and returns a structured advisory. It does not call a model or tool. A later value milestone may add an LLM node without granting tools.

### Fallback semantics

An accepted response produced by the compiled LangGraph path uses:

```text
Accepted=true
FallbackRequired=false
NoSqlAuthority=true
ReadOnly=true
RequestsCheckpointMutation=false
RequestsVisibleText=false
RequestedToolNames=[]
```

Runtime unavailable, invalid input, graph failure, invalid output, timeout, circuit open, busy, or C# validation rejection requires fallback. HTTP errors never fabricate an accepted handshake response.

### Strict Python boundary

The sidecar accepts only `POST /handshake` with `application/json`, a declared body length from 1 through 65536 bytes, a JSON object root, and required bounded fields. It returns:

- `400` for malformed JSON or invalid schema;
- `413` for oversized input;
- `415` for unsupported content type;
- `503` when runtime readiness is false;
- `500` with a fixed safe error body for an unexpected graph failure.

No exception text, stack trace, request body, endpoint, SQL, token, hidden context, caller/session identifier, or absolute path is returned or logged.

### Reproducible dependencies

The sidecar declares Python `>=3.11,<3.14` and pins `langgraph==0.3.34`. A checked-in lock file records the full resolved dependency graph. Alife and its tests do not install these dependencies; the operator prepares the runtime explicitly.

### Bounded C# response parsing

`DataAgentGraphHandshakeHttpClient` reads at most 65536 response bytes before deserialization. Oversized content throws `DataAgentGraphSidecarInvalidResponseException("response_body_too_large")`. Invalid JSON or schema throws `invalid_response_schema`. Timeouts remain `sidecar_timeout`.

### Invalid-response preservation

V4.4 maps `DataAgentGraphSidecarInvalidResponseException` to a safe `DataAgentV44ProductionShadowException` with reason `production_shadow_invalid_response`, `NetworkAttempted=true`, and circuit-failure accounting. The coordinator maps it to `DataAgentGraphHandshakeStatus.Invalid`, and V4.5 observes it as rejected rather than unavailable.

### Honest smoke evidence

The manual smoke performs only checks it actually executes and labels each check precisely:

1. health attestation;
2. valid LangGraph advisory with `FallbackRequired=false`;
3. malformed JSON returns 400;
4. oversized body returns 413;
5. unsupported content type returns 415.

Timeout, unsafe authority, saturation, circuit, and kill-switch checks remain unit/integration tests until the V4.7 live-drill harness executes them. The script must not print a live `PASS` for a check it did not run.

## Files and responsibilities

- `tools/dataagent-langgraph-sidecar/contracts.py`: strict request/response and health schema validation.
- `tools/dataagent-langgraph-sidecar/graph.py`: typed state, graph construction, one-time compile, advisory response.
- `tools/dataagent-langgraph-sidecar/server.py`: bounded HTTP handling and explicit runtime mode.
- `tools/dataagent-langgraph-sidecar/pyproject.toml`: Python/runtime dependency declaration.
- `tools/dataagent-langgraph-sidecar/requirements.lock`: fully pinned deployment dependencies.
- `tools/dataagent-langgraph-sidecar/tests/`: Python contract, mode, graph, health, and HTTP tests.
- `DataAgentGraphHandshakeHttpClient.cs`: bounded response parsing.
- `DataAgentV44ProductionShadowClient.cs`: invalid-response preservation and circuit accounting.
- `DataAgentGraphHandshakeCoordinator.cs`: V4.6 invalid-response outcome mapping.
- `run-dataagent-langgraph-manual-smoke.ps1`: truthful live smoke.
- `dataagent-v4.6-runtime-truth.md`: operator boundary and transition criteria.

## Verification and exit gate

V4.6 is complete only when:

- Python tests prove explicit runtime modes, strict startup, one-time compile, health attestation, request limits, safe errors, and accepted `FallbackRequired=false` semantics;
- C# tests prove bounded responses and end-to-end invalid-response preservation;
- the manual smoke never reports an unexecuted check as live pass;
- default tests start no runtime, install no dependency, bind no port, and access no external network;
- V4.6 dynamic/static readiness passes while V3 remains frozen at 111/95;
- full DataAgent and solution regressions pass;
- no production shadow flag is enabled by default;
- V4.7 may start only after an operator-run `/health` and valid-advisory smoke artifact prove the hardened sidecar is actually running in `langgraph` mode.

## Deferred to V4.7 and V5+

V4.7 owns the real 20-request production-canary window, seven live drills, configuration fingerprint, runtime instance identity, safe aggregate persistence, and closure artifact.

V5+ owns any proposal to let LangGraph execute tools, SQL, routing, checkpoint mutation, state writes, or visible text. V4.6 grants none of those permissions.
