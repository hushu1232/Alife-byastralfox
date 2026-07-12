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
    public void TryHandshakeAcceptsPythonStubProgressShapeWithStringStatusMessageAndFacts()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeHttpClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(PythonStubResponseJson(request.RequestId), Encoding.UTF8, "application/json")
        });

        DataAgentGraphHandshakeResponse response = client.TryHandshake(request);
        DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, response);
        DataAgentGraphHandshakeProgress progress = response.NodeProgress.Single(item => item.NodeName == DataAgentWorkflowNodeNames.QueryPlanner);

        Assert.Multiple(() =>
        {
            Assert.That(validation.Accepted, Is.True);
            Assert.That(progress.Status, Is.EqualTo(DataAgentGraphHandshakeProgressStatus.Completed));
            Assert.That(progress.ReasonCode, Is.EqualTo("planner_suggested"));
            Assert.That(progress.Message, Is.EqualTo("planner ready"));
            Assert.That(progress.Facts, Is.Not.Null);
            Assert.That(progress.Facts!["stage"], Is.EqualTo("planner"));
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

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("invalid_response_schema"));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [Test]
    public void DeclaredOversizedResponseIsRejectedBeforeDeserialization()
    {
        DataAgentGraphHandshakeHttpClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[65537])
        });

        DataAgentGraphSidecarInvalidResponseException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidResponseException>(() => client.TryHandshake(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("response_body_too_large"));
    }

    [Test]
    public void StreamedOversizedResponseWithoutContentLengthIsRejected()
    {
        DataAgentGraphHandshakeHttpClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[65537])
        });

        DataAgentGraphSidecarInvalidResponseException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidResponseException>(() => client.TryHandshake(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("response_body_too_large"));
    }

    [Test]
    public void MissingRequiredResponseFieldsAreRejectedAsInvalidSchema()
    {
        DataAgentGraphHandshakeHttpClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"RequestId\":\"request-1\"}", Encoding.UTF8, "application/json")
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

    static string PythonStubResponseJson(string requestId)
    {
        return $$"""
            {
              "RequestId": "{{requestId}}",
              "Accepted": true,
              "ReasonCode": "handshake_accepted",
              "SelectedNodes": [
                "scenario_knowledge",
                "query_planner",
                "diagnostics_router"
              ],
              "NodeProgress": [
                {
                  "NodeName": "scenario_knowledge",
                  "Status": "Completed",
                  "ReasonCode": "scenario_context_ready",
                  "Message": "scenario context ready",
                  "Facts": {
                    "stage": "scenario"
                  }
                },
                {
                  "NodeName": "query_planner",
                  "Status": "Completed",
                  "ReasonCode": "planner_suggested",
                  "Message": "planner ready",
                  "Facts": {
                    "stage": "planner"
                  }
                },
                {
                  "NodeName": "diagnostics_router",
                  "Status": "Completed",
                  "ReasonCode": "diagnostics_ready",
                  "Message": "diagnostics ready",
                  "Facts": {
                    "stage": "diagnostics"
                  }
                }
              ],
              "TraceSummary": "ScenarioKnowledge:Completed>QueryPlanner:Completed>DiagnosticsRouter:Completed",
              "ContextContribution": "graph_handshake=accepted",
              "FallbackRequired": false,
              "NoSqlAuthority": true,
              "ReadOnly": true,
              "RequestedToolNames": [
                "dataagent.query_plan.propose",
                "dataagent.diagnostics.progress.read"
              ],
              "RequestsCheckpointMutation": false,
              "RequestsVisibleText": false
            }
            """;
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

    sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(payload).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
