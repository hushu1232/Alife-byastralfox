using System.Net;
using System.Text;
using System.Text.Json;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeHttpClientTests
{
    [Test]
    public void TryHandshakePostsBoundedJsonRequestAndReturnsSafeResponse()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        HttpRequestMessage? capturedRequest = null;
        DataAgentGraphHandshakeRequest? capturedPayload = null;
        DataAgentGraphHandshakeHttpClient client = NewClient(httpRequest =>
        {
            capturedRequest = httpRequest;
            capturedPayload = JsonSerializer.Deserialize<DataAgentGraphHandshakeRequest>(
                httpRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json(NewResponse(capturedPayload!)), Encoding.UTF8, "application/json")
            };
        });

        DataAgentGraphHandshakeResponse response = client.TryHandshake(request);
        DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, response);

        Assert.Multiple(() =>
        {
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(capturedPayload, Is.Not.Null);
            Assert.That(capturedPayload!.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(capturedPayload.NoSqlAuthority, Is.True);
            Assert.That(capturedPayload.ReadOnly, Is.True);
            Assert.That(capturedPayload.FallbackAvailable, Is.True);
            Assert.That(capturedPayload.NodeManifests, Is.Not.Empty);
            Assert.That(response.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(validation.Accepted, Is.True);
        });
    }

    [Test]
    public void CoordinatorRejectsUnsafeHttpResponseAndDoesNotRetainRawPayload()
    {
        DataAgentGraphHandshakeHttpClient client = NewClient(httpRequest =>
        {
            DataAgentGraphHandshakeRequest request = JsonSerializer.Deserialize<DataAgentGraphHandshakeRequest>(
                httpRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json(NewResponse(request) with
                {
                    TraceSummary = "from document_index where status = failed limit 50"
                }), Encoding.UTF8, "application/json")
            };
        });
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), client);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("unsafe_trace"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(outcome.Response, Is.Null);
        });
    }

    [Test]
    public void NonSuccessStatusThrowsUnavailableException()
    {
        DataAgentGraphHandshakeHttpClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => client.TryHandshake(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("sidecar_unavailable"));
    }

    [Test]
    public void MalformedJsonThrowsInvalidResponseException()
    {
        DataAgentGraphHandshakeHttpClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json")
        });

        DataAgentGraphSidecarInvalidResponseException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidResponseException>(() => client.TryHandshake(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("invalid_response_schema"));
    }

    [Test]
    public void TimeoutThrowsTimeoutException()
    {
        DataAgentGraphHandshakeHttpClient client = NewClient(_ => throw new TaskCanceledException("timed out"));

        TimeoutException exception = Assert.Throws<TimeoutException>(() => client.TryHandshake(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("sidecar_timeout"));
    }

    static DataAgentGraphHandshakeHttpClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        HttpClient httpClient = new(new DelegateHandler(responseFactory));
        DataAgentGraphHandshakeHttpOptions options = new(
            new Uri("http://localhost:32123/handshake"),
            TimeSpan.FromMilliseconds(500),
            Configured: true,
            RuntimeStarted: false);

        return new DataAgentGraphHandshakeHttpClient(httpClient, options);
    }

    static string Json<T>(T value)
    {
        return JsonSerializer.Serialize(value);
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "graph-handshake-session-1-turn-1",
            "session-1",
            "turn-1",
            "owner",
            "Which gates failed?",
            "scenario_context=deterministic_csharp",
            "route_present=true;route_allows_query=true;route_reason_code=route_allowed",
            "status=Active;executed_sql=true;terminal=false",
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
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentGraphHandshakeProgressStatus.Completed, "scenario_ready"),
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            TraceSummary: "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted;planner=read_only_candidate",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    static DataAgentOrchestrationResult AcceptedResult()
    {
        DataAgentAnswer answer = new(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
            "[data_agent_context]\nsql_status=validated\nresult_explanation=Found DataAgent documentation.\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_documents",
                "document_index",
                "high",
                ["test"],
                "test accepted answer"));
        DataAgentAnalysisResponse response = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            answer,
            answer.Summary,
            answer.Context,
            Accepted: true,
            RejectedReason: string.Empty);
        DataAgentOrchestrationCheckpoint checkpoint = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            "document_index",
            TurnCount: 1,
            CanContinue: true,
            CanSummarize: true,
            Terminal: false);
        DataAgentToolRouteContext routeContext = new(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-test",
            "analysis_start",
            "route_allowed",
            string.Empty);

        return new DataAgentOrchestrationResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "schema_ready", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "plan_ready", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "executed", ExecutedSql: true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "explained", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_saved", ExecutedSql: false)
            ],
            checkpoint,
            response,
            routeContext);
    }

    sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
