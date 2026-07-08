using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeNdjsonStreamClientTests
{
    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Test]
    public void TryHandshakeStreamPostsRequestAndReturnsBufferedProgressWithFinalResponse()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse finalResponse = NewResponse(request);
        DataAgentGraphHandshakeProgress firstProgress = NewProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, "scenario_ready");
        DataAgentGraphHandshakeProgress secondProgress = NewProgress(DataAgentWorkflowNodeNames.QueryPlanner, "planner_suggested");
        HttpRequestMessage? capturedRequest = null;
        DataAgentGraphHandshakeRequest? capturedPayload = null;
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(httpRequest =>
        {
            capturedRequest = httpRequest;
            capturedPayload = JsonSerializer.Deserialize<DataAgentGraphHandshakeRequest>(
                httpRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult(),
                JsonOptions);

            return OkNdjson(
                EventJson(new DataAgentGraphHandshakeStreamEvent(DataAgentGraphHandshakeStreamEventKind.Progress, firstProgress)),
                EventJson(new DataAgentGraphHandshakeStreamEvent(DataAgentGraphHandshakeStreamEventKind.Progress, secondProgress)),
                EventJson(new DataAgentGraphHandshakeStreamEvent(DataAgentGraphHandshakeStreamEventKind.FinalResponse, Response: finalResponse)));
        });

        DataAgentGraphHandshakeStreamResult result = client.TryHandshakeStream(request);
        DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, result.Response);

        Assert.Multiple(() =>
        {
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(capturedRequest!.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(capturedRequest.RequestUri, Is.EqualTo(new Uri("http://localhost:32123/handshake-stream")));
            Assert.That(capturedPayload, Is.Not.Null);
            Assert.That(capturedPayload!.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(capturedPayload.NoSqlAuthority, Is.True);
            Assert.That(capturedPayload.ReadOnly, Is.True);
            Assert.That(capturedPayload.FallbackAvailable, Is.True);
            Assert.That(result.Response.RequestId, Is.EqualTo(finalResponse.RequestId));
            Assert.That(result.Response.Accepted, Is.EqualTo(finalResponse.Accepted));
            Assert.That(result.Response.ReasonCode, Is.EqualTo(finalResponse.ReasonCode));
            Assert.That(result.Response.SelectedNodes, Is.EqualTo(finalResponse.SelectedNodes));
            Assert.That(result.Response.NodeProgress, Is.EqualTo(finalResponse.NodeProgress));
            Assert.That(result.Response.RequestedToolNames, Is.EqualTo(finalResponse.RequestedToolNames));
            Assert.That(result.Progress, Has.Count.EqualTo(2));
            Assert.That(result.Progress[0], Is.EqualTo(firstProgress));
            Assert.That(result.Progress[1], Is.EqualTo(secondProgress));
            Assert.That(validation.Accepted, Is.True);
        });
    }

    [Test]
    public void MalformedJsonLineThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => OkNdjson("{not-json"));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void UnknownEventKindThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => OkNdjson(
            """{"Kind":999,"Progress":{"NodeName":"scenario_knowledge","Status":"Completed","ReasonCode":"scenario_ready"}}"""));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void ProgressEventWithoutProgressThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => OkNdjson(
            EventJson(new DataAgentGraphHandshakeStreamEvent(DataAgentGraphHandshakeStreamEventKind.Progress))));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void FinalResponseEventWithoutResponseThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => OkNdjson(
            EventJson(new DataAgentGraphHandshakeStreamEvent(DataAgentGraphHandshakeStreamEventKind.FinalResponse))));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void EventWithBothProgressAndResponseThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => OkNdjson(
            EventJson(new DataAgentGraphHandshakeStreamEvent(
                DataAgentGraphHandshakeStreamEventKind.Progress,
                NewProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, "scenario_ready"),
                NewResponse(request)))));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(request))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void StreamWithoutFinalResponseThrowsMissingFinalResponse()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => OkNdjson(
            EventJson(new DataAgentGraphHandshakeStreamEvent(
                DataAgentGraphHandshakeStreamEventKind.Progress,
                NewProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, "scenario_ready")))));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("missing_stream_final_response"));
    }

    [Test]
    public void ProgressOverBudgetThrowsStreamProgressOverBudget()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        List<string> lines = Enumerable.Range(0, DataAgentGraphHandshakeLimits.MaxProgressEvents + 1)
            .Select(index => EventJson(new DataAgentGraphHandshakeStreamEvent(
                DataAgentGraphHandshakeStreamEventKind.Progress,
                NewProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, $"scenario_ready_{index}"))))
            .ToList();
        lines.Add(EventJson(new DataAgentGraphHandshakeStreamEvent(
            DataAgentGraphHandshakeStreamEventKind.FinalResponse,
            Response: NewResponse(request))));
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => OkNdjson(lines.ToArray()));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(request))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("stream_progress_over_budget"));
    }

    [Test]
    public void NonSuccessStatusThrowsUnavailable()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("sidecar_unavailable"));
    }

    [Test]
    public void TimeoutThrowsTimeoutException()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => throw new TaskCanceledException("timed out"));

        TimeoutException exception = Assert.Throws<TimeoutException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("sidecar_timeout"));
    }

    static DataAgentGraphHandshakeNdjsonStreamClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        HttpClient httpClient = new(new DelegateHandler(responseFactory));
        DataAgentGraphHandshakeStreamOptions options = new(
            Enabled: true,
            Endpoint: new Uri("http://localhost:32123/handshake-stream"),
            Timeout: TimeSpan.FromMilliseconds(500),
            Configured: true,
            RuntimeStarted: false);

        return new DataAgentGraphHandshakeNdjsonStreamClient(httpClient, options);
    }

    static HttpResponseMessage OkNdjson(params string[] lines)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join('\n', lines), Encoding.UTF8, "application/x-ndjson")
        };
    }

    static string EventJson(DataAgentGraphHandshakeStreamEvent streamEvent)
    {
        return JsonSerializer.Serialize(streamEvent, JsonOptions);
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
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

    static DataAgentGraphHandshakeProgress NewProgress(string nodeName, string reasonCode)
    {
        return new DataAgentGraphHandshakeProgress(nodeName, DataAgentGraphHandshakeProgressStatus.Completed, reasonCode);
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
                NewProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, "scenario_ready"),
                NewProgress(DataAgentWorkflowNodeNames.QueryPlanner, "planner_suggested")
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

    sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
