# DataAgent V1.7 Capability Boundary Design

## Goal

DataAgent V1.7 turns the existing DataAgent module into a clearer capability boundary without changing its storage model or runtime safety policy. The work should make DataAgent look and behave like an internally pluggable module: each DataAgent capability declares its tools, risk shape, state effects, registration behavior, and readiness evidence through one stable boundary.

The version must preserve the V1.6 Tool Broker model:

```text
QChat route state
 -> ToolCapabilityRouter
 -> per-turn tool route context
 -> XmlFunctionExecutionPolicy fail-closed gate
 -> DataAgent query or analysis handler
 -> audited context output
```

V1.7 should not add PostgreSQL, LangGraph, a plugin marketplace, or new business analytics domains. Its job is to standardize the boundary around capabilities already present in V1.6.

## Current Baseline

DataAgent already has a usable V1.x NL2SQL and analysis chain:

- `DataAgentToolHandler` exposes `dataagent_query`.
- `DataAgentAnalysisToolHandler` exposes `dataagent_analysis_start`, `dataagent_analysis_continue`, `dataagent_analysis_summarize`, and `dataagent_analysis_end`.
- `DataAgentModuleService` registers handlers without static prompt tool leakage and publishes Tool Broker contract text.
- `ToolCapabilityManifest` describes governed XML tools for the FunctionCaller layer.
- `ToolCapabilityRouter` decides which DataAgent tools are available for a given turn.
- `XmlFunctionExecutionPolicy` rejects governed tools unless the current route permits them.
- `DataAgentAnalysisService` owns the analysis session state machine.
- `DataAgentToolBrokerAuditLog` persists route and execution evidence in the V1.x SQLite store.
- `tools/check-dataagent-readiness.ps1` and `tools/check-qchat-engineering-map.ps1` already treat V1.6 Tool Broker observability as required.

The weakness is that DataAgent capabilities are still registered as local handler wiring rather than as an explicit DataAgent capability boundary. This makes the module harder to extend safely and makes future V2/V3 work depend on local conventions instead of a declared contract.

## Non-Goals

V1.7 does not introduce `IDataAgentStore`, PostgreSQL, LangGraph, Python sidecars, multi-agent orchestration, a UI, a plugin marketplace, external data ingestion, chart rendering, report publishing, or new SQL features.

V1.7 does not relax owner/private gating, trusted-runtime gating, analysis-session matching, SQL validation, or prompt-leak prevention.

V1.7 does not merge DataAgent state with the QChat bot state machine. QChat may create route state and receive DataAgent context, but DataAgent owns analysis capabilities and analysis-session state.

## Recommended Scope

Use the narrow option selected for speed and quality:

```text
V1.7 = DataAgent capability/plugin boundary only
V2   = store contract and PostgreSQL migration
V2.5 = LangGraph sidecar pilot
V3   = supervisor-controlled tool governance
```

This keeps V1.7 small enough to finish quickly while still improving the architecture that V2 and V3 will depend on.

## Architecture

Add a DataAgent-local capability boundary:

```text
DataAgentCapabilityProvider
  -> declares metadata and XML tool handlers
  -> registers with DataAgentModuleService through a narrow registrar
  -> contributes ToolCapabilityManifest records
  -> contributes readiness evidence

DataAgentModuleService
  -> creates built-in providers
  -> registers providers during AwakeAsync
  -> exposes provider names for diagnostics/tests
  -> keeps prompt text limited to Tool Broker contract

ToolCapabilityRouter
  -> consumes DataAgent tool manifests from the capability boundary
  -> preserves existing V1.6 routing decisions and reason codes

XmlFunctionCaller
  -> still injects only routed XML tool documents per turn
  -> does not receive static DataAgent XML manuals in the DataAgent prompt
```

The boundary should remain internal to the .NET module. It is not a Codex plugin, not a NuGet plugin, and not a separate process.

## Components

### IDataAgentCapabilityProvider

The provider interface should be minimal and testable:

```csharp
public interface IDataAgentCapabilityProvider
{
    string Name { get; }
    IReadOnlyList<ToolCapabilityManifest> ToolManifests { get; }
    IReadOnlyList<string> ReadinessMarkers { get; }
    void Register(IDataAgentCapabilityRegistrar registrar);
}
```

Providers declare what they own and register through a small registrar instead of reaching into unrelated module internals.

### IDataAgentCapabilityRegistrar

The registrar gives providers only the operations they need:

```csharp
public interface IDataAgentCapabilityRegistrar
{
    void RegisterXmlHandlerWithoutStaticDocument(object handler, params string[] plainAreas);
    void PublishAnalysisContext(string context);
    void UpdateAnalysisRouteSessionFromContext(string context);
}
```

If implementation details require a different method shape, the contract should still preserve the same boundary: providers register XML handlers and context publication behavior, but they do not mutate Tool Broker policy directly.

### DataAgentQueryCapabilityProvider

This provider owns the single-turn query capability:

```text
name: DataAgentQueryCapabilityProvider
tools: dataagent_query
state effect: ReadsData
preconditions: TrustedRuntime, OwnerIdentity, PrivateChat
surface: OwnerPrivate
```

It wraps the existing `DataAgentToolHandler` behavior. Runtime output remains a `data_agent_context` block.

### DataAgentAnalysisCapabilityProvider

This provider owns multi-turn analysis capabilities:

```text
name: DataAgentAnalysisCapabilityProvider
tools:
  dataagent_analysis_start
  dataagent_analysis_continue
  dataagent_analysis_summarize
  dataagent_analysis_end
state effects:
  AppendsAnalysisTurn
  SummarizesAnalysis
  EndsAnalysis
preconditions:
  TrustedRuntime
  OwnerIdentity
  PrivateChat
  ActiveDataAgentAnalysisSession where required
```

It wraps the existing `DataAgentAnalysisToolHandler` and preserves the analysis session state machine.

### DataAgentCapabilityRegistry

The registry should be deterministic and fail fast:

- provider names must be unique;
- tool names must be unique across DataAgent providers;
- empty names are rejected;
- null providers are rejected;
- manifest tool names must match registered XML tools;
- registration order is stable.

This makes the boundary suitable for future V2/V3 expansion without introducing a large framework now.

## Data Flow

Registration flow:

```text
DataAgentModuleService.AwakeAsync
 -> create DataAgent services
 -> create query and analysis providers
 -> add providers to DataAgentCapabilityRegistry
 -> register provider XML handlers through IDataAgentCapabilityRegistrar
 -> publish provider manifests to the Tool Broker path
```

Runtime flow:

```text
owner private message
 -> QChat creates ToolRouteState
 -> XmlFunctionCaller routes the turn
 -> FunctionCaller injects only route-allowed XML docs
 -> model calls DataAgent XML tool only if currently exposed
 -> execution policy validates route and session scope
 -> provider handler returns DataAgent context
 -> route session state updates from context
 -> audit records remain available
```

## Safety

The V1.7 boundary must preserve these invariants:

- no static DataAgent XML tool document is inserted into the base DataAgent prompt;
- governed DataAgent XML tools fail closed when no current route exists;
- session-scoped analysis tools require the route session to match the XML `sessionId`;
- provider metadata does not expose hidden prompt text or raw XML manuals;
- readiness markers verify provider registration instead of relying on comments only;
- non-owner, group, and untrusted runtime surfaces do not receive DataAgent tools.

## Error Handling

Provider registration errors should fail at startup or test setup rather than silently degrading runtime policy. Duplicate provider names, duplicate tool names, missing manifests, and mismatched handler names should throw clear exceptions.

Runtime execution errors stay in the existing DataAgent service and XML execution policy paths. V1.7 should not add a second execution gate.

## Testing

V1.7 should add focused tests before implementation:

- provider registry rejects duplicate provider names;
- provider registry rejects duplicate tool names;
- DataAgent module registers query and analysis providers;
- DataAgent module prompt still contains Tool Broker contract and does not contain static DataAgent XML tags;
- ToolCapabilityRouter still exposes only route-allowed DataAgent tools;
- XML execution policy still rejects governed DataAgent tools without a route;
- analysis tools still require matching active session state;
- readiness scripts require the V1.7 capability boundary markers.

Full verification should use:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

## Acceptance Criteria

V1.7 is ready to merge when:

- `IDataAgentCapabilityProvider` or an equivalent DataAgent capability provider contract exists;
- query and analysis capabilities register through provider objects;
- DataAgent provider names and tool names are observable in tests;
- provider metadata feeds the Tool Broker manifest path without static prompt leakage;
- readiness scripts mark the capability boundary as required;
- all existing V1.6 Tool Broker safety tests still pass;
- full solution tests pass under the local .NET 9 SDK;
- no PostgreSQL, LangGraph, or `IDataAgentStore` implementation is added in this version.

## Future Work

V2 should introduce the real persistence boundary and PostgreSQL migration after this capability boundary is stable. That is the right time to design `IDataAgentStore` from concrete storage requirements instead of adding a premature V1.7 shell.

V2.5 can use the provider metadata as the input boundary for a LangGraph sidecar pilot. V3 can evolve provider metadata into a supervisor-controlled tool governance model with leases, budgets, conflict resolution, and cross-module audit queries.

Future multi-agent coordination should borrow the same boundary discipline. The later orchestration layer should separate permission validation, SQL generation, and report interpretation into dedicated nodes instead of rebuilding one large agent. A pre-scheduling Scenario Knowledge Package should normalize business terms and data definitions before SQL planning begins. Long-running chains should persist intermediate state through checkpoints, expose degradation paths when one node fails, and stream progress to the frontend or owner diagnostics so linked runs remain inspectable. V1.7 does not implement those nodes, but the capability metadata should be clean enough for that model to consume later.
