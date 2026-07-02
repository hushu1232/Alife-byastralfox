# DataAgent V2.7 Recent Diagnostics Cache Design

## Goal

DataAgent V2.7 adds a session-scoped recent diagnostics cache for QChat and DataAgent runtime observations.

The cache turns the V2.6 owner diagnostics surface from "latest injected diagnostic text" into a small, bounded, read-only runtime observability layer. It records sanitized recent snapshots for:

```text
semantic_state_recent
dataagent_evidence_recent
tool_route_recent
```

Owner diagnostics commands can read those snapshots by session, but they must not execute SQL, call the model, invoke XML tools, mutate QChat semantic windows, mutate DataAgent sessions, or authorize tools.

## Current Baseline

V2.5 added application-layer semantic and analysis state estimation:

- `KalmanScalarFilter` provides reusable scalar smoothing.
- `QChatSemanticStateEstimator` estimates completion, continuation, topic stability, and summary intent.
- `QChatSemanticSettleWindow` uses the estimator for wait, answer, and summarize decisions.
- `DataAgentAnalysisStateEstimator` attaches analysis confidence, answer stability, clarification need, risk, and reason code to Evidence Packs.

V2.6 made these facts visible to the owner:

- `/qchat diag semantic` exposes recent QChat semantic diagnostics.
- `/dataagent diag evidence` exposes recent DataAgent evidence diagnostics.
- `/qchat diag dataagent evidence` exposes the same evidence diagnostics from QChat.
- DataAgent publishes sanitized evidence diagnostics through a string bridge.
- QChat consumes only safe diagnostic strings and keeps no direct dependency on DataAgent internals.
- Readiness gates require the diagnostics surface.

The remaining gap is runtime continuity. V2.6 can show a recent diagnostic string, but it does not yet model "recent diagnostics" as a bounded, per-session cache with kind, timestamps, TTL, size limits, and redaction metadata.

## Selected Approach

Use an in-memory, session-scoped recent diagnostics cache in QChat.

The cache should accept only already-formatted diagnostic text or small generic diagnostic entries. It should never accept raw prompts, raw SQL, XML tool manuals, hidden tool route contexts, connection strings, API keys, or unsanitized external context.

The recommended shape is:

```csharp
public enum QChatRecentDiagnosticKind
{
    SemanticState,
    DataAgentEvidence,
    ToolRoute
}

public sealed record QChatRecentDiagnosticEntry(
    QChatRecentDiagnosticKind Kind,
    string SessionKey,
    string Source,
    string Text,
    DateTimeOffset CreatedAt,
    bool Redacted,
    string ReasonCode);
```

The cache should be a focused component, for example:

```csharp
public sealed class QChatRecentDiagnosticsCache
{
    public void Record(QChatRecentDiagnosticEntry entry);
    public QChatRecentDiagnosticEntry? GetLatest(string sessionKey, QChatRecentDiagnosticKind kind);
    public IReadOnlyList<QChatRecentDiagnosticEntry> GetRecent(string sessionKey);
}
```

The cache is deliberately not the state machine. It is a read-only observation layer for diagnostics commands.

## Project Value

V2.7 gives the project concrete improvements in six areas.

### 1. Real Runtime Observability

The owner can inspect what the system recently believed, not only what it says now.

Before V2.7:

```text
diagnostics command -> latest injected string or unavailable
```

After V2.7:

```text
diagnostics command -> session recent cache -> latest semantic/evidence/route snapshot with age and source
```

This makes debugging QChat wait/answer/summarize behavior and DataAgent evidence flow much faster.

### 2. Better Failure Localization

When a chain fails, V2.7 can show which layer is missing:

```text
semantic_state_recent=available
dataagent_evidence_recent=missing
tool_route_recent=available
```

That tells the maintainer whether the problem is semantic settling, DataAgent execution, Tool Broker routing, or the bridge between modules.

### 3. Stronger Harness Engineering Evidence

Readiness gates can prove more than "a formatter exists." They can prove:

- cache entry kinds exist,
- session filtering exists,
- TTL and capacity limits exist,
- hidden context redaction exists,
- diagnostics commands read the cache without executing tools,
- unavailable states are explicit and truthful.

This is stronger interview evidence because it demonstrates operational verification, not only unit formatting.

### 4. Safer Multi-Agent Preparation

Future LangGraph or multi-agent orchestration should consume read-only state snapshots, not poke live QChat or DataAgent internals.

V2.7 creates the right future contract:

```text
multi-agent sidecar
  -> read bounded diagnostics snapshot
  -> request capability through Tool Broker
  -> never own permission or QChat state
```

This lowers the risk of long-chain multi-agent failures spreading into live QQ reply behavior.

### 5. Cleaner QChat/DataAgent Boundary

QChat should not reference DataAgent orchestration types. DataAgent should not own QChat diagnostics UI.

V2.7 keeps the boundary simple:

```text
DataAgent formats safe evidence diagnostics
QChat records safe text by session
QChat diagnostics commands render safe cached text
```

That preserves module independence while still making the combined link observable.

### 6. More Trustworthy Demo And Interview Story

The owner can demonstrate:

```text
/qchat diag recent
/qchat diag semantic
/dataagent diag evidence
/qchat diag toolbroker
```

Then explain that the system exposes the evidence chain without exposing hidden prompts, raw SQL, or tool manuals. This directly maps to Loop Engineering, Harness Engineering, and Prompt Engineering.

## Command Contract

### `/qchat diag recent`

Returns a compact owner-only summary for the current session:

```text
QChat recent diagnostics
semantic_state_recent=available age_seconds=3 source=qchat_semantic_window redacted=false
dataagent_evidence_recent=available age_seconds=12 source=dataagent_analysis redacted=false
tool_route_recent=available age_seconds=2 source=tool_broker redacted=false
session=<session_key>
```

If no entries exist:

```text
QChat recent diagnostics
state=unavailable
reason=recent_diagnostics_empty
session=<session_key>
```

### `/qchat diag semantic`

Reads the latest `semantic_state_recent` entry for the current session. If no entry exists, it returns the existing truthful unavailable state:

```text
QChat semantic diagnostics
state=unavailable
reason=semantic_window_empty
```

### `/dataagent diag evidence`

Reads the latest `dataagent_evidence_recent` entry for the current session. If no entry exists, it returns:

```text
DataAgent evidence diagnostics
state=unavailable
reason=evidence_pack_unavailable
```

### `/qchat diag toolbroker`

Reads the latest `tool_route_recent` entry for the current session. If no entry exists, it preserves the existing unavailable or empty trace behavior.

## Runtime Data Flow

### Semantic State

```text
QChat semantic window update
  -> QChatSemanticStateEstimator
  -> QChatSemanticDiagnosticsFormatter
  -> QChatRecentDiagnosticsCache.Record(kind=SemanticState)
```

### DataAgent Evidence

```text
DataAgent analysis tool handler
  -> DataAgentEvidencePackBuilder
  -> DataAgentEvidenceDiagnosticsFormatter
  -> XmlFunctionCaller safe diagnostics bridge
  -> QChatRecentDiagnosticsCache.Record(kind=DataAgentEvidence)
```

### Tool Route

```text
Tool Broker route decision
  -> QChat diagnostic trace formatter
  -> QChatRecentDiagnosticsCache.Record(kind=ToolRoute)
```

## Cache Rules

The cache should be intentionally small:

```text
max_entries_per_session=12
max_text_chars=900
ttl=30 minutes
```

Rules:

- New entries are normalized to `\n` internally or `Environment.NewLine` at render time.
- Entries with hidden context markers are redacted instead of stored raw.
- Empty text is ignored.
- Session keys are required.
- Unknown diagnostic kinds are not accepted.
- Expired entries are ignored by reads and pruned during writes.
- Capacity eviction removes the oldest entries in the same session.
- The cache is in-memory for V2.7. Durable persistence is future work.

## Sanitization

The cache must redact or reject text containing these markers:

```text
[tool_route_context]
[/tool_route_context]
[data_agent_context]
[/data_agent_context]
[data_agent_evidence_pack]
[/data_agent_evidence_pack]
Allowed XML tools
connection_string
api_key
sk-
SELECT
INSERT
UPDATE
DELETE
```

The goal is not to parse SQL perfectly. The goal is to prevent obvious raw SQL, credentials, hidden context blocks, and tool manuals from becoming visible owner diagnostics.

When redaction happens, the stored visible text should be:

```text
<title>
state=redacted
reason=hidden_context_redacted
```

## Ownership Boundaries

### QChat Owns

- The recent diagnostics cache.
- Owner-only diagnostic commands.
- Session key association.
- Visible diagnostic rendering.
- Semantic state diagnostic recording.

### DataAgent Owns

- Evidence Pack construction.
- Evidence diagnostics formatting.
- Analysis state estimates.
- Tool Broker audit evidence on the DataAgent side.

### FunctionCaller Owns

- Safe bridge for recent DataAgent evidence diagnostic text.
- Tool execution policy and route trace exposure.

### Tool Broker Owns

- Tool permission.
- Route decisions.
- Execution denial or allow records.

No V2.7 component may move permission authority into the diagnostics cache.

## Safety Invariants

V2.7 must preserve these invariants:

- Diagnostics commands do not authorize tools.
- Diagnostics commands do not execute SQL.
- Diagnostics commands do not call the model.
- Diagnostics commands do not call XML tools.
- Diagnostics commands do not mutate QChat semantic windows.
- Diagnostics commands do not mutate DataAgent sessions.
- Diagnostics commands do not reveal hidden prompts.
- Diagnostics commands do not reveal raw SQL, API keys, or connection strings.
- QChat does not reference DataAgent orchestration types.
- DataAgent does not reference QChat diagnostics cache types.
- Tool Broker remains the only tool permission authority.

## Testing Strategy

Use test-first implementation.

Required tests:

1. Cache records and returns the latest entry by session and kind.
2. Cache isolates entries across sessions.
3. Cache evicts old entries when capacity is exceeded.
4. Cache ignores expired entries based on TTL.
5. Cache redacts hidden context markers.
6. `/qchat diag recent` returns a compact summary when entries exist.
7. `/qchat diag recent` returns `recent_diagnostics_empty` when no entries exist.
8. `/qchat diag semantic` reads `semantic_state_recent` before falling back to unavailable.
9. `/dataagent diag evidence` reads `dataagent_evidence_recent` before falling back to unavailable.
10. `/qchat diag toolbroker` reads `tool_route_recent`.
11. Non-owner access remains denied or silently dropped according to existing command policy.
12. Diagnostics reads do not call SQL, XML tools, or model code.
13. DataAgent readiness requires the V2.7 cache bridge markers.
14. QChat engineering map requires the V2.7 cache and command markers.

## Readiness Gates

Add required readiness markers after implementation:

### DataAgent

- Evidence diagnostics can be published into a safe bridge.
- Evidence diagnostics contain state estimate fields.
- Evidence diagnostics can be recorded as a recent cache entry without raw evidence tags.
- Readiness count increases only when the gate is implemented and tested.

### QChat

- Recent diagnostics cache exists.
- `/qchat diag recent` exists.
- Semantic, DataAgent evidence, and Tool Broker diagnostics read from the cache.
- Hidden context redaction is tested.
- Engineering map count increases only when the gate is implemented and tested.

## Non-Goals

V2.7 does not add LangGraph runtime.

V2.7 does not persist diagnostics to PostgreSQL.

V2.7 does not replace the QChat state machine.

V2.7 does not replace the DataAgent analysis session state machine.

V2.7 does not add a browser UI dashboard.

V2.7 does not expose diagnostics to non-owner accounts.

V2.7 does not add a new permission system.

V2.7 does not move DataAgent types into QChat.

V2.7 does not use `D:\FOXD` or any upload target other than `git@github.com:hushu1232/Alife-byastralfox.git`.

## Acceptance Criteria

V2.7 is complete when:

- A session-scoped recent diagnostics cache exists.
- The cache supports `semantic_state_recent`, `dataagent_evidence_recent`, and `tool_route_recent`.
- The cache enforces TTL, capacity, text normalization, and redaction.
- `/qchat diag recent` returns a truthful owner-only summary.
- `/qchat diag semantic` reads recent semantic diagnostics.
- `/dataagent diag evidence` reads recent DataAgent evidence diagnostics.
- `/qchat diag toolbroker` reads recent Tool Broker diagnostics.
- Missing entries return truthful unavailable states.
- Non-owner diagnostics access remains blocked by existing command policy.
- Diagnostics commands remain observational and side-effect free.
- DataAgent readiness passes with the new V2.7 gates.
- QChat engineering map passes with the new V2.7 gates.
- Focused QChat and DataAgent tests pass.
- Full solution tests pass under the local .NET 9 SDK.
- Final work is pushed only to `git@github.com:hushu1232/Alife-byastralfox.git`.

Required verification commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests|FullyQualifiedName~DataAgentModuleServiceTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal -m:1
git diff --check
```

## Interview Framing

V2.7 can be described like this:

```text
I added a session-scoped recent diagnostics cache for my bot and DataAgent runtime. QChat semantic estimates, DataAgent Evidence Pack summaries, and Tool Broker route traces are recorded as bounded, sanitized, owner-only snapshots. Diagnostics commands only read these snapshots. They cannot execute SQL, call tools, call the model, or mutate state machines. This gives the system debuggability and explainability while preserving Tool Broker as the permission authority.
```

This demonstrates:

- Loop Engineering: runtime state is observed across recent windows instead of explained by one prompt.
- Harness Engineering: cache behavior, redaction, command access, and readiness gates prove the chain.
- Prompt Engineering: hidden prompts and model outputs are not trusted as permission or diagnostics authority.

## Future Work

V2.8 can persist recent diagnostics through the Store Boundary:

```text
semantic_state_audit
dataagent_evidence_audit
tool_route_audit
```

V2.9 can move the durable store path toward PostgreSQL as the primary V2 audit backend while keeping SQLite compatibility for local mode.

V3 can let LangGraph or another multi-agent sidecar consume the recent diagnostics snapshots as read-only evidence while Tool Broker remains the permission authority.

## Spec Self-Review

- Open-item scan: no unfinished marker or open item remains.
- Scope check: this is one bounded V2.7 milestone focused on recent diagnostics cache, not persistence or LangGraph runtime.
- Boundary check: diagnostics are observational only and do not authorize tools, execute SQL, call the model, or mutate state.
- Data boundary check: QChat stores safe diagnostic entries; DataAgent still owns Evidence Pack formatting and analysis estimates.
- Testability check: the selected approach can be tested without live QQ, live model calls, PostgreSQL, or LangGraph.
