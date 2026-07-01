# DataAgent V2.6 Owner Diagnostics Design

## Goal

DataAgent V2.6 exposes the V2.5 semantic state estimator and Evidence Pack state as owner-only diagnostics, so QChat and DataAgent can be inspected without changing tool authorization, SQL execution, or state-machine ownership.

The selected user-facing commands are:

```text
/qchat diag semantic
/dataagent diag evidence
```

These commands should make V2.5 demonstrable: the project should be able to show why QChat waits, answers, or summarizes, and why DataAgent considers an analysis stable, risky, clarifying, or terminal.

## Current Baseline

V2.5 already added the important runtime facts:

- `KalmanScalarFilter` in `Alife.Framework` provides application-layer scalar state smoothing.
- `QChatSemanticStateEstimator` converts a semantic settle window into completion, continuation, topic stability, summary intent, and decision flags.
- `QChatSemanticSettleWindow` consumes the estimator for `ShouldSettle` while preserving hard max-window and max-message behavior.
- `DataAgentAnalysisStateEstimator` converts `DataAgentEvidencePack` into analysis confidence, answer stability, clarification need, risk level, and reason code.
- `DataAgentEvidencePackFormatter` emits sanitized estimator fields in `[data_agent_evidence_pack]`.
- `QChatDiagnosticsService` already handles owner-only `/qchat` diagnostics such as route, status, profile, and Tool Broker trace.
- `QChatCommandAccessPolicy` already drops non-owner `/qchat` commands silently.
- DataAgent readiness and QChat engineering map already require estimator markers.

The missing piece is an owner-only diagnostic surface that exposes the estimator facts directly and safely.

## Selected Approach

Use a small diagnostics-layer extension, not a runtime orchestration change.

`/qchat diag semantic` should be handled by `QChatDiagnosticsService`. The command should return a deterministic text report built from a `QChatSemanticStateEstimate` plus compact window metadata. It should not trigger a model call, send a message through OneBot directly, mutate the settle window, or authorize tools.

`/dataagent diag evidence` should expose a sanitized DataAgent Evidence Pack diagnostic summary. In V2.6, this can be implemented as a formatter/helper contract and surfaced through the same owner-only QChat diagnostics path. It should reuse `DataAgentEvidencePackFormatter` and the V2.5 estimator fields rather than inventing a parallel analysis model.

The first V2.6 implementation should prefer testable in-process formatting over a persistent runtime cache. A future V2.7 can wire these diagnostics to recent session snapshots or PostgreSQL audit history.

## Command Contract

### `/qchat diag semantic`

The command should return a compact report with stable keys:

```text
QChat semantic diagnostics
semantic_completion=<0..1>
continuation_likelihood=<0..1>
topic_stability=<0..1>
summary_intent=<0..1>
should_wait=<true|false>
should_answer=<true|false>
should_summarize=<true|false>
reason_code=<reason>
window_messages=<count>
window_age_seconds=<seconds>
last_update_age_seconds=<seconds>
```

If no semantic window estimate is available, the command should still be truthful:

```text
QChat semantic diagnostics
state=unavailable
reason=semantic_window_empty
```

The unavailable state must not pretend a live window exists.

### `/dataagent diag evidence`

The command should return a compact report with stable keys:

```text
DataAgent evidence diagnostics
analysis_confidence=<0..1>
answer_stability=<0..1>
clarification_need=<0..1>
risk_level=<0..1>
state_estimate_reason_code=<reason>
route_allowed=<true|false>
route_allows_query=<true|false>
executed_sql=<true|false>
terminal=<true|false>
tool_broker_audit_allowed=<true|false>
```

If no recent Evidence Pack is available, the command should return:

```text
DataAgent evidence diagnostics
state=unavailable
reason=evidence_pack_unavailable
```

The output should stay compact enough for QQ messages and should not include raw SQL text, raw prompts, connection strings, API keys, full hidden tool manifests, or unsanitized external context.

## Architecture

V2.6 keeps this boundary:

```text
QChat / DataAgent runtime facts
  -> semantic/evidence state estimate
  -> owner-only diagnostic formatter
  -> visible diagnostic text for owner
```

It must not become:

```text
diagnostic command
  -> tool route permission
  -> SQL execution
  -> state transition
```

Suggested implementation units:

```csharp
public sealed record QChatSemanticDiagnosticsSnapshot(
    QChatSemanticStateEstimate? Estimate,
    int WindowMessageCount,
    TimeSpan WindowAge,
    TimeSpan LastUpdateAge);
```

```csharp
public static class QChatSemanticDiagnosticsFormatter
{
    public static string Format(QChatSemanticDiagnosticsSnapshot snapshot);
}
```

```csharp
public static class DataAgentEvidenceDiagnosticsFormatter
{
    public static string Format(DataAgentEvidencePack? pack);
}
```

These helpers should be deterministic and easy to test without a live QQ bot, live model, live SQL database, or LangGraph sidecar.

## Runtime Integration

V2.6 should initially add the commands to `QChatDiagnosticsService.TryHandle`.

For the first implementation, `QChatDiagnosticsRuntimeState` can be extended with optional diagnostic strings or optional snapshots. This follows the existing `RecentToolRouteTrace` pattern and avoids coupling `QChatDiagnosticsService` to QChat runtime internals.

Recommended shape:

```csharp
public sealed record QChatDiagnosticsRuntimeState(
    bool ReplyTimingDelayEnabled = false,
    bool ConversationSettleWindowEnabled = false,
    bool InternetAccessEnabled = false,
    string? RecentToolRouteTrace = null,
    string? RecentSemanticEstimate = null,
    string? RecentDataAgentEvidence = null);
```

The command handler can return sanitized `RecentSemanticEstimate` and `RecentDataAgentEvidence` when present, and unavailable diagnostics when absent.

This is intentionally smaller than wiring live caches in the first V2.6 step. It gives the project a stable command contract and tests before runtime snapshot persistence is introduced.

## Owner-Only Boundary

V2.6 relies on the existing owner-only `/qchat` command gate:

- Owner `/qchat diag semantic` is allowed.
- Owner `/dataagent diag evidence` should be accepted only if it is routed through the same owner-only command handling path or an equivalent owner-only policy.
- Non-owner private users and group members must not receive diagnostics.
- Non-owner command attempts should remain silently dropped where the existing `/qchat` command policy already does that.

If `/dataagent diag evidence` is implemented as a new literal command prefix instead of a `/qchat` subcommand, it must receive an explicit owner-only command policy test before any runtime handling is added.

## Safety Invariants

V2.6 must preserve these invariants:

- Diagnostics do not authorize tools.
- Diagnostics do not expose XML tool manuals.
- Diagnostics do not call `XmlFunctionCaller`.
- Diagnostics do not execute SQL.
- Diagnostics do not mutate DataAgent sessions.
- Diagnostics do not mutate QChat semantic windows.
- Diagnostics do not call the LLM.
- Diagnostics do not reveal hidden prompts or raw external context.
- Diagnostics remain deterministic and sanitized.
- Tool Broker remains the only permission authority.
- DataAgent state machine ownership remains in `DataAgentAnalysisService` and orchestration code.

## Testing Strategy

Use test-first implementation.

Required focused tests:

1. QChat semantic diagnostics with an estimate.
   - Build a `QChatSemanticStateEstimate`.
   - Format it through the diagnostic command or formatter.
   - Assert stable keys for completion, continuation, topic stability, summary intent, decision flags, reason code, and window metadata.

2. QChat semantic diagnostics unavailable state.
   - Invoke `/qchat diag semantic` with no recent semantic estimate.
   - Assert `state=unavailable` and `reason=semantic_window_empty`.

3. DataAgent evidence diagnostics with a pack.
   - Build a `DataAgentEvidencePack` containing V2.5 estimator fields.
   - Format it through the diagnostic command or formatter.
   - Assert stable keys for analysis confidence, answer stability, clarification need, risk level, reason code, route flags, execution flag, terminal flag, and Tool Broker audit flag.

4. DataAgent evidence diagnostics unavailable state.
   - Invoke the diagnostic command with no pack or no recent evidence string.
   - Assert `state=unavailable` and `reason=evidence_pack_unavailable`.

5. Sanitization.
   - Include a fake hidden context tag, copied tool-route wrapper, or multiline unsafe text in diagnostic input.
   - Assert the visible diagnostic output is normalized and does not leak hidden wrapper text.

6. Owner-only command policy.
   - Assert the chosen command surface is owner-only.
   - If `/dataagent diag evidence` remains a literal command, add a dedicated command access policy test for that prefix.

7. Readiness and engineering map.
   - Add QChat engineering map markers for semantic diagnostics.
   - Add DataAgent readiness markers for evidence diagnostics.
   - Increment required counts only when the gate is truly required.

## Readiness Gates

Add required checks after the implementation lands:

- `QChat semantic diagnostics`
  - `QChatDiagnosticsService` recognizes `/qchat diag semantic`.
  - diagnostic output includes `semantic_completion`, `continuation_likelihood`, and `reason_code`.
  - tests cover available and unavailable states.

- `DataAgent evidence diagnostics`
  - DataAgent diagnostic formatter or command recognizes evidence diagnostics.
  - diagnostic output includes `analysis_confidence`, `risk_level`, and `state_estimate_reason_code`.
  - tests cover available and unavailable states.

The readiness gates should prove diagnostics exist without requiring live QQ, live model calls, live PostgreSQL, or LangGraph.

## Non-Goals

V2.6 does not add LangGraph runtime.

V2.6 does not persist estimator traces to PostgreSQL.

V2.6 does not build a WebBridge UI, chart dashboard, or external API.

V2.6 does not make diagnostics visible to non-owner accounts.

V2.6 does not add a new permission system.

V2.6 does not execute SQL for diagnostics.

V2.6 does not replace DataAgent Evidence Pack, QChat semantic settle window, Tool Broker, or existing state machines.

V2.6 does not use `D:\FOXD` or any upload target other than `git@github.com:hushu1232/Alife-byastralfox.git`.

## Acceptance Criteria

V2.6 is complete when:

- `/qchat diag semantic` returns a deterministic owner-only semantic diagnostic report.
- DataAgent evidence diagnostics return a deterministic owner-only evidence diagnostic report.
- unavailable states are truthful and explicit.
- diagnostic output is sanitized and compact.
- diagnostics do not execute SQL, call tools, call the model, or mutate state.
- QChat command access remains owner-only.
- DataAgent readiness passes with the new diagnostic gate.
- QChat engineering map passes with the new diagnostic gate.
- focused QChat and DataAgent tests pass.
- full solution tests pass under the local .NET 9 SDK.
- the final work is pushed only to `git@github.com:hushu1232/Alife-byastralfox.git`.

Required verification commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatCommandAccessPolicyTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentEvidencePackTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
git diff --check
```

## Interview Framing

V2.6 should be described as the point where V2.5 becomes operationally visible:

```text
I added owner-only diagnostic commands for my semantic estimator and DataAgent Evidence Pack. The model is not trusted to decide permissions; it produces observations, the application filters them into stable state, Tool Broker and state machines decide, and diagnostics expose the evidence chain to the owner. This is useful for debugging and interviews because I can show exactly why the bot waited, answered, summarized, or rejected a DataAgent path without leaking hidden prompts or bypassing security gates.
```

This demonstrates:

- Loop Engineering: semantic state is observed across a window instead of decided in one shot.
- Harness Engineering: diagnostics, tests, and readiness gates prove the behavior.
- Prompt Engineering: hidden prompts and external context remain sanitized and non-authoritative.

## Future Work

V2.7 can add a session-scoped recent diagnostics cache:

```text
semantic_state_recent
dataagent_evidence_recent
tool_route_recent
```

V2.8 can persist estimator traces through the Store Boundary, preferably with PostgreSQL as the primary V2 store and SQLite as compatibility/local mode.

V3 can let LangGraph or another multi-agent sidecar consume these diagnostic snapshots as read-only evidence while Tool Broker remains the permission authority.

## Spec Self-Review

- Placeholder scan: no TBD, TODO, or open placeholder remains.
- Scope check: this is one bounded V2.6 milestone focused on owner-only diagnostics, not persistence or LangGraph.
- Boundary check: diagnostics are observational only and do not authorize tools, execute SQL, call the model, or mutate state.
- Testability check: the selected approach can be tested without live QQ, live model calls, PostgreSQL, or LangGraph.
