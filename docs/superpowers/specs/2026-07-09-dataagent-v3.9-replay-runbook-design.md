# DataAgent V3.9 Replay Runbook Design

## Goal

DataAgent V3.9 turns the V3.8 end-to-end chain contract into an offline replay runbook. A developer or reviewer should be able to run one command, replay a fixture through the real DataAgent route/policy/analysis/diagnostics chain, and receive a stable Markdown or JSON report that shows what happened and which expected markers passed.

The default command is:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1
```

The command must run without Python, sidecars, QQ, NapCat, Postgres, browser automation, model calls, or network access.

## Context

V3.8 proves the DataAgent owner/private chain contract with deterministic tests. The important chain is:

1. Tool Broker route state is produced for an owner/private/trusted turn.
2. `ToolCapabilityRouter` allows the DataAgent analysis start tool.
3. `XmlFunctionExecutionPolicy` gates XML execution for the routed tool.
4. `XmlPolicyDataAgentToolRouteContextAccessor` transfers policy route state into `DataAgentAnalysisToolHandler`.
5. `DataAgentAnalysisOrchestrator` executes route gate, schema/plan/validate/execute/explain/checkpoint nodes.
6. Diagnostics are published for evidence, trace, progress, and graph state.
7. `QChatDiagnosticsService` exposes owner-readable diagnostics text.

V3.9 does not add new runtime authority. It packages that chain into an explicit replay harness and runbook output so the chain is easier to demonstrate, debug, and audit.

## Recommended Architecture

Use a PowerShell wrapper plus a small .NET replay harness.

The PowerShell script owns command-line ergonomics and repo-relative defaults. The .NET harness owns fixture parsing, real DataAgent chain execution, expected marker validation, and report formatting.

This avoids a weak PowerShell-only replay that merely reads fixture text, while still giving users a simple `tools/` entry point.

## Files

Create:

- `tools/replay-dataagent-chain.ps1`
  - User-facing runbook entry point.
  - Locates the repo root.
  - Defaults `-Fixture` to the V3.9 owner readiness analysis fixture.
  - Supports `-Format markdown` and `-Format json`.
  - Invokes the user-local .NET SDK when available at `C:\Users\hu shu\.dotnet\dotnet.exe`, otherwise falls back to `dotnet`.

- `tools/dataagent-replay/Alife.Tools.DataAgentReplay.csproj`
  - Small console project.
  - References `Alife.Function.DataAgent`, `Alife.Function.FunctionCaller`, and `Alife.Function.QChat`.

- `tools/dataagent-replay/Program.cs`
  - Parses `--fixture` and `--format`.
  - Runs the replay.
  - Writes Markdown or JSON to stdout.
  - Exits non-zero when required expected markers are missing or fixture parsing fails.

- `tools/dataagent-replay/DataAgentReplayModels.cs`
  - Defines fixture and result models.
  - Keeps JSON schema stable and explicit.

- `tools/dataagent-replay/DataAgentReplayRunner.cs`
  - Executes the real offline chain.
  - Uses fake store/planner/clock collaborators.
  - Uses real `ToolCapabilityRouter`, `XmlFunctionExecutionPolicy`, `XmlPolicyDataAgentToolRouteContextAccessor`, `DataAgentAnalysisToolHandler`, `DataAgentAnalysisOrchestrator`, `DataAgentService`, and `QChatDiagnosticsService`.

- `tools/dataagent-replay/DataAgentReplayReportFormatter.cs`
  - Formats Markdown.
  - Emits sections for route, XML policy, route context, orchestration, diagnostics, expected marker results, and offline boundary.

- `Tests/Alife.Test.DataAgent/Fixtures/DataAgentReplay/v3.9-owner-readiness-analysis.json`
  - Default replay fixture.
  - Defines owner/private/trusted route state, utterance, fixed planner plan, expected markers, and offline boundary expectations.

- `Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs`
  - NUnit contract tests for the script, fixture, harness, Markdown output, JSON output, marker validation, and offline boundary.

Modify:

- `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
  - Include the fixture as test content if the current project settings do not already copy JSON fixture files.

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add dynamic marker `DataAgentReplayRunbookPresent`.

- `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Increase dynamic readiness count by one.
  - Assert `DataAgentReplayRunbookPresent` details.
  - Increase static readiness summary and expected script count by one after the static script is updated.

- `tools/check-dataagent-readiness.ps1`
  - Add static marker `DataAgentReplayRunbookPresent`.
  - Increase `$expectedRequired` from `92` to `93`.

- Version guard readiness tests that assert `$expectedRequired = 92`
  - Update them to `93` in the same way V3.8 aligned older version guards.

## CLI Behavior

Default run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1
```

Equivalent explicit run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1 `
    -Fixture Tests\Alife.Test.DataAgent\Fixtures\DataAgentReplay\v3.9-owner-readiness-analysis.json `
    -Format markdown
```

JSON run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1 `
    -Fixture Tests\Alife.Test.DataAgent\Fixtures\DataAgentReplay\v3.9-owner-readiness-analysis.json `
    -Format json
```

Invalid format must fail fast with a clear message and a non-zero exit code.

Missing fixture must fail fast with a clear message and a non-zero exit code.

Missing expected markers must produce a report and exit non-zero. This makes the runbook usable both by humans and by tests.

## Fixture Shape

The first fixture should stay intentionally narrow. It represents one owner/private DataAgent readiness analysis replay.

```json
{
  "version": "v3.9",
  "name": "owner-readiness-analysis",
  "callerId": "owner",
  "utterance": "DataAgent analyze project readiness",
  "routeState": {
    "isOwner": true,
    "isPrivate": true,
    "trustedRuntime": true,
    "activeDataAgentSessionId": "",
    "activeDataAgentSessionStatus": ""
  },
  "planner": {
    "dataset": "document_index",
    "intent": "find_dataagent_documents",
    "select": ["path", "title"],
    "filters": [
      {
        "field": "tags",
        "operator": "contains",
        "value": "dataagent"
      }
    ],
    "limit": 20
  },
  "expectedMarkers": [
    "route_allowed",
    "dataagent_analysis_start",
    "RouteGate:Succeeded",
    "Execute:Succeeded",
    "Checkpoint:Succeeded",
    "sql_status=validated",
    "DataAgent evidence diagnostics",
    "DataAgent trace diagnostics",
    "DataAgent progress diagnostics",
    "graph_sidecar",
    "sidecar_authority=false",
    "default_tests_live_runtime=false"
  ]
}
```

Future versions may add multi-turn or denied-route fixtures, but V3.9 should not broaden beyond one accepted owner/private analysis replay.

## Replay Execution

The replay runner must use the real production classes for the chain boundaries:

- `ToolCapabilityRouter`
- `XmlFunctionExecutionPolicy`
- `XmlPolicyDataAgentToolRouteContextAccessor`
- `DataAgentAnalysisToolHandler`
- `DataAgentAnalysisOrchestrator`
- `DataAgentAnalysisService`
- `DataAgentService`
- `QChatDiagnosticsService`

The replay runner must use deterministic offline collaborators:

- Recording in-memory `IDataAgentStore`
- Fixed `IDataAgentQueryPlanner`
- Fixed `DateTimeOffset` clock
- `InMemoryDataAgentAnalysisSessionStore`
- `DataAgentProgressRecorder`
- `DataAgentTraceRecorder`
- `DataAgentGraphHandshakeCoordinator` with disabled options and disabled sidecar client

The runner should perform the same accepted-path shape V3.8 tightened:

1. Build `ToolRouteState` from fixture route state.
2. Route the utterance with `ToolCapabilityRouter`.
3. Configure `XmlFunctionExecutionPolicy` with governed tool names and current route.
4. Consume `dataagent_analysis_start` through XML policy.
5. Execute `DataAgentAnalysisToolHandler.Start`.
6. Feed the returned context through real `XmlFunctionCaller.UpdateDataAgentAnalysisRouteSessionFromContext`.
7. Build QChat diagnostics runtime state from captured evidence, trace, progress, and graph diagnostics.
8. Invoke QChat diagnostics for evidence, trace, progress, and graph.
9. Validate all fixture expected markers against the combined report text.

## Markdown Report

The Markdown report should be stable and compact.

Required sections:

```text
# DataAgent Replay: owner-readiness-analysis
## Fixture
## Route Decision
## XML Policy
## Route Context
## Orchestration
## Session
## Diagnostics
## Expected Markers
## Offline Boundary
```

The report must include:

- Fixture name and version
- Route domain, intent, reason code, and allowed tools
- XML policy allowed/denied status and reason
- Route context presence, tool name, query permission, reason code, and route session id
- Orchestration trace
- Session id and status
- Evidence, trace, progress, and graph diagnostics summaries
- QChat diagnostics handled status for evidence, trace, progress, and graph
- Expected marker results as `PASS` or `MISSING`
- Offline markers `sidecar_authority=false` and `default_tests_live_runtime=false`

The report must not include raw SQL text beyond existing redacted diagnostics.

## JSON Report

JSON output must be machine-readable and stable.

Top-level shape:

```json
{
  "fixture": {},
  "route": {},
  "xmlPolicy": {},
  "routeContext": {},
  "orchestration": {},
  "session": {},
  "diagnostics": {},
  "expectedMarkers": [],
  "offlineBoundary": {},
  "passed": true
}
```

The JSON report should not include hidden context blocks or raw SQL. If a value is sensitive or verbose, represent it as a status, summary, or redacted field.

## Error Handling

Fixture parse errors:

- Print a clear error to stderr.
- Exit non-zero.

Missing fixture:

- Print the resolved path.
- Exit non-zero.

Unsupported format:

- Print accepted formats: `markdown`, `json`.
- Exit non-zero.

Missing expected markers:

- Print the report with `MISSING` marker rows.
- Exit non-zero.

Unexpected replay exception:

- Print the exception type and message.
- Do not dump hidden DataAgent context.
- Exit non-zero.

## Readiness

Add dynamic readiness marker:

```text
DataAgentReplayRunbookPresent
```

Dynamic pass detail:

```text
cli=true;fixture=true;real_chain=true;markdown=true;json=true;expected_markers=true;sidecar_authority=false;default_tests_live_runtime=false
```

The dynamic check should verify:

- PowerShell wrapper exists.
- .NET harness project and runner exist.
- Default fixture exists.
- Tests exist.
- Runner source references real chain classes.
- Runner source references disabled sidecar options/client.
- Formatter supports Markdown and JSON.
- Script defaults to the fixture.
- Script uses the local .NET SDK fallback pattern.

Add static readiness marker with the same name and update script expected required count from `92` to `93`.

## Tests

Required tests:

- Default script runs and emits Markdown report.
- Explicit fixture script run emits Markdown report.
- `-Format json` emits parseable JSON.
- JSON contains stable top-level fields.
- Missing expected marker exits non-zero.
- Missing fixture exits non-zero.
- Unsupported format exits non-zero.
- Replay runner uses real route/policy/accessor chain, not a fixed route accessor.
- Replay runner keeps graph sidecar disabled.
- Markdown report includes all required sections.
- Markdown and JSON reports agree on `passed` and expected marker statuses.
- Readiness dynamic marker passes.
- Static readiness script contains V3.9 runbook markers and expected count `93`.
- Full DataAgent tests remain offline by default.

The default focused verification command should be:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReplayRunbookTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

The final verification command should remain:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

## Out Of Scope

V3.9 does not:

- Add a QChat owner command.
- Start Python, FastAPI, uvicorn, or any graph sidecar runtime.
- Connect to QQ, NapCat, Postgres, or browser automation.
- Call an LLM or embedding model.
- Add a web UI.
- Modify `D:\FOXD` or upload workflows.
- Replace V3.8 chain contract tests.

## Success Criteria

V3.9 is complete when:

- `tools\replay-dataagent-chain.ps1` runs with no arguments and emits a passing Markdown runbook.
- The same script emits parseable JSON with `-Format json`.
- The runner executes the real route/policy/accessor/handler/orchestrator/diagnostics chain with deterministic offline collaborators.
- Fixture expected markers produce PASS/MISSING results and missing markers make the command fail.
- Readiness includes `DataAgentReplayRunbookPresent`.
- Static readiness reports `93 required passed, 0 required missing`.
- Full `Alife.Test.DataAgent` passes with live tests skipped by default.
