# LangGraph Multi-Agent Orchestration Design

## Decision

LangGraph should not be enabled as a runtime dependency in V1.6 or V1.7. The project should prepare for it through Tool Broker observability, DataAgent capability boundaries, and provider-neutral persistence first. LangGraph can start as a V2.5 sidecar pilot for DataAgent analysis workflows, then become a V3 supervisor-controlled orchestration layer when PostgreSQL-backed state and audit are stable.

This decision keeps the existing Alife .NET runtime as the system of record. QChat continues to own QQ ingress, persona state, semantic settle windows, visible reply policy, voice, vision, and runtime readiness. Tool Broker remains the only authority for tool exposure and execution permission. LangGraph may propose agent handoffs and workflow steps, but it must not bypass Tool Broker or own QChat's main loop.

## Goals

- Create a clear path from V1.5 Tool Broker gates to V3 multi-agent orchestration.
- Achieve multi-agent "linked run" behavior without weakening the existing state machine.
- Keep LangGraph integration reversible until V2.5 proves value.
- Preserve Harness, Loop, and Prompt Engineering evidence as required project gates.
- Use PostgreSQL in V2 as the durable run-state and audit store for multi-agent traces.
- Make DataAgent the first LangGraph pilot surface because it is analytical, bounded, and easier to verify than live QQ chat.

## Non-Goals

- Do not replace QChatService with LangGraph.
- Do not let LangGraph expose or execute tools directly.
- Do not introduce LangGraph before Tool Broker decisions are observable and auditable.
- Do not start with a swarm architecture for user-facing chat.
- Do not make Python sidecar availability required for V1.x QChat operation.
- Do not move persona state, TTS warmup, vision routing, or OneBot loops out of .NET in V1.x or V2.

## Current Baseline

V1.5 introduces scheme 3: centralized Tool Broker runtime gates.

- DataAgent prompt no longer statically exposes XML tool manuals.
- Per-turn `[tool_route_context]` tells the model which tools are available.
- `XmlFunctionExecutionPolicy` fail-closes governed tools without an active route.
- DataAgent analysis session tools require route session ID alignment.
- QChat wraps model dispatch in scoped Tool Route state.
- DataAgent readiness and QChat engineering map have required gates for Tool Broker behavior.

This is the correct base for future orchestration because it separates tool availability from prompt desire.

## Target Milestones

### V1.6: Tool Broker Observability

V1.6 should add route decision reason codes, execution audit records, DataAgent Tool Broker audit persistence, and owner-only QChat diagnostics. This makes every tool allowance or denial inspectable.

LangGraph runtime is not enabled in V1.6.

### V1.7: DataAgent Capability Boundary

V1.7 should make DataAgent capabilities explicit through provider-like interfaces. The goal is to make DataAgent tools look like bounded capabilities that a future orchestrator can call without knowing module internals.

LangGraph runtime is not enabled in V1.7.

### V2: Durable Store And PostgreSQL

V2 should introduce provider-neutral store interfaces and PostgreSQL as the durable persistence option. Multi-agent orchestration needs reliable storage for agent runs, step outputs, handoffs, tool decisions, user review points, and resume checkpoints.

LangGraph runtime is still optional in V2. It can be prepared behind contracts, but production QChat must not depend on it.

### V2.5: LangGraph Sidecar Pilot

V2.5 should introduce LangGraph as a sidecar process for DataAgent analysis workflows only. The pilot should run behind a feature flag and should never take over QQ message handling.

The pilot workflow is:

```text
DataAgent request
  -> .NET workflow adapter
  -> LangGraph sidecar supervisor
  -> schema/intent agent
  -> NL2SQL planner agent
  -> SQL safety agent
  -> Tool Broker execution gate
  -> query execution
  -> data analyst agent
  -> report writer agent
  -> .NET DataAgent context result
```

### V3: Supervisor-Controlled Tool Governance

V3 can promote LangGraph or an equivalent supervisor layer into the main multi-agent governance model. The supervisor may own agent task budgets, handoff strategy, cross-agent memory retrieval, and human-in-the-loop interrupts. Tool Broker still owns tool permission.

## Architecture

```text
Alife .NET runtime
  QChatService
  DataAgentService
  Tool Broker
  Store/Audit
        |
        | request/response adapter
        v
LangGraph sidecar, V2.5+
  supervisor
  specialist agents
  checkpointing
  resumable workflow state
```

The .NET runtime remains the primary process. LangGraph is a subordinate orchestration sidecar. The sidecar receives bounded workflow requests and returns structured results. It never sends QQ messages, never calls OneBot, never writes visible chat text directly, and never bypasses .NET policy.

## Ownership Boundaries

### .NET Owns

- QQ ingress and egress.
- QChat persona and self-state machines.
- Semantic settle window and reply timing.
- TTS and vision runtime readiness.
- Tool Broker manifests, routing, and execution policy.
- DataAgent SQL safety and execution boundary.
- Store contracts and audit records.
- Required readiness scripts.

### LangGraph Owns

- Multi-agent workflow sequencing.
- Specialist agent handoff.
- Short-running and long-running task checkpoints.
- Human-in-the-loop interrupts for analysis tasks.
- Agent step trace formatting.
- Resume commands for a workflow thread.

### Shared Contract

The shared contract should be a small workflow API, not a direct dependency on LangGraph types:

```text
AgentWorkflowRequest
AgentWorkflowResult
AgentWorkflowStep
AgentWorkflowStatus
AgentHandoffRecord
ToolBrokerDecisionSnapshot
```

This keeps .NET testable without the Python sidecar.

## State Model

The project should keep one state authority for each category:

- Persona state: QChat .NET state machine.
- Tool permission state: Tool Broker .NET state.
- Data analysis session state: DataAgent .NET store.
- Multi-agent workflow state: LangGraph sidecar, mirrored to .NET audit/store after V2.
- Durable persistence: PostgreSQL in V2, with SQLite compatibility retained for V1.x.

The sidecar must receive a `workflow_id`, `caller_id`, `session_id`, and allowed capability set. It must return a bounded step trace and final result. The .NET caller persists the accepted result.

## Tool Permission Rule

LangGraph can request a tool call. It cannot authorize a tool call.

The required flow is:

```text
LangGraph proposed tool
  -> .NET adapter
  -> ToolCapabilityRouter
  -> XmlFunctionExecutionPolicy
  -> allowed or denied audit
  -> result returned to LangGraph
```

If Tool Broker denies the tool, LangGraph receives a structured denial and must choose one of these paths:

- ask a clarification question,
- summarize what can be answered without the tool,
- stop the workflow with a policy-denied status,
- request owner or human review when allowed by the surface.

## First Pilot Surface

The first LangGraph pilot should be DataAgent complex analysis, not QChat live reply generation.

Reasons:

- DataAgent has bounded inputs and outputs.
- NL2SQL has clear safety gates.
- SQL query plans can be tested deterministically.
- Analysis sessions already have summary windows.
- Output can return as `[data_agent_context]` without disturbing visible QChat tone.

## Multi-Agent Roles For The Pilot

- `DataSupervisorAgent`: chooses the next specialist and owns workflow completion.
- `SchemaIntentAgent`: maps the question to approved datasets and fields.
- `Nl2SqlPlannerAgent`: produces a structured query plan, not raw SQL execution.
- `SqlSafetyAgent`: validates read-only and approved-field constraints.
- `DataAnalystAgent`: interprets rows and points out limitations.
- `ReportWriterAgent`: produces the final bounded user-facing analysis.

These roles should be created only after DataAgent capability providers exist.

## Failure Handling

- Sidecar unavailable: .NET falls back to the existing DataAgent planner.
- Tool denied: workflow returns a policy-denied step and continues only if a safe fallback exists.
- Checkpoint unavailable: workflow is rejected before starting.
- Timeout: .NET stops waiting and records a timed-out workflow result.
- Invalid sidecar output: .NET rejects it through strict parsing and records an audit event.
- Human review required: workflow pauses and returns an interrupt object; QChat visible text is not sent until .NET policy approves it.

## Readiness Gates

LangGraph pilot cannot start until all gates pass:

- DataAgent readiness reports `0 required missing`.
- QChat engineering map reports `0 required missing`.
- Tool Broker route decisions include stable reason codes.
- Execution-denial audit exists.
- DataAgent can persist Tool Broker audit evidence.
- QChat owner diagnostics can inspect recent route decisions without leaking hidden prompts.
- DataAgent has provider-like capability boundaries.
- PostgreSQL store contract is implemented or the pilot explicitly runs in a V2.5 local-only mode with skipped live PostgreSQL tests.

## Testing Strategy

- Unit tests for shared workflow contract serialization.
- Unit tests for sidecar-unavailable fallback.
- Unit tests for Tool Broker denial propagation.
- Contract tests for DataAgent capability providers.
- Integration tests for DataAgent workflow adapter without live LangGraph.
- Optional live tests for LangGraph sidecar guarded by an environment variable.
- Optional live PostgreSQL tests guarded by `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`.

## Rollout

1. V1.6: implement observability and readiness gates.
2. V1.7: implement DataAgent capability provider boundary.
3. V2: implement PostgreSQL-backed store contracts.
4. V2.5: add LangGraph sidecar pilot behind a feature flag.
5. V3: promote supervisor-controlled multi-agent governance after pilot evidence is stable.

## Accepted Fast Path

The fastest high-quality route is not to install LangGraph now. The fastest route is to make the project LangGraph-ready by finishing V1.6 and V1.7 prerequisites, then adding the V2.5 sidecar with a small DataAgent-only pilot.

This preserves the existing state machine and Tool Broker work while still moving toward multi-agent linked runs.
