# DataAgent V2.17 Diagnostics Command Contract Design

## Purpose

V2.17 closes the V2 line by hardening the QChat ingress contract for DataAgent owner diagnostics.

V2.16 made DataQueryGraph dry-run diagnostics visible to the owner. Its final review left one narrow engineering debt: the same DataAgent diagnostics command vocabulary is repeated across access policy, owner command routing, and diagnostics rendering. That duplication is small today, but it sits on a safety boundary. If one layer later accepts `/dataagent diag graph` while another layer forgets it, a command can be dropped unexpectedly, routed to the model, or handled differently from the owner-only diagnostics surface.

The goal of V2.17 is not to add more graph behavior. It is to make the existing diagnostics vocabulary explicit, shared, tested, and ready for V3.

## Non-Overengineering Rule

V2.17 is a closure and hardening release.

This version must not:

- Add Python, LangGraph, FastAPI, HTTP sidecar calls, or process management.
- Make DataQueryGraph authoritative.
- Add SQL execution paths.
- Move SQL, permissions, checkpoint, evidence, trace, or progress authority out of the existing C# DataAgent pipeline.
- Let QChat reference `DataAgentDataQueryGraph*` model types.
- Force QChat, desktop, browser, RAG, voice, memory, or other deterministic plugin abilities into graph nodes.
- Build a general command framework when a small local contract is enough.

The useful outcome is intentionally small: every QChat layer that recognizes DataAgent diagnostics should share one command contract.

## Current Foundation

The current QChat boundary already includes:

- `QChatCommandAccessPolicy`, which blocks non-owner `/qchat` and selected `/dataagent` diagnostics commands before they reach normal model dispatch.
- `QChatOwnerCommandService.IsDiagnosticsCommand`, which determines whether an incoming text should enter owner diagnostics handling.
- `QChatDiagnosticsService.TryHandle`, which renders DataAgent evidence, trace, progress, and graph diagnostics.
- `QChatRecentDiagnosticsCache`, `QChatRecentDiagnosticsFormatter`, and `QChatDiagnosticTextSanitizer`, which bound and sanitize owner diagnostics text.
- QChat engineering-map checks that prove QChat does not import `DataAgentDataQueryGraph*` pilot model types.

The current duplicated DataAgent diagnostics vocabulary is:

```text
/dataagent diag evidence
/dataagent diagnostics evidence
/dataagent diag trace
/dataagent diagnostics trace
/dataagent diag progress
/dataagent diagnostics progress
/dataagent diag graph
/dataagent diagnostics graph
```

The existing QChat diagnostics aliases are:

```text
/qchat diag dataagent evidence
/qchat diagnostics dataagent evidence
/qchat diag dataagent trace
/qchat diagnostics dataagent trace
/qchat diag dataagent progress
/qchat diagnostics dataagent progress
/qchat diag dataagent graph
/qchat diagnostics dataagent graph
```

Both families should remain owner-only through existing QChat command access rules.

## Selected Approach

Add one small QChat-local command contract:

```text
sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs
```

The contract should parse only strings and return only QChat-local concepts. It should not depend on DataAgent assemblies or graph pilot models.

Recommended shape:

```csharp
public enum QChatDataAgentDiagnosticsTopic
{
    Evidence,
    Trace,
    Progress,
    Graph
}

public static class QChatDataAgentDiagnosticsCommandContract
{
    public static bool TryParseDataAgentCommand(string? text, out QChatDataAgentDiagnosticsTopic topic);
    public static bool TryParseDataAgentCommandSuffix(string? command, out QChatDataAgentDiagnosticsTopic topic);
    public static bool TryParseQChatDataAgentDiagnosticsCommandSuffix(string? command, out QChatDataAgentDiagnosticsTopic topic);
    public static IReadOnlyList<string> SupportedDataAgentCommandSuffixes { get; }
}
```

The exact API can stay smaller if implementation shows fewer methods are needed, but the contract must centralize:

- The supported topics: `evidence`, `trace`, `progress`, and `graph`.
- The accepted verbs: `diag` and `diagnostics`.
- The `/dataagent` prefix boundary.
- The `/qchat diag dataagent <topic>` and `/qchat diagnostics dataagent <topic>` suffix boundary, if `QChatDiagnosticsService` benefits from reusing it.
- Copied menu description stripping with `" - "`.
- Case-insensitive matching.
- Whitespace-safe prefix handling.

This is preferred over a larger command framework because the problem is not command extensibility in general. It is one duplicated safety vocabulary.

## Alternatives Considered

### Keep Duplicates And Add Tests

This is the smallest code change. We could leave the three copies and add table-driven tests for all command variants.

It improves coverage but leaves drift possible. Future edits would still require touching multiple files in sync. Because this duplication sits in an owner-only ingress boundary, coverage alone is not enough.

This option is rejected for V2.17.

### General QChat Command Router

This would create a generic command parser or routing table for all `/qchat`, `/dataagent`, `/approve`, `/deny`, `/status`, and future commands.

It is too large for the current problem. QChat already has many mature command paths with different semantics. Replacing them would create a migration risk without improving the DataAgent diagnostics boundary proportionally.

This option is rejected for V2.17.

### Focused DataAgent Diagnostics Contract

This option extracts only the repeated DataAgent diagnostics vocabulary into one QChat-local helper and updates the three consumers.

It reduces drift, keeps the existing command surfaces, keeps QChat string-only, and avoids runtime or architecture churn.

This is the selected option.

## Component Design

### QChatDataAgentDiagnosticsCommandContract

The new contract should live in the QChat project because it governs QChat ingress and rendering behavior.

Responsibilities:

- Recognize `/dataagent diag <topic>` and `/dataagent diagnostics <topic>`.
- Recognize suffixes used behind `/qchat`, such as `diag dataagent <topic>`.
- Normalize copied menu descriptions.
- Expose the supported `/dataagent` suffixes for tests and future menu checks.
- Return a `QChatDataAgentDiagnosticsTopic`, not raw strings, to make dispatch exhaustive.

Non-responsibilities:

- It does not build diagnostics text.
- It does not read caches.
- It does not know DataQueryGraph plan or dry-run types.
- It does not authorize SQL or tools.
- It does not decide sender ownership. Ownership remains in `QChatCommandAccessPolicy` and the service-level sender role checks.

### QChatCommandAccessPolicy

`QChatCommandAccessPolicy.IsOwnerDiagnosticCommand` should keep the existing broad `/qchat` behavior.

For `/dataagent`, it should delegate to the new contract instead of maintaining its own topic list. This preserves the current behavior:

- Owner `/dataagent diag <known-topic>` is allowed into owner command handling.
- Non-owner `/dataagent diag <known-topic>` is dropped silently.
- Unknown `/dataagent` forms are not considered owner diagnostics commands and pass through according to the existing non-command behavior.

### QChatOwnerCommandService

`QChatOwnerCommandService.IsDiagnosticsCommand` should continue to return true for valid `/qchat` diagnostics commands as it does today.

For `/dataagent`, it should delegate to the shared contract. This prevents the event router from treating one vocabulary as diagnostics while access policy or rendering treats a different vocabulary as diagnostics.

### QChatDiagnosticsService

`QChatDiagnosticsService.TryHandle` should use the shared parsed topic for DataAgent diagnostics dispatch.

The existing output methods remain authoritative for text:

- `BuildDataAgentEvidenceDiagnosticsText`
- `BuildDataAgentTraceDiagnosticsText`
- `BuildDataAgentProgressDiagnosticsText`
- `BuildDataAgentGraphDiagnosticsText`

The service should preserve current fallback behavior:

- Unknown `/dataagent` commands return `Handled=false`.
- Unknown `/qchat` commands continue to follow the existing root-menu fallback.
- Missing recent DataAgent diagnostics return explicit unavailable text.
- Cached recent diagnostics are preferred over legacy fallback text.
- Unsafe legacy fallback text is sanitized.

### QChatService Adapter Ingress

Add a focused adapter test for the full ingress boundary:

```text
non-owner sends /dataagent diag graph
-> command access policy classifies it as owner-only diagnostics
-> QChat service drops it silently
-> no model dispatch
-> no private or group reply
```

This test matters because unit tests around `QChatCommandAccessPolicy` prove the decision, but the adapter test proves the decision is honored before model dispatch.

## Data And Control Flow

The V2.17 command flow should be:

```text
Incoming QQ text
-> QChat command ingress policy
-> shared DataAgent diagnostics command contract for /dataagent forms
-> owner-only command routing
-> QChat diagnostics service
-> shared parsed topic
-> existing diagnostics text builder
-> sanitized owner-visible diagnostics reply
```

For non-owner `/dataagent diag graph`, the flow should be:

```text
Incoming QQ text
-> QChat command ingress policy
-> shared contract recognizes graph diagnostics
-> sender is not owner
-> DropSilently
-> no model dispatch
-> no reply
```

For ordinary user text mentioning DataAgent without the command prefix, the flow should stay unchanged:

```text
Incoming QQ text
-> shared contract does not recognize command
-> normal QChat conversation path can proceed according to existing policy
```

## Safety Model

V2.17 inherits the existing DataAgent and QChat safety boundaries:

- DataAgent remains the only owner of QueryPlan, SQL validation, SQL compilation, SQL safety, read-only execution, evidence, trace, progress, checkpoint, and DataQueryGraph dry-run projection.
- QChat remains a string-only consumer of diagnostics.
- QChat must not import `DataAgentDataQueryGraph*` types.
- Diagnostics commands remain owner-only.
- Non-owner diagnostics commands are silently dropped instead of generating a visible denial that could disclose the command surface.
- Unknown `/dataagent` commands must not become privileged commands by accident.
- The contract must fail closed: only known topics and known verbs match.

This also supports the future LangGraph goal without prematurely implementing it. By making the command contract deterministic, V2.17 reduces one source of model attention dilution: diagnostics command routing is no longer a prompt-level decision or a set of overlapping string checks scattered across layers.

## Attention Dilution And Tool Choice

The larger V2 and V3 theme is to reduce random tool selection caused by overlapping tool names, duplicated descriptions, and overly broad model-visible capability surfaces.

V2.17 contributes in a small but concrete way:

- Owner diagnostics commands are parsed by deterministic code, not inferred by a model.
- Evidence, trace, progress, and graph are explicit topics, not free-form strings.
- QChat ingress does not need to expose DataAgent graph implementation details.
- The graph dry-run remains observable without making the graph runtime authoritative.
- Deterministic plugin abilities stay deterministic services unless a later V3 design assigns them to scoped graph nodes.

In V3, the same idea should scale from diagnostics commands to agent node manifests: each node sees a small scoped capability set instead of the whole project tool surface.

## Readiness And Engineering Map

QChat engineering map should add one required marker for this contract if implementation changes are substantial enough to justify a new gate.

Recommended check name:

```text
DataAgent diagnostics command contract
```

The check should prove:

- `QChatDataAgentDiagnosticsCommandContract` exists.
- `QChatCommandAccessPolicy` uses it for `/dataagent` diagnostics.
- `QChatOwnerCommandService` uses it for `/dataagent` diagnostics.
- `QChatDiagnosticsService` uses parsed topics for DataAgent diagnostics dispatch.
- QChat source still omits `DataAgentDataQueryGraph`.

If this check is added to `tools/check-qchat-engineering-map.ps1`, the expected required count should increase from `60` to `61`, and `QChatEngineeringMapRequiredV2Tests` should assert the new required marker.

DataAgent readiness does not need a new required gate for V2.17 because this version is QChat ingress hardening only. Adding a DataAgent readiness gate would imply a DataAgent behavior change that does not exist.

## Tests

V2.17 tests should be table-driven and focused.

Contract tests:

- All `/dataagent diag|diagnostics {evidence,trace,progress,graph}` variants parse to the expected topic.
- All `/qchat diag|diagnostics dataagent {evidence,trace,progress,graph}` suffixes parse to the expected topic.
- Copied menu description suffixes are stripped.
- Prefix boundaries are strict: `/dataagentx`, `/dataagent/diag`, and `dataagent diag evidence` do not match.
- Unknown topics fail closed.
- Empty or null text fails closed.

Access policy tests:

- Owner is allowed for every supported `/dataagent` diagnostics variant.
- Non-owner is silently dropped for every supported `/dataagent` diagnostics variant.
- Unknown `/dataagent` commands remain `NotCommand`.

Owner command service tests:

- `IsDiagnosticsCommand` returns true for every supported `/dataagent` diagnostics variant.
- `IsDiagnosticsCommand` remains false for unknown `/dataagent` commands.

Diagnostics service tests:

- Every `/dataagent diag|diagnostics <topic>` variant renders the expected diagnostics family.
- Every `/qchat diag|diagnostics dataagent <topic>` variant renders the expected diagnostics family.
- Graph diagnostics still prefer recent cache over legacy fallback.
- Unsafe legacy graph diagnostics fallback text remains redacted.
- Missing diagnostics still render explicit unavailable states.

Adapter ingress test:

- Non-owner `/dataagent diag graph` is dropped before model dispatch and produces no reply.

Readiness tests:

- QChat engineering-map required tests include the new command contract marker if the script adds that marker.
- Boundary tests still confirm QChat does not import `DataAgentDataQueryGraph*`.

No test should require live QChat, PostgreSQL, model calls, network, Python, LangGraph, browser automation, or sidecar processes.

## Documentation

Add a short developer note if implementation changes need a runtime-facing explanation:

```text
docs/dataagent/dataagent-v2.17-diagnostics-command-contract.md
```

It should explain:

- V2.17 is a QChat diagnostics command contract hardening release.
- It does not add LangGraph runtime behavior.
- It does not change SQL authority.
- QChat remains string-only.
- Supported owner diagnostics topics are evidence, trace, progress, and graph.
- Non-owner diagnostics commands are silently dropped before model dispatch.
- This is the final V2 closure gate unless implementation reveals a real gap.

## Acceptance Criteria

V2.17 is complete when:

- A shared QChat-local DataAgent diagnostics command contract exists.
- `QChatCommandAccessPolicy` delegates `/dataagent` diagnostics matching to the contract.
- `QChatOwnerCommandService` delegates `/dataagent` diagnostics matching to the contract.
- `QChatDiagnosticsService` dispatches DataAgent diagnostics through parsed topics instead of duplicated hard-coded command strings.
- The supported DataAgent diagnostics vocabulary remains unchanged.
- Unknown `/dataagent` commands fail closed.
- Non-owner `/dataagent diag graph` is dropped before model dispatch.
- QChat still does not reference `DataAgentDataQueryGraph*` types.
- Focused QChat tests pass.
- `tools/check-qchat-engineering-map.ps1` passes.
- QChat production no-import scan for `DataAgentDataQueryGraph` returns no matches.
- Full restore, build, and test verification pass before declaring the implementation complete.

## V3.0 Start Gate

V3.0 should start after V2.17 is implemented, verified, and reviewed.

The practical V3.0 entry conditions are:

- QueryPlan-first SQL safety remains stable.
- PostgreSQL checkpoint persistence remains verified.
- Tool Broker dynamic exposure and route permissions remain verified.
- Scenario knowledge packs are integrated as deterministic context, not uncontrolled model memory.
- Evidence, trace, progress, and graph diagnostics are owner-visible and separately inspectable.
- QChat has no direct DataQueryGraph model imports.
- QChat diagnostics command ingress is deterministic and shared by all relevant layers.
- Non-agentized plugin abilities remain deterministic services unless a V3 design explicitly makes them scoped graph nodes.

Once these are true, V3.0 can begin as a real LangGraph adapter design rather than a speculative project showcase. The first V3 milestone should still be conservative: connect a sidecar behind the already proven C# contract, keep SQL authority in C#, and expose only scoped node manifests to reduce attention dilution and random tool choice.

## Future Outlook

After V2.17, V2 should be considered closed unless verification uncovers a true safety or boundary gap.

Expected effects:

- Less command drift across QChat ingress, owner routing, and diagnostics rendering.
- Safer owner diagnostics because all `/dataagent` diagnostics commands share one parser.
- Cleaner V3 handoff because the graph diagnostics surface is stable before a runtime sidecar exists.
- Lower model uncertainty because diagnostics command handling is deterministic.
- Better protection against over-agentization because deterministic plugin capabilities stay outside graph nodes until a real V3 design assigns them scoped roles.

The V3.0 story should be: V2 proved the safety boundary, persistence, diagnostics, and graph-shaped contract; V3 can then add a runtime adapter in a controlled way, not as decoration.

## Self-Review

- Placeholder scan: no placeholder requirements are present.
- Scope check: this is one QChat command contract hardening release, not a runtime integration.
- Boundary check: QChat remains a string-only consumer and must not import DataQueryGraph model types.
- Safety check: diagnostics parsing does not authorize SQL, tools, route state, checkpoint mutation, evidence, progress, trace, visible text, or QQ ingress by itself.
- Ambiguity check: unknown `/dataagent` commands fail closed and do not become owner diagnostics commands.
- V3 readiness check: V3.0 starts only after V2.17 implementation and verification, not before.
