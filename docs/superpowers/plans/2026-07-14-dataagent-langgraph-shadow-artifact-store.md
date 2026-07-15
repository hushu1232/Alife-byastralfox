# DataAgent LangGraph Shadow Artifact Store Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Persist C#-classified, sanitized LangGraph shadow outcomes in DataAgent SQLite with 90-day/20-per-scope retention and expose owner-only aggregate diagnostics.

**Architecture:** Add a dedicated artifact model, SQLite repository, and formatter under DataAgent. Only C# creates the model after validation/gating; the repository accepts no raw sidecar payload. Extend the existing DataAgent diagnostics command parser and runtime state with one aggregate string, preserving current owner gating and no raw artifact output.

**Tech Stack:** .NET 9, C#, Microsoft.Data.Sqlite, NUnit, existing QChat diagnostics contracts.

---

## File Structure

- Create: sources/Alife.Function/Alife.Function.DataAgent/DataAgentLangGraphShadowArtifactStore.cs — models, sanitizing repository, retention, aggregate formatter.
- Modify: sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs — add the dedicated SQLite table and indexes.
- Modify: sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs and IDataAgentStore.cs — expose artifact write/read only through C# store boundary.
- Create: Tests/Alife.Test.DataAgent/DataAgentLangGraphShadowArtifactStoreTests.cs — storage, sanitization, retention, write-failure tests.
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs — add the LangGraph topic and suffixes.
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs — render only aggregate LangGraph diagnostics from runtime state.
- Modify: Tests/Alife.Test.QChat/QChatDataAgentDiagnosticsCommandContractTests.cs and QChatDiagnosticsServiceTests.cs — command, owner, and redaction contracts.

### Task 1: Add sanitized artifact persistence and retention

**Files:**
- Create: sources/Alife.Function/Alife.Function.DataAgent/DataAgentLangGraphShadowArtifactStore.cs
- Modify: sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs
- Modify: sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs
- Modify: sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs
- Create: Tests/Alife.Test.DataAgent/DataAgentLangGraphShadowArtifactStoreTests.cs

- [ ] **Step 1: Write failing repository tests**

Create the test file with these named behaviors and fixed clock values:

~~~csharp
[Test] public void RecordPersistsAcceptedAndRejectedSafeMetadataOnly()
[Test] public void RecordRejectsRawSqlSecretsAndHiddenContextFromSummary()
[Test] public void RecordPrunesExpiredAndKeepsNewestTwentyPerSessionReplay()
[Test] public void AggregateReportsCountsLatestReasonAndRetentionWithoutSummary()
[Test] public void StoreWriteFailureDoesNotChangeSuppliedDecision()
~~~

Use a temporary SQLite file, call DataAgentSchemaInitializer.Initialize, and create records with:
- outcomes accepted, gate_rejected, protocol_rejected, timeout, fallback;
- session_id owner-session-1 and replay_id replay-1;
- fixed CreatedAt 2026-07-14 UTC;
- unsafe summaries containing SELECT, Bearer, password, connection_string, and hidden_context.

The first test must expect a missing DataAgentLangGraphShadowArtifactStore type.

- [ ] **Step 2: Run the RED test**

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter 'FullyQualifiedName~DataAgentLangGraphShadowArtifactStoreTests' -v:minimal
~~~

Expected: compilation fails because the artifact store and record types do not exist.

- [ ] **Step 3: Create the bounded artifact model and schema**

In DataAgentLangGraphShadowArtifactStore.cs define:

~~~csharp
public enum DataAgentLangGraphShadowOutcome
{
    Accepted,
    GateRejected,
    ProtocolRejected,
    Timeout,
    Fallback
}

public sealed record DataAgentLangGraphShadowArtifact(
    string ArtifactId,
    string SessionId,
    string ReplayId,
    DataAgentLangGraphShadowOutcome Outcome,
    string ReasonCode,
    string Summary,
    int ContextChars,
    bool DiffGatePassed,
    bool FallbackRequired,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record DataAgentLangGraphShadowArtifactAggregate(
    int Total,
    int Accepted,
    int GateRejected,
    int ProtocolRejected,
    int Timeout,
    int Fallback,
    string LatestReasonCode,
    DateTimeOffset? OldestCreatedAt,
    DateTimeOffset? NewestCreatedAt,
    int RetentionDays,
    int PerScopeLimit);
~~~

Set RetentionDays to 90 and PerScopeLimit to 20. Add a schema table named langgraph_shadow_artifact with bounded metadata columns only: artifact_id, session_id, replay_id, outcome, reason_code, summary, context_chars, diff_gate_passed, fallback_required, created_at, expires_at. Add an index on session_id, replay_id, created_at, artifact_id and an expiry index.

The Write method must:
1. reject or redact unsafe identifiers/reason/summary using the existing unsafe diagnostic detector plus SQL/secret markers;
2. insert through parameters in one SQLite transaction;
3. delete expires_at <= now;
4. delete rows beyond the newest twenty for the incoming session/replay scope, ordered by created_at DESC then artifact_id DESC;
5. return a bounded write result rather than throwing raw database exceptions.

The ReadAggregate method must never select or format Summary.

- [ ] **Step 4: Add the IDataAgentStore boundary**

Append these members to IDataAgentStore and implement direct delegation in SqliteDataAgentStore:

~~~csharp
DataAgentLangGraphShadowArtifactWriteResult RecordLangGraphShadowArtifact(
    DataAgentLangGraphShadowArtifact artifact,
    DateTimeOffset now);

DataAgentLangGraphShadowArtifactAggregate ReadLangGraphShadowArtifactAggregate(
    DateTimeOffset now);
~~~

Do not add any method usable by LangGraph. C# callers pass only already-classified metadata.

- [ ] **Step 5: Run the GREEN test**

Run the Step 2 command.

Expected: all five new repository tests pass, including retention, sanitization, and no-raw-summary aggregate behavior.

- [ ] **Step 6: Commit Task 1**

~~~powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentLangGraphShadowArtifactStore.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs Tests/Alife.Test.DataAgent/DataAgentLangGraphShadowArtifactStoreTests.cs
git commit -m "feat: store sanitized LangGraph shadow artifacts"
~~~

### Task 2: Provide owner-only aggregate diagnostics

**Files:**
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs
- Modify: Tests/Alife.Test.QChat/QChatDataAgentDiagnosticsCommandContractTests.cs
- Modify: Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs

- [ ] **Step 1: Write failing diagnostics tests**

Add LangGraph to the existing diagnostics topic case data. Add tests with these expected contracts:

~~~csharp
[Test] public void TryParseLangGraphDataAgentDiagnosticCommand()
[Test] public void TryHandleLangGraphDiagnosticsShowsOnlyAggregateSafeFieldsForOwner()
[Test] public void TryHandleLangGraphDiagnosticsReturnsUnavailableWithoutAggregate()
[Test] public void NonOwnerLangGraphDiagnosticsAreDroppedBeforeReply()
~~~

The owner aggregate fixture must contain:
~~~text
DataAgent LangGraph diagnostics
total=5
accepted=1
gate_rejected=1
protocol_rejected=1
timeout=1
fallback=1
latest_reason=sidecar_timeout
retention_days=90
per_scope_limit=20
~~~

Assert it does not contain SELECT, Bearer, password, connection_string, hidden_context, raw artifact summary, or local path text.

- [ ] **Step 2: Run the RED tests**

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QChatDataAgentDiagnosticsCommandContractTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatCommandAccessPolicyTests' -v:minimal
~~~

Expected: the langgraph command is unrecognized and the runtime-state field is missing.

- [ ] **Step 3: Add the minimal command and formatter route**

Add LangGraph to QChatDataAgentDiagnosticsTopic and accept exactly:
~~~text
diag langgraph
diagnostics langgraph
~~~

Add nullable RecentDataAgentLangGraph to QChatDiagnosticsRuntimeState. Extend BuildDataAgentDiagnosticsText with a LangGraph branch. The branch must sanitize cached/runtime text using the existing QChatDiagnosticTextSanitizer and return this exact unavailable fallback when empty:

~~~text
DataAgent LangGraph diagnostics
state=unavailable
reason=langgraph_artifact_aggregate_unavailable
~~~

Add the two command suffixes to SupportedDataAgentCommandSuffixes and the root diagnostics menu. Reuse existing owner command-access policy; do not add a new authorization path and do not publish a model-facing message.

- [ ] **Step 4: Run the GREEN diagnostics tests**

Run the Step 2 command again.

Expected: command parsing, owner-only output, no-data fallback, and non-owner drop all pass.

- [ ] **Step 5: Commit Task 2**

~~~powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs Tests/Alife.Test.QChat/QChatDataAgentDiagnosticsCommandContractTests.cs Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs
git commit -m "feat: expose LangGraph shadow aggregate diagnostics"
~~~

### Task 3: Verify no authority expansion

**Files:**
- Verify: all files from Tasks 1 and 2.
- Verify: sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarContract.cs
- Verify: sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowIntegration.cs

- [ ] **Step 1: Add an artifact-boundary regression test**

In DataAgentLangGraphShadowArtifactStoreTests.cs add a test that supplies a rejected manual-shadow result marked CallsSidecar=false, AgentAdvisoryOnly=true, and CSharpValidationAuthority=true. Persist only the C#-derived outcome/reason metadata. Assert the stored aggregate contains no authority claim or raw advisory field.

- [ ] **Step 2: Run focused DataAgent and QChat suites**

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter 'FullyQualifiedName~DataAgentLangGraphShadowArtifactStoreTests|FullyQualifiedName~DataAgentV40RealLangGraphManualShadowIntegrationTests|FullyQualifiedName~DataAgentV41RealLangGraphManualShadowContextBudgetTests' -v:minimal
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QChatDataAgentDiagnosticsCommandContractTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatCommandAccessPolicyTests' -v:minimal
~~~

Expected: all selected tests pass and no test permits SQL, tool, audit, checkpoint, or QQ authority to LangGraph.

- [ ] **Step 3: Run full affected suites and patch checks**

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
git diff alife-byastralfox/master...HEAD --check
~~~

Expected: both suites pass and diff check reports no whitespace errors.

- [ ] **Step 4: Commit verification test**

~~~powershell
git add Tests/Alife.Test.DataAgent/DataAgentLangGraphShadowArtifactStoreTests.cs
git commit -m "test: lock LangGraph artifact authority boundary"
~~~
