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

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(valid), Is.True);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankWorkflow), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankSession), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankTrace), Is.False);
            Assert.That(DataAgentGraphSidecarContract.IsRequestValid(blankCapability), Is.False);
        });
    }

    [Test]
    public void ResponseValidationRejectsSqlAndForbiddenAuthorityClaims()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();
        DataAgentGraphSidecarResponse safe = NewResponse(
            requestedCapabilityName: "DataAgentQueryPlanValidator",
            trace: ["QueryPlanner:Proposed", "QueryPlanValidation:DelegatedToCSharp"],
            claimedAuthorities: [DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent]);
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
        string requestedCapabilityName,
        IReadOnlyList<string> trace,
        IReadOnlyList<DataAgentGraphSidecarAuthority> claimedAuthorities)
    {
        return new DataAgentGraphSidecarResponse(
            "wf-1",
            true,
            "intent_proposed",
            "Delegating to C# DataAgent safety service.",
            DataAgentGraphSidecarNodeKind.QueryPlanValidation,
            requestedCapabilityName,
            true,
            trace,
            claimedAuthorities);
    }
}
