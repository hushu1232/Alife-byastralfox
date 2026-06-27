# DataAgent v1.1 Planner And Tool Integration Design

## Goal

Expand DataAgent v1 from an internally callable NL2SQL core into an Alife-registered, tool-callable capability while preserving the QueryPlan safety boundary. v1.1 introduces a pluggable planner interface, keeps the deterministic planner as the default, adds a DataAgent tool handler, and promotes planner/tool integration into readiness.

## Current Registration Status

DataAgent v1 is registered as an engineering capability:

- `Alife.slnx` includes `Sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj`.
- `Alife.slnx` includes `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`.
- `tools/check-dataagent-readiness.ps1` exists and verifies the core chain.
- `tools/check-qchat-engineering-map.ps1` declares `DataAgent readiness script` as required.
- `Tests/Alife.Test.DataAgent` proves catalog, QueryPlan validation, SQL safety, SQLite execution, audit, service summaries, context output, and readiness.

DataAgent v1 is not yet registered as a runtime/tool capability:

- There is no `DataAgentModuleService` subclass registered through the Alife module lifecycle.
- There is no XML function handler exposed to FunctionCaller.
- There is no tool prompt injected through `InteractiveModule.Prompt(string)`.
- There is no tested QChat-facing tool surface.

v1.1 is responsible for closing that runtime registration gap.

## Non-Goals

v1.1 does not add PostgreSQL, Vue ChatBI, live LLM planning, arbitrary SQL execution, report publishing, file mutation, or external data crawling. Those remain deferred to later versions.

## Recommended Approach

Use a small interface-first upgrade:

```text
QChat/tool request
 -> DataAgentToolHandler
 -> DataAgentService
 -> IDataAgentQueryPlanner
 -> QueryPlan validator
 -> SQL compiler
 -> SQL safety validator
 -> SQLite executor
 -> audit log
 -> data_agent_context
```

The deterministic planner remains the default so existing behavior stays stable. The interface gives v2 and later versions a controlled place to add PostgreSQL-aware or LLM-assisted planning without changing the service safety chain.

## Planner Interface

Add:

```csharp
public interface IDataAgentQueryPlanner
{
    DataAgentQueryPlan Plan(DataAgentQueryRequest request);
}
```

Add:

```csharp
public sealed record DataAgentQueryRequest(
    string Question,
    string Role,
    string Locale,
    bool AllowLiveSources);
```

The request shape intentionally includes role, locale, and live-source allowance even though v1.1 will keep deterministic behavior. These fields define the future expansion boundary without adding live dependencies now.

## Deterministic Planner

Move the current private `DataAgentService.ResolvePlan(question)` rules into:

```csharp
public sealed class DeterministicDataAgentQueryPlanner : IDataAgentQueryPlanner
```

The default planner must preserve v1 fixture behavior:

- QChat/TTS/vision readiness questions map to `runtime_readiness_check`.
- Runtime readiness required-evidence questions map to `engineering_gate`.
- Test result questions map to `test_run`.
- DataAgent/NL2SQL document questions map to `document_index`.
- Unknown project-state questions default to missing required gates.

## Service Injection

Change `DataAgentService` so its default constructor remains compatible:

```csharp
public DataAgentService(string databasePath)
    : this(databasePath, new DeterministicDataAgentQueryPlanner())
{
}
```

Add injectable constructor:

```csharp
public DataAgentService(string databasePath, IDataAgentQueryPlanner planner)
```

`DataAgentService.Answer(question)` should create a `DataAgentQueryRequest` and call `planner.Plan(request)`. The service remains responsible for validation, SQL compilation, SQL safety, execution, audit, and context output.

## Safety Invariant

v1.1 must prove that planner injection does not weaken safety.

If an injected planner returns an invalid QueryPlan, the service must reject it through `DataAgentQueryPlanValidator`, audit the rejection, and return:

```text
[data_agent_context]
sql_status=rejected
rejected_reason=unsupported_operator:starts_with
[/data_agent_context]
```

No planner implementation may bypass validation or provide executable SQL directly.

## Tool Handler

Add a tool-facing handler:

```csharp
public sealed class DataAgentToolHandler
{
    [XmlFunction(FunctionMode.OneShot, name: "dataagent_query")]
    public string Query(string question)
}
```

The handler should call `DataAgentService.Answer(question)` and return the `DataAgentAnswer.Context` block. The handler must not expose arbitrary SQL execution.

The XML tool name should stay explicit and narrow:

```text
dataagent_query
```

Allowed input:

```text
question
```

Returned output:

```text
data_agent_context
```

## Runtime Module Service

Add a lightweight module service:

```csharp
public sealed class DataAgentModuleService : InteractiveModule<DataAgentModuleService>
```

Responsibilities:

- Initialize the local DataAgent SQLite store.
- Import deterministic engineering fixture data.
- Create `DataAgentService`.
- Register `DataAgentToolHandler` with FunctionCaller when available.
- Inject a concise prompt describing the `dataagent_query` tool.

The prompt must explain that DataAgent results are evidence-backed but still dynamic data context. It must not tell the model to treat DataAgent output as stable persona or system authority.

## Readiness Upgrade

Extend DataAgent readiness with these checks:

```text
PlannerInterfacePresent
DeterministicPlannerPassesFixtures
ServiceUsesInjectedPlanner
UnsafePlannerOutputRejected
ToolHandlerReturnsDataAgentContext
```

`tools/check-dataagent-readiness.ps1` should include markers for the planner interface and tool handler.

The engineering map should gain one required entry:

```text
DataAgent planner/tool integration
```

Expected engineering-map direction:

```text
32 required passed -> 33 required passed
0 required missing
```

If implementation folds the new markers into the existing DataAgent readiness script entry instead of a separate entry, the required count may stay 32. The preferred design is a separate required entry because runtime registration is a distinct capability from core readiness.

## Tests

Add focused tests in `Tests/Alife.Test.DataAgent`:

- `DataAgentPlannerTests`
  - deterministic planner returns the same QueryPlans as v1.
  - request role/locale/live flags do not change default deterministic behavior.

- `DataAgentServicePlannerInjectionTests`
  - service uses an injected planner.
  - invalid planner output is rejected.
  - rejected output is audited.
  - rejected context uses `sql_status=rejected`.

- `DataAgentToolHandlerTests`
  - handler returns `[data_agent_context]`.
  - handler does not expose raw SQL-only output.
  - handler preserves evidence when available.

- `DataAgentV11ReadinessTests`
  - readiness includes planner/tool checks.
  - engineering map declares planner/tool integration as required.

## Acceptance Criteria

v1.1 is complete when:

- `IDataAgentQueryPlanner` and `DataAgentQueryRequest` exist.
- `DeterministicDataAgentQueryPlanner` owns the former service-local planning rules.
- `DataAgentService` supports planner injection without changing v1 public behavior.
- unsafe injected planner output is rejected and audited.
- `DataAgentToolHandler` exposes only natural-language query input and data context output.
- `DataAgentModuleService` registers the handler with FunctionCaller when available.
- readiness verifies planner/tool integration.
- engineering map has no missing required checks.
- full `.NET 9` build and tests pass.

## Distance To V2

V2 can start after v1.1 passes and the tool boundary is stable. The minimum gate before V2 is:

```text
planner abstraction stable
tool handler stable
unsafe planner rejection tested
runtime registration tested
readiness required
engineering map green
full test suite green
```

After that, V2 can add PostgreSQL as an optional live connector without disturbing the v1/v1.1 SQLite required harness.

V2 should begin with:

```text
IDataSourceConnector
ISqlDialect
PostgresDataSourceConnector
PostgresSchemaIntrospector
PostgresReadOnlyQueryExecutor
PostgresPermissionMapper
```

PostgreSQL should start optional/live, then become required only after its bootstrap, read-only role setup, schema introspection, permission checks, and live diagnostics are stable.
