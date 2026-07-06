# DataAgent V2.16 DataQueryGraph Owner Diagnostics Design

## Purpose

V2.16 surfaces the V2.15 DataQueryGraph dry-run pilot through the existing owner-only diagnostics bridge.

The goal is observability, not new authority. The owner should be able to inspect the dry-run graph shape, accepted or fallback reason, runtime marker, scoped node capability summary, and no-execute behavior for denied or terminal workflows from QChat diagnostics.

V2.16 must not introduce real LangGraph runtime behavior. It should not add Python, FastAPI, HTTP sidecar calls, process management, or a sidecar adapter. The existing C# DataAgent pipeline remains the only runtime authority for planning, validation, SQL compilation, SQL safety, read-only execution, checkpoints, evidence, progress, and trace.

## Current Foundation

V2.15 already added:

- `DataAgentDataQueryGraphPilot`.
- `DataAgentDataQueryGraphOptions`.
- `DataAgentDataQueryGraphDryRunResult`.
- `DataAgentDataQueryGraphPlan`.
- `DataAgentDataQueryGraphNode`.
- `DataAgentDataQueryGraphTransition`.
- `DataAgentDataQueryGraphTraceFormatter`.
- `ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED`, disabled by default.
- Readiness gates proving no LangGraph runtime, scoped nodes, no SQL authority, fallback behavior, and denied or terminal no-execute behavior.
- QChat engineering-map guards proving QChat does not directly import DataQueryGraph pilot types.

The existing diagnostics bridge already supports:

- DataAgent evidence diagnostics.
- DataAgent trace diagnostics.
- DataAgent progress diagnostics.
- FunctionCaller string storage for recent diagnostics.
- QChat owner-only commands such as `/dataagent diag evidence`, `/dataagent diag trace`, and `/dataagent diag progress`.
- `QChatRecentDiagnosticsCache` for per-session owner diagnostics summaries.

V2.16 should extend this proven string bridge instead of creating a new diagnostics subsystem.

## Non-Overengineering Rule

V2.16 is not a LangGraph integration release. It is a small owner diagnostics bridge for an existing C# dry-run result.

This version should not:

- Make DataQueryGraph authoritative.
- Replace `DataAgentAnalysisOrchestrator`.
- Add a graph runtime process.
- Add a new command framework.
- Add new SQL execution paths.
- Expose graph diagnostics to non-owner senders.
- Let QChat import DataQueryGraph types.
- Force unrelated QQchat, browser, desktop, or RAG plugin capabilities into graph nodes.

The useful outcome is narrow: after a DataAgent analysis turn, the owner can ask QChat for the latest DataQueryGraph dry-run diagnostics.

## Selected Approach

The selected approach is a dedicated graph diagnostics channel.

Add owner-only commands:

```text
/dataagent diag graph
/dataagent diagnostics graph
/qchat diag dataagent graph
/qchat diagnostics dataagent graph
```

These commands return the latest bounded DataQueryGraph dry-run diagnostics for the current QChat diagnostics session.

This is preferred over folding graph output into `/dataagent diag trace` because trace diagnostics represent the actual C# orchestration timeline, while graph diagnostics represent the dry-run graph projection. Keeping them separate makes debugging clearer and avoids mixing observed runtime facts with pilot graph shape.

## Alternatives Considered

### Fold Graph Into Trace Diagnostics

This option would append DataQueryGraph dry-run text to the existing DataAgent trace diagnostics.

It touches fewer files, but it weakens the diagnostic boundary. Owner output would mix the actual trace timeline with graph dry-run output, making it harder to tell which lines describe current runtime behavior and which describe the future graph projection.

This option is rejected for V2.16.

### Real LangGraph Adapter

This option would add Python or LangGraph runtime scaffolding and connect it to the C# sidecar contract.

It is too early. V2.14 and V2.15 intentionally proved the contract and dry-run graph shape before any runtime dependency. V2.16 should use the pilot to improve observability before adding operational complexity.

This option is rejected for V2.16.

### Dedicated Owner Diagnostics Channel

This option adds a small string-only bridge from DataAgent to QChat.

It keeps QChat clean, makes owner UX explicit, and reuses existing diagnostics storage and redaction behavior.

This is the selected option.

## DataAgent Design

`DataAgentAnalysisToolHandler` should gain one optional publisher:

```csharp
Action<string>? dataQueryGraphDiagnosticsPublisher = null
```

After each orchestration result is produced and the normal analysis context is built, `PublishResult` should run the dry-run pilot and publish a bounded string:

```csharp
DataAgentDataQueryGraphDryRunResult graphResult =
    DataAgentDataQueryGraphPilot.DryRun(result);

dataQueryGraphDiagnosticsPublisher?.Invoke(
    DataAgentDataQueryGraphTraceFormatter.Format(graphResult));
```

This should run for:

- `Start`.
- `Continue`.
- `Summarize`.
- `End`.

When the environment flag is missing or disabled, the diagnostics should still be meaningful and should report:

```text
enabled=false
accepted=false
reason=dataquerygraph_disabled
fallback=pilot_disabled
runtime=no_langgraph_runtime
```

When the flag is enabled, diagnostics should report the dry-run graph state for the produced orchestration result.

The implementation should reuse `DataAgentDataQueryGraphTraceFormatter` unless a small wrapper is needed for an owner-facing title. It should not create a second formatter with different SQL redaction rules unless implementation reveals a concrete need.

## FunctionCaller Bridge

`XmlFunctionCaller` should store the latest graph diagnostics as normalized text, parallel to evidence, trace, and progress:

```text
RecentDataAgentGraphDiagnostics
RecordRecentDataAgentGraphDiagnostics(...)
```

This bridge should not reference DataQueryGraph models. It stores strings only.

Normalization should match the existing recent diagnostics pattern:

- Blank input becomes an empty string.
- Line endings are normalized to `\n`.
- Leading and trailing whitespace is trimmed.
- Storage is protected by its own lock.

## DataAgent Module Wiring

`DataAgentModuleService` should pass `functionService.RecordRecentDataAgentGraphDiagnostics` into `DataAgentAnalysisCapabilityProvider`.

`DataAgentAnalysisCapabilityProvider` should pass that optional publisher into `DataAgentAnalysisToolHandler`.

This preserves the existing ownership model:

- DataAgent creates the graph diagnostics text.
- FunctionCaller stores recent diagnostics text.
- QChat retrieves strings and applies owner-only access control.

## QChat Design

QChat should add a string-only diagnostics channel:

- `QChatRecentDiagnosticKind.DataAgentGraph`.
- `QChatDiagnosticsRuntimeState.RecentDataAgentGraph`.
- `QChatDiagnosticsService.BuildDataAgentGraphDiagnosticsText`.
- `QChatRecentDiagnosticsFormatter.Title` support for DataAgent graph diagnostics.
- `QChatRecentDiagnosticsFormatter.FormatSummary` line:

```text
dataagent_graph_recent=...
```

The unavailable state should be explicit:

```text
DataAgent graph diagnostics
state=unavailable
reason=graph_diagnostics_unavailable
```

QChat should accept these owner commands:

```text
/dataagent diag graph
/dataagent diagnostics graph
/qchat diag dataagent graph
/qchat diagnostics dataagent graph
```

QChat should keep these commands owner-only by extending both:

- `QChatOwnerCommandService.IsDiagnosticsCommand`.
- `QChatCommandAccessPolicy`.

Non-owner senders should be dropped silently by the same policy used for existing diagnostics commands.

## QChat Boundary

QChat must not import DataQueryGraph pilot types.

The forbidden marker list should include all existing V2.15 types, and if V2.16 adds any new DataQueryGraph-specific DataAgent formatter type, it should be added to that guard as well.

Expected boundary:

```text
DataAgentDataQueryGraph* types -> DataAgent project only
graph diagnostics text -> FunctionCaller string bridge -> QChat owner diagnostics
```

QChat may know the label `DataAgentGraph` because that is a local diagnostics cache kind. It may not know how graph plans or dry-run results are built.

## Safety Model

V2.16 inherits V2.15 graph safety rules:

- The graph pilot defaults disabled.
- The graph pilot has no LangGraph runtime.
- The graph pilot has no SQL authority.
- The graph pilot has no Tool Broker route authority.
- The graph pilot has no checkpoint mutation authority.
- The graph pilot has no evidence, progress, trace, or visible text authority.
- Only `read_only_execute` may carry the `ExecuteReadOnlyQuery` capability in dry-run node scope.
- Route-denied workflows must not contain execute nodes.
- Terminal summarize or end workflows must not contain execute nodes.
- Unknown graph nodes fail closed.

V2.16 also adds diagnostics safety:

- Graph diagnostics must remain owner-only.
- QChat stores and displays graph diagnostics as bounded sanitized text.
- Unsafe SQL-like text should be rejected or redacted by DataAgent formatting before it reaches QChat.
- QChat diagnostics cache should still apply `QChatDiagnosticTextSanitizer`.
- Hidden context tags, tool route context, evidence packs, credentials, raw SQL, and connection strings must not leak through graph diagnostics.

## Data Flow

The V2.16 flow should be:

```text
QChat owner/private or allowed route state
-> Tool Broker allows DataAgent analysis tool
-> DataAgentAnalysisToolHandler calls DataAgentAnalysisOrchestrator
-> Existing C# DataAgent pipeline produces DataAgentOrchestrationResult
-> DataAgent publishes normal analysis context
-> DataAgent publishes evidence, trace, and progress diagnostics as today
-> DataAgent runs DataQueryGraphPilot.DryRun(result)
-> DataAgentDataQueryGraphTraceFormatter formats bounded graph diagnostics
-> FunctionCaller stores recent graph diagnostics string
-> QChat owner command /dataagent diag graph retrieves latest string
-> QChatRecentDiagnosticsCache can include dataagent_graph_recent in summary
```

No step in this flow gives graph diagnostics authority to run tools or produce user-visible answers.

## Attention Dilution And Tool Selection

The purpose of surfacing the graph diagnostics is to make future agent decomposition inspectable.

The owner should be able to confirm that a future graph-shaped workflow reduces tool ambiguity by showing:

- Route gate stays separate from query planning.
- Scenario knowledge stays hint-only.
- Query planner does not get read-only execution authority.
- Validator, compiler, safety, and executor remain deterministic nodes.
- Diagnostics reading is separate from SQL execution.
- Denied and terminal turns cannot accidentally inherit execute capability.

This supports the larger goal: later, if a real LangGraph sidecar is approved, each node can receive a small scoped capability manifest instead of the full overlapping project tool surface. V2.16 should not implement the sidecar, but it should make the boundary visible enough to trust.

## Readiness And Engineering Map Gates

DataAgent readiness should add a required check, tentatively:

```text
DataQueryGraphOwnerDiagnosticsPresent
```

The check should prove:

- `DataAgentAnalysisToolHandler` publishes graph diagnostics.
- `DataAgentAnalysisCapabilityProvider` accepts and forwards the graph publisher.
- `DataAgentModuleService` wires `functionService.RecordRecentDataAgentGraphDiagnostics`.
- `XmlFunctionCaller` stores recent graph diagnostics.
- `DataAgentDataQueryGraphTraceFormatter` is reused or wrapped safely.
- Disabled result diagnostics are available.
- No LangGraph runtime is introduced.

QChat engineering map should add a required check, tentatively:

```text
DataAgent DataQueryGraph owner diagnostics
```

The check should prove:

- `/dataagent diag graph` exists.
- Recent diagnostics summary includes `dataagent_graph_recent`.
- QChat has a local `DataAgentGraph` diagnostics cache kind.
- QChat does not import DataQueryGraph pilot model types.
- Owner-only command access covers graph diagnostics.

Static required counts should increase only for meaningful new gates. The implementation should avoid adding duplicate readiness checks that prove the same thing twice.

## Tests

V2.16 tests should be focused and deterministic.

DataAgent tests:

- `DataAgentDataQueryGraphTraceFormatter` still formats disabled, accepted, fallback, denied, terminal, and unsafe SQL cases safely.
- `DataAgentAnalysisToolHandler.Start` publishes graph diagnostics when a graph publisher is provided.
- `Continue`, `Summarize`, and `End` publish graph diagnostics without changing analysis context behavior.
- Route-denied graph diagnostics do not imply execution.
- Terminal graph diagnostics do not imply execution.
- Handler tests confirm graph diagnostics do not require trace or evidence publishers.
- `XmlFunctionCaller` stores recent graph diagnostics.
- `DataAgentModuleService` wires the graph diagnostics publisher.

QChat tests:

- `/dataagent diag graph` returns unavailable when no graph diagnostics exist.
- `/dataagent diag graph` returns cached graph diagnostics for owner.
- `/qchat diag dataagent graph` and `/qchat diagnostics dataagent graph` work.
- Existing copied-menu-line stripping works for graph commands.
- Unsafe legacy graph diagnostics fallback text is redacted.
- `QChatRecentDiagnosticsCache` stores and redacts DataAgent graph diagnostics.
- Recent diagnostics summary includes `dataagent_graph_recent`.
- Owner command service passes recent graph diagnostics into runtime state.
- Command access policy allows owner graph diagnostics and silently drops non-owner graph diagnostics.
- QChat boundary test forbids direct `DataAgentDataQueryGraph*` imports.

No test should require live QChat, live PostgreSQL, network, Python, LangGraph, model calls, HTTP, or sidecar processes.

## Documentation

Add a developer note:

```text
docs/dataagent/dataagent-v2.16-dataquerygraph-owner-diagnostics.md
```

It should explain:

- V2.16 surfaces V2.15 dry-run output through owner-only diagnostics.
- The graph pilot remains disabled by default.
- Enabling the pilot only enables dry-run diagnostics.
- There is still no LangGraph runtime.
- QChat remains a string consumer.
- DataAgent remains the graph and SQL safety owner.
- The owner can use `/dataagent diag graph` to inspect graph state.
- Non-agentized plugin abilities remain deterministic services unless a future design explicitly assigns them to graph nodes.

## Acceptance Criteria

V2.16 is complete when:

- `DataAgentAnalysisToolHandler` publishes DataQueryGraph diagnostics through an optional publisher.
- `DataAgentAnalysisCapabilityProvider` and `DataAgentModuleService` wire the publisher.
- `XmlFunctionCaller` stores and exposes recent graph diagnostics text.
- QChat exposes `/dataagent diag graph` and `/qchat diag dataagent graph`.
- Graph diagnostics are owner-only.
- Recent diagnostics summary includes `dataagent_graph_recent`.
- QChat redacts unsafe graph diagnostics text.
- QChat source does not directly import DataQueryGraph pilot model types.
- DataAgent readiness includes the owner diagnostics bridge gate.
- QChat engineering map includes the owner diagnostics bridge gate.
- Focused DataAgent tests pass.
- Focused QChat tests pass.
- Readiness scripts pass with updated required counts.
- Full restore, build, and test verification pass sequentially when implementation is complete.

## Future Outlook

After V2.16, the project will have a visible graph dry-run channel without paying the operational cost of a real graph runtime.

This enables two future paths:

- V2.17 can tighten per-node manifest output if owner diagnostics show the dry-run graph is useful.
- A later V3 track can add a real LangGraph adapter only after the C# graph contract, node scopes, diagnostics, and fallback behavior are already proven.

The expected effect is a more auditable multi-agent transition:

- Less attention dilution because future graph nodes can see smaller scoped capability sets.
- Less random tool selection because similar plugin tools are not all presented to the same model step.
- Safer SQL handling because validation, compilation, safety, and execution remain deterministic services.
- Better owner debugging because graph projection, trace timeline, progress, and evidence are separate diagnostics surfaces.
- Cleaner plugin governance because QChat, DataAgent, desktop, browser, and RAG capabilities remain coordinated by existing route and capability boundaries instead of being collapsed into one oversized agent.

## Self-Review

- Placeholder scan: no placeholder requirements are present.
- Scope check: this is one bounded owner diagnostics bridge, not a real graph runtime.
- Boundary check: QChat remains a string consumer and must not import DataQueryGraph model types.
- Safety check: graph diagnostics do not authorize SQL, tools, route state, checkpoint mutation, evidence, progress, trace, visible text, or QQ ingress.
- Implementation readiness: the design is focused enough for one implementation plan after user review.
