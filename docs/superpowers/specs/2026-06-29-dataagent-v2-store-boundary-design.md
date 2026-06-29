# DataAgent V2.0 Store Boundary Design

## Goal

DataAgent V2.0 introduces a provider-neutral store boundary so DataAgent can keep the current SQLite behavior while adding PostgreSQL as the durable V2 persistence provider. The version should move query execution, schema initialization, fixture import, query audit, and Tool Broker audit persistence behind one explicit store contract without changing the visible DataAgent NL2SQL behavior.

V2.0 is a storage boundary release. It is not a LangGraph release, not a multi-agent runtime release, and not a new analytics-domain release.

## Current Baseline

DataAgent V1.x is already functional and required-gated:

- `DataAgentService` validates planner output, compiles SQL, runs read-only queries, builds `[data_agent_context]`, and records accepted or rejected query audit records.
- `DataAgentQueryExecutor` executes compiled SQL directly against SQLite.
- `DataAgentSchemaInitializer` creates the SQLite schema.
- `DataAgentFixtureImporter` imports local engineering evidence fixtures into SQLite.
- `DataAgentAuditLog` writes query audit records to SQLite.
- `DataAgentToolBrokerAuditLog` writes Tool Broker route and execution evidence to SQLite.
- `DataAgentModuleService` initializes the SQLite file and registers query and analysis capability providers.
- `tools/check-dataagent-readiness.ps1` reports `45 required passed, 0 required missing`.
- `tools/check-qchat-engineering-map.ps1` reports `42 required passed, 0 required missing, 0 optional present, 0 optional missing`.

The current weakness is not capability coverage. The weakness is that storage behavior is spread across several SQLite-specific classes and `DataAgentService` still depends on a database path. That makes PostgreSQL migration risky unless V2 first defines a stable store contract.

## Non-Goals

V2.0 does not introduce LangGraph, a Python sidecar, multi-agent orchestration, a supervisor runtime, a plugin marketplace, chart rendering, report publishing, external data ingestion, or new business query domains.

V2.0 does not remove SQLite. SQLite remains the default local V1.x-compatible provider.

V2.0 does not relax SQL safety, Tool Broker route gating, owner/private constraints, prompt-leak prevention, analysis-session state transitions, or DataAgent capability provider boundaries.

V2.0 does not make live PostgreSQL availability required for the default test suite. PostgreSQL live tests must be skipped unless an explicit environment variable is present.

## Design Decision

Use a conservative contract-first migration:

```text
V1.x behavior
  DataAgentService(databasePath)
  -> SQLite-specific helper classes

V2.0 behavior
  DataAgentService(IDataAgentStore, planner)
  -> SqliteDataAgentStore by default
  -> PostgresDataAgentStore when configured
```

The existing `DataAgentService(string databasePath)` constructor should remain and internally create `SqliteDataAgentStore`. This keeps current callers stable while allowing new V2 callers to inject an `IDataAgentStore`.

## Architecture

```text
DataAgentModuleService
  -> choose store provider
  -> store.Initialize()
  -> store.ImportFixtures() for local seeded evidence
  -> DataAgentService(store, planner)
  -> capability providers

DataAgentService
  -> planner.Plan(request)
  -> validate envelope
  -> validate plan against DataAgentCatalog
  -> compile with DataAgentSqlCompiler
  -> safety validate SQL
  -> store.Query(compiledSql)
  -> build DataAgent context
  -> store.RecordAccepted(...) or store.RecordRejected(...)

Tool Broker audit
  -> store.RecordToolBrokerAudit(...)
  -> store.ReadToolBrokerAudit()
```

`DataAgentService` should keep owning planner validation, plan validation, SQL compilation, SQL safety validation, result explanation, and context formatting. The store should own persistence and query execution, not prompt or planner behavior.

## Store Contract

The first V2 contract should be narrow and based on current behavior:

```csharp
public interface IDataAgentStore
{
    string ProviderName { get; }
    void Initialize();
    void ImportFixtures();
    DataAgentQueryResult Query(DataAgentCompiledSql compiledSql);
    void RecordAccepted(DataAgentAcceptedAuditInput input);
    void RecordRejected(DataAgentRejectedAuditInput input);
    IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit();
    void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record);
    IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit();
}
```

The contract intentionally accepts `DataAgentCompiledSql`, not `DataAgentQueryPlan`. `DataAgentService` should remain the owner of query-plan validation and SQL compilation so provider implementations do not duplicate planner logic.

`DataAgentAcceptedAuditInput` and `DataAgentRejectedAuditInput` should be small records that preserve the existing query-audit fields. They should not include raw prompt text beyond the user question already stored in the existing V1.x audit table.

## SQLite Provider

`SqliteDataAgentStore` should wrap existing SQLite behavior:

- `Initialize()` delegates to `DataAgentSchemaInitializer`.
- `ImportFixtures()` delegates to `DataAgentFixtureImporter`.
- `Query()` delegates to the current `DataAgentQueryExecutor`.
- query audit methods delegate to the current `DataAgentAuditLog`.
- Tool Broker audit methods delegate to the current `DataAgentToolBrokerAuditLog`.

The first implementation should prefer delegation over moving all SQL at once. This keeps the V2.0 diff smaller and preserves V1.x behavior.

SQLite compatibility requirements:

- `DataAgentService(string databasePath)` still works.
- `DataAgentModuleService` can still create the default local SQLite file under `AppContext.BaseDirectory`.
- all existing DataAgent tests continue to pass without PostgreSQL.
- existing readiness scripts continue to report `0 required missing`.

## PostgreSQL Provider

`PostgresDataAgentStore` should implement the same contract behind a PostgreSQL connection string:

```csharp
public sealed class PostgresDataAgentStore(string connectionString) : IDataAgentStore
```

The provider should support:

- schema initialization for the existing DataAgent tables;
- read-only execution of compiled, safety-validated SQL;
- accepted and rejected query audit writes;
- Tool Broker audit writes and reads;
- fixture import for local engineering evidence when a test or bootstrap path requests it.

The PostgreSQL provider should not receive raw natural-language questions and generate SQL by itself. It receives already compiled and safety-validated SQL from `DataAgentService`.

PostgreSQL-specific behavior must stay behind the provider. No normal DataAgent caller should import PostgreSQL types directly.

## Provider Selection

V2.0 should keep SQLite as the default provider. PostgreSQL should be opt-in through configuration, not automatic discovery.

Recommended environment variables:

```text
ALIFE_DATAAGENT_STORE_PROVIDER=sqlite|postgres
ALIFE_DATAAGENT_SQLITE_PATH=<local sqlite path>
ALIFE_DATAAGENT_POSTGRES_CONNECTION=<runtime connection string>
ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION=<live test connection string>
```

Default behavior:

- missing `ALIFE_DATAAGENT_STORE_PROVIDER` means SQLite;
- `sqlite` requires a SQLite path or uses the existing module default;
- `postgres` requires `ALIFE_DATAAGENT_POSTGRES_CONNECTION`;
- live PostgreSQL tests require `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`;
- absence of the live test variable skips live PostgreSQL tests, not fails them.

## Data Flow

Accepted query flow:

```text
question
 -> DataAgentService
 -> planner
 -> DataAgentQueryPlanValidator
 -> DataAgentSqlCompiler
 -> DataAgentSqlSafetyValidator
 -> IDataAgentStore.Query(compiledSql)
 -> DataAgentResultSummarizer
 -> DataAgentResultExplainer
 -> DataAgentContextProvider
 -> IDataAgentStore.RecordAccepted(...)
 -> DataAgentAnswer
```

Rejected query flow:

```text
invalid planner output or unsafe SQL
 -> DataAgentService rejects before store.Query
 -> IDataAgentStore.RecordRejected(...)
 -> DataAgentContextProvider.BuildRejected(...)
 -> DataAgentAnswer(validated: false)
```

Clarification flow:

```text
planner asks for clarification
 -> DataAgentService sanitizes clarification
 -> IDataAgentStore.RecordRejected(reason: needs_clarification)
 -> DataAgentContextProvider.BuildClarification(...)
```

Tool Broker audit flow:

```text
Tool Broker decision or XML execution audit
 -> DataAgent module audit adapter
 -> IDataAgentStore.RecordToolBrokerAudit(...)
 -> owner diagnostics and readiness can read evidence
```

## Schema Strategy

V2.0 should keep the logical table shape stable across SQLite and PostgreSQL:

- `engineering_gate`
- `runtime_readiness_check`
- `module_capability`
- `test_run`
- `document_index`
- `query_audit`
- `tool_broker_audit`

Provider implementations may use provider-specific SQL syntax, but public fields and semantics should match. PostgreSQL IDs can use identity columns; SQLite can keep `INTEGER PRIMARY KEY AUTOINCREMENT`.

The design does not require a full migration framework in V2.0. The first version can use explicit initializer classes for each provider. A later V2.x can introduce migrations if schema churn grows.

## Safety

The store boundary must preserve these invariants:

- only `DataAgentService` compiles and safety-validates SQL;
- store providers execute only compiled SQL objects created after validation;
- no provider receives hidden prompt text or XML manuals;
- PostgreSQL provider cannot bypass Tool Broker policy;
- live PostgreSQL tests are environment-gated;
- default readiness does not depend on a network database;
- query audit and Tool Broker audit remain bounded and structured.

PostgreSQL connection strings must never be committed to the repository or echoed in readiness output.

## Error Handling

Provider selection errors should fail fast:

- unknown provider name;
- `postgres` selected without a connection string;
- empty SQLite path when SQLite is explicitly configured;
- schema initialization failure;
- store query failure.

Default local operation should not fail because PostgreSQL is not installed or not reachable. PostgreSQL failures only fail when PostgreSQL is explicitly selected or when a live PostgreSQL test is intentionally enabled.

## Testing Strategy

V2.0 should use TDD with contract tests first:

- `SqliteDataAgentStore` implements schema initialization and query execution.
- `SqliteDataAgentStore` preserves accepted/rejected query audit behavior.
- `SqliteDataAgentStore` preserves Tool Broker audit behavior.
- `DataAgentService(string databasePath)` remains backward compatible.
- `DataAgentService(IDataAgentStore, planner)` uses the injected store.
- provider selection defaults to SQLite.
- provider selection rejects invalid PostgreSQL configuration.
- PostgreSQL live tests are skipped without `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION`.
- PostgreSQL live tests initialize schema and execute a read-only query when the env var is present.

Required verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

## Readiness Gates

V2.0 should add required readiness evidence for:

- `DataAgentStoreBoundaryPresent`
- `SqliteStoreCompatibilityPresent`
- `PostgresStoreProviderPresent`
- `PostgresLiveTestsEnvironmentGated`
- `DataAgentServiceUsesStoreBoundary`

Default readiness should continue to pass without live PostgreSQL.

The QChat engineering map should not require PostgreSQL runtime availability. It may require the store boundary as Harness evidence once the DataAgent readiness gate owns the detailed provider checks.

## Rollout

1. Add the store contract and contract tests.
2. Wrap existing SQLite behavior in `SqliteDataAgentStore`.
3. Update `DataAgentService` to accept `IDataAgentStore` while preserving path constructors.
4. Add provider selection and tests.
5. Add `PostgresDataAgentStore` behind the same contract.
6. Add environment-gated live PostgreSQL tests.
7. Update readiness scripts.
8. Run full verification and merge.

This order keeps the system usable after each task and avoids a high-risk storage rewrite.

## Acceptance Criteria

V2.0 is ready to merge when:

- `IDataAgentStore` exists and is used by `DataAgentService`;
- SQLite behavior remains the default and all V1.x DataAgent tests pass;
- PostgreSQL provider exists behind the same contract;
- live PostgreSQL tests are skipped unless `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is set;
- DataAgent readiness reports `0 required missing`;
- QChat engineering map reports `0 required missing`;
- no LangGraph runtime, Python sidecar, supervisor runtime, or new query domain is introduced;
- full solution tests pass under the local .NET 9 SDK.

## Future Work

V2.x can add schema migrations, richer PostgreSQL indexing, and durable DataAgent analysis-session storage after the initial store boundary is stable.

V2.5 can introduce the LangGraph sidecar pilot for DataAgent analysis workflows after PostgreSQL-backed state and audit are available.

V3 can evolve into supervisor-controlled tool governance only after V2 storage and V2.5 linked-run evidence are stable.
