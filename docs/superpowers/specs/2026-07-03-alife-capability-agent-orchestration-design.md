# Alife Capability Agent Orchestration Design

## Purpose

V2.10 does not add LangGraph runtime behavior. It makes Alife's existing plugin system governable by classifying which capabilities are interaction surfaces, deterministic services, context providers, workflow candidates, perception adapters, presentation adapters, or external bridges.

The purpose is to reduce attention dilution and random tool selection before introducing any multi-agent runtime. QChat, DataAgent, Tool Broker, Memory, Browser, DesktopControl, Vision, Speech, DeskPet, Emotion, MCP, Python, and Developer-facing capabilities should be governed through explicit roles rather than being treated as one flat list of callable tools.

## Non-Overengineering Rule

Do not agentize a capability just because it exists. Agent workflows are reserved for long-running, semantic, recoverable chains such as DataAgent query analysis, owner diagnostics routing, future web research, and future desktop stewardship.

Safety checks, SQL validation, read-only execution, audit, redaction, file policies, approval gates, perception adapters, and presentation adapters remain deterministic services. They can be called by a workflow node, but they should not become free-form agents.

LangGraph remains a later optional workflow runtime. It is not the authority for QChat permissions, SQL safety, desktop approval, audit, redaction, or visible reply policy.

## Current System Roles

- QChat is the interaction and owner-command surface.
- FunctionCaller is the Tool Broker and execution policy layer.
- DataAgent is the first workflow candidate because it already has route, plan, validate, execute, explain, evidence, checkpoint, progress, and trace boundaries.
- Browser and DesktopControl are future workflow candidates behind owner gates and approval policies.
- Memory is a context provider.
- Vision, Speech, and Auditory are perception adapters.
- DeskPet, Emotion, and VirtualWorld are presentation adapters.
- MCP, Python, and Developer services are external or high-risk bridges.

## V2.10 Deliverables

- Alife capability governance catalog.
- DataAgent engineering scenario knowledge pack.
- DataAgent node-level tool-scope policy.
- Readiness and engineering-map gates.

## DataAgent Workflow Candidate Shape

DataAgent is suitable for agent/workflow treatment because its chain already has clear node responsibilities:

- `route_gate` checks whether the caller and route are allowed.
- `scenario_knowledge` maps business terms into controlled catalog hints.
- `query_planner` may call the model, but only to generate a QueryPlan.
- `query_plan_validator`, `sql_compiler`, `sql_safety`, and `read_only_execute` remain deterministic.
- `result_explainer` may call the model to summarize safe results.
- `evidence_audit` and `checkpoint_progress` create audit and progress evidence.
- `diagnostics_router` may interpret owner diagnostics intent, but it cannot plan or execute queries.

This shape gives a future LangGraph pilot a useful graph without moving safety authority out of C#.

## Later Work

V2.11 may wire DataAgent scenario-pack context into planner prompts and owner diagnostics. V2.12 may productize PostgreSQL-backed audit and checkpoint storage. V2.13 may add the disabled-by-default LangGraph sidecar contract. V2.14 may pilot a DataQueryGraph that calls C# safety services instead of bypassing them.
