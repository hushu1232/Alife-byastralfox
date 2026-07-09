using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV315AuthorityFallbackRegressionTests
{
    [Test]
    public void DefaultSidecarPolicyRejectsEveryForbiddenAuthority()
    {
        DataAgentGraphSidecarPolicy policy = DataAgentGraphSidecarPolicy.CreateDefault();

        DataAgentGraphSidecarAuthority[] forbiddenAuthorities =
        [
            DataAgentGraphSidecarAuthority.AuthorizeDataset,
            DataAgentGraphSidecarAuthority.AuthorizeField,
            DataAgentGraphSidecarAuthority.AuthorizeOperator,
            DataAgentGraphSidecarAuthority.AuthorizeLimit,
            DataAgentGraphSidecarAuthority.ProvideExecutableSql,
            DataAgentGraphSidecarAuthority.ExecuteSql,
            DataAgentGraphSidecarAuthority.DecideToolRoute,
            DataAgentGraphSidecarAuthority.MutateCheckpoint,
            DataAgentGraphSidecarAuthority.WriteEvidence,
            DataAgentGraphSidecarAuthority.WriteAudit,
            DataAgentGraphSidecarAuthority.WriteProgress,
            DataAgentGraphSidecarAuthority.WriteDiagnostics,
            DataAgentGraphSidecarAuthority.SendVisibleQChatText,
            DataAgentGraphSidecarAuthority.OwnQqIngress
        ];

        Assert.Multiple(() =>
        {
            foreach (DataAgentGraphSidecarAuthority authority in forbiddenAuthorities)
            {
                DataAgentGraphSidecarResponse response = NewSidecarResponse([authority]);

                Assert.That(policy.Forbids(authority), Is.True, authority.ToString());
                Assert.That(
                    DataAgentGraphSidecarContract.IsResponseSafe(response, policy),
                    Is.False,
                    authority.ToString());
            }
        });
    }

    [Test]
    public void HandshakeValidatorRejectsAuthorityAndFallbackRegressions()
    {
        DataAgentGraphHandshakeRequest request = NewHandshakeRequest();
        DataAgentGraphHandshakeResponse safe = NewHandshakeResponse(request);

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, safe).Accepted, Is.True);
            AssertRejected(request, safe with { NoSqlAuthority = false }, "sql_authority_requested");
            AssertRejected(request, safe with { ReadOnly = false }, "sql_authority_requested");
            AssertRejected(request, safe with { RequestsCheckpointMutation = true }, "checkpoint_mutation_requested");
            AssertRejected(request, safe with { RequestsVisibleText = true }, "visible_text_requested");
            AssertRejected(request, safe with { RequestedToolNames = ["qchat.send"] }, "unknown_tool");
            AssertRejected(request, safe with { RequestedToolNames = [DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery] }, "unknown_tool");
            AssertRejected(request, safe with { RequestId = "wrong-request" }, "request_id_mismatch");
            AssertRejected(request, safe with { TraceSummary = new string('t', request.TraceBudgetChars + 1) }, "unsafe_trace");
            AssertRejected(request, safe with { NodeProgress = Enumerable.Repeat(
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested"),
                request.ProgressBudget + 1).ToArray() }, "progress_invalid");
        });
    }

    [Test]
    public void V315DocumentDeclaresAuthorityFallbackRegressionBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.15-authority-fallback-regression.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("authority_regression=true"));
            Assert.That(doc, Does.Contain("forbidden_authorities_rejected=true"));
            Assert.That(doc, Does.Contain("fallback_required=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("no_sql_authority=true"));
            Assert.That(doc, Does.Contain("no_visible_text=true"));
            Assert.That(doc, Does.Contain("AuthorizeDataset"));
            Assert.That(doc, Does.Contain("OwnQqIngress"));
        });
    }

    static void AssertRejected(
        DataAgentGraphHandshakeRequest request,
        DataAgentGraphHandshakeResponse response,
        string reasonCode)
    {
        DataAgentGraphHandshakeValidationResult result =
            DataAgentGraphHandshakeValidator.Validate(request, response);

        Assert.That(result.Accepted, Is.False, reasonCode);
        Assert.That(result.ReasonCode, Is.EqualTo(reasonCode));
    }

    static DataAgentGraphSidecarResponse NewSidecarResponse(
        IReadOnlyList<DataAgentGraphSidecarAuthority> claimedAuthorities)
    {
        return new DataAgentGraphSidecarResponse(
            WorkflowId: "wf-1",
            Accepted: true,
            ReasonCode: "authority_claimed",
            Message: "advisory response",
            ProposedNodeKind: DataAgentGraphSidecarNodeKind.QueryPlanner,
            RequestedCapabilityName: null,
            RequiresCSharpSafetyService: false,
            Trace: ["QueryPlanner:AdvisoryOnly"],
            ClaimedAuthorities: claimedAuthorities);
    }

    static DataAgentGraphHandshakeRequest NewHandshakeRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "request-1",
            "session-1",
            "turn-1",
            "owner",
            "Which required gates failed?",
            "scenario_context=ready",
            "route_present=true",
            "status=Active",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: 128,
            ProgressBudget: 2);
    }

    static DataAgentGraphHandshakeResponse NewHandshakeResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            RequestId: request.RequestId,
            Accepted: true,
            ReasonCode: "handshake_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress:
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphHandshakeProgressStatus.Completed,
                    "planner_suggested")
            ],
            TraceSummary: "QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
