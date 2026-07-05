using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphSidecarContractTests
{
    [Test]
    public void OptionsDefaultToDisabledAndParseOnlyExplicitTrueValues()
    {
        string?[] disabledValues =
        [
            null,
            string.Empty,
            "   ",
            "false",
            "FALSE",
            "0",
            "no",
            "unexpected"
        ];

        string[] enabledValues =
        [
            "true",
            "TRUE",
            "1",
            "yes",
            " YES "
        ];

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarOptions.Disabled.Enabled, Is.False);
            Assert.That(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED"));

            foreach (string? value in disabledValues)
            {
                Assert.That(DataAgentGraphSidecarOptions.FromValue(value).Enabled, Is.False, $"Expected disabled for '{value}'.");
            }

            foreach (string value in enabledValues)
            {
                Assert.That(DataAgentGraphSidecarOptions.FromValue(value).Enabled, Is.True, $"Expected enabled for '{value}'.");
            }
        });
    }

    [Test]
    [NonParallelizable]
    public void ExplicitEnableDoesNotCreateRuntime()
    {
        string? previous = Environment.GetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, null);
            DataAgentGraphSidecarOptions defaultOptions = DataAgentGraphSidecarOptions.FromEnvironment();

            Environment.SetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, "true");
            DataAgentGraphSidecarOptions enabledOptions = DataAgentGraphSidecarOptions.FromEnvironment();

            Assert.Multiple(() =>
            {
                Assert.That(defaultOptions.Enabled, Is.False);
                Assert.That(enabledOptions.Enabled, Is.True);
                Assert.That(DataAgentGraphSidecarContract.IsRuntimeAvailable, Is.False);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataAgentGraphSidecarOptions.EnabledEnvironmentVariable, previous);
        }
    }

    [Test]
    public void DefaultPolicyAllowsIntentAndForbidsAuthoritySurfaces()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();

        Assert.Multiple(() =>
        {
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent), Is.True);
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.RequestCSharpSafetyService), Is.True);
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.ReturnBoundedTrace), Is.True);
            Assert.That(policy.Allows(DataAgentGraphSidecarAuthority.ReportDeterministicFallback), Is.True);

            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeDataset), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeField), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeOperator), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.AuthorizeLimit), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.ProvideExecutableSql), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.ExecuteSql), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.DecideToolRoute), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.MutateCheckpoint), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteEvidence), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteAudit), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteProgress), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.WriteDiagnostics), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.SendVisibleQChatText), Is.True);
            Assert.That(policy.Forbids(DataAgentGraphSidecarAuthority.OwnQqIngress), Is.True);

            Assert.That(policy.NoSqlAuthority, Is.True);
            Assert.That(policy.NoToolRouteAuthority, Is.True);
            Assert.That(policy.NoCheckpointAuthority, Is.True);
            Assert.That(policy.NoEvidenceAuthority, Is.True);
            Assert.That(policy.NoVisibleTextAuthority, Is.True);
        });
    }

    [Test]
    public void DefaultPolicyClassifiesEveryAuthorityAsExactlyAllowedOrForbidden()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();

        Assert.Multiple(() =>
        {
            foreach (DataAgentGraphSidecarAuthority authority in Enum.GetValues<DataAgentGraphSidecarAuthority>())
            {
                bool isAllowed = policy.Allows(authority);
                bool isForbidden = policy.Forbids(authority);

                Assert.That(isAllowed ^ isForbidden, Is.True, $"{authority} must be exactly one of allowed or forbidden.");
            }
        });
    }

    [Test]
    public void RequestValidationRequiresBoundedIdentity()
    {
        DataAgentGraphSidecarRequest valid = NewRequest(
            workflowId: "wf-1",
            sessionId: "session-1",
            allowedCapabilityNames: ["DataAgentQueryPlanner", "DataAgentQueryPlanValidator"]);
        DataAgentGraphSidecarRequest blankWorkflow = valid with { WorkflowId = " " };
        DataAgentGraphSidecarRequest blankSession = valid with { SessionId = "" };
        DataAgentGraphSidecarRequest blankTrace = valid with { TraceId = " " };
        DataAgentGraphSidecarRequest blankCapability = valid with { AllowedCapabilityNames = ["DataAgentQueryPlanner", " "] };
        DataAgentGraphSidecarRequest overlongWorkflow = valid with { WorkflowId = new string('w', 129) };
        DataAgentGraphSidecarRequest overlongSession = valid with { SessionId = new string('s', 129) };
        DataAgentGraphSidecarRequest overlongCaller = valid with { CallerId = new string('c', 129) };
        DataAgentGraphSidecarRequest overlongQuestion = valid with { Question = new string('q', 2049) };
        DataAgentGraphSidecarRequest overlongScenarioContext = valid with { ScenarioContext = new string('x', 4097) };
        DataAgentGraphSidecarRequest overlongTrace = valid with { TraceId = new string('t', 129) };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(valid), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankWorkflow), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankSession), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankTrace), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankCapability), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongWorkflow), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongSession), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongCaller), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongQuestion), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongScenarioContext), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongTrace), Is.False);
        });
    }

    [Test]
    public void RequestValidationBoundsOptionalCheckpointFields()
    {
        DataAgentGraphSidecarRequest valid = NewRequest(
            workflowId: "wf-1",
            sessionId: "session-1",
            allowedCapabilityNames: ["DataAgentQueryPlanner"]);
        DataAgentGraphSidecarRequest absentCheckpointFields = valid with
        {
            CheckpointSessionId = null,
            CheckpointStatus = null
        };
        DataAgentGraphSidecarRequest overlongCheckpointSessionId = valid with
        {
            CheckpointSessionId = new string('c', 129)
        };
        DataAgentGraphSidecarRequest overlongCheckpointStatus = valid with
        {
            CheckpointStatus = new string('s', 65)
        };
        DataAgentGraphSidecarRequest blankCheckpointSessionId = valid with
        {
            CheckpointSessionId = "   "
        };
        DataAgentGraphSidecarRequest blankCheckpointStatus = valid with
        {
            CheckpointStatus = "   "
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(valid), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(absentCheckpointFields), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongCheckpointSessionId), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongCheckpointStatus), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankCheckpointSessionId), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankCheckpointStatus), Is.False);
        });
    }

    [Test]
    public void RequestValidationRequiresBoundedAllowlistedCapabilities()
    {
        DataAgentGraphSidecarRequest valid = NewRequest(
            workflowId: "wf-1",
            sessionId: "session-1",
            allowedCapabilityNames: ["DataAgentQueryPlanner"]);
        DataAgentGraphSidecarRequest emptyCapabilities = valid with { AllowedCapabilityNames = [] };
        DataAgentGraphSidecarRequest tooManyCapabilities = valid with
        {
            AllowedCapabilityNames = Enumerable.Repeat("DataAgentQueryPlanner", 17).ToArray()
        };
        DataAgentGraphSidecarRequest overlongCapability = valid with { AllowedCapabilityNames = [new string('c', 129)] };
        DataAgentGraphSidecarRequest forbiddenCapability = valid with { AllowedCapabilityNames = ["ExecuteSqlAsync"] };
        DataAgentGraphSidecarRequest unknownCapability = valid with { AllowedCapabilityNames = ["FutureUnreviewedCapability"] };
        DataAgentGraphSidecarRequest tooManyNodeKinds = valid with
        {
            AllowedNodeKinds = Enumerable.Repeat(DataAgentGraphSidecarNodeKind.QueryPlanner, 17).ToArray()
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(valid), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(emptyCapabilities), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(tooManyCapabilities), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(overlongCapability), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(forbiddenCapability), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(unknownCapability), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(tooManyNodeKinds), Is.False);
        });
    }

    [Test]
    public void RequestValidationFailsClosedForNullInputAndLists()
    {
        DataAgentGraphSidecarRequest valid = NewRequest(
            workflowId: "wf-1",
            sessionId: "session-1",
            allowedCapabilityNames: ["DataAgentQueryPlanner"]);

        AssertInvalidWithoutThrowing(() => DataAgentGraphSidecarContract.IsRequestValid(null!));
        AssertInvalidWithoutThrowing(() => DataAgentGraphSidecarContract.IsRequestValid(valid with { AllowedNodeKinds = null! }));
        AssertInvalidWithoutThrowing(() => DataAgentGraphSidecarContract.IsRequestValid(valid with { AllowedCapabilityNames = null! }));
    }

    [Test]
    public void ResponseValidationRejectsSqlAndForbiddenAuthorityClaims()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanValidator",
            trace: ["QueryPlanner:Proposed", "QueryPlanValidation:DelegatedToCSharp"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        DataAgentGraphSidecarResponse sqlTrace = safe with
        {
            Trace = ["SELECT * FROM document_index"]
        };
        DataAgentGraphSidecarResponse sqlCapability = safe with
        {
            RequestedCapabilityName = "ExecuteSql"
        };
        DataAgentGraphSidecarResponse authorityClaim = safe with
        {
            ClaimedAuthorities = [DataAgentGraphSidecarAuthority.ExecuteSql]
        };
        DataAgentGraphSidecarResponse visibleTextClaim = safe with
        {
            ClaimedAuthorities = [DataAgentGraphSidecarAuthority.SendVisibleQChatText]
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(safe, policy), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(sqlTrace, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(sqlCapability, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(authorityClaim, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(visibleTextClaim, policy), Is.False);
        });
    }

    [Test]
    public void ResponseValidationRejectsEmptyTooManyAndDuplicateAuthorityClaims()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        DataAgentGraphSidecarResponse emptyAuthorities = safe with { ClaimedAuthorities = [] };
        DataAgentGraphSidecarResponse tooManyAuthorities = safe with
        {
            ClaimedAuthorities =
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService,
                DataAgentGraphSidecarAuthority.ReturnBoundedTrace,
                DataAgentGraphSidecarAuthority.ReportDeterministicFallback,
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent
            ]
        };
        DataAgentGraphSidecarResponse duplicateAuthorities = safe with
        {
            ClaimedAuthorities =
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent
            ]
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(safe, policy), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(emptyAuthorities, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(tooManyAuthorities, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(duplicateAuthorities, policy), Is.False);
        });
    }

    [Test]
    public void ResponseValidationFailsClosedForNullInputPolicyAndLists()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);

        AssertInvalidWithoutThrowing(() => DataAgentGraphSidecarContract.IsResponseSafe(null!, policy));
        AssertInvalidWithoutThrowing(() => DataAgentGraphSidecarContract.IsResponseSafe(safe, null!));
        AssertInvalidWithoutThrowing(() => DataAgentGraphSidecarContract.IsResponseSafe(safe with { Trace = null! }, policy));
        AssertInvalidWithoutThrowing(() => DataAgentGraphSidecarContract.IsResponseSafe(safe with { ClaimedAuthorities = null! }, policy));
    }

    [Test]
    public void ResponseValidationRejectsCapabilitiesOutsideDefaultAllowlist()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        DataAgentGraphSidecarResponse unknownCapability = safe with { RequestedCapabilityName = "FutureUnreviewedCapability" };
        DataAgentGraphSidecarResponse executeSqlAsync = safe with { RequestedCapabilityName = "ExecuteSqlAsync" };
        DataAgentGraphSidecarResponse storeQueryAsync = safe with { RequestedCapabilityName = "IDataAgentStore.QueryAsync" };
        DataAgentGraphSidecarResponse qualifiedExecutor = safe with
        {
            RequestedCapabilityName = "Alife.Function.DataAgent.DataAgentQueryExecutor"
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(safe, policy), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(unknownCapability, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(executeSqlAsync, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(storeQueryAsync, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(qualifiedExecutor, policy), Is.False);
        });
    }

    [Test]
    public void ResponseValidationRequiresRequestedCapabilityFromOriginatingRequest()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarRequest request = NewRequest(
            workflowId: "wf-1",
            sessionId: "session-1",
            allowedCapabilityNames: ["DataAgentQueryPlanner"]);
        DataAgentGraphSidecarRequest invalidRequest = request with { AllowedCapabilityNames = [] };
        DataAgentGraphSidecarResponse allowedResponse = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        DataAgentGraphSidecarResponse disallowedResponse = allowedResponse with
        {
            RequestedCapabilityName = "DataAgentQueryPlanValidator"
        };
        DataAgentGraphSidecarResponse wrongWorkflowResponse = allowedResponse with
        {
            WorkflowId = "wf-2"
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(allowedResponse, policy, request), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(disallowedResponse, policy, request), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(wrongWorkflowResponse, policy, request), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(allowedResponse, policy, invalidRequest), Is.False);
        });
    }

    [Test]
    public void ResponseValidationAllowsRequestBoundResponsesWithoutCapabilityRequests()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarRequest request = NewRequest(
            workflowId: "wf-1",
            sessionId: "session-1",
            allowedCapabilityNames: ["DataAgentQueryPlanner"]);
        DataAgentGraphSidecarResponse orchestrationIntent = NewResponse(
            requestedCapabilityName: null,
            requiresCSharpSafetyService: false,
            trace: ["ScenarioContext:IntentOnly"],
            claimedAuthorities: [DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent]);
        DataAgentGraphSidecarResponse deterministicFallback = NewResponse(
            requestedCapabilityName: null,
            requiresCSharpSafetyService: false,
            trace: ["Terminal:FallbackOnly"],
            claimedAuthorities: [DataAgentGraphSidecarAuthority.ReportDeterministicFallback]);
        DataAgentGraphSidecarResponse boundedTrace = NewResponse(
            requestedCapabilityName: null,
            requiresCSharpSafetyService: false,
            trace: ["Diagnostics:TraceOnly"],
            claimedAuthorities: [DataAgentGraphSidecarAuthority.ReturnBoundedTrace]);

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(orchestrationIntent, policy, request), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(deterministicFallback, policy, request), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(boundedTrace, policy, request), Is.True);
        });
    }

    [Test]
    public void ResponseValidationRequiresCapabilityRequestsToUseCSharpSafetyAuthority()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse missingSafetyRequirement = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            requiresCSharpSafetyService: false,
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        DataAgentGraphSidecarResponse missingSafetyAuthority = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities: [DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent]);

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(missingSafetyRequirement, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(missingSafetyAuthority, policy), Is.False);
        });
    }

    [Test]
    public void ResponseValidationDetectsSqlAcrossNewlineAndTabButAllowsBenignSemicolon()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        DataAgentGraphSidecarResponse sqlWithNewline = safe with { Trace = ["SELECT\n* FROM document_index"] };
        DataAgentGraphSidecarResponse sqlWithTab = safe with { Trace = ["select\t* from document_index"] };
        DataAgentGraphSidecarResponse benignSemicolon = safe with { Trace = ["Planner proposed; C# validator decides"] };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(sqlWithNewline, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(sqlWithTab, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(benignSemicolon, policy), Is.True);
        });
    }

    [Test]
    public void ResponseValidationDetectsObviousExecutableSqlKeywordsInTraceAndMessage()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        string[] executableSqlTexts =
        [
            "CREATE INDEX idx_t_id ON t(id)",
            "CREATE TABLE sidecar_probe (id integer)",
            "CREATE TEMP TABLE t (id int)",
            "WITH recent AS (VALUES (1))",
            "WITH RECURSIVE recent AS (VALUES (1))",
            "EXECUTE refresh_sidecar_probe",
            "CALL refresh_sidecar_probe()",
            "MERGE INTO target USING source ON target.id = source.id",
            "GRANT USAGE ON SCHEMA public TO analyst",
            "REVOKE USAGE ON SCHEMA public FROM analyst",
            "PRAGMA table_info(document_index)",
            "BEGIN TRANSACTION",
            "COMMIT",
            "ROLLBACK"
        ];

        Assert.Multiple(() =>
        {
            foreach (string executableSqlText in executableSqlTexts)
            {
                Assert.That(
                    DataAgentGraphSidecarContract.IsResponseSafe(safe with { Trace = [executableSqlText] }, policy),
                    Is.False,
                    $"Trace should reject SQL text: {executableSqlText}");
                Assert.That(
                    DataAgentGraphSidecarContract.IsResponseSafe(safe with { Message = executableSqlText }, policy),
                    Is.False,
                    $"Message should reject SQL text: {executableSqlText}");
            }
        });
    }

    [Test]
    public void ResponseValidationRequiresBoundedTraceAndText()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanner",
            trace: ["QueryPlanner:Proposed"],
            claimedAuthorities:
            [
                DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
                DataAgentGraphSidecarAuthority.RequestCSharpSafetyService
            ]);
        DataAgentGraphSidecarResponse tooManyTraceEntries = safe with
        {
            Trace = Enumerable.Repeat("QueryPlanner:Proposed", 17).ToArray()
        };
        DataAgentGraphSidecarResponse overlongTraceEntry = safe with { Trace = [new string('t', 257)] };
        DataAgentGraphSidecarResponse overlongReason = safe with { ReasonCode = new string('r', 129) };
        DataAgentGraphSidecarResponse overlongMessage = safe with { Message = new string('m', 1025) };
        DataAgentGraphSidecarResponse overlongCapability = safe with { RequestedCapabilityName = new string('c', 129) };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(safe, policy), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(tooManyTraceEntries, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(overlongTraceEntry, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(overlongReason, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(overlongMessage, policy), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsResponseSafe(overlongCapability, policy), Is.False);
        });
    }

    static void AssertInvalidWithoutThrowing(Func<bool> validation)
    {
        bool? result = null;

        Assert.DoesNotThrow(() => result = validation());
        Assert.That(result, Is.False);
    }

    static DataAgentGraphSidecarRequest NewRequest(
        string workflowId,
        string sessionId,
        IReadOnlyList<string>? allowedCapabilityNames = null)
    {
        return new DataAgentGraphSidecarRequest(
            workflowId,
            sessionId,
            "owner",
            "Which required engineering gates failed?",
            "scenario_context=true",
            DataAgentGraphSidecarContract.DefaultAllowedNodeKinds,
            allowedCapabilityNames ?? ["DataAgentQueryPlanner"],
            "checkpoint-1",
            "Active",
            "trace-1");
    }

    static DataAgentGraphSidecarResponse NewResponse(
        string? requestedCapabilityName,
        IReadOnlyList<string> trace,
        IReadOnlyList<DataAgentGraphSidecarAuthority> claimedAuthorities,
        bool requiresCSharpSafetyService = true)
    {
        return new DataAgentGraphSidecarResponse(
            "wf-1",
            true,
            "intent_proposed",
            "Delegating to C# DataAgent safety service.",
            DataAgentGraphSidecarNodeKind.QueryPlanValidation,
            requestedCapabilityName,
            requiresCSharpSafetyService,
            trace,
            claimedAuthorities);
    }
}
