using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeContractTests
{
    [Test]
    public void OptionsDefaultDisabledAndParseOnlyExplicitTrueLikeValues()
    {
        string?[] disabledValues = [null, "", "   ", "false", "FALSE", "0", "no", "unexpected"];
        string[] enabledValues = ["true", "TRUE", "1", "yes", " YES "];

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeOptions.Disabled.Enabled, Is.False);
            Assert.That(DataAgentGraphHandshakeOptions.EnabledEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED"));

            foreach (string? value in disabledValues)
                Assert.That(DataAgentGraphHandshakeOptions.FromValue(value).Enabled, Is.False, $"Expected disabled for '{value}'.");

            foreach (string value in enabledValues)
                Assert.That(DataAgentGraphHandshakeOptions.FromValue(value).Enabled, Is.True, $"Expected enabled for '{value}'.");
        });
    }

    [Test]
    public void DefaultManifestScopesNodeCapabilitiesAndNeverExposesSqlExecution()
    {
        IReadOnlyList<DataAgentGraphNodeManifest> manifests = DataAgentGraphHandshakeManifestFactory.CreateDefault();

        DataAgentGraphNodeManifest queryPlanner = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.QueryPlanner);
        DataAgentGraphNodeManifest sqlSafety = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.SqlSafety);
        DataAgentGraphNodeManifest readOnlyExecute = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.ReadOnlyExecute);
        DataAgentGraphNodeManifest diagnostics = manifests.Single(manifest => manifest.NodeName == DataAgentWorkflowNodeNames.DiagnosticsRouter);

        Assert.Multiple(() =>
        {
            Assert.That(manifests.Select(manifest => manifest.NodeName), Does.Contain(DataAgentWorkflowNodeNames.ScenarioKnowledge));
            Assert.That(queryPlanner.AllowedToolNames, Does.Contain(DataAgentGraphHandshakeToolNames.ProposeQueryPlan));
            Assert.That(queryPlanner.AllowedToolNames, Does.Not.Contain(DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery));
            Assert.That(sqlSafety.AllowedToolNames, Does.Contain(DataAgentGraphHandshakeToolNames.ReadSqlSafetyStatus));
            Assert.That(readOnlyExecute.AllowedToolNames, Is.Empty);
            Assert.That(readOnlyExecute.DeniedCapabilityMarkers, Does.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
            Assert.That(diagnostics.AllowedToolNames, Does.Contain(DataAgentGraphHandshakeToolNames.ReadProgressDiagnostics));
            Assert.That(manifests.SelectMany(manifest => manifest.AllowedToolNames), Does.Not.Contain(DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery));
        });
    }

    [Test]
    public void BuildRequestDefaultsToReadOnlyNoSqlAuthorityAndFallbackAvailable()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();

        Assert.Multiple(() =>
        {
            Assert.That(request.NoSqlAuthority, Is.True);
            Assert.That(request.ReadOnly, Is.True);
            Assert.That(request.FallbackAvailable, Is.True);
            Assert.That(request.NodeManifests, Is.Not.Empty);
            Assert.That(request.TraceBudgetChars, Is.EqualTo(DataAgentGraphHandshakeLimits.MaxTraceSummaryChars));
            Assert.That(request.ProgressBudget, Is.EqualTo(DataAgentGraphHandshakeLimits.MaxProgressEvents));
        });
    }

    [Test]
    public void ValidatorAcceptsSafeResponseAndRejectsAuthorityOverreach()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse safe = NewResponse(request);
        DataAgentGraphHandshakeResponse sqlAuthority = safe with { NoSqlAuthority = false };
        DataAgentGraphHandshakeResponse unknownNode = safe with { SelectedNodes = ["unknown_node"] };
        DataAgentGraphHandshakeResponse unknownTool = safe with { RequestedToolNames = ["browser.open"] };
        DataAgentGraphHandshakeResponse checkpointMutation = safe with { RequestsCheckpointMutation = true };
        DataAgentGraphHandshakeResponse visibleText = safe with { RequestsVisibleText = true };
        DataAgentGraphHandshakeResponse sqlTrace = safe with { TraceSummary = "SELECT * FROM document_index" };
        DataAgentGraphHandshakeResponse wrongRequest = safe with { RequestId = "wrong-request" };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, safe).Accepted, Is.True);
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, sqlAuthority).ReasonCode, Is.EqualTo("sql_authority_requested"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unknownNode).ReasonCode, Is.EqualTo("unknown_node"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unknownTool).ReasonCode, Is.EqualTo("unknown_tool"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, checkpointMutation).ReasonCode, Is.EqualTo("checkpoint_mutation_requested"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, visibleText).ReasonCode, Is.EqualTo("visible_text_requested"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, sqlTrace).ReasonCode, Is.EqualTo("unsafe_trace"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, wrongRequest).ReasonCode, Is.EqualTo("request_id_mismatch"));
        });
    }

    [Test]
    public void ValidatorRejectsInvalidProgressAndOverBudgetTrace()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse safe = NewResponse(request);
        DataAgentGraphHandshakeResponse unknownProgressNode = safe with
        {
            NodeProgress = [new DataAgentGraphHandshakeProgress("unknown_node", DataAgentGraphHandshakeProgressStatus.Completed, "done")]
        };
        DataAgentGraphHandshakeResponse tooManyProgressEvents = safe with
        {
            NodeProgress = Enumerable.Range(0, DataAgentGraphHandshakeLimits.MaxProgressEvents + 1)
                .Select(index => new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, $"p{index}"))
                .ToArray()
        };
        DataAgentGraphHandshakeResponse overBudgetTrace = safe with
        {
            TraceSummary = new string('x', DataAgentGraphHandshakeLimits.MaxTraceSummaryChars + 1)
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unknownProgressNode).ReasonCode, Is.EqualTo("progress_invalid"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, tooManyProgressEvents).ReasonCode, Is.EqualTo("progress_invalid"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, overBudgetTrace).ReasonCode, Is.EqualTo("unsafe_trace"));
        });
    }

    [Test]
    public void ValidatorScopesRequestedToolsToSelectedNodes()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse safe = NewResponse(request) with
        {
            SelectedNodes = [DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress =
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            RequestedToolNames = [DataAgentGraphHandshakeToolNames.ProposeQueryPlan]
        };
        DataAgentGraphHandshakeResponse crossNodeTool = safe with
        {
            RequestedToolNames = [DataAgentGraphHandshakeToolNames.ReadTraceDiagnostics]
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, safe).Accepted, Is.True);
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, crossNodeTool).ReasonCode, Is.EqualTo("unknown_tool"));
        });
    }

    [Test]
    public void ValidatorRejectsExecutionToolEvenWhenManifestAccidentallyAllowsIt()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeRequest graphExecutionAllowed = request with
        {
            NodeManifests = AddAllowedTool(
                request.NodeManifests,
                DataAgentWorkflowNodeNames.QueryPlanner,
                DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery)
        };
        DataAgentGraphHandshakeRequest capabilityExecutionAllowed = request with
        {
            NodeManifests = AddAllowedTool(
                request.NodeManifests,
                DataAgentWorkflowNodeNames.QueryPlanner,
                DataAgentNodeCapabilities.ExecuteReadOnlyQuery)
        };
        DataAgentGraphHandshakeRequest blankBusinessTerm = request with
        {
            NodeManifests = ReplaceManifest(
                request.NodeManifests,
                DataAgentWorkflowNodeNames.QueryPlanner,
                manifest => manifest with { BusinessTerms = ["dataset", " "] })
        };
        DataAgentGraphHandshakeResponse requestedExecution = NewResponse(graphExecutionAllowed) with
        {
            SelectedNodes = [DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress =
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            RequestedToolNames = [DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery]
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(graphExecutionAllowed, requestedExecution).ReasonCode, Is.EqualTo("invalid_request_schema"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(capabilityExecutionAllowed, NewResponse(capabilityExecutionAllowed)).ReasonCode, Is.EqualTo("invalid_request_schema"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(blankBusinessTerm, NewResponse(blankBusinessTerm)).ReasonCode, Is.EqualTo("invalid_request_schema"));
        });
    }

    [Test]
    public void ValidatorRejectsUndefinedProgressStatusAndUnsafeReasonCodes()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse safe = NewResponse(request);
        DataAgentGraphHandshakeResponse undefinedProgressStatus = safe with
        {
            NodeProgress =
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, (DataAgentGraphHandshakeProgressStatus)999, "planner_suggested")
            ]
        };
        DataAgentGraphHandshakeResponse unsafeResponseReason = safe with { ReasonCode = "free text reason" };
        DataAgentGraphHandshakeResponse unsafeProgressReason = safe with
        {
            NodeProgress =
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "free text reason")
            ]
        };
        DataAgentGraphHandshakeResponse dashedReason = safe with { ReasonCode = "route-allowed" };
        DataAgentGraphHandshakeResponse dottedReason = safe with { ReasonCode = "data.agent.reason" };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, undefinedProgressStatus).ReasonCode, Is.EqualTo("progress_invalid"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unsafeResponseReason).ReasonCode, Is.EqualTo("invalid_response_schema"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, unsafeProgressReason).ReasonCode, Is.EqualTo("progress_invalid"));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, dashedReason).Accepted, Is.True);
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, dottedReason).Accepted, Is.True);
        });
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "request-1",
            "session-1",
            "turn-1",
            "owner",
            "Which required gates failed?",
            "scenario_context=true",
            "route_allowed",
            "dataset=engineering_gate;limit<=50",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphHandshakeResponse NewResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            request.RequestId,
            Accepted: true,
            ReasonCode: "handshake_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress:
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentGraphHandshakeProgressStatus.Completed, "scenario_context_ready"),
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            TraceSummary: "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    static IReadOnlyList<DataAgentGraphNodeManifest> AddAllowedTool(
        IReadOnlyList<DataAgentGraphNodeManifest> manifests,
        string nodeName,
        string toolName)
    {
        return ReplaceManifest(
            manifests,
            nodeName,
            manifest => manifest with
            {
                AllowedToolNames = manifest.AllowedToolNames.Append(toolName).ToArray()
            });
    }

    static IReadOnlyList<DataAgentGraphNodeManifest> ReplaceManifest(
        IReadOnlyList<DataAgentGraphNodeManifest> manifests,
        string nodeName,
        Func<DataAgentGraphNodeManifest, DataAgentGraphNodeManifest> replace)
    {
        return manifests
            .Select(manifest => manifest.NodeName == nodeName ? replace(manifest) : manifest)
            .ToArray();
    }
}
